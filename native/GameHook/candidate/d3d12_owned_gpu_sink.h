// D3D12 owned-device GPU renderer for a private test swapchain. No game injection, detours or app state access.
// Reuses MiniMixer's Pbgra32 FrameView; validates actual GPU shader/blend/readback.
#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>
#include <cstdlib>
#include "graphics_contract.h"
#include "owned_present_attachment.h"

namespace minimixer {
using Microsoft::WRL::ComPtr;
static bool Check(HRESULT hr,const char* where) {
    if(SUCCEEDED(hr))return true;
    std::printf("FAIL %s HRESULT=0x%08X\n",where,static_cast<unsigned>(hr));
    return false;
}
static D3D12_HEAP_PROPERTIES Heap(D3D12_HEAP_TYPE kind) {
    D3D12_HEAP_PROPERTIES p{};p.Type=kind;
    p.CPUPageProperty=D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
    p.MemoryPoolPreference=D3D12_MEMORY_POOL_UNKNOWN;
    p.CreationNodeMask=p.VisibleNodeMask=1;return p;
}
static D3D12_RESOURCE_DESC Buffer(UINT64 bytes) {
    D3D12_RESOURCE_DESC r{};r.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
    r.Width=bytes;r.Height=1;r.DepthOrArraySize=1;r.MipLevels=1;
    r.SampleDesc.Count=1;r.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;return r;
}
static D3D12_RESOURCE_BARRIER Transition(ID3D12Resource* resource,
                                         D3D12_RESOURCE_STATES from,
                                         D3D12_RESOURCE_STATES to) {
    D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource=resource;
    b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    b.Transition.StateBefore=from;b.Transition.StateAfter=to;return b;
}
constexpr char Shader[] = R"(
struct VSResult { float4 pos : SV_POSITION; float2 uv : TEXCOORD; };
VSResult VS(uint vertex : SV_VertexID) {
    float2 p=float2(vertex==2?3.0:-1.0,vertex==1?3.0:-1.0);
    VSResult o;o.pos=float4(p,0.0,1.0);
    o.uv=float2((p.x+1.0)*0.5,(1.0-p.y)*0.5);return o;
}
Texture2D frame : register(t0);
SamplerState frameSampler : register(s0);
float4 PS(VSResult input) : SV_TARGET {
    return frame.Sample(frameSampler,input.uv);
})";
class OwnedD3d12GpuSink {
    ComPtr<IDXGIFactory4> factory_;
    ComPtr<ID3D12Device> device_;
    ComPtr<ID3D12CommandQueue> queue_;
    ComPtr<IDXGISwapChain3> chain_;
    ComPtr<ID3D12DescriptorHeap> rtvHeap_,srvHeap_;
    ComPtr<ID3D12RootSignature> root_;
    ComPtr<ID3D12PipelineState> pipeline_;
    ComPtr<ID3D12CommandAllocator> allocator_;
    ComPtr<ID3D12GraphicsCommandList> commands_;
    ComPtr<ID3D12Fence> fence_;
    HANDLE event_{};
    UINT64 fenceValue_{};
    ComPtr<ID3D12Resource> backs_[2];
    ComPtr<ID3D12Resource> texture_,upload_,readback_;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT inputLayout_{},outputLayout_{};
    UINT64 readbackBytes_{};
    UINT inputWidth_{},inputHeight_{},surfaceWidth_{},surfaceHeight_{};
    UINT rtvIncrement_{};
    UINT textureBuilds_{},pipelineBuilds_{},submitted_{},resizes_{};
    std::uint64_t lastSignature_{};
    bool signatureSet_{},warp_{},callbackDrawOk_{},postOk_{},lostDevice_{};
    OwnedPresentAttachmentD3d12 attachment_;
    const FrameView* currentFrame_{};
    static void BeforeOwnedPresent(IDXGISwapChain*,void* state) noexcept {
        auto* self=static_cast<OwnedD3d12GpuSink*>(state);
        ++self->hookCallbacks;
        // Fail closed when no valid frame is ready; never draw stale pixels.
        self->callbackDrawOk_=self->currentFrame_ &&
                              self->DrawAndVerify(*self->currentFrame_);
#if defined(MINIMIXER_D3D12_PROBE_HOOKS)
        if(self->testRejectDetach_){
            self->testDetachRejected_=!self->DetachHook();
        }
        if(self->testNestedPresent_) {
            self->testNestedPresent_=false;
            self->testNestedResult_=self->chain_->Present(0,0);
        }
#endif
    }
    static void AfterOwnedPresent(HRESULT result,void* state) noexcept {
        auto* self=static_cast<OwnedD3d12GpuSink*>(state);
        if(FAILED(result) || FAILED(self->device_->GetDeviceRemovedReason()))
            self->lostDevice_=true;
        self->postOk_=SUCCEEDED(result) && !self->lostDevice_;
    }
    bool DetachHook() noexcept {
        if(!attachment_.Attached())return true;
        if(!attachment_.Detach())return false;
        ++hookDetaches;
        return true;
    }
#if defined(MINIMIXER_D3D12_PROBE_HOOKS)
    bool testRejectDetach_{},testDetachRejected_{},testNestedPresent_{};
    HRESULT testNestedResult_{E_FAIL};
#endif
public:
    unsigned gpuFrames{},gpuMatches{},gpuErrors{},gpuResizes{},gpuVariants{},gpuPresents{};
    unsigned hookCallbacks{},hookAttaches{},hookDetaches{};
    const char* Driver() const noexcept {return warp_?"WARP":"HARDWARE";}
    ~OwnedD3d12GpuSink(){Reset();}
    // Lifetime belongs to the client's serial IPC message loop. GPU work is
    // fenced before releasing resources. Not thread-safe or an injected hook.
    void Reset() {
        // The own IPC/message thread must quiesce Present before Reset.
        // Never release a hooked vtable or swapchain while Detach failed.
        if(!DetachHook())std::terminate();
        if(queue_ && fence_ && event_)WaitGpu();
        readback_.Reset();upload_.Reset();texture_.Reset();
        for(auto& b:backs_)b.Reset();
        commands_.Reset();allocator_.Reset();pipeline_.Reset();root_.Reset();
        srvHeap_.Reset();rtvHeap_.Reset();chain_.Reset();queue_.Reset();
        fence_.Reset();device_.Reset();factory_.Reset();
        if(event_){CloseHandle(event_);event_=nullptr;}
        inputWidth_=inputHeight_=surfaceWidth_=surfaceHeight_=0;
        readbackBytes_=0;signatureSet_=false;
        lostDevice_=false;currentFrame_=nullptr;
    }
    bool Init(HWND window,bool warp,UINT width=64,UINT height=64) {
        if(!window || !width || !height)return false;
        warp_=warp;
        if(!Check(CreateDXGIFactory2(0,IID_PPV_ARGS(factory_.GetAddressOf())),"factory"))
            return false;
        ComPtr<IDXGIAdapter> adapter;
        if(warp && !Check(factory_->EnumWarpAdapter(IID_PPV_ARGS(adapter.GetAddressOf())),"WARP adapter"))
            return false;
        if(!Check(D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,
                                      IID_PPV_ARGS(device_.GetAddressOf())),"device"))return false;
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        if(!Check(device_->CreateCommandQueue(&q,IID_PPV_ARGS(queue_.GetAddressOf())),"queue"))
            return false;
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=width;desc.Height=height;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.BufferCount=2;
        desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        ComPtr<IDXGISwapChain1> base;
        if(!Check(factory_->CreateSwapChainForHwnd(queue_.Get(),window,&desc,nullptr,
                   nullptr,base.GetAddressOf()),"swapchain") ||
           !Check(base.As(&chain_),"swapchain3"))return false;
        D3D12_DESCRIPTOR_HEAP_DESC rtvs{};rtvs.NumDescriptors=2;
        rtvs.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        if(!Check(device_->CreateDescriptorHeap(&rtvs,IID_PPV_ARGS(rtvHeap_.GetAddressOf())),"RTV heap"))
            return false;
        D3D12_DESCRIPTOR_HEAP_DESC srvs{};srvs.NumDescriptors=1;
        srvs.Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        srvs.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if(!Check(device_->CreateDescriptorHeap(&srvs,IID_PPV_ARGS(srvHeap_.GetAddressOf())),"SRV heap"))
            return false;
        rtvIncrement_=device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        if(!AcquireBackbuffers())return false;
        surfaceWidth_=width;surfaceHeight_=height;
        if(!Check(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
                    IID_PPV_ARGS(allocator_.GetAddressOf())),"allocator") ||
           !Check(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
                    allocator_.Get(),nullptr,IID_PPV_ARGS(commands_.GetAddressOf())),"list") ||
           !Check(commands_->Close(),"close init list") ||
           !Check(device_->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                    IID_PPV_ARGS(fence_.GetAddressOf())),"fence"))return false;
        event_=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!event_){puts("FAIL fence event");return false;}
        return CreatePipeline();
    }
    bool AcquireBackbuffers() {
        for(UINT i=0;i<2;++i){
            if(!Check(chain_->GetBuffer(i,IID_PPV_ARGS(backs_[i].GetAddressOf())),
                      "backbuffer"))return false;
            auto handle=rtvHeap_->GetCPUDescriptorHandleForHeapStart();
            handle.ptr+=static_cast<SIZE_T>(i)*rtvIncrement_;
            device_->CreateRenderTargetView(backs_[i].Get(),nullptr,handle);
        }
        return true;
    }
    bool CreatePipeline() {
        ComPtr<ID3DBlob> vs,ps,rootCode,rootError;
        if(!Check(D3DCompile(Shader,std::strlen(Shader),nullptr,nullptr,nullptr,
                  "VS","vs_5_0",0,0,vs.GetAddressOf(),nullptr),"VS compile") ||
           !Check(D3DCompile(Shader,std::strlen(Shader),nullptr,nullptr,nullptr,
                  "PS","ps_5_0",0,0,ps.GetAddressOf(),nullptr),"PS compile"))return false;
        D3D12_DESCRIPTOR_RANGE range{};
        range.RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        range.NumDescriptors=1;range.BaseShaderRegister=0;
        D3D12_ROOT_PARAMETER parameter{};
        parameter.ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        parameter.DescriptorTable.NumDescriptorRanges=1;
        parameter.DescriptorTable.pDescriptorRanges=&range;
        parameter.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
        D3D12_STATIC_SAMPLER_DESC sampler{};
        sampler.Filter=D3D12_FILTER_MIN_MAG_MIP_POINT;
        sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.MaxLOD=D3D12_FLOAT32_MAX;
        sampler.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
        D3D12_ROOT_SIGNATURE_DESC sig{};
        sig.NumParameters=1;sig.pParameters=&parameter;
        sig.NumStaticSamplers=1;sig.pStaticSamplers=&sampler;
        sig.Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
        if(!Check(D3D12SerializeRootSignature(&sig,D3D_ROOT_SIGNATURE_VERSION_1,
                 rootCode.GetAddressOf(),rootError.GetAddressOf()),"serialize root") ||
           !Check(device_->CreateRootSignature(0,rootCode->GetBufferPointer(),
                 rootCode->GetBufferSize(),IID_PPV_ARGS(root_.GetAddressOf())),"root"))return false;
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psd{};
        psd.pRootSignature=root_.Get();
        psd.VS={vs->GetBufferPointer(),vs->GetBufferSize()};
        psd.PS={ps->GetBufferPointer(),ps->GetBufferSize()};
        psd.BlendState.RenderTarget[0].BlendEnable=TRUE;
        auto& blend=psd.BlendState.RenderTarget[0];
        blend.SrcBlend=D3D12_BLEND_ONE;
        blend.DestBlend=D3D12_BLEND_INV_SRC_ALPHA;
        blend.BlendOp=D3D12_BLEND_OP_ADD;
        blend.SrcBlendAlpha=D3D12_BLEND_ONE;
        blend.DestBlendAlpha=D3D12_BLEND_INV_SRC_ALPHA;
        blend.BlendOpAlpha=D3D12_BLEND_OP_ADD;
        blend.RenderTargetWriteMask=D3D12_COLOR_WRITE_ENABLE_ALL;
        psd.SampleMask=UINT_MAX;
        psd.RasterizerState.FillMode=D3D12_FILL_MODE_SOLID;
        psd.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
        psd.RasterizerState.DepthClipEnable=TRUE;
        psd.DepthStencilState.DepthEnable=FALSE;
        psd.DepthStencilState.StencilEnable=FALSE;
        psd.PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psd.NumRenderTargets=1;
        psd.RTVFormats[0]=DXGI_FORMAT_B8G8R8A8_UNORM;
        psd.SampleDesc.Count=1;
        if(!Check(device_->CreateGraphicsPipelineState(&psd,
                  IID_PPV_ARGS(pipeline_.GetAddressOf())),"pipeline"))return false;
        ++pipelineBuilds_;return true;
    }
    bool WaitGpu() {
        ++fenceValue_;
        if(!Check(queue_->Signal(fence_.Get(),fenceValue_),"fence signal"))return false;
        if(fence_->GetCompletedValue()<fenceValue_){
            if(!Check(fence_->SetEventOnCompletion(fenceValue_,event_),"fence event"))return false;
            if(WaitForSingleObject(event_,5000)!=WAIT_OBJECT_0){
                puts("FAIL GPU timeout");return false;
            }
        }
        return true;
    }
    bool PrepareTexture(const FrameView& frame) {
        if(texture_ && frame.width==inputWidth_ && frame.height==inputHeight_)
            return true;
        texture_.Reset();upload_.Reset();
        D3D12_RESOURCE_DESC tex{};
        tex.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        tex.Width=frame.width;tex.Height=frame.height;
        tex.DepthOrArraySize=1;tex.MipLevels=1;
        tex.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        tex.SampleDesc.Count=1;
        auto gpu=Heap(D3D12_HEAP_TYPE_DEFAULT);
        if(!Check(device_->CreateCommittedResource(&gpu,D3D12_HEAP_FLAG_NONE,
                   &tex,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,
                   IID_PPV_ARGS(texture_.GetAddressOf())),"texture"))return false;
        UINT64 total=0;
        device_->GetCopyableFootprints(&tex,0,1,0,&inputLayout_,nullptr,nullptr,&total);
        auto up=Heap(D3D12_HEAP_TYPE_UPLOAD);
        auto buf=Buffer(total);
        if(!Check(device_->CreateCommittedResource(&up,D3D12_HEAP_FLAG_NONE,&buf,
                    D3D12_RESOURCE_STATE_GENERIC_READ,nullptr,
                    IID_PPV_ARGS(upload_.GetAddressOf())),"upload"))return false;
        D3D12_SHADER_RESOURCE_VIEW_DESC view{};
        view.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        view.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        view.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D;
        view.Texture2D.MipLevels=1;
        device_->CreateShaderResourceView(texture_.Get(),&view,
                          srvHeap_->GetCPUDescriptorHandleForHeapStart());
        inputWidth_=frame.width;inputHeight_=frame.height;
        ++textureBuilds_;return true;
    }
    bool EnsureReadback(UINT index) {
        D3D12_RESOURCE_DESC desc=backs_[index]->GetDesc();
        UINT64 total=0;
        device_->GetCopyableFootprints(&desc,0,1,0,&outputLayout_,nullptr,nullptr,&total);
        if(readback_ && readbackBytes_>=total)return true;
        readback_.Reset();
        auto heap=Heap(D3D12_HEAP_TYPE_READBACK);
        auto buf=Buffer(total);
        if(!Check(device_->CreateCommittedResource(&heap,D3D12_HEAP_FLAG_NONE,
                   &buf,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,
                   IID_PPV_ARGS(readback_.GetAddressOf())),"readback"))return false;
        readbackBytes_=total;return true;
    }
    // Real upload/readback only for offline smoke. A production Present path
    // must use asynchronous fences and must not map readback per frame.
    bool DrawAndVerify(const FrameView& frame) {
        if(lostDevice_ || !device_ || FAILED(device_->GetDeviceRemovedReason())) {
            lostDevice_=true;return false;
        }
        if(frame.width!=surfaceWidth_ || frame.height!=surfaceHeight_) {
            puts("FAIL source/swapchain dimensions mismatch");return false;
        }
        if(!frame.valid()) {puts("FAIL input contract");return false;}
        if(!PrepareTexture(frame))return false;
        UINT index=chain_->GetCurrentBackBufferIndex();
        if(!EnsureReadback(index))return false;
        std::uint8_t* mapped=nullptr;
        D3D12_RANGE noRead{0,0};
        if(!Check(upload_->Map(0,&noRead,reinterpret_cast<void**>(&mapped)),"upload map"))
            return false;
        for(UINT y=0;y<frame.height;++y){
            std::memcpy(mapped+inputLayout_.Offset+static_cast<size_t>(y)*
                         inputLayout_.Footprint.RowPitch,
                        frame.bgra+static_cast<size_t>(y)*frame.stride,
                        static_cast<size_t>(frame.width)*4);
        }
        upload_->Unmap(0,nullptr);
        if(!Check(allocator_->Reset(),"allocator reset") ||
           !Check(commands_->Reset(allocator_.Get(),pipeline_.Get()),"cmd reset"))
            return false;
        D3D12_TEXTURE_COPY_LOCATION source{},destination{};
        source.pResource=upload_.Get();
        source.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        source.PlacedFootprint=inputLayout_;
        destination.pResource=texture_.Get();
        destination.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        destination.SubresourceIndex=0;
        commands_->CopyTextureRegion(&destination,0,0,0,&source,nullptr);
        auto texToShader=Transition(texture_.Get(),D3D12_RESOURCE_STATE_COPY_DEST,
                                   D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        auto backToRt=Transition(backs_[index].Get(),D3D12_RESOURCE_STATE_PRESENT,
                                 D3D12_RESOURCE_STATE_RENDER_TARGET);
        commands_->ResourceBarrier(1,&texToShader);
        commands_->ResourceBarrier(1,&backToRt);
        auto rtv=rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        rtv.ptr+=static_cast<SIZE_T>(index)*rtvIncrement_;
        const float black[4]={0.f,0.f,0.f,1.f};
        commands_->OMSetRenderTargets(1,&rtv,FALSE,nullptr);
        commands_->ClearRenderTargetView(rtv,black,0,nullptr);
        D3D12_VIEWPORT viewport{0.f,0.f,static_cast<float>(surfaceWidth_),
                                static_cast<float>(surfaceHeight_),0.f,1.f};
        D3D12_RECT rect{0,0,static_cast<LONG>(surfaceWidth_),
                        static_cast<LONG>(surfaceHeight_)};
        commands_->RSSetViewports(1,&viewport);
        commands_->RSSetScissorRects(1,&rect);
        commands_->SetPipelineState(pipeline_.Get());
        commands_->SetGraphicsRootSignature(root_.Get());
        ID3D12DescriptorHeap* heaps[]={srvHeap_.Get()};
        commands_->SetDescriptorHeaps(1,heaps);
        commands_->SetGraphicsRootDescriptorTable(0,srvHeap_->GetGPUDescriptorHandleForHeapStart());
        commands_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        commands_->DrawInstanced(3,1,0,0);
        auto texRestore=Transition(texture_.Get(),D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                                   D3D12_RESOURCE_STATE_COPY_DEST);
        auto backRead=Transition(backs_[index].Get(),D3D12_RESOURCE_STATE_RENDER_TARGET,
                                  D3D12_RESOURCE_STATE_COPY_SOURCE);
        commands_->ResourceBarrier(1,&texRestore);
        commands_->ResourceBarrier(1,&backRead);
        D3D12_TEXTURE_COPY_LOCATION gpuSource{},cpuTarget{};
        gpuSource.pResource=backs_[index].Get();
        gpuSource.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        gpuSource.SubresourceIndex=0;
        cpuTarget.pResource=readback_.Get();
        cpuTarget.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        cpuTarget.PlacedFootprint=outputLayout_;
        commands_->CopyTextureRegion(&cpuTarget,0,0,0,&gpuSource,nullptr);
        auto backRestore=Transition(backs_[index].Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,
                                    D3D12_RESOURCE_STATE_PRESENT);
        commands_->ResourceBarrier(1,&backRestore);
        if(!Check(commands_->Close(),"close command"))return false;
        ID3D12CommandList* lists[]={commands_.Get()};
        queue_->ExecuteCommandLists(1,lists);
        ++submitted_;
        if(!WaitGpu())return false;
        std::uint8_t* pixels=nullptr;
        D3D12_RANGE read{0,static_cast<SIZE_T>(readbackBytes_)};
        if(!Check(readback_->Map(0,&read,reinterpret_cast<void**>(&pixels)),"read map"))return false;
        auto pixel=[&](UINT x,UINT y){
            return pixels+outputLayout_.Offset+
                   static_cast<size_t>(y)*outputLayout_.Footprint.RowPitch+x*4;
        };
        // Compare actual IPC pixels, including padded stride, at 25 locations.
        // Premultiplied RGB must be unchanged when blended onto opaque black.
        bool match=true,visible=false;
        std::uint64_t signature=1469598103934665603ull;
        for(unsigned yi=1;yi<=5;++yi)for(unsigned xi=1;xi<=5;++xi){
            const UINT x=(frame.width-1)*xi/6,y=(frame.height-1)*yi/6;
            const auto* actual=pixel(x,y);
            const auto* expected=frame.bgra+static_cast<size_t>(y)*frame.stride+x*4;
            for(int c=0;c<3;++c) {
                if(std::abs(static_cast<int>(actual[c])-static_cast<int>(expected[c]))>3)
                    match=false;
                signature=(signature ^ expected[c])*1099511628211ull;
            }
            if(expected[3]>24 && (expected[0]>8||expected[1]>8||expected[2]>8))
                visible=true;
        }
        readback_->Unmap(0,nullptr);
        ++gpuFrames;
        if(!match || !visible) {
            std::puts("FAIL D3D12_GPU_FRAME_MISMATCH_OR_EMPTY");
            ++gpuErrors;
            return false;
        }
        ++gpuMatches;
        if(signatureSet_ && signature!=lastSignature_)++gpuVariants;
        lastSignature_=signature;signatureSet_=true;
        return true;
    }
public:
    bool Render(const FrameView& frame,bool throughHook=false) {
        if(lostDevice_ || !device_) {++gpuErrors;return false;}
        if(throughHook) {
            if(!attachment_.Attached()) {
                if(!attachment_.Attach(chain_.Get(),&BeforeOwnedPresent,
                                       &AfterOwnedPresent,this)) {
                    ++gpuErrors;return false;
                }
                ++hookAttaches;
            }
            currentFrame_=&frame;callbackDrawOk_=postOk_=false;
            const HRESULT result=chain_->Present(0,0);
            currentFrame_=nullptr;
            if(FAILED(result)||!callbackDrawOk_||!postOk_){
                ++gpuErrors;return false;
            }
        } else {
            if(!DetachHook()) {++gpuErrors;return false;}
            if(!DrawAndVerify(frame))return false;
            const HRESULT result=chain_->Present(0,0);
            if(FAILED(result)||FAILED(device_->GetDeviceRemovedReason())){
                lostDevice_=true;++gpuErrors;return false;
            }
        }
        ++gpuPresents;
        return true;
    }
    bool Resize(UINT w,UINT h) {
        if(!DetachHook() || lostDevice_ || !WaitGpu())return false;
        for(auto& back:backs_)back.Reset();
        readback_.Reset();readbackBytes_=0;
        if(!Check(chain_->ResizeBuffers(2,w,h,DXGI_FORMAT_UNKNOWN,0),"ResizeBuffers"))
            return false;
        surfaceWidth_=w;surfaceHeight_=h;
        ++resizes_;++gpuResizes;return AcquireBackbuffers();
    }
    bool Prepare(HWND window,const FrameView& frame,bool useWarp) {
        if(!frame.valid() || !window)return false;
        if(!device_)return Init(window,useWarp,frame.width,frame.height);
        if(warp_!=useWarp)return false; // no mixed-device reinitialization
        if(surfaceWidth_!=frame.width || surfaceHeight_!=frame.height)
            return Resize(frame.width,frame.height);
        return true;
    }
    bool HookAttached() const noexcept{return attachment_.Attached();}
    unsigned HookNestedSkipped() const noexcept{return attachment_.NestedSkipped();}
#if defined(MINIMIXER_D3D12_PROBE_HOOKS)
    void EnableNestedPresentDetachProbe() noexcept {
        testNestedPresent_=true;
        testRejectDetach_=true;
        testDetachRejected_=false;
        testNestedResult_=E_FAIL;
    }
    bool NestedProbeSucceeded() const noexcept {
        return testDetachRejected_ && SUCCEEDED(testNestedResult_) &&
               attachment_.NestedSkipped()==1 && attachment_.InFlight()==0;
    }
    // Synthetic test fault ONLY: no OS GPU reset, no driver manipulation.
    bool AttemptOffThreadDetachForTest() noexcept{return DetachHook();}
    void SimulateReportedDeviceLossForTest() noexcept {lostDevice_=true;}
    bool QuarantinedForTest() const noexcept{return lostDevice_;}
#endif
    UINT PipelineBuilds() const noexcept{return pipelineBuilds_;}
    UINT TextureBuilds() const noexcept{return textureBuilds_;}
    UINT Submissions() const noexcept{return submitted_;}
};

} // namespace minimixer
