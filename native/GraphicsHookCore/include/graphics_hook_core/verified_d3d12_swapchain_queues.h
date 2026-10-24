#pragma once
// DXGI factory observes the command queue actually supplied at D3D12
// swapchain creation. This is the one and only producer of trusted queue
// identity; ExecuteCommandLists on an unrelated queue is never evidence.
// Bounded mapping, no game offsets or new rendering/IPC responsibilities.
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <array>
#include <mutex>
#include <cstdint>
#include <limits>
namespace graphics_hook_core {
class VerifiedSwapchainQueues final {
    // A private DXGI byte token follows the SWAPCHAIN's own lifetime.
    // Pointer identity alone is insufficient: COM may recycle a released
    // swapchain's address while the bounded table retains the old pointer.
    inline static constexpr GUID StampGuid{
        0x2d85397b,0xdf73,0x4594,{0xbd,0x87,0xeb,0x68,0xfa,0x59,0x42,0x10}};
    struct Association {
        IDXGISwapChain* swapchain{}; // weak, checked with DXGI private data
        std::uint64_t serial{};
        std::uint64_t lastUse{};
        Microsoft::WRL::ComPtr<ID3D12CommandQueue> queue;
    };
    std::mutex lock_;
    std::array<Association,16> entries_{};
    std::uint64_t sequence_{};
    bool StillSame(const Association& entry,IDXGISwapChain* chain) const noexcept {
        if(!entry.queue || entry.swapchain!=chain || !chain)return false;
        std::uint64_t observed{};
        UINT bytes=sizeof(observed);
        return SUCCEEDED(chain->GetPrivateData(StampGuid,&bytes,&observed)) &&
               bytes==sizeof(observed) && observed==entry.serial;
    }
public:
    bool Record(IDXGISwapChain* swapchain,IUnknown* creationQueue) noexcept {
        if(!swapchain||!creationQueue)return false;
        Microsoft::WRL::ComPtr<ID3D12CommandQueue> queue;
        if(FAILED(creationQueue->QueryInterface(IID_PPV_ARGS(&queue))) ||
           queue->GetDesc().Type!=D3D12_COMMAND_LIST_TYPE_DIRECT)return false;
        Microsoft::WRL::ComPtr<ID3D12Device> fromSwap,fromQueue;
        if(FAILED(swapchain->GetDevice(IID_PPV_ARGS(&fromSwap))) ||
           FAILED(queue->GetDevice(IID_PPV_ARGS(&fromQueue))) ||
           fromSwap.Get()!=fromQueue.Get())return false;
        std::lock_guard<std::mutex> guard(lock_);
        const std::uint64_t serial=++sequence_;
        // A new private-data stamp prevents an old weak address from being
        // treated as proof when DXGI allocates a replacement at the same
        // COM pointer. No weak-chain dereference occurs during eviction.
        if(FAILED(swapchain->SetPrivateData(StampGuid,sizeof(serial),&serial)))
            return false; // fail closed if DXGI refuses provenance
        Association* selected=nullptr;
        for(auto& entry:entries_){
            if(entry.swapchain==swapchain){selected=&entry;break;}
            if(!selected && !entry.swapchain)selected=&entry;
        }
        if(!selected){
            // Bounded LRU: if all 16 slots are occupied, retain the most
            // recently used associations. Eviction causes ONLY a denied
            // draw for the old chain; it never assigns a guessed queue.
            selected=&entries_[0];
            for(auto& entry:entries_)
                if(entry.lastUse<selected->lastUse)selected=&entry;
        }
        selected->queue=queue;
        selected->swapchain=swapchain;
        selected->serial=serial;
        selected->lastUse=serial;
        return true;
    }
    ID3D12CommandQueue* Lookup(IDXGISwapChain* swapchain) noexcept {
        std::lock_guard<std::mutex> guard(lock_);
        for(auto& entry:entries_){
            if(StillSame(entry,swapchain)){
                entry.lastUse=++sequence_;
                ID3D12CommandQueue* result=entry.queue.Get();
                result->AddRef(); // caller releases after Present
                return result;
            }
        }
        return nullptr;
    }
    // Diagnostics only: the stored foreign swapchain is weak and may have
    // been destroyed between Presents. NEVER dereference it from Snapshot.
    // Rendering uses Lookup(live Present argument), which checks the stamp.
    bool Has(IDXGISwapChain* swapchain) noexcept {
        std::lock_guard<std::mutex> guard(lock_);
        for(const auto& entry:entries_)
            if(entry.swapchain==swapchain && entry.queue)return true;
        return false;
    }
    // ResizeBuffers1 may install new presentation queues. Drop the old
    // association before calling real DXGI; never keep a stale queue on
    // failure, unknown queues or concurrent resize.
    void Forget(IDXGISwapChain* swapchain) noexcept {
        std::lock_guard<std::mutex> guard(lock_);
        for(auto& entry:entries_){
            if(entry.swapchain==swapchain){
                entry.queue.Reset();
                entry.swapchain=nullptr;
                entry.serial=entry.lastUse=0;
            }
        }
    }
    void Clear() noexcept {
        std::lock_guard<std::mutex> guard(lock_);
        for(auto& entry:entries_){
            entry.queue.Reset();
            entry.swapchain=nullptr;
        }
    }
};
} // namespace graphics_hook_core
