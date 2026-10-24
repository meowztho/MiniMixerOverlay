// Owned-process integration: the native DXGI factory hook must associate
// the actual swapchain queue, never an unrelated DIRECT queue on same device.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <cstdio>
#include <string>
#include <vector>
#include "owned_qualified_target_fixture.h"
using Microsoft::WRL::ComPtr;
using Match=unsigned (WINAPI*)(IDXGISwapChain*,ID3D12CommandQueue*);
static LRESULT CALLBACK Window(HWND h,UINT m,WPARAM w,LPARAM l){
    return DefWindowProcW(h,m,w,l);
}
static std::string Trace(){
    wchar_t temp[MAX_PATH]{},path[MAX_PATH]{};
    GetTempPathW(MAX_PATH,temp);
    swprintf_s(path,L"%sMiniMixerLivePresent_%lu.txt",temp,GetCurrentProcessId());
    HANDLE f=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,
                         nullptr,OPEN_EXISTING,0,nullptr);
    if(f==INVALID_HANDLE_VALUE)return {};
    DWORD n=GetFileSize(f,nullptr);
    std::string s;
    if(n<65536){
        s.resize(n);
        DWORD read{};
        if(!ReadFile(f,s.data(),n,&read,nullptr))s.clear();
        else s.resize(read);
    }
    CloseHandle(f);
    return s;
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;
    WNDCLASSW wc{};
    wc.hInstance=GetModuleHandleW(nullptr);
    wc.lpfnWndProc=Window;
    wc.lpszClassName=L"MiniMixerOwnedQueueIdentity";
    if(!RegisterClassW(&wc))return 3;
    HWND a=CreateWindowW(wc.lpszClassName,L"Own DXGI test A",WS_OVERLAPPEDWINDOW,
                         0,0,320,320,nullptr,nullptr,wc.hInstance,nullptr);
    HWND b=CreateWindowW(wc.lpszClassName,L"Own DXGI test B",WS_OVERLAPPEDWINDOW,
                         0,0,320,320,nullptr,nullptr,wc.hInstance,nullptr);
    if(!a||!b)return 4;
    ComPtr<IDXGIFactory4> factory;
    ComPtr<IDXGIAdapter> warp;
    ComPtr<ID3D12Device> device;
    if(FAILED(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory)))||
       FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp)))||
       FAILED(D3D12CreateDevice(warp.Get(),D3D_FEATURE_LEVEL_11_0,
                                IID_PPV_ARGS(&device))))return 5;
    D3D12_COMMAND_QUEUE_DESC qDesc{};qDesc.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
    ComPtr<ID3D12CommandQueue> game,unrelated;
    if(FAILED(device->CreateCommandQueue(&qDesc,IID_PPV_ARGS(&game)))||
       FAILED(device->CreateCommandQueue(&qDesc,IID_PPV_ARGS(&unrelated))))return 6;
    HANDLE qualification=OwnedPublishQualifiedWindow(a,L"Own DXGI test A");
    if(!qualification)return 14;
    HMODULE module=LoadLibraryW(argv[1]); // never another process
    if(!module)return 7;
    auto match=reinterpret_cast<Match>(GetProcAddress(module,"MiniMixerProbeQueueMatches"));
    using ReadBound=UINT_PTR (WINAPI*)();
    auto bound=reinterpret_cast<ReadBound>(GetProcAddress(module,"MiniMixerProbeBoundChain"));
    using ReadPresent1=unsigned (WINAPI*)();
    auto present1Count=reinterpret_cast<ReadPresent1>(GetProcAddress(module,"MiniMixerProbePresent1Calls"));
    if(!match || !present1Count || !bound)return 8;
    bool ready=false;
    for(int i=0;i<240;++i){
        if(Trace().find("REQUIRED_DXGI_HOOKS_READY")!=std::string::npos){
            ready=true;break;
        }
        Sleep(50);
    }
    if(!ready){printf("FAIL hook ready: %s\n",Trace().c_str());return 9;}
    // Intentionally execute on unrelated first; this was the F-01 bug.
    unrelated->ExecuteCommandLists(0,nullptr);
    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width=256;desc.Height=256;
    desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count=1;desc.BufferCount=2;
    desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    ComPtr<IDXGISwapChain1> target,other;
    if(FAILED(factory->CreateSwapChainForHwnd(game.Get(),a,&desc,nullptr,nullptr,
                                              target.GetAddressOf())) ||
       FAILED(factory->CreateSwapChainForHwnd(unrelated.Get(),b,&desc,nullptr,nullptr,
                                              other.GetAddressOf())))return 10;
    const bool linked=match(target.Get(),game.Get())==1;
    const bool unrelatedRejected=match(target.Get(),unrelated.Get())==0;
    const bool isolated=match(other.Get(),unrelated.Get())==1 &&
                        match(other.Get(),game.Get())==0;
    // Helper presents first; it must never capture the approved game HWND.
    const bool helperPresent=SUCCEEDED(other->Present(0,0));
    const bool helperDenied=helperPresent&&bound()==0;
    const bool presentOk=helperPresent&&SUCCEEDED(target->Present(0,0));
    const bool selectedCorrectly=bound()==reinterpret_cast<UINT_PTR>(target.Get());
    DXGI_PRESENT_PARAMETERS presentParams{};
    const unsigned beforePresent1=present1Count();
    const bool present1Forwarded=SUCCEEDED(target->Present1(0,0,&presentParams)) &&
        present1Count()>beforePresent1;
    // A D3D12 ResizeBuffers1 can explicitly transfer presentation to
    // another DIRECT queue. The former association must not survive.
    ComPtr<IDXGISwapChain3> changeable;
    if(FAILED(target.As(&changeable)))return 13;
    const UINT nodeMask[2]={1,1};
    IUnknown* newQueues[2]={unrelated.Get(),unrelated.Get()};
    const HRESULT resizeResult=changeable->ResizeBuffers1(
        2,256,256,DXGI_FORMAT_UNKNOWN,0,nodeMask,newQueues);
    const bool rebound=SUCCEEDED(resizeResult) &&
                       match(target.Get(),game.Get())==0 &&
                       match(target.Get(),unrelated.Get())==1;
    printf("OWN_D3D12_FACTORY_QUEUES actualMatch=%d wrongQueueRejected=%d "
           "secondSwapchainIsolated=%d presentOk=%d resizeHr=%08X rebound=%d\n",
           linked,unrelatedRejected,isolated,presentOk,
           (unsigned)resizeResult,rebound);
    // A long-lived game can create many distinct auxiliary swapchains.
    // Previously the 17th unique chain was permanently denied solely
    // because a weak-pointer table was full. Every creation must now be
    // attributable to its actual queue, while an evicted old chain fails
    // closed rather than accidentally inheriting another queue.
    std::vector<HWND> extraWindows;
    std::vector<ComPtr<IDXGISwapChain1>> extras;
    bool churnPassed=true;
    for(int i=0;i<24;++i){
        HWND window=CreateWindowW(wc.lpszClassName,L"Own DXGI lifecycle extra",
                     WS_OVERLAPPEDWINDOW,0,0,320,320,nullptr,nullptr,
                     wc.hInstance,nullptr);
        if(!window){churnPassed=false;break;}
        extraWindows.push_back(window);
        ComPtr<IDXGISwapChain1> chain;
        const HRESULT hr=factory->CreateSwapChainForHwnd(game.Get(),window,
                                      &desc,nullptr,nullptr,chain.GetAddressOf());
        if(FAILED(hr) || !chain ||
           match(chain.Get(),game.Get())!=1 ||
           match(chain.Get(),unrelated.Get())!=0){
            churnPassed=false;break;
        }
        extras.push_back(chain);
    }
    const bool evictedOld=churnPassed && extras.size()==24 &&
        match(extras.front().Get(),game.Get())==0;
    const bool latestAlive=churnPassed && extras.size()==24 &&
        match(extras.back().Get(),game.Get())==1;
    printf("OWN_D3D12_ASSOCIATION_CHURN count=%zu newest_verified=%d "
           "evicted_old_fail_closed=%d passed=%d\n",
           extras.size(),latestAlive,evictedOld,churnPassed);
    wchar_t stopName[110]{};
    swprintf_s(stopName,L"Local\\MiniMixerOverlayStop_%lu",GetCurrentProcessId());
    HANDLE stop=OpenEventW(EVENT_MODIFY_STATE,FALSE,stopName);
    if(!stop||!SetEvent(stop))return 11;
    CloseHandle(stop);
    bool unhooked=false;
    for(int i=0;i<240;++i){
        if(Trace().find("UNHOOKED")!=std::string::npos){unhooked=true;break;}
        Sleep(50);
    }
    extras.clear();
    for(auto w:extraWindows)DestroyWindow(w);
    changeable.Reset();other.Reset();target.Reset();device.Reset();
    unrelated.Reset();game.Reset();factory.Reset();warp.Reset();
    DestroyWindow(b);DestroyWindow(a);
    if(linked&&unrelatedRejected&&isolated&&helperDenied&&selectedCorrectly&&presentOk&&present1Forwarded&&rebound&&churnPassed&&evictedOld&&latestAlive&&unhooked){
        puts("PASS OWN_D3D12_VERIFIED_QUEUE matching=1 wrong=0 independent=1 helper_denied=1 exact_target=1 present1=1 resize_rebound=1 bounded_churn=1 unhook=1");
        return 0;
    }
    return 12;
}
