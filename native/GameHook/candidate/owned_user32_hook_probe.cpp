// Explicit Windows owned-process contract for the vendor injector callback.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstdint>
using Calls=unsigned (WINAPI*)();
int main(int argc,char** argv) {
    if(argc!=2)return 1;
    HMODULE dll=LoadLibraryA(argv[1]);
    if(!dll){std::printf("FAIL load %lu\n",GetLastError());return 2;}
    auto hook=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"msg_hook_proc_ov"));
    auto calls=reinterpret_cast<Calls>(GetProcAddress(dll,"MiniMixerProbeHookCalls"));
    if(!hook||!calls){puts("FAIL ABI exports");FreeLibrary(dll);return 3;}
    HHOOK installed=SetWindowsHookExW(WH_GETMESSAGE,hook,dll,GetCurrentThreadId());
    if(!installed){std::printf("FAIL SetWindowsHookEx %lu\n",GetLastError());FreeLibrary(dll);return 4;}
    const DWORD tid=GetCurrentThreadId();
    MSG msg{};
    for(int i=0;i<2000;++i) {
        PostThreadMessageW(tid,WM_APP+50,0,0);
        if(!PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){
            puts("FAIL missing message");
            UnhookWindowsHookEx(installed);FreeLibrary(dll);return 5;
        }
    }
    const unsigned count=calls();
    if(!UnhookWindowsHookEx(installed)){
        puts("FAIL UnhookWindowsHookEx");
        return 6; // fail-closed: do not free module while installed
    }
    if(count<1900){std::printf("FAIL native hook was not called: %u\n",count);
        FreeLibrary(dll);return 7;}
    if(!FreeLibrary(dll)){puts("FAIL release module");return 8;}
    std::printf("PASS OWNED_USER32_CALLBACKS calls=%u hookRemoved=1\n",count);
    return 0;
}
