#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cwchar>
#include "live_repeat_marker.h"
namespace{
LRESULT CALLBACK OwnedWndProc(HWND hwnd,UINT msg,WPARAM w,LPARAM l){
    return DefWindowProcW(hwnd,msg,w,l);
}
template<class T> void Release(T*&p){if(p){p->Release();p=nullptr;}}
}
int main(int argc,char** argv){
    if(argc==3 && std::strcmp(argv[1],"--stop")==0){
        const auto pid=std::strtoul(argv[2],nullptr,10);
        if(pid==0 || pid>0xffffffffu)return 20;
        wchar_t name[110]{};
        swprintf_s(name,L"Local\\MiniMixerOwnedMarkerStop_%lu",pid);
        HANDLE event=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);
        if(!event)return 21;
        const bool sent=SetEvent(event)!=FALSE;
        CloseHandle(event);
        std::printf("OWNED_STOP_SIGNAL pid=%lu result=%d\n",pid,sent?1:0);
        return sent?0:22;
    }
    const bool untilStop=argc==2 && std::strcmp(argv[1],"--until-stop")==0;
    if(argc>1 && !untilStop)return 23;
    WNDCLASSW wc{};wc.hInstance=GetModuleHandleW(nullptr);
    wc.lpfnWndProc=OwnedWndProc;wc.lpszClassName=L"MiniMixerLiveRepeatOwnTest";
    if(!RegisterClassW(&wc))return 1;
    HWND wnd=CreateWindowExW(0,wc.lpszClassName,L"owned GPU",
                             WS_POPUP,0,0,320,240,nullptr,nullptr,wc.hInstance,nullptr);
    if(!wnd)return 2;
    IDXGIFactory2* factory{};
    ID3D12Device* device{};
    ID3D12CommandQueue* queue{};
    IDXGISwapChain1* intermediate{};
    IDXGISwapChain3* chain{};
    HRESULT hr=D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device));
    if(FAILED(hr))return 3;
    D3D12_COMMAND_QUEUE_DESC cd{};cd.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
    hr=device->CreateCommandQueue(&cd,IID_PPV_ARGS(&queue));
    if(FAILED(hr))return 4;
    hr=CreateDXGIFactory2(0,IID_PPV_ARGS(&factory));
    if(FAILED(hr))return 5;
    DXGI_SWAP_CHAIN_DESC1 sd{};
    sd.Width=320;sd.Height=240;sd.BufferCount=2;
    sd.Format=DXGI_FORMAT_R10G10B10A2_UNORM;
    sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    hr=factory->CreateSwapChainForHwnd(queue,wnd,&sd,nullptr,nullptr,&intermediate);
    if(FAILED(hr))return 6;
    hr=intermediate->QueryInterface(IID_PPV_ARGS(&chain));
    if(FAILED(hr))return 7;
    LiveRepeatMarker renderer;
    if(!renderer.Init(chain,queue,wnd)){puts("FAIL init");return 8;}
    HANDLE event=nullptr;
    if(untilStop){
        wchar_t name[110]{};
        swprintf_s(name,L"Local\\MiniMixerOwnedMarkerStop_%lu",GetCurrentProcessId());
        event=CreateEventW(nullptr,TRUE,FALSE,name);
        if(!event){puts("FAIL owned stop event");return 13;}
        std::printf("OWNED_MARKER_READY pid=%lu\n",GetCurrentProcessId());
        std::fflush(stdout);
    }
    bool stopSeen=false;
    const DWORD begin=GetTickCount();
    for(int i=0;!untilStop ? i<420 : (GetTickCount()-begin)<20000;++i){
        if(event && WaitForSingleObject(event,0)==WAIT_OBJECT_0){
            stopSeen=true;
            break;
        }
        renderer.Draw();
        hr=chain->Present(0,0);
        if(FAILED(hr)){puts("FAIL present");return 9;}
        Sleep(10);
    }
    if(event){CloseHandle(event);event=nullptr;}
    if(untilStop && !stopSeen){puts("FAIL missing external stop");return 14;}
    if((!untilStop && (renderer.submitted<300 || renderer.submitted>420)) ||
       (untilStop && renderer.submitted<20) ||
       renderer.deviceErrors!=0 || renderer.resizeGuard!=0 ||
       renderer.bufferCount()!=2){
        std::printf("FAIL bounded draw submitted=%u skipped=%u errors=%u resize=%u\n",
        renderer.submitted.load(),renderer.skipped.load(),renderer.deviceErrors.load(),renderer.resizeGuard.load());
        return 10;
    }
    const DWORD begun=GetTickCount();
    while(!renderer.CanRelease() && GetTickCount()-begun<1500)Sleep(10);
    if(!renderer.Cleanup()){puts("FAIL fenced cleanup");return 11;}
    Release(chain);Release(intermediate);Release(queue);Release(device);Release(factory);
    DestroyWindow(wnd);
    std::printf("PASS LIVE_REPEAT_OWNED_D3D12 frames=%u skipped=%u errors=%u fencedCleanup=1 stopConfirmed=%d\n",
        renderer.submitted.load(),renderer.skipped.load(),renderer.deviceErrors.load(),stopSeen?1:0);
    return 0;
}
