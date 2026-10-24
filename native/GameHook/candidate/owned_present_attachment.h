#pragma once
// Scoped Present interception for a swapchain owned by the caller's test
// process. This is NOT an injection/detouring implementation.
#include <windows.h>
#include <dxgi.h>
#include <array>
#include <atomic>
#include <cstring>
#include <exception>

namespace minimixer {
// The number of slots is determined by the *exact* owned COM interface:
 // IDXGISwapChain: 18 slots; IDXGISwapChain3: 40 slots (indices 0..39).
 // Never attach this class to a different interface or an unowned swapchain.
template<size_t MethodCount>
class ScopedOwnedPresentAttachment final {
    static_assert(MethodCount==18 || MethodCount==40,
                  "Only owned IDXGISwapChain and IDXGISwapChain3 are supported");
public:
    using PresentFn=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
    using BeforeFn=void (*)(IDXGISwapChain*,void*) noexcept;
    using AfterFn=void (*)(HRESULT,void*) noexcept;
private:
    static constexpr size_t PresentSlot=8;
    inline static std::atomic<ScopedOwnedPresentAttachment*> active_{nullptr};
    IDXGISwapChain* chain_{};
    void*** objectSlot_{};
    void** originalTable_{};
    std::array<void*,MethodCount> table_{};
    PresentFn originalPresent_{};
    BeforeFn before_{};
    AfterFn after_{};
    void* user_{};
    DWORD attachingThread_{};
    std::atomic_flag drawing_=ATOMIC_FLAG_INIT;
    std::atomic<unsigned> inFlight_{0};
    std::atomic<unsigned> callbacks_{0},nestedSkipped_{0};
    bool attached_{};

    static HRESULT STDMETHODCALLTYPE Thunk(IDXGISwapChain* chain,UINT interval,UINT flags) noexcept {
        auto* owner=active_.load(std::memory_order_acquire);
        if(!owner || chain!=owner->chain_ || !owner->originalPresent_)return E_FAIL;
        owner->inFlight_.fetch_add(1,std::memory_order_acq_rel);
        // Nested Present must still reach the original function; only
        // overlay drawing is skipped. No waiting/locking in Present.
        if(!owner->drawing_.test_and_set(std::memory_order_acquire)) {
            ++owner->callbacks_;
            if(owner->before_)owner->before_(chain,owner->user_);
            owner->drawing_.clear(std::memory_order_release);
        } else {
            ++owner->nestedSkipped_;
        }
        const HRESULT result=owner->originalPresent_(chain,interval,flags);
        if(owner->after_)owner->after_(result,owner->user_);
        owner->inFlight_.fetch_sub(1,std::memory_order_acq_rel);
        return result;
    }
public:
    ScopedOwnedPresentAttachment()=default;
    ScopedOwnedPresentAttachment(const ScopedOwnedPresentAttachment&)=delete;
    ScopedOwnedPresentAttachment& operator=(const ScopedOwnedPresentAttachment&)=delete;
    ~ScopedOwnedPresentAttachment() {
        // A failed detach cannot be repaired by freeing the vtable.
        // Caller MUST quiesce and detach before destroying this object.
        if(attached_) std::terminate();
    }
    bool Attach(IDXGISwapChain* owned,BeforeFn before,AfterFn after,void* user) noexcept {
        if(attached_ || !owned || !before)return false;
        auto* expected=static_cast<ScopedOwnedPresentAttachment*>(nullptr);
        if(!active_.compare_exchange_strong(expected,this,std::memory_order_acq_rel))
            return false;
        auto** slot=reinterpret_cast<void***>(owned);
        void** original=*slot;
        if(!original || !original[PresentSlot]) {
            active_.store(nullptr,std::memory_order_release);
            return false;
        }
        std::memcpy(table_.data(),original,sizeof(table_));
        originalPresent_=reinterpret_cast<PresentFn>(original[PresentSlot]);
        table_[PresentSlot]=reinterpret_cast<void*>(&Thunk);
        chain_=owned;objectSlot_=slot;originalTable_=original;
        before_=before;after_=after;user_=user;
        attachingThread_=GetCurrentThreadId();
        // Only changes this instance's vptr. No shared/global vtable patch.
        const auto found=InterlockedCompareExchangePointer(
            reinterpret_cast<void* volatile*>(objectSlot_),table_.data(),original);
        if(found!=original) {
            chain_=nullptr;objectSlot_=nullptr;originalTable_=nullptr;
            originalPresent_=nullptr;before_=nullptr;after_=nullptr;user_=nullptr;
            attachingThread_=0;
            active_.store(nullptr,std::memory_order_release);
            return false;
        }
        attached_=true;
        return true;
    }
    bool Detach() noexcept {
        if(!attached_)return false;
        // Caller-owned Present and Detach must be serialized on one thread.
        // Reject off-thread Detach before touching the COM vptr.
        if(GetCurrentThreadId()!=attachingThread_)return false;
        // Fail closed if requested from callback or while another callback
        // is active. The owner retries only after its Present loop quiesces.
        if(inFlight_.load(std::memory_order_acquire)!=0)return false;
        const auto found=InterlockedCompareExchangePointer(
            reinterpret_cast<void* volatile*>(objectSlot_),originalTable_,table_.data());
        if(found!=table_.data())return false;
        attached_=false;
        chain_=nullptr;objectSlot_=nullptr;originalTable_=nullptr;
        originalPresent_=nullptr;before_=nullptr;after_=nullptr;user_=nullptr;
        attachingThread_=0;
        active_.store(nullptr,std::memory_order_release);
        return true;
    }
    bool Attached() const noexcept{return attached_;}
    unsigned CallbackCount() const noexcept{return callbacks_;}
    unsigned NestedSkipped() const noexcept{return nestedSkipped_;}
    unsigned InFlight() const noexcept{return inFlight_.load(std::memory_order_acquire);}
};
using OwnedPresentAttachment=ScopedOwnedPresentAttachment<18>;
using OwnedPresentAttachmentD3d12=ScopedOwnedPresentAttachment<40>;
} // namespace minimixer
