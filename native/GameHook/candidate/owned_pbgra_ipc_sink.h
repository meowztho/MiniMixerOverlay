#pragma once
// Owned Win32 standalone IPC -> DXGI 10-bit compositing validation.
// Uses real MiniMixer Pbgra32 FrameView, never attaches to a game.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <cstdint>
#include "live_repeat_marker.h"
#include "d3d12_pbgra_compositor.h"

namespace minimixer {
class OwnedPbgraIpcSink final {
    template<class T> using Ptr=Microsoft::WRL::ComPtr<T>;
    Ptr<ID3D12Device> device_;
    Ptr<ID3D12CommandQueue> queue_;
    Ptr<IDXGIFactory2> factory_;
    Ptr<IDXGISwapChain3> swapchain_;
    LiveRepeatMarker backdrop_;
    D3d12PbgraCompositor painter_;
    UINT sourceW_{},sourceH_{};
    HWND target_{};
    std::uint64_t signature_{};
    bool signatureKnown_{};
    static D3D12_RESOURCE_DESC Buffer(UINT64 bytes){
        D3D12_RESOURCE_DESC r{};r.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
        r.Width=bytes;r.Height=1;r.DepthOrArraySize=1;r.MipLevels=1;
        r.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;r.SampleDesc.Count=1;
        return r;
    }
    static D3D12_RESOURCE_BARRIER Barrier(ID3D12Resource* p,
                         D3D12_RESOURCE_STATES from,D3D12_RESOURCE_STATES to){
        D3D12_RESOURCE_BARRIER b{};
        b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition.pResource=p;b.Transition.StateBefore=from;
        b.Transition.StateAfter=to;
        b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        return b;
    }
    static bool Near(float actual,float expected){
        return std::fabs(actual-expected)<0.08f;
    }
    bool Verify(const FrameView& frame){
        Ptr<ID3D12Resource> back,readback;
        Ptr<ID3D12CommandAllocator> alloc;
        Ptr<ID3D12GraphicsCommandList> list;
        Ptr<ID3D12Fence> fence;
        const UINT idx=swapchain_->GetCurrentBackBufferIndex();
        if(FAILED(swapchain_->GetBuffer(idx,IID_PPV_ARGS(&back))))return false;
        const auto desc=back->GetDesc();
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT layout{};
        UINT64 total{};
        device_->GetCopyableFootprints(&desc,0,1,0,&layout,nullptr,nullptr,&total);
        D3D12_HEAP_PROPERTIES heap{};
        heap.Type=D3D12_HEAP_TYPE_READBACK;
        heap.CreationNodeMask=heap.VisibleNodeMask=1;
        const auto buffer=Buffer(total);
        if(FAILED(device_->CreateCommittedResource(&heap,D3D12_HEAP_FLAG_NONE,
                   &buffer,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,
                   IID_PPV_ARGS(&readback))) ||
           FAILED(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
                   IID_PPV_ARGS(&alloc))) ||
           FAILED(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
                   alloc.Get(),nullptr,IID_PPV_ARGS(&list))) ||
           FAILED(device_->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                   IID_PPV_ARGS(&fence))))return false;
        auto barrier=Barrier(back.Get(),D3D12_RESOURCE_STATE_PRESENT,
                                      D3D12_RESOURCE_STATE_COPY_SOURCE);
        list->ResourceBarrier(1,&barrier);
        D3D12_TEXTURE_COPY_LOCATION source{},dest{};
        source.pResource=back.Get();source.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dest.pResource=readback.Get();dest.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        dest.PlacedFootprint=layout;
        list->CopyTextureRegion(&dest,0,0,0,&source,nullptr);
        barrier=Barrier(back.Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,
                                   D3D12_RESOURCE_STATE_PRESENT);
        list->ResourceBarrier(1,&barrier);
        if(FAILED(list->Close()))return false;
        ID3D12CommandList* lists[]{list.Get()};
        queue_->ExecuteCommandLists(1,lists);
        if(FAILED(queue_->Signal(fence.Get(),1)))return false;
        if(fence->GetCompletedValue()<1){
            HANDLE ev=CreateEventW(nullptr,FALSE,FALSE,nullptr);
            if(!ev)return false;
            const bool ok=SUCCEEDED(fence->SetEventOnCompletion(1,ev)) &&
                WaitForSingleObject(ev,1500)==WAIT_OBJECT_0;
            CloseHandle(ev);
            if(!ok)return false;
        }
        std::uint8_t* data{};
        const D3D12_RANGE read{0,static_cast<SIZE_T>(total)};
        if(FAILED(readback->Map(0,&read,reinterpret_cast<void**>(&data)))||!data)
            return false;
        const UINT coords[5][2]={{10,10},{25,20},{38,32},{50,40},{70,52}};
        bool matches=true,visible=false;
        for(const auto& xy:coords){
            const UINT x=xy[0],y=xy[1];
            if(x>=frame.width||y>=frame.height){matches=false;break;}
            // The upstream marker paints its cyan center at x=44..292,
            // y=56..130. Our WPF content starts at (80,70).
            const auto* original=frame.bgra+static_cast<size_t>(y)*frame.stride+x*4;
            const auto* actual=data+layout.Offset+
              static_cast<size_t>(y+70)*layout.Footprint.RowPitch+
              static_cast<size_t>(x+80)*4;
            const UINT32 packed=*reinterpret_cast<const UINT32*>(actual);
            const float r=static_cast<float>(packed&1023u)/1023.f;
            const float g=static_cast<float>((packed>>10)&1023u)/1023.f;
            const float b=static_cast<float>((packed>>20)&1023u)/1023.f;
            const float remaining=1.f-original[3]/255.f;
            const float expectedR=original[2]/255.f+0.02f*remaining;
            const float expectedG=original[1]/255.f+0.85f*remaining;
            const float expectedB=original[0]/255.f+0.90f*remaining;
            if(original[3]>20)visible=true;
            if(!Near(r,expectedR)||!Near(g,expectedG)||!Near(b,expectedB)){
                matches=false;
            }
        }
        D3D12_RANGE wrote{0,0};
        readback->Unmap(0,&wrote);
        return matches&&visible;
    }
public:
    unsigned gpuFrames{},gpuMatches{},gpuPresents{},gpuResizes{},
             gpuVariants{},gpuErrors{};
    const char* Driver() const noexcept{return "HARDWARE";}
    bool Prepare(HWND hwnd,const FrameView& frame){
        if(!hwnd||!frame.valid())return false;
        if(!swapchain_){
            target_=hwnd;
            if(FAILED(D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,
                              IID_PPV_ARGS(&device_))))return false;
            D3D12_COMMAND_QUEUE_DESC q{};
            q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
            if(FAILED(device_->CreateCommandQueue(&q,IID_PPV_ARGS(&queue_))) ||
               FAILED(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory_))))return false;
            const UINT w=std::max(512u,frame.width+120u);
            const UINT h=std::max(288u,frame.height+120u);
            DXGI_SWAP_CHAIN_DESC1 sd{};
            sd.Width=w;sd.Height=h;sd.BufferCount=2;
            sd.Format=DXGI_FORMAT_R10G10B10A2_UNORM;
            sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
            sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
            Ptr<IDXGISwapChain1> base;
            if(FAILED(factory_->CreateSwapChainForHwnd(queue_.Get(),hwnd,
                               &sd,nullptr,nullptr,&base)) ||
               FAILED(base.As(&swapchain_)) ||
               !backdrop_.Init(swapchain_.Get(),queue_.Get(),hwnd) ||
               !painter_.Init(swapchain_.Get(),queue_.Get(),hwnd))return false;
            sourceW_=frame.width;sourceH_=frame.height;
        } else if(sourceW_!=frame.width||sourceH_!=frame.height){
            if(!painter_.WaitIdle(1500) || !backdrop_.WaitForIdle(1500) ||
               !painter_.PrepareForResize() || !backdrop_.PrepareForResize())
                return false;
            const UINT w=std::max(640u,frame.width+120u);
            const UINT h=std::max(360u,frame.height+120u);
            if(FAILED(swapchain_->ResizeBuffers(2,w,h,DXGI_FORMAT_UNKNOWN,0)) ||
               !SetWindowPos(hwnd,nullptr,0,0,static_cast<int>(w),
                          static_cast<int>(h),
                          SWP_NOMOVE|SWP_NOACTIVATE|SWP_NOZORDER)||
               !backdrop_.Init(swapchain_.Get(),queue_.Get(),hwnd) ||
               !painter_.Init(swapchain_.Get(),queue_.Get(),hwnd))
                return false;
            sourceW_=frame.width;sourceH_=frame.height;
            ++gpuResizes;
        }
        return target_==hwnd&&backdrop_.ready()&&painter_.Ready();
    }
    bool Render(const FrameView& frame){
        if(!swapchain_||!frame.valid())return false;
        if(!backdrop_.WaitForIdle(1500)||!painter_.WaitIdle(1500) ||
           !backdrop_.Draw() || !painter_.Draw(frame,80,70))return false;
        if(!Verify(frame)) {++gpuErrors;return false;}
        ++gpuMatches;
        if(FAILED(swapchain_->Present(0,0))){++gpuErrors;return false;}
        ++gpuPresents;++gpuFrames;
        std::uint64_t signature=1469598103934665603ULL;
        for(unsigned y=8;y<frame.height;y+=17){
          const auto* p=frame.bgra+static_cast<size_t>(y)*frame.stride;
          for(unsigned x=8;x<frame.width;x+=23){
             for(unsigned c=0;c<4;++c)
                signature=(signature^p[x*4+c])*1099511628211ULL;
          }
        }
        if(signatureKnown_ && signature_!=signature)++gpuVariants;
        signature_=signature;signatureKnown_=true;
        return true;
    }
    void Reset(){
        if(swapchain_){
            if(!painter_.WaitIdle(2500)||!backdrop_.WaitForIdle(2500))
                std::terminate(); // Never drop refs on active GPU work.
            if(!painter_.Cleanup()||!backdrop_.Cleanup())std::terminate();
        }
        swapchain_.Reset();factory_.Reset();queue_.Reset();device_.Reset();
        target_=nullptr;sourceW_=sourceH_=0;
    }
};
}
