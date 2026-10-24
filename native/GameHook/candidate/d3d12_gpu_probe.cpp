// Isolated D3D12 owned-swapchain GPU proof, no IPC and no injection.
#define MINIMIXER_D3D12_PROBE_HOOKS 1
#include "d3d12_owned_gpu_sink.h"
#include <thread>
#include <atomic>
using minimixer::OwnedD3d12GpuSink;
using minimixer::FrameView;
static LRESULT CALLBACK WindowProc(HWND h,UINT m,WPARAM w,LPARAM l){
    return m==WM_CLOSE?0:DefWindowProcW(h,m,w,l);
}
int main(int argc,char** argv) {
    bool warp=false,hook=false;
    for(int i=1;i<argc;++i){
        if(std::strcmp(argv[i],"--warp")==0)warp=true;
        else if(std::strcmp(argv[i],"--hook")==0)hook=true;
        else {puts("FAIL unknown test argument");return 1;}
    }
    const wchar_t* name=L"MiniMixerOwnedD3D12Probe";
    WNDCLASSW wc{};wc.hInstance=GetModuleHandleW(nullptr);
    wc.lpfnWndProc=WindowProc;wc.lpszClassName=name;
    RegisterClassW(&wc);
    HWND hwnd=CreateWindowExW(0,name,L"MiniMixer-owned-D3D12-test",
                             WS_OVERLAPPEDWINDOW,0,0,256,256,
                             nullptr,nullptr,wc.hInstance,nullptr);
    if(!hwnd){puts("FAIL window creation");return 2;}
    OwnedD3d12GpuSink probe;
    if(!probe.Init(hwnd,warp)){DestroyWindow(hwnd);return 3;}
    constexpr UINT first=64;
    std::vector<std::uint8_t> source(first*first*4);
    for(UINT y=0;y<first;++y)for(UINT x=0;x<first/2;++x){
        auto i=(static_cast<size_t>(y)*first+x)*4;
        source[i+1]=128;source[i+3]=128;
    }
    FrameView frame{source.data(),first,first,first*4};
    // Reuse unchanged pipeline and texture through three real GPU frames.
    if(!probe.Render(frame,hook) || !probe.Render(frame,hook)) {
        DestroyWindow(hwnd);return 4;
    }
    if(hook){
        std::atomic<bool> crossThreadDetached{false};
        std::thread foreign([&](){
            crossThreadDetached.store(probe.AttemptOffThreadDetachForTest());
        });
        foreign.join();
        if(crossThreadDetached || !probe.HookAttached()){
            puts("FAIL D3D12 off-thread detach was allowed");return 15;
        }
    }
    if(hook)probe.EnableNestedPresentDetachProbe();
    if(!probe.Render(frame,hook)){DestroyWindow(hwnd);return 4;}
    if(hook && (!probe.HookAttached() || !probe.NestedProbeSucceeded() ||
                probe.HookNestedSkipped()!=1 || probe.hookCallbacks!=3 ||
                probe.hookAttaches!=1 || probe.hookDetaches!=0)){
        puts("FAIL D3D12 nested Present, live Detach or callback lifecycle");
        DestroyWindow(hwnd);return 8;
    }
    if(!probe.Resize(96,96)){DestroyWindow(hwnd);return 5;}
    constexpr UINT second=96, padded=second*4+16;
    std::vector<std::uint8_t> newData(padded*second);
    for(UINT y=0;y<second;++y)for(UINT x=0;x<second/2;++x){
        auto i=static_cast<size_t>(y)*padded+x*4;
        newData[i+1]=92;newData[i+3]=128;
    }
    FrameView resized{newData.data(),second,second,padded};
    if(hook && (probe.HookAttached() || probe.hookDetaches!=1)) {
        puts("FAIL D3D12 detach before ResizeBuffers");return 9;
    }
    if(!probe.Render(resized,hook)){DestroyWindow(hwnd);return 6;}
    if(probe.gpuFrames!=4 || probe.gpuMatches!=4 || probe.gpuPresents!=4 ||
       probe.gpuResizes!=1 || probe.PipelineBuilds()!=1 ||
       probe.TextureBuilds()!=2 || probe.Submissions()!=4) {
        puts("FAIL D3D12 resource/present counters");return 7;
    }
    if(hook && (!probe.HookAttached() || probe.hookAttaches!=2 ||
                probe.hookCallbacks!=4 || probe.hookDetaches!=1)){
        puts("FAIL D3D12 hook reattach after resize");return 10;
    }
    std::printf("PASS D3D12_OWNED_GPU driver=%s graphicsShader=1 pbgraBlend=1 "
                "readback=1 present=%u submitted=%u resized=%u pipelineBuilds=%u "
                "textureBuilds=%u ownedPresentHook=%u hookCallbacks=%u hookAttaches=%u nestedSkipped=%u\n",
                probe.Driver(),probe.gpuPresents,
                probe.Submissions(),probe.gpuResizes,probe.PipelineBuilds(),probe.TextureBuilds(),
                hook?1:0,probe.hookCallbacks,probe.hookAttaches,probe.HookNestedSkipped());
    if(hook){
        // Synthetic DXGI failure result quarantine. A failed device must
        // never draw or Present again until explicit owner-controlled reset.
        probe.SimulateReportedDeviceLossForTest();
        if(!probe.QuarantinedForTest() || probe.Render(resized,hook) ||
           probe.gpuPresents!=4 || probe.hookCallbacks!=4){
            puts("FAIL D3D12 synthetic device quarantine");return 12;
        }
    }
    probe.Reset();
    if(hook && (probe.HookAttached() || probe.hookDetaches!=2)){
        puts("FAIL D3D12 final detach");return 11;
    }
    if(hook){
        // The test owns the HWND and queue, so it can create a fresh device
        // after teardown. This does NOT prove recovery from a real driver TDR.
        if(!probe.Init(hwnd,warp,96,96) || !probe.Render(resized,true) ||
           probe.gpuPresents!=5 || probe.PipelineBuilds()!=2 ||
           probe.hookAttaches!=3 || probe.hookCallbacks!=5){
            puts("FAIL D3D12 reset/rebuild after reported device failure");
            return 13;
        }
        probe.Reset();
        if(probe.HookAttached() || probe.hookDetaches!=3){
            puts("FAIL D3D12 recovery final detach");return 14;
        }
        puts("PASS D3D12 synthetic loss fails closed; new owned device recovers");
    }
    DestroyWindow(hwnd);
    return 0;
}
