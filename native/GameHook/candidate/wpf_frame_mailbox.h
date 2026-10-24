#pragma once
// IPC-side immutable MiniMixer Pbgra32 frame snapshots. An IPC/message
// worker does ALL named-object waits and copies. Present only reads the
// published immutable shared_ptr and never opens mutexes, waits, or parses
// untrusted frame headers. No independent IPC host or audio owner.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>
#include <atomic>
#include "graphics_contract.h"

namespace minimixer {
struct WpfFrameSnapshot final {
    std::vector<std::uint8_t> pixels;
    std::uint32_t width{},height{};
    int left{},top{};
    std::uint64_t generation{},publishedAtMs{};
    FrameView View() const noexcept {
        return {pixels.data(),width,height,width*4};
    }
};

class WpfFrameMailbox final {
    HANDLE mapping_{},mutex_{};
    const std::uint8_t* view_{};
    std::string currentBuffer_,currentMutex_;
    std::shared_ptr<const WpfFrameSnapshot> published_{};
    std::uint64_t generation_{};
    int left_{},top_{};
    static constexpr std::uint64_t Limit=64ull*1024u*1024u;
    void CloseHandles() noexcept {
        if(view_)UnmapViewOfFile(view_);
        if(mapping_)CloseHandle(mapping_);
        if(mutex_)CloseHandle(mutex_);
        view_=nullptr;mapping_=mutex_=nullptr;
    }
    bool Open() noexcept {
        if(mapping_&&mutex_&&view_)return true;
        CloseHandles();
        if(currentBuffer_.empty()||currentMutex_.empty())return false;
        mapping_=OpenFileMappingA(FILE_MAP_READ,FALSE,currentBuffer_.c_str());
        mutex_=OpenMutexA(SYNCHRONIZE|MUTEX_MODIFY_STATE,FALSE,
                          currentMutex_.c_str());
        if(!mapping_||!mutex_){CloseHandles();return false;}
        view_=static_cast<const std::uint8_t*>(
            MapViewOfFile(mapping_,FILE_MAP_READ,0,0,0));
        if(!view_){CloseHandles();return false;}
        return true;
    }
public:
    std::atomic<unsigned> successful{0},missed{0},invalid{0},abandoned{0};
    // Only called on the owning IPC worker thread.
    void Configure(const std::string& mutexName,
                   const std::string& bufferName,int x,int y) noexcept {
        if(mutexName!=currentMutex_||bufferName!=currentBuffer_){
            CloseHandles();
            currentMutex_=mutexName;
            currentBuffer_=bufferName;
            std::atomic_store_explicit(&published_,
               std::shared_ptr<const WpfFrameSnapshot>{},
               std::memory_order_release);
        }
        left_=x;top_=y;
    }
    void SetBounds(int x,int y) noexcept {left_=x;top_=y;}
    void SetBuffer(const std::string& bufferName) noexcept {
        if(bufferName!=currentBuffer_){
            CloseHandles();currentBuffer_=bufferName;
            std::atomic_store_explicit(&published_,
               std::shared_ptr<const WpfFrameSnapshot>{},
               std::memory_order_release);
        }
    }
    std::shared_ptr<const WpfFrameSnapshot> Latest() const noexcept {
        auto current=std::atomic_load_explicit(&published_,
                                               std::memory_order_acquire);
        // A dead/paused WPF host must not leave stale UI rendered forever.
        if(current && GetTickCount64()-current->publishedAtMs>2000u)
            return {};
        return current;
    }
    bool Capture() noexcept {
        if(!Open()){
            ++missed;
            std::atomic_store_explicit(&published_,
                std::shared_ptr<const WpfFrameSnapshot>{},
                std::memory_order_release);
            return false;
        }
        const DWORD state=WaitForSingleObject(mutex_,8);
        if(state!=WAIT_OBJECT_0){
            if(state==WAIT_ABANDONED){++abandoned;ReleaseMutex(mutex_);}
            ++missed;return false;
        }
        bool ok=false;
        do{
            MEMORY_BASIC_INFORMATION mbi{};
            if(!VirtualQuery(view_,&mbi,sizeof(mbi))||mbi.RegionSize<8)
                break;
            std::int32_t width{},height{};
            std::memcpy(&width,view_,4);
            std::memcpy(&height,view_+4,4);
            if(width<2||height<2||width>4096||height>4096)
                break;
            const std::uint64_t length=
                static_cast<std::uint64_t>(width)*height*4u;
            if(length>Limit||length+8u>mbi.RegionSize)break;
            auto candidate=std::make_shared<WpfFrameSnapshot>();
            candidate->width=static_cast<std::uint32_t>(width);
            candidate->height=static_cast<std::uint32_t>(height);
            candidate->left=left_;candidate->top=top_;
            candidate->generation=++generation_;
            candidate->publishedAtMs=GetTickCount64();
            candidate->pixels.assign(view_+8,
                                     view_+8+static_cast<std::size_t>(length));
            std::shared_ptr<const WpfFrameSnapshot> immutable=candidate;
            std::atomic_store_explicit(&published_,std::move(immutable),
                                       std::memory_order_release);
            ok=true;
        }while(false);
        ReleaseMutex(mutex_);
        if(ok)++successful;
        else {
            ++invalid;
            std::atomic_store_explicit(&published_,
                std::shared_ptr<const WpfFrameSnapshot>{},
                std::memory_order_release);
        }
        return ok;
    }
    void Reset() noexcept {
        CloseHandles();currentBuffer_.clear();currentMutex_.clear();
        std::atomic_store_explicit(&published_,
           std::shared_ptr<const WpfFrameSnapshot>{},
           std::memory_order_release);
    }
    ~WpfFrameMailbox(){Reset();}
    WpfFrameMailbox()=default;
    WpfFrameMailbox(const WpfFrameMailbox&)=delete;
    WpfFrameMailbox& operator=(const WpfFrameMailbox&)=delete;
};
} // namespace minimixer
