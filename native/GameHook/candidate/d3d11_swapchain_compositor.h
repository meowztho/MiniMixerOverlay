#pragma once
// D3D11 swapchain compositor owned by existing Present/Resize/Stop boundary.
// Uses D3D11's logical writable GetBuffer(0) even in DXGI flip mode:
// unlike D3D12, GetBuffer(1) is not writable/available on D3D11 flip.
// No new IPC, game identity, UI or audio owner.
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include "d3d11_renderer.h"

namespace minimixer {
class D3d11SwapchainCompositor final {
    template<class T> using Ptr=Microsoft::WRL::ComPtr<T>;
    Ptr<IDXGISwapChain> chain_;
    Ptr<ID3D11Device> device_;
    Ptr<ID3D11DeviceContext> context_;
    Ptr<ID3D11RenderTargetView> target_;
    D3d11Renderer main_;
    D3d11Renderer sphere_;
    HWND window_{};
    UINT width_{},height_{};
public:
    unsigned mainFrames{},sphereFrames{},errors{};
    bool Ready() const noexcept { return target_!=nullptr; }
    UINT Width() const noexcept { return width_; }
    UINT Height() const noexcept { return height_; }
    static bool Supported(const DXGI_SWAP_CHAIN_DESC& desc) noexcept {
        const bool flip=desc.SwapEffect==DXGI_SWAP_EFFECT_FLIP_DISCARD ||
                        desc.SwapEffect==DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
        if(desc.BufferDesc.Width<32 || desc.BufferDesc.Height<32 ||
           desc.SampleDesc.Count!=1 || desc.SampleDesc.Quality!=0 ||
           desc.BufferCount==0 || desc.BufferCount>8 ||
           (flip && desc.BufferCount<2) ||
           (!flip && desc.SwapEffect!=DXGI_SWAP_EFFECT_DISCARD))
            return false;
        return desc.BufferDesc.Format==DXGI_FORMAT_B8G8R8A8_UNORM ||
               desc.BufferDesc.Format==DXGI_FORMAT_R8G8B8A8_UNORM ||
               desc.BufferDesc.Format==DXGI_FORMAT_R10G10B10A2_UNORM;
    }
    bool Init(IDXGISwapChain* chain, HWND hwnd) {
        if(Ready())return chain_.Get()==chain && hwnd==window_;
        if(!chain || !hwnd)return false;
        DXGI_SWAP_CHAIN_DESC desc{};
        if(FAILED(chain->GetDesc(&desc)) ||
           desc.OutputWindow!=hwnd || !Supported(desc))return false;
        Ptr<ID3D11Device> device;
        if(FAILED(chain->GetDevice(IID_PPV_ARGS(&device))))return false;
        Ptr<ID3D11DeviceContext> ctx;
        device->GetImmediateContext(ctx.GetAddressOf());
        if(!ctx)return false;
        // In D3D11 flip-model the single writable logical buffer is index 0.
        // GetBuffer(1) returns DXGI_ERROR_INVALID_CALL on actual Win11/WARP.
        Ptr<ID3D11Texture2D> buffer;
        if(FAILED(chain->GetBuffer(0,IID_PPV_ARGS(&buffer))))return false;
        D3D11_TEXTURE2D_DESC d{};
        buffer->GetDesc(&d);
        if(d.Width!=desc.BufferDesc.Width ||
           d.Height!=desc.BufferDesc.Height ||
           d.Format!=desc.BufferDesc.Format ||
           d.SampleDesc.Count!=1)return false;
        Ptr<ID3D11RenderTargetView> target;
        if(FAILED(device->CreateRenderTargetView(
            buffer.Get(),nullptr,target.GetAddressOf())))return false;
        chain_=chain;device_=device;context_=ctx;target_=target;
        width_=desc.BufferDesc.Width;height_=desc.BufferDesc.Height;
        window_=hwnd;
        return true;
    }
    bool Draw(const FrameView& frame,UINT left,UINT top,bool isSphere) {
        if(!Ready() || !frame.valid() ||
           frame.width>width_ || frame.height>height_ ||
           left>width_-frame.width || top>height_-frame.height)return false;
        auto& renderer=isSphere?sphere_:main_;
        const bool rendered=renderer.Draw(device_.Get(),context_.Get(),
                     target_.Get(),frame,left,top,width_,height_);
        if(rendered){
            if(isSphere)++sphereFrames;
            else ++mainFrames;
        }else ++errors;
        return rendered;
    }
    void NotifyPresentResult(HRESULT hr) {
        if(!Ready())return;
        if(!main_.NotifyPresentResult(device_.Get(),hr))++errors;
        if(!sphere_.NotifyPresentResult(device_.Get(),hr))++errors;
    }
    bool ReleaseForResize() {
        if(!main_.Reset() || !sphere_.Reset())return false;
        target_.Reset();context_.Reset();device_.Reset();chain_.Reset();
        width_=height_=0;window_=nullptr;
        return true;
    }
};
} // namespace minimixer
