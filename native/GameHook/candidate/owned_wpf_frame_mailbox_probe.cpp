// Real Win32 named-file-mapping/Mutex mailbox test. An IPC owner publishes
// immutable frames while another Windows thread continuously consumes Latest.
// No game hook, WPF app or external injection.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <atomic>
#include <thread>
#include "wpf_frame_mailbox.h"
namespace {
constexpr unsigned Bytes=262144;
void WriteFrame(std::uint8_t* mem,unsigned w,unsigned h,std::uint8_t color){
    const std::int32_t width=static_cast<std::int32_t>(w);
    const std::int32_t height=static_cast<std::int32_t>(h);
    std::memcpy(mem,&width,4);std::memcpy(mem+4,&height,4);
    std::memset(mem+8,color,static_cast<std::size_t>(w)*h*4);
}
}
int main(){
    char mapName[120]{},mutexName[120]{};
    sprintf_s(mapName,"Local\\MiniMixerOwnedFrame_%lu",
              GetCurrentProcessId());
    sprintf_s(mutexName,"Local\\MiniMixerOwnedMutex_%lu",
              GetCurrentProcessId());
    HANDLE mapping=CreateFileMappingA(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,
                                      0,Bytes,mapName);
    HANDLE mutex=CreateMutexA(nullptr,FALSE,mutexName);
    if(!mapping||!mutex)return 1;
    auto* bytes=static_cast<std::uint8_t*>(MapViewOfFile(
                           mapping,FILE_MAP_WRITE|FILE_MAP_READ,0,0,Bytes));
    if(!bytes)return 2;
    minimixer::WpfFrameMailbox mailbox;
    mailbox.Configure(mutexName,mapName,120,70);
    std::atomic<bool> running{true};
    std::atomic<unsigned> checked{0},bad{0},seenLast{0};
    std::thread reader([&](){
        std::shared_ptr<const minimixer::WpfFrameSnapshot> held;
        while(running.load(std::memory_order_acquire)){
            auto snapshot=mailbox.Latest();
            if(!snapshot){Sleep(0);continue;}
            const auto view=snapshot->View();
            if(!view.valid() || snapshot->left!=120 || snapshot->top!=70 ||
               snapshot->generation<seenLast.load()){++bad;continue;}
            auto previous=seenLast.load();
            if(snapshot->generation>previous)seenLast.store(
                           static_cast<unsigned>(snapshot->generation));
            const auto color=snapshot->pixels.front();
            const auto size=static_cast<std::size_t>(view.width)*view.height*4;
            if(size!=snapshot->pixels.size() ||
               snapshot->pixels[size/2]!=color ||
               snapshot->pixels.back()!=color ||
               !view.valid())++bad;
            held=snapshot;
            ++checked;
            Sleep(0);
        }
        if(held && held->pixels.empty())++bad;
    });
    for(unsigned i=0;i<300;++i){
        if(WaitForSingleObject(mutex,100)!=WAIT_OBJECT_0)return 3;
        WriteFrame(bytes,(i%2)?48:32,(i%2)?28:24,
                   static_cast<std::uint8_t>((i%250)+1));
        ReleaseMutex(mutex);
        if(!mailbox.Capture())return 4;
        if(i%11==0)Sleep(1);
    }
    running.store(false,std::memory_order_release);
    reader.join();
    auto latest=mailbox.Latest();
    if(!latest || mailbox.successful!=300 || latest->generation!=300 ||
       checked.load()<5 || bad.load()!=0 || latest->left!=120 ||
       latest->top!=70)return 5;
    const auto before=latest->pixels.front();
    // Malformed header must not publish or retain old valid frames.
    if(WaitForSingleObject(mutex,100)!=WAIT_OBJECT_0)return 6;
    const std::int32_t oversized=8192;
    std::memcpy(bytes,&oversized,4);
    ReleaseMutex(mutex);
    if(mailbox.Capture() || mailbox.Latest() || mailbox.invalid!=1 ||
       latest->pixels.front()!=before)return 7;
    // A new mapping/buffer generation revokes the prior snapshot.
    mailbox.SetBuffer("Local\\nonexistent-MiniMixer-test-buffer");
    if(mailbox.Latest() || mailbox.Capture())return 8;
    mailbox.SetBuffer(mapName);
    if(WaitForSingleObject(mutex,100)!=WAIT_OBJECT_0)return 9;
    WriteFrame(bytes,32,24,77);
    ReleaseMutex(mutex);
    if(!mailbox.Capture() || !mailbox.Latest())return 10;
    // Abandonment must fail closed AND ReleaseMutex (Win32 ownership).
    HANDLE start=CreateEventA(nullptr,TRUE,FALSE,nullptr);
    std::thread abandon([&](){
        const DWORD state=WaitForSingleObject(mutex,1000);
        if(state!=WAIT_OBJECT_0){++bad;SetEvent(start);return;}
        SetEvent(start);
        // Deliberately abandon mutex: thread exits while owning it.
    });
    if(WaitForSingleObject(start,1200)!=WAIT_OBJECT_0)return 11;
    abandon.join();
    CloseHandle(start);
    if(mailbox.Capture() || mailbox.abandoned!=1)return 12;
    if(WaitForSingleObject(mutex,100)!=WAIT_OBJECT_0)return 13;
    ReleaseMutex(mutex);
    // Age-out must refuse to deliver a stale frame without a new Capture.
    Sleep(2050);
    if(mailbox.Latest())return 14;
    mailbox.Reset();
    UnmapViewOfFile(bytes);CloseHandle(mapping);CloseHandle(mutex);
    std::printf("PASS OWNED_WPF_MAILBOX produced=300 consumed=%u corruptRejected=1 staleRejected=1 abandonedRejected=1 bufferGenerationReset=1 latestImmutable=1\n",
                checked.load());
    return 0;
}
