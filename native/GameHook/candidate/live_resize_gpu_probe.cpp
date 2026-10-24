// Owned-process validation of D3D12 backbuffer release BEFORE actual
// IDXGISwapChain::ResizeBuffers, and renderer recreation afterward.
// No injected DLL, no foreign process, no global input interception.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <cstdio>
#include "live_repeat_marker.h"
namespace {
LRESULT CALLBACK Wnd(HWND h,UINT m,WPARAM w,LPARAM l) {
    return DefWindowProcW(h,m,w,l);
}
template <class T> void Drop(T*& v) {if(v){v->Release();v=nullptr;}}
}
int main() {
    const auto module=GetModuleHandleW(nullptr);
    WNDCLASSW wc{};
    wc.hInstance=module;wc.lpfnWndProc=Wnd;
    wc.lpszClassName=L"MiniMixerDXGIResizeLifecycleTest";
    if(!RegisterClassW(&wc))return 1;
    HWND hwnd=CreateWindowExW(0,wc.lpszClassName,L"Owned resize test",
               WS_POPUP,0,0,320,240,nullptr,nullptr,module,nullptr);
    if(!hwnd)return 2;
    ID3D12Device* device{};
    ID3D12CommandQueue* queue{};
    IDXGIFactory2* factory{};
    IDXGISwapChain1* base{};
    IDXGISwapChain3* chain{};
    HRESULT hr=D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,
                                  IID_PPV_ARGS(&device));
    if(FAILED(hr))return 3;
    D3D12_COMMAND_QUEUE_DESC qdesc{};
    qdesc.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
    hr=device->CreateCommandQueue(&qdesc,IID_PPV_ARGS(&queue));
    if(FAILED(hr))return 4;
    hr=CreateDXGIFactory2(0,IID_PPV_ARGS(&factory));
    if(FAILED(hr))return 5;
    DXGI_SWAP_CHAIN_DESC1 sd{};
    sd.Width=320;sd.Height=240;sd.BufferCount=2;
    sd.Format=DXGI_FORMAT_R10G10B10A2_UNORM;
    sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    hr=factory->CreateSwapChainForHwnd(queue,hwnd,&sd,nullptr,nullptr,&base);
    if(FAILED(hr))return 6;
    hr=base->QueryInterface(IID_PPV_ARGS(&chain));
    if(FAILED(hr))return 7;
    LiveRepeatMarker marker;
    if(!marker.Init(chain,queue,hwnd))return 8;
    int completedResizes=0;
    bool pendingFenceRejected=false;
    const auto resize=[&](UINT width,UINT height)->bool {
        if(completedResizes==0){
            // Force a real unsignalled D3D12 queue fence. While that work
            // remains outstanding, the resource owner MUST reject teardown.
            const DWORD previous=GetTickCount();
            while(!marker.CanRelease() && GetTickCount()-previous<1600)Sleep(5);
            if(!marker.CanRelease())return false;
            ID3D12Fence* gate{};
            if(FAILED(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                                         IID_PPV_ARGS(&gate))))return false;
            const HRESULT queued=queue->Wait(gate,1);
            bool enqueued=false;
            if(SUCCEEDED(queued)){
                enqueued=marker.Draw() && SUCCEEDED(chain->Present(0,0));
                pendingFenceRejected=enqueued && !marker.PrepareForResize();
            }
            const HRESULT unblocked=gate->Signal(1);
            Drop(gate);
            if(!enqueued || FAILED(unblocked) || !pendingFenceRejected){
                puts("FAIL pending GPU work was not protected");
                return false;
            }
        }
        // Exclusive lifecycle ownership: no concurrent Present callbacks.
        // A real injected integration must establish that independently.
        const DWORD start=GetTickCount();
        while(!marker.CanRelease() && GetTickCount()-start<1600)Sleep(5);
        if(!marker.CanRelease()){
            puts("FAIL pending fence prevents resize");
            return false;
        }
        if(!marker.PrepareForResize()){
            puts("FAIL outstanding swapchain references were not released");
            return false;
        }
        if(marker.ready() || marker.attempted()){
            puts("FAIL renderer not disarmed before DXGI resize");
            return false;
        }
        // The actual DXGI call, not a mocked/fake resize method.
        hr=chain->ResizeBuffers(2,width,height,DXGI_FORMAT_UNKNOWN,0);
        if(FAILED(hr)){
            std::printf("FAIL DXGI ResizeBuffers hr=0x%08X\n",
                        static_cast<unsigned>(hr));
            return false;
        }
        if(!SetWindowPos(hwnd,nullptr,0,0,static_cast<int>(width),
                         static_cast<int>(height),
                         SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOZORDER)){
            puts("FAIL owner geometry resize");
            return false;
        }
        if(!marker.Init(chain,queue,hwnd) ||
           marker.width()!=width || marker.height()!=height ||
           marker.bufferCount()!=2) {
            puts("FAIL reinitialized renderer geometry");
            return false;
        }
        ++completedResizes;
        std::printf("RESIZE_REAL_DXGI=%u to=%ux%u\n",
                    completedResizes,width,height);
        return true;
    };
    for(int i=0;i<420;++i){
        if(i==135 && !resize(448,300))return 9;
        if(i==285 && !resize(640,400))return 10;
        if(!marker.Draw() || FAILED(chain->Present(0,0))) {
            std::printf("FAIL render/present frame=%d\n",i);
            return 11;
        }
        Sleep(8);
    }
    const DWORD start=GetTickCount();
    while(!marker.CanRelease() && GetTickCount()-start<1600)Sleep(5);
    if(!marker.Cleanup()){
        puts("FAIL final GPU fence cleanup");
        return 12;
    }
    Drop(chain);Drop(base);Drop(factory);Drop(queue);Drop(device);
    DestroyWindow(hwnd);
    if(completedResizes!=2 || !pendingFenceRejected ||
       marker.submitted.load()!=421 ||
       marker.deviceErrors.load()!=0 || marker.resizeGuard.load()!=0){
        puts("FAIL incomplete resize sequence or GPU errors");
        return 13;
    }
    std::printf("PASS OWNED_D3D12_REAL_RESIZE frames=%u resizes=%d errors=%u inFlightReleaseDenied=%d fencedCleanup=1\n",
       marker.submitted.load(),completedResizes,marker.deviceErrors.load(),
       pendingFenceRejected?1:0);
    return 0;
}
