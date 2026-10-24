#pragma once
// Single-shot in-game D3D12 backbuffer pixel verification. No input, audio,
// per-frame readback or persistent game resources. Deliberately not a product
// renderer: all resources are released after its fence and hooks quiesce.
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <atomic>
#include <cstdint>
#include <cstring>

struct LiveMarker final {
    std::atomic<bool> tried{false};
    std::atomic<int> result{0}; // 0 pending, 1 submitted, -N failure, 2 readback verified
    std::atomic<std::uint32_t> observedPixel{};
    DXGI_FORMAT format{DXGI_FORMAT_UNKNOWN};
    ID3D12Device* dev{};
    ID3D12CommandQueue* queue{};
    ID3D12Resource* buffer{},*staging{};
    ID3D12DescriptorHeap* rtv{};
    ID3D12CommandAllocator* allocator{};
    ID3D12GraphicsCommandList* list{};
    ID3D12Fence* fence{};
    UINT width{},height{};
    std::uint64_t fenceSignal{1};
    bool submittedWithoutFence{};

    template<class T> static void Release(T*& value) noexcept {
        if(value){value->Release();value=nullptr;}
    }
    bool DrawOnce(IDXGISwapChain* chain,ID3D12CommandQueue* candidate) noexcept {
        if(!chain||!candidate||tried.exchange(true,std::memory_order_acq_rel))return false;
        result.store(-1);
        D3D12_COMMAND_QUEUE_DESC queueDesc=candidate->GetDesc();
        if(queueDesc.Type!=D3D12_COMMAND_LIST_TYPE_DIRECT){result.store(-2);return false;}
        ID3D12Device* sourceDevice{};
        HRESULT hr=chain->GetDevice(__uuidof(ID3D12Device),reinterpret_cast<void**>(&sourceDevice));
        if(FAILED(hr)||!sourceDevice){result.store(-3);return false;}
        ID3D12Device* queueDevice{};
        hr=candidate->GetDevice(IID_PPV_ARGS(&queueDevice));
        if(FAILED(hr)||!queueDevice){sourceDevice->Release();result.store(-4);return false;}
        IUnknown* left{},*right{};
        sourceDevice->QueryInterface(IID_PPV_ARGS(&left));
        queueDevice->QueryInterface(IID_PPV_ARGS(&right));
        const bool sameDevice=left&&right&&left==right;
        Release(left);Release(right);Release(queueDevice);
        if(!sameDevice){sourceDevice->Release();result.store(-5);return false;}
        dev=sourceDevice; // Retained until after bounded async queue work.
        queue=candidate;queue->AddRef();

        IDXGISwapChain3* extended{};
        hr=chain->QueryInterface(IID_PPV_ARGS(&extended));
        if(FAILED(hr)||!extended){result.store(-6);return false;}
        const UINT index=extended->GetCurrentBackBufferIndex();
        hr=extended->GetBuffer(index,IID_PPV_ARGS(&buffer));
        extended->Release();
        if(FAILED(hr)||!buffer){result.store(-7);return false;}
        const D3D12_RESOURCE_DESC desc=buffer->GetDesc();
        width=static_cast<UINT>(desc.Width);height=desc.Height;format=desc.Format;
        if(width<128||height<120||desc.SampleDesc.Count!=1){result.store(-8);return false;}

        D3D12_DESCRIPTOR_HEAP_DESC hd{};
        hd.NumDescriptors=1;hd.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        hr=dev->CreateDescriptorHeap(&hd,IID_PPV_ARGS(&rtv));
        if(FAILED(hr)){result.store(-9);return false;}
        const auto handle=rtv->GetCPUDescriptorHandleForHeapStart();
        dev->CreateRenderTargetView(buffer,nullptr,handle);

        hr=dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&allocator));
        if(FAILED(hr)){result.store(-10);return false;}
        hr=dev->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
                                  allocator,nullptr,IID_PPV_ARGS(&list));
        if(FAILED(hr)){result.store(-11);return false;}
        hr=dev->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence));
        if(FAILED(hr)){result.store(-12);return false;}

        D3D12_HEAP_PROPERTIES readHeap{};
        readHeap.Type=D3D12_HEAP_TYPE_READBACK;
        readHeap.CreationNodeMask=readHeap.VisibleNodeMask=1;
        D3D12_RESOURCE_DESC rd{};
        rd.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
        rd.Width=256;rd.Height=1;rd.DepthOrArraySize=1;rd.MipLevels=1;
        rd.SampleDesc.Count=1;rd.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        hr=dev->CreateCommittedResource(&readHeap,D3D12_HEAP_FLAG_NONE,
              &rd,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&staging));
        if(FAILED(hr)){result.store(-13);return false;}

        D3D12_RESOURCE_BARRIER b{};
        b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition.pResource=buffer;
        b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        b.Transition.StateBefore=D3D12_RESOURCE_STATE_PRESENT;
        b.Transition.StateAfter=D3D12_RESOURCE_STATE_RENDER_TARGET;
        list->ResourceBarrier(1,&b);

        constexpr float color[4]={0.94f,0.09f,0.69f,1.0f};
        const D3D12_RECT rect{64,64,240,116};
        list->ClearRenderTargetView(handle,color,1,&rect);

        b.Transition.StateBefore=D3D12_RESOURCE_STATE_RENDER_TARGET;
        b.Transition.StateAfter=D3D12_RESOURCE_STATE_COPY_SOURCE;
        list->ResourceBarrier(1,&b);
        D3D12_TEXTURE_COPY_LOCATION dst{},src{};
        dst.pResource=staging;dst.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        dst.PlacedFootprint.Offset=0;
        dst.PlacedFootprint.Footprint.Format=format;
        dst.PlacedFootprint.Footprint.Width=1;
        dst.PlacedFootprint.Footprint.Height=1;
        dst.PlacedFootprint.Footprint.Depth=1;
        dst.PlacedFootprint.Footprint.RowPitch=256;
        src.pResource=buffer;src.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        src.SubresourceIndex=0;
        const D3D12_BOX pixelBox{70,70,0,71,71,1};
        list->CopyTextureRegion(&dst,0,0,0,&src,&pixelBox);

        b.Transition.StateBefore=D3D12_RESOURCE_STATE_COPY_SOURCE;
        b.Transition.StateAfter=D3D12_RESOURCE_STATE_PRESENT;
        list->ResourceBarrier(1,&b);
        hr=list->Close();
        if(FAILED(hr)){result.store(-14);return false;}
        ID3D12CommandList* cmds[]{list};
        queue->ExecuteCommandLists(1,cmds);
        submittedWithoutFence=true;
        hr=queue->Signal(fence,fenceSignal);
        if(FAILED(hr)){result.store(-15);return false;}
        submittedWithoutFence=false;
        result.store(1,std::memory_order_release);
        return true;
    }
    void CheckReadback() noexcept {
        if(result.load(std::memory_order_acquire)!=1 || !fence ||
           fence->GetCompletedValue()<fenceSignal)return;
        void* ptr{};
        D3D12_RANGE range{0,4};
        if(FAILED(staging->Map(0,&range,&ptr))||!ptr){
            result.store(-16);return;
        }
        std::uint32_t pixel=0;
        std::memcpy(&pixel,ptr,4);
        D3D12_RANGE writeRange{0,0};
        staging->Unmap(0,&writeRange);
        observedPixel.store(pixel);
        // BGRA8/RGBA8 expected: nearly opaque magenta.
        const unsigned b0=(pixel>>0)&0xff,b1=(pixel>>8)&0xff,b2=(pixel>>16)&0xff;
        const bool isTenBit=format==DXGI_FORMAT_R10G10B10A2_UNORM;
        const bool supported=isTenBit || format==DXGI_FORMAT_B8G8R8A8_UNORM ||
                             format==DXGI_FORMAT_R8G8B8A8_UNORM;
        const unsigned red10=pixel&1023u;
        const unsigned green10=(pixel>>10)&1023u;
        const unsigned blue10=(pixel>>20)&1023u;
        const unsigned alpha10=(pixel>>30)&3u;
        const bool matches=isTenBit
             ? red10>=900 && red10<=1000 && green10>=65 && green10<=130 &&
               blue10>=660 && blue10<=750 && alpha10==3
             : format==DXGI_FORMAT_B8G8R8A8_UNORM
               ? b2>=200 && b0>=125 && b1<=90
               : b0>=200 && b2>=125 && b1<=90;
        result.store(supported?(matches?2:-17):-18);
    }
    // Caller holds exclusive Present/Resize ownership. A GPU submission
    // cannot be freed until its fence completes. Never spin on Present.
    bool WaitForIdle(DWORD timeoutMs) const noexcept {
        if(submittedWithoutFence)return false;
        if(!fence || result.load(std::memory_order_acquire)!=1)
            return !submittedWithoutFence;
        if(fence->GetCompletedValue()>=fenceSignal)return true;
        HANDLE completed=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!completed)return false;
        const HRESULT hr=fence->SetEventOnCompletion(fenceSignal,completed);
        const DWORD wait=SUCCEEDED(hr) ?
              WaitForSingleObject(completed,timeoutMs):WAIT_FAILED;
        CloseHandle(completed);
        return wait==WAIT_OBJECT_0 &&
               fence->GetCompletedValue()>=fenceSignal;
    }
    // Shared ownership contract: release this one-shot buffer BEFORE the
    // original ResizeBuffers, even if pixel readback was never inspected.
    // Reset for a fresh one-shot readback on the newly sized swapchain.
    bool PrepareForResize() noexcept {
        if(!tried.load(std::memory_order_acquire))return true;
        if(!WaitForIdle(0) || !Cleanup())return false;
        observedPixel.store(0,std::memory_order_release);
        result.store(0,std::memory_order_release);
        tried.store(false,std::memory_order_release);
        format=DXGI_FORMAT_UNKNOWN;
        width=0;height=0;
        submittedWithoutFence=false;
        return true;
    }
    // After the Present hook has been disabled and all submissions have
    // finished; if GPU is still using a buffer, refuse teardown.
    bool Cleanup() noexcept {
        if(submittedWithoutFence ||
           (result.load()==1 && fence && fence->GetCompletedValue()<fenceSignal))
            return false;
        Release(staging);Release(fence);Release(list);Release(allocator);
        Release(rtv);Release(buffer);Release(queue);Release(dev);
        return true;
    }
};
