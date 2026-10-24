// Owned D3D11 swapchain + own-process LoadLibraryW hook-life test.
// NO external process injection, no user game, no audio action, no anti-cheat.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <cstdio>
#include <string>
#include <fstream>
#include <sstream>
#include "owned_qualified_target_fixture.h"

using Microsoft::WRL::ComPtr;
static LRESULT CALLBACK WindowProc(HWND w,UINT m,WPARAM a,LPARAM b){
    return DefWindowProcW(w,m,a,b);
}
using ReadCalls=unsigned (WINAPI*)();
static std::string ReadLog(DWORD pid){
    wchar_t temp[MAX_PATH]{},path[MAX_PATH]{};
    GetTempPathW(MAX_PATH,temp);
    swprintf_s(path,L"%sMiniMixerLivePresent_%lu.txt",temp,pid);
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|
                            FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,0,nullptr);
    if(file==INVALID_HANDLE_VALUE)return {};
    DWORD length=GetFileSize(file,nullptr);
    std::string content;
    if(length<256*1024){
        content.resize(length);
        DWORD read{};
        if(!ReadFile(file,content.data(),length,&read,nullptr))content.clear();
        else content.resize(read);
    }
    CloseHandle(file);
    return content;
}
static bool AwaitLog(DWORD pid,const char* text,DWORD milliseconds){
    const auto start=GetTickCount64();
    while(GetTickCount64()-start<milliseconds){
        if(ReadLog(pid).find(text)!=std::string::npos)return true;
        Sleep(50);
    }
    return false;
}
int wmain(int argc,wchar_t** argv){
    if(argc<2 || argc>3){puts("Usage: owned_d3d11_provider_hook_probe <own-built-dll> [--invalidate]");return 2;}
    const bool invalidate=argc==3 && wcscmp(argv[2],L"--invalidate")==0;
    WNDCLASSW wc{};wc.lpfnWndProc=WindowProc;wc.hInstance=GetModuleHandleW(nullptr);
    wc.lpszClassName=L"MiniMixerOwnedD3D11LiveProvider";
    if(!RegisterClassW(&wc))return 3;
    HWND window=CreateWindowW(wc.lpszClassName,L"Owned D3D11 Provider",
        WS_OVERLAPPEDWINDOW,0,0,256,256,nullptr,nullptr,wc.hInstance,nullptr);
    if(!window)return 4;
    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferDesc.Width=192;desc.BufferDesc.Height=192;
    desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount=2;desc.SampleDesc.Count=1;
    desc.OutputWindow=window;desc.Windowed=TRUE;
    desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    ComPtr<IDXGISwapChain> chain;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    auto hr=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&desc,
        chain.GetAddressOf(),device.GetAddressOf(),nullptr,context.GetAddressOf());
    if(FAILED(hr)){printf("FAIL owned WARP swapchain hr=0x%08X\n",(unsigned)hr);return 5;}
    HANDLE qualification=OwnedPublishQualifiedWindow(window,L"Owned D3D11 Provider");
    if(!qualification)return 16;
    HMODULE hook=LoadLibraryW(argv[1]); // ONLY own-process module
    if(!hook){printf("FAIL own-process LoadLibrary err=%lu\n",GetLastError());return 6;}
    auto calls=reinterpret_cast<ReadCalls>(GetProcAddress(
        hook,"MiniMixerProbeD3D11PresentCalls"));
    if(!calls){puts("FAIL D3D11 Present counter export missing");return 7;}
    DWORD pid=GetCurrentProcessId();
    if(!AwaitLog(pid,"REQUIRED_DXGI_HOOKS_READY",12000)){
        printf("FAIL DXGI hook not installed: %s\n",ReadLog(pid).c_str());
        return 8;
    }
    const unsigned baseline=calls();
    using BoundChain=UINT_PTR (WINAPI*)();
    auto bound=reinterpret_cast<BoundChain>(GetProcAddress(
        hook,"MiniMixerProbeBoundChain"));
    if(!bound)return 17;
    // A helper window/swapchain presents FIRST. It is not the exact
    // user-qualified HWND and must never be selected.
    HWND helper=CreateWindowW(wc.lpszClassName,L"Own unrelated helper",
        WS_OVERLAPPEDWINDOW,0,0,256,256,nullptr,nullptr,wc.hInstance,nullptr);
    if(!helper)return 18;
    ComPtr<IDXGIFactory> factory;
    if(FAILED(chain->GetParent(IID_PPV_ARGS(&factory))))return 19;
    DXGI_SWAP_CHAIN_DESC otherDesc=desc;
    otherDesc.OutputWindow=helper;
    ComPtr<IDXGISwapChain> other;
    if(FAILED(factory->CreateSwapChain(device.Get(),&otherDesc,
                                       other.GetAddressOf())))return 20;
    if(FAILED(other->Present(0,0)) || bound()!=0){
        puts("FAIL helper chain hijacked qualified target");return 21;
    }
    for(int i=0;i<80;++i){
        hr=chain->Present(0,0);
        if(FAILED(hr)){puts("FAIL game-analog original Present");return 9;}
        Sleep(2);
    }
    const unsigned captured=calls();
    if(bound()!=reinterpret_cast<UINT_PTR>(chain.Get())){
        puts("FAIL approved HWND not bound to correct chain");return 22;
    }
    if(captured<baseline+81){
        printf("FAIL live native D3D11 Present calls=%u baseline=%u\n",captured,baseline);
        return 10;
    }
    const std::string live=ReadLog(pid);
    // Background snapshots run on a 3s cadence; read again once after 3s.
    Sleep(3500);
    const std::string status=ReadLog(pid);
    if(status.find("GAME_D3D11_HR=0x00000000")==std::string::npos){
        printf("FAIL expected foreign D3D11 device not recognized: %s\n",status.c_str());
        return 11;
    }
    // Release old chain and create an eligible successor on the SAME HWND.
    // A replacement is accepted only after the old chain goes inactive.
    other.Reset();DestroyWindow(helper);
    context->ClearState();
    chain.Reset();
    Sleep(1700);
    ComPtr<IDXGISwapChain> successor;
    auto replacementHr=factory->CreateSwapChain(device.Get(),&desc,
                                               successor.GetAddressOf());
    if(FAILED(replacementHr)){
        printf("FAIL owned successor DXGI CreateSwapChain hr=%08X\\n",
               (unsigned)replacementHr);return 23;
    }
    if(FAILED(successor->Present(0,0)) ||
       bound()!=reinterpret_cast<UINT_PTR>(successor.Get())){
        puts("FAIL same-HWND successor not rebound");return 24;
    }
    bool revoked=true;
    using QualifiedWindow=UINT_PTR (WINAPI*)();
    auto qualified=reinterpret_cast<QualifiedWindow>(GetProcAddress(
        hook,"MiniMixerProbeQualifiedWindow"));
    if(!qualified)return 25;
    if(invalidate){
        DestroyWindow(window);
        // Present may itself fail on the now-invalid HWND; either outcome
        // must NOT re-enable a formerly qualified swapchain.
        (void)successor->Present(0,0);
        revoked=qualified()==0;
        if(!revoked){puts("FAIL cached HWND remains render-qualified");return 26;}
    }
    const unsigned capturedAfterSuccessor=calls();
    wchar_t name[90]{};
    swprintf_s(name,L"Local\\MiniMixerOverlayStop_%lu",pid);
    HANDLE stop=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);
    if(!stop || !SetEvent(stop)){puts("FAIL own-process Stop event");return 12;}
    CloseHandle(stop);
    if(!AwaitLog(pid,"UNHOOKED",12000)){
        printf("FAIL own native UNHOOKED: %s\n",ReadLog(pid).c_str());return 13;
    }
    for(int i=0;i<5;++i){
        const auto result=successor->Present(0,0);
        if(!invalidate && FAILED(result))return 14;
    }
    if(calls()!=capturedAfterSuccessor){
        printf("FAIL native Present still intercepted after Stop: %u vs %u\n",
               calls(),capturedAfterSuccessor);return 15;
    }
    printf("PASS OWN_D3D11_NATIVE_DLL target=own WARP calls=%u original_after_unhook=1"
           " D3D11_api=1 helper_denied=1 successor_rebound=1"
           " hwnd_revoked=%d hook_unhook=1 no_external_injection=1\n",
           captured-baseline,revoked?1:0);
    // DLL deliberately stays pinned until ordinary process exit.
    context.Reset();device.Reset();chain.Reset();
    DestroyWindow(window);
    return 0;
}
