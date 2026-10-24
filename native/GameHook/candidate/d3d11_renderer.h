#pragma once
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include "graphics_contract.h"
#include "d3d11_state_guard.h"

namespace minimixer {

// Owned-device D3D11 renderer. Only one render operation may run at a time.
// Concurrent/nested calls fail closed (never wait in Present). The caller must
// still coordinate the underlying D3D11 immediate context and renderer lifetime.
// Owns no swapchain/backbuffer/RTV, so ResizeBuffers is owned by the caller.
inline constexpr char D3d11OverlayShader[] = R"(
    struct V { float2 pos : POSITION; float2 uv : TEXCOORD; };
    struct P { float4 pos : SV_POSITION; float2 uv : TEXCOORD; };
    P VS(V v) { P p; p.pos=float4(v.pos,0,1); p.uv=v.uv; return p; }
    Texture2D frame : register(t0);
    SamplerState linearSampler : register(s0);
    float4 PS(P p) : SV_TARGET { return frame.Sample(linearSampler,p.uv); }
)";
struct D3d11Vertex { float x, y, u, v; };

class D3d11Renderer final {
    template<typename T> using ComPtr = Microsoft::WRL::ComPtr<T>;
public:
    struct Counters {
        unsigned staticBuilds = 0;
        unsigned textureBuilds = 0;
        unsigned uploadedFrames = 0;
        unsigned busyRejects = 0;
        unsigned deviceLosses = 0;
    };
private:
    // Nonblocking admission protects the renderer cache, not arbitrary game
    // commands sent to the same immediate context by other owners.
    mutable std::atomic_flag occupied_ = ATOMIC_FLAG_INIT;
    class Lease final {
        std::atomic_flag& flag_;
        bool acquired_;
    public:
        explicit Lease(std::atomic_flag& flag) noexcept
            :flag_(flag),acquired_(!flag.test_and_set(std::memory_order_acquire)) {}
        Lease(const Lease&)=delete;
        Lease& operator=(const Lease&)=delete;
        ~Lease() {if(acquired_)flag_.clear(std::memory_order_release);}
        explicit operator bool() const noexcept {return acquired_;}
    };
    std::atomic<unsigned> staticBuilds_{0},textureBuilds_{0},uploadedFrames_{0};
    std::atomic<unsigned> busyRejects_{0},deviceLosses_{0};
    ComPtr<ID3D11Device> failedDevice_;
#if defined(MINIMIXER_D3D11_PROBE_HOOKS)
    // Deterministic test-only synchronization point; absent from product build.
    void (*beforeGpuWork_)(void*) = nullptr;
    void* probeContext_ = nullptr;
#endif
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11VertexShader> vertexShader_;
    ComPtr<ID3D11PixelShader> pixelShader_;
    ComPtr<ID3D11InputLayout> layout_;
    ComPtr<ID3D11Buffer> vertex_;
    ComPtr<ID3D11SamplerState> sampler_;
    ComPtr<ID3D11BlendState> blend_;
    ComPtr<ID3D11RasterizerState> rasterizer_;
    ComPtr<ID3D11Texture2D> texture_;
    ComPtr<ID3D11ShaderResourceView> resourceView_;
    UINT width_ = 0, height_ = 0;

    static bool IsDeviceFailure(HRESULT hr) noexcept {
        return hr==DXGI_ERROR_DEVICE_REMOVED || hr==DXGI_ERROR_DEVICE_RESET ||
               hr==DXGI_ERROR_DEVICE_HUNG || hr==DXGI_ERROR_DRIVER_INTERNAL_ERROR;
    }
    void ReleaseCache() {
        resourceView_.Reset();texture_.Reset();
        rasterizer_.Reset();blend_.Reset();sampler_.Reset();vertex_.Reset();
        layout_.Reset();pixelShader_.Reset();vertexShader_.Reset();device_.Reset();
        width_=height_=0;
    }
    void Quarantine(ID3D11Device* device) {
        if (device_.Get()==device) ReleaseCache();
        // Retain identity so even a temporarily successful status query cannot
        // accidentally revive a device rejected by a prior Present failure.
        if (failedDevice_.Get()!=device) {
            failedDevice_=device;
            ++deviceLosses_;
        }
    }
    bool CreateStaticResources(ID3D11Device* device) {
        ComPtr<ID3DBlob> vsBytes, psBytes;
        if (FAILED(D3DCompile(D3d11OverlayShader, std::strlen(D3d11OverlayShader),
            nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,vsBytes.GetAddressOf(),nullptr)) ||
            FAILED(D3DCompile(D3d11OverlayShader, std::strlen(D3d11OverlayShader),
            nullptr,nullptr,nullptr,"PS","ps_4_0",0,0,psBytes.GetAddressOf(),nullptr)) ||
            FAILED(device->CreateVertexShader(vsBytes->GetBufferPointer(),vsBytes->GetBufferSize(),
                nullptr,vertexShader_.GetAddressOf())) ||
            FAILED(device->CreatePixelShader(psBytes->GetBufferPointer(),psBytes->GetBufferSize(),
                nullptr,pixelShader_.GetAddressOf()))) return false;
        D3D11_INPUT_ELEMENT_DESC inputs[] = {
            {"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,8,D3D11_INPUT_PER_VERTEX_DATA,0}
        };
        if (FAILED(device->CreateInputLayout(inputs,2,vsBytes->GetBufferPointer(),
            vsBytes->GetBufferSize(),layout_.GetAddressOf()))) return false;
        const std::array<D3d11Vertex,6> vertices{{
            {-1.f,1.f,0.f,0.f},{1.f,1.f,1.f,0.f},{-1.f,-1.f,0.f,1.f},
            {1.f,1.f,1.f,0.f},{1.f,-1.f,1.f,1.f},{-1.f,-1.f,0.f,1.f}
        }};
        D3D11_BUFFER_DESC vb{};vb.ByteWidth=sizeof(vertices);
        vb.Usage=D3D11_USAGE_IMMUTABLE;vb.BindFlags=D3D11_BIND_VERTEX_BUFFER;
        D3D11_SUBRESOURCE_DATA vdata{};vdata.pSysMem=vertices.data();
        if (FAILED(device->CreateBuffer(&vb,&vdata,vertex_.GetAddressOf()))) return false;
        D3D11_SAMPLER_DESC sd{};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;
        sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;
        sd.MaxLOD=D3D11_FLOAT32_MAX;
        if (FAILED(device->CreateSamplerState(&sd,sampler_.GetAddressOf()))) return false;
        D3D11_BLEND_DESC bd{};bd.RenderTarget[0].BlendEnable=TRUE;
        bd.RenderTarget[0].SrcBlend=D3D11_BLEND_ONE;
        bd.RenderTarget[0].DestBlend=D3D11_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOp=D3D11_BLEND_OP_ADD;
        bd.RenderTarget[0].SrcBlendAlpha=D3D11_BLEND_ONE;
        bd.RenderTarget[0].DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOpAlpha=D3D11_BLEND_OP_ADD;
        bd.RenderTarget[0].RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
        if (FAILED(device->CreateBlendState(&bd,blend_.GetAddressOf()))) return false;
        D3D11_RASTERIZER_DESC rd{};rd.FillMode=D3D11_FILL_SOLID;
        rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;
        return SUCCEEDED(device->CreateRasterizerState(&rd,rasterizer_.GetAddressOf()));
    }

    bool PrepareTexture(ID3D11Device* device, const FrameView& frame) {
        if (texture_ && width_==frame.width && height_==frame.height) return true;
        D3D11_TEXTURE2D_DESC td{};td.Width=frame.width;td.Height=frame.height;
        td.ArraySize=1;td.MipLevels=1;td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        td.SampleDesc.Count=1;td.Usage=D3D11_USAGE_DYNAMIC;
        td.BindFlags=D3D11_BIND_SHADER_RESOURCE;td.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
        ComPtr<ID3D11Texture2D> newTexture;
        ComPtr<ID3D11ShaderResourceView> newView;
        if (FAILED(device->CreateTexture2D(&td,nullptr,newTexture.GetAddressOf())) ||
            FAILED(device->CreateShaderResourceView(newTexture.Get(),nullptr,
                                                    newView.GetAddressOf()))) return false;
        texture_=newTexture;
        resourceView_=newView;
        width_=frame.width;
        height_=frame.height;
        ++textureBuilds_;
        return true;
    }
public:
    // Explicitly reset after safe detach/teardown; caller still owns RTV/context.
    // false means another Draw/Reset/Present-result is currently executing.
    bool Reset() {
        Lease lease(occupied_);
        if (!lease) {++busyRejects_;return false;}
        ReleaseCache();
        failedDevice_.Reset();
        return true;
    }
    // Feed results from the caller's actual Present. This does not intercept
    // Present or attempt to recreate a device/swapchain owned by someone else.
    bool NotifyPresentResult(ID3D11Device* device, HRESULT result) {
        Lease lease(occupied_);
        if (!lease) {++busyRejects_;return false;}
        if (!device) return false;
        if (IsDeviceFailure(result)) Quarantine(device);
        return true;
    }
    Counters Stats() const noexcept {
        return {staticBuilds_.load(),textureBuilds_.load(),uploadedFrames_.load(),
                busyRejects_.load(),deviceLosses_.load()};
    }
#if defined(MINIMIXER_D3D11_PROBE_HOOKS)
    void SetBeforeGpuWorkForTest(void (*hook)(void*),void* state) {
        beforeGpuWork_=hook;probeContext_=state;
    }
#endif
    // Region placement is relative to the caller-validated backbuffer, not
    // the window's desktop coordinates. Zero surface dimensions preserve
    // historical full-frame owned-probe behavior.
    bool Draw(ID3D11Device* device, ID3D11DeviceContext* ctx,
              ID3D11RenderTargetView* target, const FrameView& frame,
              UINT left=0, UINT top=0, UINT surfaceWidth=0, UINT surfaceHeight=0) {
        Lease lease(occupied_);
        if (!lease) {++busyRejects_;return false;}
        // Reject invalid/mismatched region before cache or GPU state changes.
        if (!device || !ctx || !target || !frame.valid()) return false;
        if (surfaceWidth==0 && surfaceHeight==0) {
            surfaceWidth=frame.width;surfaceHeight=frame.height;
        }
        if (surfaceWidth<frame.width || surfaceHeight<frame.height ||
            left>surfaceWidth-frame.width || top>surfaceHeight-frame.height)
            return false;
        if (failedDevice_.Get()==device) return false;
        if (FAILED(device->GetDeviceRemovedReason())) {
            Quarantine(device);
            return false;
        }
        ComPtr<ID3D11Device> contextDevice,targetDevice;
        ctx->GetDevice(contextDevice.GetAddressOf());
        target->GetDevice(targetDevice.GetAddressOf());
        if (contextDevice.Get()!=device||targetDevice.Get()!=device) return false;
        if (failedDevice_) failedDevice_.Reset(); // genuine replacement device
        if (device_.Get()!=device) {
            ReleaseCache();
            if (!CreateStaticResources(device)) {ReleaseCache();return false;}
            device_=device;
            ++staticBuilds_;
        }
        if (!PrepareTexture(device,frame)) return false;
#if defined(MINIMIXER_D3D11_PROBE_HOOKS)
        if(beforeGpuWork_)beforeGpuWork_(probeContext_);
#endif
        ScopedD3d11DrawState restore(ctx);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(ctx->Map(texture_.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&mapped))) return false;
        for (UINT y=0;y<frame.height;++y)
            std::memcpy(static_cast<std::uint8_t*>(mapped.pData)+y*mapped.RowPitch,
                        frame.bgra+static_cast<size_t>(y)*frame.stride,
                        static_cast<size_t>(frame.width)*4);
        ctx->Unmap(texture_.Get(),0);
        UINT stride=sizeof(D3d11Vertex),offset=0;
        ID3D11Buffer* vb=vertex_.Get();
        ID3D11ShaderResourceView* sr=resourceView_.Get();
        ID3D11SamplerState* sam=sampler_.Get();
        ctx->IASetVertexBuffers(0,1,&vb,&stride,&offset);
        ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->IASetInputLayout(layout_.Get());
        ctx->VSSetShader(vertexShader_.Get(),nullptr,0);
        ctx->PSSetShader(pixelShader_.Get(),nullptr,0);
        // Inherited game shader/occlusion states must never gate overlay pixels.
        // ScopedD3d11DrawState restores both states after the draw.
        ctx->GSSetShader(nullptr,nullptr,0);
        // Triangle-list overlay vertices are incompatible with a game's
        // inherited patch-list hull/domain shaders. Disable both for this
        // draw only; ScopedD3d11DrawState restores shaders + class instances.
        ctx->HSSetShader(nullptr,nullptr,0);
        ctx->DSSetShader(nullptr,nullptr,0);
        ctx->SetPredication(nullptr,FALSE);
        ctx->PSSetShaderResources(0,1,&sr);
        ctx->PSSetSamplers(0,1,&sam);
        ctx->OMSetBlendState(blend_.Get(),nullptr,0xffffffff);
        ctx->OMSetRenderTargets(1,&target,nullptr);
        ctx->RSSetState(rasterizer_.Get());
        D3D11_VIEWPORT viewport{};
        viewport.TopLeftX=static_cast<float>(left);
        viewport.TopLeftY=static_cast<float>(top);
        viewport.Width=static_cast<float>(frame.width);
        viewport.Height=static_cast<float>(frame.height);
        viewport.MinDepth=0;viewport.MaxDepth=1;
        ctx->RSSetViewports(1,&viewport);
        ctx->Draw(6,0);
        ++uploadedFrames_;
        return true;
    }
};
} // namespace minimixer
