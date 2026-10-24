#pragma once
// Isolated owned-swapchain consumer of frames already copied by the IPC client.
// No foreign hooks, injection, shared game context or window capture.
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include "d3d11_renderer.h"
#include "owned_present_attachment.h"

namespace minimixer {
class OwnedD3d11IpcSink final {
    template<class T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGISwapChain> chain_;
    ComPtr<ID3D11RenderTargetView> target_;
    ComPtr<ID3D11Texture2D> staging_;
    D3d11Renderer renderer_;
    OwnedPresentAttachment attachment_;
    const FrameView* currentFrame_{};
    bool callbackOk_{},postOk_{};
    static void BeforeHook(IDXGISwapChain*,void* user) noexcept {
        auto* self=static_cast<OwnedD3d11IpcSink*>(user);
        ++self->hookCallbacks;
        self->callbackOk_=self->currentFrame_ &&
            self->DrawAndVerify(*self->currentFrame_);
    }
    static void AfterHook(HRESULT hr,void* user) noexcept {
        auto* self=static_cast<OwnedD3d11IpcSink*>(user);
        self->postOk_=self->renderer_.NotifyPresentResult(self->device_.Get(),hr);
    }
    bool DetachHook() {
        if(!attachment_.Attached())return true;
        if(!attachment_.Detach())return false;
        ++hookDetaches;
        return true;
    }
    UINT width_{},height_{};
    bool warp_{};
    std::uint64_t lastSignature_{};
    bool signatureSet_{};
public:
    unsigned gpuFrames{},gpuMatches{},gpuErrors{},gpuResizes{},gpuVariants{};
    unsigned gpuPresents{},hookCallbacks{},hookAttaches{},hookDetaches{};
    const char* Driver() const noexcept {return warp_?"WARP":"HARDWARE";}
    void Reset() {
        // Single-threaded IPC message loop is the owner; Reset is not
        // invoked concurrently with Present.
        if(!DetachHook())std::terminate();
        renderer_.Reset();
        context_.Reset();target_.Reset();staging_.Reset();
        chain_.Reset();device_.Reset();
        width_=height_=0;currentFrame_=nullptr;
    }
    bool Prepare(HWND window,UINT width,UINT height,bool useWarp) {
        if(device_ && width_==width && height_==height && warp_==useWarp)return true;
        if(device_ && warp_==useWarp) {
            if(!DetachHook())return false;
            // Swapchain owns the backbuffer. Release every backbuffer reference
            // before ResizeBuffers; the renderer retains only its input texture.
            context_->OMSetRenderTargets(0,nullptr,nullptr);
            target_.Reset();staging_.Reset();
            HRESULT hr=chain_->ResizeBuffers(2,width,height,DXGI_FORMAT_UNKNOWN,0);
            if(FAILED(hr)){Reset();return false;}
            ++gpuResizes;
        } else {
            Reset();
            DXGI_SWAP_CHAIN_DESC desc{};
            desc.BufferDesc.Width=width;desc.BufferDesc.Height=height;
            desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
            desc.SampleDesc.Count=1;
            desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
            desc.BufferCount=2;desc.OutputWindow=window;
            desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
            D3D_FEATURE_LEVEL wanted[]={D3D_FEATURE_LEVEL_11_0,D3D_FEATURE_LEVEL_10_1};
            D3D_FEATURE_LEVEL actual{};
            auto driver=useWarp?D3D_DRIVER_TYPE_WARP:D3D_DRIVER_TYPE_HARDWARE;
            HRESULT hr=D3D11CreateDeviceAndSwapChain(nullptr,driver,nullptr,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,wanted,2,D3D11_SDK_VERSION,
                &desc,chain_.GetAddressOf(),device_.GetAddressOf(),&actual,
                context_.GetAddressOf());
            if(FAILED(hr)){Reset();return false;}
            warp_=useWarp;
        }
        ComPtr<ID3D11Texture2D> buffer;
        HRESULT hr=chain_->GetBuffer(0,__uuidof(ID3D11Texture2D),
                                     reinterpret_cast<void**>(buffer.GetAddressOf()));
        if(FAILED(hr) || FAILED(device_->CreateRenderTargetView(buffer.Get(),nullptr,
                                                                target_.GetAddressOf()))) {
            Reset();return false;
        }
        D3D11_TEXTURE2D_DESC stage{};
        buffer->GetDesc(&stage);
        stage.BindFlags=0;stage.MiscFlags=0;
        stage.Usage=D3D11_USAGE_STAGING;
        stage.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        if(FAILED(device_->CreateTexture2D(&stage,nullptr,staging_.GetAddressOf()))){
            Reset();return false;
        }
        width_=width;height_=height;
        return true;
    }
    // Called only on the owning IPC message thread, optionally from the
    // owned Present callback. GPU readback intentionally stays test-only.
    bool DrawAndVerify(const FrameView& frame) {
        if(!renderer_.Draw(device_.Get(),context_.Get(),target_.Get(),frame)) {
            ++gpuErrors;return false;
        }
        ++gpuFrames;
        // Readback only for this bounded cross-process smoke test. Do not use
        // Map(D3D11_MAP_READ) per-frame on a production Present hot path.
        ComPtr<ID3D11Texture2D> back;
        if(FAILED(chain_->GetBuffer(0,__uuidof(ID3D11Texture2D),
                                  reinterpret_cast<void**>(back.GetAddressOf())))){
            ++gpuErrors;return false;
        }
        context_->CopyResource(staging_.Get(),back.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(context_->Map(staging_.Get(),0,D3D11_MAP_READ,0,&mapped))){
            ++gpuErrors;return false;
        }
        // Compare a coarse grid against the actual IPC Pbgra32 frame.
        // On black (alpha=1) premultiplied RGB should match input RGB, ±3.
        bool match=true,visible=false;
        std::uint64_t signature=1469598103934665603ull;
        for(unsigned yi=1;yi<=5;++yi)for(unsigned xi=1;xi<=5;++xi){
            const UINT x=(frame.width-1)*xi/6,y=(frame.height-1)*yi/6;
            const auto* actual=static_cast<const unsigned char*>(mapped.pData)+
                               static_cast<size_t>(y)*mapped.RowPitch+x*4;
            const auto* expected=frame.bgra+static_cast<size_t>(y)*frame.stride+x*4;
            for(int ch=0;ch<3;++ch){
                if(std::abs(static_cast<int>(actual[ch])-static_cast<int>(expected[ch]))>3)
                    match=false;
                signature=(signature ^ expected[ch])*1099511628211ull;
            }
            if(expected[3]>24 && (expected[0]>8||expected[1]>8||expected[2]>8))
                visible=true;
        }
        context_->Unmap(staging_.Get(),0);
        // Requiring nontransparent content prevents a black-frame false pass.
        if(!match||!visible){++gpuErrors;return false;}
        ++gpuMatches;
        if(signatureSet_&&signature!=lastSignature_)++gpuVariants;
        lastSignature_=signature;signatureSet_=true;
        return true;
    }
public:
    bool Render(HWND window,const FrameView& frame,bool useWarp,bool hookPresent=false) {
        if(!window || !frame.valid()) {++gpuErrors;return false;}
        if(!Prepare(window,frame.width,frame.height,useWarp)){
            ++gpuErrors;return false;
        }
        constexpr float black[4]={0,0,0,1};
        context_->ClearRenderTargetView(target_.Get(),black);
        if(hookPresent) {
            if(!attachment_.Attached()) {
                if(!attachment_.Attach(chain_.Get(),&BeforeHook,&AfterHook,this)) {
                    ++gpuErrors;return false;
                }
                ++hookAttaches;
            }
            currentFrame_=&frame;
            callbackOk_=false;postOk_=false;
            const HRESULT result=chain_->Present(0,0);
            currentFrame_=nullptr;
            if(FAILED(result)||!callbackOk_||!postOk_) {
                ++gpuErrors;Reset();return false;
            }
            ++gpuPresents;
            return true;
        }
        if(!DetachHook()){++gpuErrors;return false;}
        if(!DrawAndVerify(frame))return false;
        const HRESULT present=chain_->Present(0,0);
        if(!renderer_.NotifyPresentResult(device_.Get(),present) || FAILED(present)){
            ++gpuErrors;Reset();return false;
        }
        ++gpuPresents;
        return true;
    }
};
} // namespace minimixer
