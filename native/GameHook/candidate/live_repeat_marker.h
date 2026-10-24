#pragma once
// Test-only bounded rectangle rendering into an owned or authorized D3D12
// swapchain. No polling in a hot path, no blocking on GPU work, no input,
// backbuffer screenshot or game-specific offsets. Caller serializes Present.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <atomic>
#include <cstdint>

class LiveRepeatMarker final {
    template<typename T> static void Drop(T*& p) noexcept {
        if(p){p->Release();p=nullptr;}
    }
    struct Frame {
        ID3D12Resource* buffer{};
        ID3D12CommandAllocator* allocator{};
        ID3D12GraphicsCommandList* list{};
        std::uint64_t completedFence{};
        D3D12_CPU_DESCRIPTOR_HANDLE rtv{};
    };
    static constexpr unsigned MaxBuffers=8;
    // Until explicitly stopped: not limited to the former 140-frame probe.
    // Stop command is handled by the bounded-wait DLL controller.
    static constexpr unsigned MaxFrames=0xffffffffu;
    Frame frames_[MaxBuffers]{};
    ID3D12Device* device_{};
    ID3D12CommandQueue* queue_{};
    ID3D12Fence* fence_{};
    // Published only after GPU setup completes; the diagnostic worker
    // reads it concurrently with the Present owner.
    std::atomic<ID3D12Fence*> publishedFence_{nullptr};
    ID3D12DescriptorHeap* heap_{};
    IDXGISwapChain3* chain_{};
    std::uint64_t nextFence_{};
    bool untrackedSubmission_{}; // GPU work whose fence Signal failed must never be freed.
    HWND target_{};
    UINT bufferCount_{};
    UINT width_{},height_{};
    DXGI_FORMAT format_{DXGI_FORMAT_UNKNOWN};
    std::atomic<bool> tried_{false};
    std::atomic<bool> ready_{false};
    std::atomic<bool> failed_{false};
public:
    // The diagnostic worker samples these concurrently with the render
    // owner. Do not read non-atomic counters across threads.
    std::atomic<unsigned> submitted{0},skipped{0},deviceErrors{0},resizeGuard{0};
    unsigned bufferCount()const noexcept{return bufferCount_;}
    UINT width()const noexcept{return width_;}
    UINT height()const noexcept{return height_;}
    bool ready()const noexcept{return ready_;}
    bool failed()const noexcept{return failed_;}
    bool attempted()const noexcept{return tried_.load();}
    std::uint64_t fenceCompleted()const noexcept {
        auto* visible=publishedFence_.load(std::memory_order_acquire);
        return visible ? visible->GetCompletedValue():0;
    }
    bool Init(IDXGISwapChain* sc,ID3D12CommandQueue* q,HWND expectedHwnd) noexcept {
        if(!sc||!q||!expectedHwnd || tried_.exchange(true))return false;
        auto invalid=[this](){failed_=true;++deviceErrors;return false;};
        DXGI_SWAP_CHAIN_DESC d{};
        if(FAILED(sc->GetDesc(&d)) || d.OutputWindow!=expectedHwnd ||
           d.BufferCount<2 || d.BufferCount>MaxBuffers ||
           d.BufferDesc.Width<128 || d.BufferDesc.Height<120) return invalid();
        if(d.BufferDesc.Format!=DXGI_FORMAT_R10G10B10A2_UNORM &&
           d.BufferDesc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM &&
           d.BufferDesc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM) return invalid();
        if(q->GetDesc().Type!=D3D12_COMMAND_LIST_TYPE_DIRECT)return invalid();
        ID3D12Device* chainDev{};
        ID3D12Device* queueDev{};
        if(FAILED(sc->GetDevice(IID_PPV_ARGS(&chainDev)))||
           FAILED(q->GetDevice(IID_PPV_ARGS(&queueDev)))||
           !chainDev || !queueDev){
            Drop(chainDev);Drop(queueDev);return invalid();
        }
        IUnknown *a{},*b{};
        chainDev->QueryInterface(IID_PPV_ARGS(&a));
        queueDev->QueryInterface(IID_PPV_ARGS(&b));
        bool matches=a && b && a==b;
        Drop(a);Drop(b);Drop(queueDev);
        if(!matches){Drop(chainDev);return invalid();}
        device_=chainDev;
        queue_=q;queue_->AddRef();
        if(FAILED(sc->QueryInterface(IID_PPV_ARGS(&chain_))))return invalid();
        bufferCount_=d.BufferCount;
        width_=d.BufferDesc.Width;height_=d.BufferDesc.Height;
        format_=d.BufferDesc.Format;
        D3D12_DESCRIPTOR_HEAP_DESC hd{};
        hd.NumDescriptors=bufferCount_;
        hd.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        if(FAILED(device_->CreateDescriptorHeap(&hd,IID_PPV_ARGS(&heap_))))return invalid();
        if(FAILED(device_->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence_))))
            return invalid();
        const UINT increment=device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        D3D12_CPU_DESCRIPTOR_HANDLE base=heap_->GetCPUDescriptorHandleForHeapStart();
        for(UINT i=0;i<bufferCount_;++i){
            auto& fr=frames_[i];
            if(FAILED(chain_->GetBuffer(i,IID_PPV_ARGS(&fr.buffer))))return invalid();
            auto rd=fr.buffer->GetDesc();
            if(rd.Width!=width_ || rd.Height!=height_ || rd.Format!=format_ ||
               rd.SampleDesc.Count!=1)return invalid();
            fr.rtv.ptr=base.ptr+static_cast<SIZE_T>(increment)*i;
            device_->CreateRenderTargetView(fr.buffer,nullptr,fr.rtv);
            if(FAILED(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
                                                       IID_PPV_ARGS(&fr.allocator))))return invalid();
            if(FAILED(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
                                                 fr.allocator,nullptr,
                                                 IID_PPV_ARGS(&fr.list))))return invalid();
            if(FAILED(fr.list->Close()))return invalid();
        }
        target_=expectedHwnd;
        publishedFence_.store(fence_,std::memory_order_release);
        ready_.store(true,std::memory_order_release);
        return true;
    }
    bool Draw() noexcept {
        if(!ready_ || failed_ || submitted>=MaxFrames)return false;
        DXGI_SWAP_CHAIN_DESC desc{};
        if(FAILED(chain_->GetDesc(&desc)) ||
           desc.OutputWindow!=target_ || desc.BufferCount!=bufferCount_ ||
           desc.BufferDesc.Width!=width_ || desc.BufferDesc.Height!=height_ ||
           desc.BufferDesc.Format!=format_){
            ++resizeGuard;
            ready_=false; // No more game resource references created.
            return false;
        }
        const UINT idx=chain_->GetCurrentBackBufferIndex();
        if(idx>=bufferCount_){failed_=true;++deviceErrors;return false;}
        auto& frame=frames_[idx];
        if(frame.completedFence && fence_->GetCompletedValue()<frame.completedFence){
            ++skipped;return false; // Never block the game Present thread.
        }
        if(FAILED(frame.allocator->Reset()) ||
           FAILED(frame.list->Reset(frame.allocator,nullptr))){
            failed_=true;++deviceErrors;return false;
        }
        D3D12_RESOURCE_BARRIER b{};
        b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition.pResource=frame.buffer;
        b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        b.Transition.StateBefore=D3D12_RESOURCE_STATE_PRESENT;
        b.Transition.StateAfter=D3D12_RESOURCE_STATE_RENDER_TARGET;
        frame.list->ResourceBarrier(1,&b);
        // Bright high-contrast diagnostic panel in top-left of game image.
        static constexpr float frameColor[4]={0.95f,0.06f,0.72f,1.0f};
        static constexpr float centerColor[4]={0.02f,0.85f,0.90f,1.0f};
        const D3D12_RECT border{36,48,300,138};
        const D3D12_RECT center{44,56,292,130};
        frame.list->ClearRenderTargetView(frame.rtv,frameColor,1,&border);
        frame.list->ClearRenderTargetView(frame.rtv,centerColor,1,&center);
        b.Transition.StateBefore=D3D12_RESOURCE_STATE_RENDER_TARGET;
        b.Transition.StateAfter=D3D12_RESOURCE_STATE_PRESENT;
        frame.list->ResourceBarrier(1,&b);
        if(FAILED(frame.list->Close())){failed_=true;++deviceErrors;return false;}
        ID3D12CommandList* lists[]={frame.list};
        queue_->ExecuteCommandLists(1,lists);
        untrackedSubmission_=true;
        auto id=++nextFence_;
        if(FAILED(queue_->Signal(fence_,id))){
            failed_=true;++deviceErrors;return false;
        }
        frame.completedFence=id;
        untrackedSubmission_=false;
        ++submitted;
        return true;
    }
    bool CanRelease() const noexcept {
        if(untrackedSubmission_)return false;
        if(!fence_)return true;
        const auto completed=fence_->GetCompletedValue();
        for(UINT i=0;i<bufferCount_;++i)
            if(frames_[i].completedFence>completed)return false;
        return true;
    }
    // Caller must own Present/Resize serialization. No spin/poll loop:
    // SetEventOnCompletion wakes only when previously submitted GPU work
    // completes. A bounded timeout refuses to release resources.
    bool WaitForIdle(DWORD timeoutMs) const noexcept {
        if(untrackedSubmission_)return false;
        if(CanRelease())return true;
        if(!fence_ || !nextFence_)return false;
        HANDLE event=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!event)return false;
        const HRESULT hr=fence_->SetEventOnCompletion(nextFence_,event);
        const DWORD wait=SUCCEEDED(hr) ? WaitForSingleObject(event,timeoutMs)
                                      : WAIT_FAILED;
        CloseHandle(event);
        return wait==WAIT_OBJECT_0 && CanRelease();
    }
    // Caller MUST already have exclusive lifecycle ownership: no active
    // Present callbacks and no concurrent ResizeBuffers/Present.
    // This is NOT an in-game detour. It releases every old buffer reference
    // before the caller invokes real DXGI ResizeBuffers.
    bool PrepareForResize() noexcept {
        if(!tried_.load(std::memory_order_acquire) || !CanRelease())
            return false;
        if(!Cleanup())return false;
        // A new successful DXGI ResizeBuffers may now be followed by Init().
        // Preserve lifetime counters but invalidate old dimensions/state.
        bufferCount_=0;
        width_=0;height_=0;
        target_=nullptr;
        format_=DXGI_FORMAT_UNKNOWN;
        nextFence_=0;
        untrackedSubmission_=false;
        failed_.store(false,std::memory_order_release);
        tried_.store(false,std::memory_order_release);
        return true;
    }
    bool Cleanup() noexcept {
        if(!CanRelease())return false;
        for(auto& f:frames_){
            Drop(f.list);Drop(f.allocator);Drop(f.buffer);
            // A recreated fence starts at zero. Never carry an old
            // buffer's in-flight fence number into the new swapchain.
            f.completedFence=0;
            f.rtv={};
        }
        // Cleanup is called by the controller only AFTER hooks are removed,
        // Present callbacks quiesced and the worker has stopped sampling.
        publishedFence_.store(nullptr,std::memory_order_release);
        Drop(chain_);Drop(fence_);Drop(heap_);Drop(queue_);Drop(device_);
        ready_.store(false,std::memory_order_release);
        return true;
    }
};
