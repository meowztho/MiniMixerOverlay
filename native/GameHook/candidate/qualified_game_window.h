#pragma once
// One-time read-only authorization from the existing GameHookBridge and
// NativeGameHookLoader. Not a second target selector. It intentionally
// rejects missing/stale identity rather than choosing the first Present.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dxgi.h>
#include <cstdint>
#include <cstring>
#include <utility>
#include <string>
namespace minimixer {
class QualifiedGameWindow final {
    HWND hwnd_{};
    DWORD pid_{};
    std::wstring title_;
    static constexpr std::uint32_t Magic=0x4D4D5447;
    static constexpr std::uint32_t Version=1;
    static bool QueryTitle(HWND window,wchar_t (&buffer)[260]) noexcept {
        if(!window||!IsWindow(window))return false;
        DWORD_PTR count{};
        // GetWindowTextW can synchronously call another in-process WndProc.
        // Never block the native startup worker on a hung/unpumped UI thread.
        return SendMessageTimeoutW(window,WM_GETTEXT,260,
               reinterpret_cast<LPARAM>(buffer),
               SMTO_ABORTIFHUNG|SMTO_BLOCK,40,&count)!=0 &&
               count>0 && count<260;
    }
    static std::uint64_t ProcessStartFileTime() noexcept {
        FILETIME create{},exit{},kernel{},user{};
        if(!GetProcessTimes(GetCurrentProcess(),&create,&exit,&kernel,&user))
            return 0;
        return (static_cast<std::uint64_t>(create.dwHighDateTime)<<32) |
                create.dwLowDateTime;
    }
public:
    bool Load() noexcept {
        if(hwnd_)return true;
        wchar_t name[100]{};
        const auto pid=GetCurrentProcessId();
        swprintf_s(name,L"Local\\MiniMixerQualifiedTarget_%lu",pid);
        HANDLE mapping=OpenFileMappingW(FILE_MAP_READ,FALSE,name);
        if(!mapping)return false;
        auto* view=static_cast<const std::uint8_t*>(
            MapViewOfFile(mapping,FILE_MAP_READ,0,0,560));
        if(!view){CloseHandle(mapping);return false;}
        struct Header {
            std::uint32_t magic,version,pid,hostPid;
            std::uint64_t hwnd,started;
            std::uint32_t titleLength;
        };
        Header head{};
        static_assert(sizeof(Header)==40);
        std::memcpy(&head,view,sizeof(head));
        bool accepted=false;
        if(head.magic==Magic && head.version==Version &&
           head.pid==pid && head.hostPid!=pid && head.hostPid!=0 &&
           head.started!=0 && head.started==ProcessStartFileTime() &&
           head.titleLength>0 && head.titleLength<=255 &&
           head.hwnd!=0){
            // Fixed 40-byte header; remaining is bounded UTF-16LE title.
            std::wstring title;
            title.resize(head.titleLength);
            std::memcpy(title.data(),view+40,head.titleLength*sizeof(wchar_t));
            const auto hwnd=reinterpret_cast<HWND>(
                static_cast<UINT_PTR>(head.hwnd));
            wchar_t actual[260]{};
            DWORD windowPid{};
            if(GetWindowThreadProcessId(hwnd,&windowPid) &&
               windowPid==pid && QueryTitle(hwnd,actual) &&
               title==actual){
                pid_=pid;hwnd_=hwnd;title_=std::move(title);
                accepted=true;
            }
        }
        UnmapViewOfFile(view);
        CloseHandle(mapping);
        return accepted;
    }
    HWND Hwnd() const noexcept {return hwnd_;}
    bool Accepts(IDXGISwapChain* chain,bool newChain) const noexcept {
        if(!hwnd_ || !chain)return false;
        DWORD currentPid{};
        // Revalidate the qualified title only for a NEW binding. Never
        // send cross-thread WM_GETTEXT on each ordinary Present.
        if(!GetWindowThreadProcessId(hwnd_,&currentPid) ||
           currentPid!=pid_)return false;
        if(newChain){
            wchar_t currentTitle[260]{};
            if(!QueryTitle(hwnd_,currentTitle) ||
               title_!=currentTitle)return false;
        }
        DXGI_SWAP_CHAIN_DESC desc{};
        return SUCCEEDED(chain->GetDesc(&desc)) &&
               desc.OutputWindow==hwnd_;
    }
};
} // namespace minimixer
