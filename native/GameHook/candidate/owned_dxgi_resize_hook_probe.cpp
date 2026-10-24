// OWN-PROCESS native DXGI ResizeBuffers vtable-hook integration test.
// Does not inject a DLL, does not touch any running game process.
// Only intercepts IDXGISwapChain::Present and ResizeBuffers for the owned test chain.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <MinHook.h>
#include <cstdio>
#include <cstdint>
#include <mutex>
#include <atomic>
#include <thread>
#include "live_repeat_marker.h"
#include "live_game_marker.h"
#include "owned_dxgi_resize_lifecycle.h"
#include "live_frame_resume.h"
#include "d3d12_pbgra_compositor.h"
#include <array>

namespace {
template<class T> void Drop(T*& p) noexcept {if(p){p->Release();p=nullptr;}}
LRESULT CALLBACK WndProc(HWND hwnd,UINT msg,WPARAM w,LPARAM l){
    return DefWindowProcW(hwnd,msg,w,l);
}
using RealResize=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
using RealPresent=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using RealResize1=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain3*,UINT,UINT,
                  UINT,DXGI_FORMAT,UINT,const UINT*,IUnknown* const*);

struct Context {
    IDXGISwapChain3* target{};
    ID3D12CommandQueue* queue{};
    HWND hwnd{};
    LiveRepeatMarker marker;
    LiveMarker first;
    minimixer::D3d12PbgraCompositor compositor,sphereCompositor;
    std::array<std::uint8_t,64*32*4> wpfPixels{};
    std::array<std::uint8_t,24*24*4> spherePixels{};
    std::mutex exclusive;
    std::atomic<unsigned> callbacks{0},resized{0},waitDenied{0},
                          foreignPass{0},originalFail{0},recoverFail{0},
                          exclusiveDenied{0},presentCallbacks{0},
                          presentDrawn{0},presentBypassed{0},
                          presentErrors{0},foreignPresent{0},
                          resize1Calls{0},firstPrepared{0},
                          firstVerified{0},bothOwnersFreed{0},
                          rearmRequested{0},rearmConfirmed{0},
                          rearmPresentCount{0},maxRearmPresents{0},
                          compositeDraws{0},allThreeReleased{0},
                          sphereDraws{0},allFourReleased{0};
};
static Context* g_context{};
static RealResize g_original{};
static RealPresent g_originalPresent{};
static RealResize1 g_originalResize1{};
HRESULT STDMETHODCALLTYPE OnPresent(IDXGISwapChain* sc,
                                    UINT sync,UINT flags) noexcept {
    auto* ctx=g_context;
    if(!ctx || !g_originalPresent)return DXGI_ERROR_INVALID_CALL;
    ++ctx->presentCallbacks;
    if(sc!=ctx->target){
        ++ctx->foreignPresent;
        return g_originalPresent(sc,sync,flags);
    }
    std::unique_lock<std::mutex> lock(ctx->exclusive,std::try_to_lock);
    if(!lock.owns_lock()){
        ++ctx->presentBypassed;
        return g_originalPresent(sc,sync,flags);
    }
    const bool verified=AdvanceVerifiedOneShotOnPresent(
        ctx->first,sc,ctx->queue);
    if(ctx->first.result.load(std::memory_order_acquire)<0)
        ++ctx->presentErrors;
    if(ctx->rearmRequested.load()!=ctx->rearmConfirmed.load()){
        const unsigned count=++ctx->rearmPresentCount;
        if(verified){
            ctx->rearmConfirmed.store(ctx->rearmRequested.load());
            if(count>ctx->maxRearmPresents.load())
                ctx->maxRearmPresents.store(count);
            ctx->rearmPresentCount.store(0);
        }
    }
    if(verified){
        ++ctx->firstVerified;
        if(!ctx->marker.Draw())++ctx->presentErrors;
        else ++ctx->presentDrawn;
        minimixer::FrameView wpf{ctx->wpfPixels.data(),64,32,64*4};
        if(!ctx->compositor.Draw(wpf,80,85))++ctx->presentErrors;
        else ++ctx->compositeDraws;
        minimixer::FrameView sphere{ctx->spherePixels.data(),24,24,24*4};
        if(!ctx->sphereCompositor.Draw(sphere,220,120))++ctx->presentErrors;
        else ++ctx->sphereDraws;
    }
    // Always return the game's original Present, even if overlay draw fails.
    return g_originalPresent(sc,sync,flags);
}
HRESULT STDMETHODCALLTYPE OnResize(IDXGISwapChain* sc,
                                    UINT count,UINT width,UINT height,
                                    DXGI_FORMAT format,UINT flags) noexcept {
    auto* ctx=g_context;
    if(!ctx || !g_original)return DXGI_ERROR_INVALID_CALL;
    ++ctx->callbacks;
    if(sc!=ctx->target) {
        ++ctx->foreignPass;
        return g_original(sc,count,width,height,format,flags);
    }
    std::unique_lock<std::mutex> lock(ctx->exclusive,std::try_to_lock);
    if(!lock.owns_lock()){
        ++ctx->exclusiveDenied;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    OwnedResizeOutcome outcome=OwnedResizeOutcome::BlockedOnGpu;
    const HRESULT hr=WithOwnedBackbuffersReleased(
        ctx->first,ctx->marker,&ctx->compositor,&ctx->sphereCompositor,
        180,outcome,[&]() noexcept {
            return g_original(sc,count,width,height,format,flags);
        });
    if(outcome!=OwnedResizeOutcome::ForwardedOriginal){
        ++ctx->waitDenied;
        return hr;
    }
    ++ctx->firstPrepared;
    if(!ctx->first.buffer && !ctx->marker.ready())++ctx->bothOwnersFreed;
    if(!ctx->first.buffer && !ctx->marker.ready() &&
       !ctx->compositor.Ready())++ctx->allThreeReleased;
    if(!ctx->first.buffer && !ctx->marker.ready() &&
       !ctx->compositor.Ready() && !ctx->sphereCompositor.Ready())
        ++ctx->allFourReleased;
    if(FAILED(hr))++ctx->originalFail;
    // Re-arm GPU readback on the next Present after either success or
    // failed original ResizeBuffers, never the sleeping diagnostics thread.
    if(!ctx->marker.Init(ctx->target,ctx->queue,ctx->hwnd) ||
       !ctx->compositor.Init(ctx->target,ctx->queue,ctx->hwnd) ||
       !ctx->sphereCompositor.Init(ctx->target,ctx->queue,ctx->hwnd))
        ++ctx->recoverFail;
    ++ctx->rearmRequested;
    ctx->rearmPresentCount.store(0);
    if(SUCCEEDED(hr))++ctx->resized;
    return hr;
}
// Independent method IDXGISwapChain3::ResizeBuffers1, same owners and
// ownership contract as ResizeBuffers, but a distinct DXGI ABI.
HRESULT STDMETHODCALLTYPE OnResize1(IDXGISwapChain3* sc,
    UINT count,UINT width,UINT height,DXGI_FORMAT format,UINT flags,
    const UINT* masks,IUnknown* const* queues) noexcept {
    auto* ctx=g_context;
    if(!ctx || !g_originalResize1)return DXGI_ERROR_INVALID_CALL;
    ++ctx->resize1Calls;
    if(sc!=ctx->target)
        return g_originalResize1(sc,count,width,height,format,flags,masks,queues);
    std::unique_lock<std::mutex> lock(ctx->exclusive,std::try_to_lock);
    if(!lock.owns_lock())return DXGI_ERROR_WAS_STILL_DRAWING;
    OwnedResizeOutcome outcome=OwnedResizeOutcome::BlockedOnGpu;
    const HRESULT hr=WithOwnedBackbuffersReleased(
        ctx->first,ctx->marker,&ctx->compositor,&ctx->sphereCompositor,
        180,outcome,[&]() noexcept {
            return g_originalResize1(sc,count,width,height,
                                     format,flags,masks,queues);
        });
    if(outcome!=OwnedResizeOutcome::ForwardedOriginal){
        ++ctx->waitDenied;
        return hr;
    }
    ++ctx->firstPrepared;
    if(!ctx->first.buffer && !ctx->marker.ready())++ctx->bothOwnersFreed;
    if(!ctx->first.buffer && !ctx->marker.ready() &&
       !ctx->compositor.Ready())++ctx->allThreeReleased;
    if(!ctx->first.buffer && !ctx->marker.ready() &&
       !ctx->compositor.Ready() && !ctx->sphereCompositor.Ready())
        ++ctx->allFourReleased;
    if(FAILED(hr))++ctx->originalFail;
    if(!ctx->marker.Init(ctx->target,ctx->queue,ctx->hwnd) ||
       !ctx->compositor.Init(ctx->target,ctx->queue,ctx->hwnd) ||
       !ctx->sphereCompositor.Init(ctx->target,ctx->queue,ctx->hwnd))
        ++ctx->recoverFail;
    ++ctx->rearmRequested;
    ctx->rearmPresentCount.store(0);
    if(SUCCEEDED(hr))++ctx->resized;
    return hr;
}
int Execute() {
    const HINSTANCE module=GetModuleHandleW(nullptr);
    WNDCLASSW wc{};wc.hInstance=module;wc.lpfnWndProc=WndProc;
    wc.lpszClassName=L"MiniMixerDXGIActualResizeHookTest";
    if(!RegisterClassW(&wc))return 1;
    HWND hwnd=CreateWindowExW(0,wc.lpszClassName,L"Owned DXGI Resize Hook",
                              WS_POPUP,0,0,320,240,nullptr,nullptr,module,nullptr);
    if(!hwnd)return 2;
    ID3D12Device* device{};
    ID3D12CommandQueue* queue{};
    IDXGIFactory2* factory{};
    IDXGISwapChain1* base{},*unrelatedBase{};
    IDXGISwapChain3* chain{};
    HWND unrelatedHwnd{};
    if(FAILED(D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,
                                IID_PPV_ARGS(&device))))return 3;
    D3D12_COMMAND_QUEUE_DESC qdesc{};
    qdesc.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
    if(FAILED(device->CreateCommandQueue(&qdesc,IID_PPV_ARGS(&queue))))return 4;
    if(FAILED(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory))))return 5;
    DXGI_SWAP_CHAIN_DESC1 sd{};
    sd.Width=320;sd.Height=240;sd.BufferCount=2;
    sd.Format=DXGI_FORMAT_R10G10B10A2_UNORM;
    sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    if(FAILED(factory->CreateSwapChainForHwnd(queue,hwnd,&sd,nullptr,nullptr,&base)))
        return 6;
    if(FAILED(base->QueryInterface(IID_PPV_ARGS(&chain))))return 7;
    Context ctx;
    ctx.hwnd=hwnd;ctx.target=chain;ctx.queue=queue;
    for(std::size_t i=0;i<ctx.wpfPixels.size();i+=4){
        ctx.wpfPixels[i+0]=0;ctx.wpfPixels[i+1]=150;
        ctx.wpfPixels[i+2]=0;ctx.wpfPixels[i+3]=200;
    }
    for(std::size_t i=0;i<ctx.spherePixels.size();i+=4){
        ctx.spherePixels[i+0]=12;ctx.spherePixels[i+1]=205;
        ctx.spherePixels[i+2]=18;ctx.spherePixels[i+3]=220;
    }
    if(!ctx.marker.Init(chain,queue,hwnd) ||
       !ctx.compositor.Init(chain,queue,hwnd) ||
       !ctx.sphereCompositor.Init(chain,queue,hwnd))return 8;
    // Real MinHook vtable target on this actual owned IDXGISwapChain3.
    void** methods=*reinterpret_cast<void***>(chain);
    void* originalSlot=methods[13]; // IDXGISwapChain::ResizeBuffers
    if(MH_Initialize()!=MH_OK)return 9;
    void* presentSlot=methods[8]; // IDXGISwapChain::Present
    void* resize1Slot=methods[39]; // IDXGISwapChain3::ResizeBuffers1
    if(MH_CreateHook(originalSlot,reinterpret_cast<void*>(&OnResize),
                     reinterpret_cast<void**>(&g_original))!=MH_OK)return 10;
    if(MH_CreateHook(presentSlot,reinterpret_cast<void*>(&OnPresent),
                     reinterpret_cast<void**>(&g_originalPresent))!=MH_OK)return 11;
    if(MH_CreateHook(resize1Slot,reinterpret_cast<void*>(&OnResize1),
                     reinterpret_cast<void**>(&g_originalResize1))!=MH_OK)return 35;
    g_context=&ctx;
    if(MH_QueueEnableHook(originalSlot)!=MH_OK ||
       MH_QueueEnableHook(presentSlot)!=MH_OK ||
       MH_QueueEnableHook(resize1Slot)!=MH_OK ||
       MH_ApplyQueued()!=MH_OK)return 31;
    const auto resize=[&](UINT width,UINT height,bool shouldWork)->bool {
        const HRESULT hr=chain->ResizeBuffers(2,width,height,DXGI_FORMAT_UNKNOWN,0);
        if(shouldWork && FAILED(hr)){
            std::printf("FAIL hooked original ResizeBuffers 0x%08X\n",
                        static_cast<unsigned>(hr));
            return false;
        }
        if(!shouldWork && SUCCEEDED(hr)){
            puts("FAIL resize unexpectedly accepted");
            return false;
        }
        if(shouldWork) {
            // Window geometry follows the real swapchain geometry.
            if(!SetWindowPos(hwnd,nullptr,0,0,static_cast<int>(width),
                             static_cast<int>(height),
                             SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOZORDER))
                return false;
            if(ctx.marker.width()!=width||ctx.marker.height()!=height||
               !ctx.marker.ready())return false;
        }
        return true;
    };
    unsigned negativePassed=0;
    unsigned singleShotPendingDenied=0;
    unsigned resizeCalls=0;
    bool foreignVerified=false;
    for(int i=0;i<422;++i){
        if(i==70){
            // Negative: the actual GPU queue is gated on an unsignalled
            // fence. The hook must reject resizing without freeing buffers.
            if(!ctx.marker.WaitForIdle(800))return 12;
            ID3D12Fence* gate{};
            if(FAILED(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                                         IID_PPV_ARGS(&gate))))return 13;
            const HRESULT gated=queue->Wait(gate,1);
            if(FAILED(gated))return 14;
            const unsigned beforeDraw=ctx.presentDrawn.load();
            const bool queued=SUCCEEDED(chain->Present(0,0)) &&
                              ctx.presentDrawn.load()==beforeDraw+1;
            const HRESULT refused=chain->ResizeBuffers(2,448,300,DXGI_FORMAT_UNKNOWN,0);
            if(queued&&refused==DXGI_ERROR_WAS_STILL_DRAWING&&
               ctx.marker.ready() && ctx.marker.width()==320 &&
               ctx.waitDenied.load()==1)
                ++negativePassed;
            const HRESULT release=gate->Signal(1);
            Drop(gate);
            if(FAILED(release) || negativePassed!=1){
                puts("FAIL hook released busy backbuffers");
                return 15;
            }
            if(!resize(448,300,true))return 16;
            ++resizeCalls;
        }
        if(i==170){
            // Invalid DXGI arguments must not permanently disable our marker.
            const UINT before=ctx.originalFail.load();
            const HRESULT rejected=chain->ResizeBuffers(
                100,999,999,DXGI_FORMAT_UNKNOWN,0);
            if(SUCCEEDED(rejected) ||
               ctx.originalFail.load()!=before+1 || !ctx.marker.ready()){
                puts("FAIL original ResizeBuffers failure recovery");
                return 17;
            }
        }
        if(i==205){
            // ACTUAL two-thread contention: another Windows OS thread holds
            // the exclusive lifecycle gate while this render thread invokes
            // the real hooked DXGI Present and ResizeBuffers functions.
            std::atomic<bool> holding{false},release{false};
            std::thread contender([&](){
                std::lock_guard<std::mutex> held(ctx.exclusive);
                holding.store(true,std::memory_order_release);
                while(!release.load(std::memory_order_acquire))Sleep(1);
            });
            const DWORD start=GetTickCount();
            while(!holding.load(std::memory_order_acquire) &&
                  GetTickCount()-start<1500)Sleep(1);
            const unsigned beforeBypass=ctx.presentBypassed.load();
            const bool ready=holding.load(std::memory_order_acquire);
            const HRESULT denied=ready?chain->ResizeBuffers(
                2,777,600,DXGI_FORMAT_UNKNOWN,0):E_FAIL;
            const HRESULT passthrough=ready?chain->Present(0,0):E_FAIL;
            release.store(true,std::memory_order_release);
            contender.join();
            if(denied!=DXGI_ERROR_WAS_STILL_DRAWING ||
               FAILED(passthrough) || ctx.exclusiveDenied.load()!=1 ||
               ctx.presentBypassed.load()!=beforeBypass+1 ||
               !ctx.marker.ready() || ctx.marker.width()!=448)return 33;
        }
        if(i==235){
            // A different swapchain must bypass our marker owner even when
            // the shared dxgi.dll implementation has been hooked globally.
            unrelatedHwnd=CreateWindowExW(0,wc.lpszClassName,L"unrelated swapchain",
                  WS_POPUP,0,0,240,180,nullptr,nullptr,module,nullptr);
            DXGI_SWAP_CHAIN_DESC1 other=sd;
            other.Width=240;other.Height=180;
            if(!unrelatedHwnd ||
               FAILED(factory->CreateSwapChainForHwnd(queue,unrelatedHwnd,
                              &other,nullptr,nullptr,&unrelatedBase)))
                return 18;
            const HRESULT hr=unrelatedBase->ResizeBuffers(
                2,260,200,DXGI_FORMAT_UNKNOWN,0);
            const HRESULT otherPresent=unrelatedBase->Present(0,0);
            foreignVerified=SUCCEEDED(hr) && SUCCEEDED(otherPresent) &&
                            ctx.foreignPass.load()>0 &&
                            ctx.foreignPresent.load()>0 &&
                            ctx.marker.ready() && ctx.marker.width()==448;
            if(!foreignVerified)return 19;
        }
        if(i==300){
            // Independent negative: the ONE-SHOT marker also owns a strong
            // backbuffer reference. Even with repeated-frames fence idle,
            // a pending one-shot readback must reject ResizeBuffers1.
            if(!ctx.marker.WaitForIdle(1000) ||
               !ctx.first.WaitForIdle(1000) ||
               !ctx.first.PrepareForResize())return 39;
            ID3D12Fence* gate{};
            if(FAILED(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                                         IID_PPV_ARGS(&gate))))return 40;
            if(FAILED(queue->Wait(gate,1))){
                Drop(gate);
                return 41;
            }
            const bool submitted=ctx.first.DrawOnce(chain,queue);
            IUnknown* queues[2]={queue,queue};
            const UINT nodeMasks[2]={1,1};
            const UINT deniedBefore=ctx.waitDenied.load();
            const HRESULT denied=chain->ResizeBuffers1(
                2,640,400,DXGI_FORMAT_UNKNOWN,0,nodeMasks,queues);
            if(submitted && denied==DXGI_ERROR_WAS_STILL_DRAWING &&
               ctx.waitDenied.load()==deniedBefore+1 && ctx.first.buffer &&
               ctx.marker.ready())
                ++singleShotPendingDenied;
            const HRESULT released=gate->Signal(1);
            Drop(gate);
            if(FAILED(released) || singleShotPendingDenied!=1)return 42;
            const HRESULT hr=chain->ResizeBuffers1(
                2,640,400,DXGI_FORMAT_UNKNOWN,0,nodeMasks,queues);
            if(FAILED(hr)){
                std::printf("FAIL real ResizeBuffers1 hr=0x%08X\n",
                            static_cast<unsigned>(hr));
                return 36;
            }
            if(!SetWindowPos(hwnd,nullptr,0,0,640,400,
                 SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOZORDER) ||
               ctx.marker.width()!=640 || ctx.marker.height()!=400 ||
               !ctx.marker.ready())return 37;
            ++resizeCalls;
        }
        if(FAILED(chain->Present(0,0)) || ctx.presentErrors.load()>0){
            std::printf("FAIL hooked D3D12 Present frame=%d\n",i);
            return 21;
        }
        Sleep(6);
    }
    // Detach while its callback is no longer active (owned single thread).
    if(MH_QueueDisableHook(originalSlot)!=MH_OK ||
       MH_QueueDisableHook(presentSlot)!=MH_OK ||
       MH_QueueDisableHook(resize1Slot)!=MH_OK ||
       MH_ApplyQueued()!=MH_OK)return 22;
    if(MH_RemoveHook(originalSlot)!=MH_OK ||
       MH_RemoveHook(presentSlot)!=MH_OK ||
       MH_RemoveHook(resize1Slot)!=MH_OK)return 23;
    if(MH_Uninitialize()!=MH_OK)return 24;
    g_context=nullptr;
    const auto callbacksBefore=ctx.callbacks.load();
    const auto presentBefore=ctx.presentCallbacks.load();
    if(!ctx.first.WaitForIdle(1100) || !ctx.marker.WaitForIdle(1100) ||
       !ctx.compositor.WaitIdle(1100) ||
       !ctx.sphereCompositor.WaitIdle(1100) ||
       !ctx.first.PrepareForResize() || !ctx.marker.PrepareForResize() ||
       !ctx.compositor.PrepareForResize() ||
       !ctx.sphereCompositor.PrepareForResize())
        return 25;
    if(FAILED(chain->ResizeBuffers(2,384,256,DXGI_FORMAT_UNKNOWN,0)))
        return 26;
    if(ctx.callbacks.load()!=callbacksBefore)return 27;
    if(ctx.presentCallbacks.load()!=presentBefore)return 34;
    if(!ctx.marker.Init(chain,queue,hwnd) ||
       !ctx.compositor.Init(chain,queue,hwnd) ||
       !ctx.sphereCompositor.Init(chain,queue,hwnd) ||
       !ctx.marker.Draw() ||
       !ctx.compositor.Draw({ctx.wpfPixels.data(),64,32,64*4},80,85) ||
       !ctx.sphereCompositor.Draw({ctx.spherePixels.data(),24,24,24*4},220,120) ||
       FAILED(chain->Present(0,0)))return 28;
    if(!ctx.marker.WaitForIdle(1100) || !ctx.compositor.WaitIdle(1100) ||
       !ctx.sphereCompositor.WaitIdle(1100) ||
       !ctx.marker.Cleanup() || !ctx.compositor.Cleanup() ||
       !ctx.sphereCompositor.Cleanup())return 29;
    if(!ctx.first.Cleanup())return 38;
    Drop(unrelatedBase);
    Drop(chain);Drop(base);Drop(factory);Drop(queue);Drop(device);
    if(unrelatedHwnd)DestroyWindow(unrelatedHwnd);
    DestroyWindow(hwnd);
    if(ctx.resized!=2 || resizeCalls!=2 ||ctx.originalFail!=1 ||
       ctx.recoverFail!=0 || ctx.waitDenied!=2||ctx.exclusiveDenied!=1||
       ctx.presentBypassed!=1||ctx.presentErrors!=0||
       ctx.presentDrawn<390||!foreignVerified ||
       ctx.resize1Calls!=2 || singleShotPendingDenied!=1 ||
       ctx.firstPrepared!=3 || ctx.bothOwnersFreed!=3 ||
       ctx.allThreeReleased!=3 ||ctx.allFourReleased!=3 ||
       ctx.compositeDraws<390 || ctx.sphereDraws<390 ||
       ctx.firstVerified.load()==0 ||
       ctx.rearmRequested!=3 || ctx.rearmConfirmed!=3 ||
       ctx.maxRearmPresents>60 ||
       ctx.marker.submitted!=ctx.presentDrawn.load()+1 ||
       ctx.marker.deviceErrors!=0||ctx.marker.resizeGuard!=0 ||
       ctx.compositor.errors!=0||ctx.sphereCompositor.errors!=0){
        puts("FAIL final hook lifecycle counters");
        return 30;
    }
    std::printf("PASS OWNED_DXGI_PRESENT_RESIZE_HOOK frames=%u presentDrawn=%u resizes=%u resize1=%u bothOwnersReleased=%u threeOwnersReleased=%u fourOwnersReleased=%u pbgraDraws=%u sphereDraws=%u firstPixelVerified=%u rearmCycles=%u maxRearmPresentCalls=%u pendingDenied=%u singleShotPendingDenied=%u concurrentDenied=%u originalFailures=%u recoverFailures=%u otherSwapchainForwarded=%u unhookRestored=1 cleanup=1\n",
       ctx.marker.submitted.load(),ctx.presentDrawn.load(),
       ctx.resized.load(),ctx.resize1Calls.load(),
       ctx.bothOwnersFreed.load(),ctx.allThreeReleased.load(),
       ctx.allFourReleased.load(),ctx.compositeDraws.load(),
       ctx.sphereDraws.load(),ctx.firstVerified.load(),
       ctx.rearmConfirmed.load(),ctx.maxRearmPresents.load(),
       ctx.waitDenied.load(),singleShotPendingDenied,
       ctx.exclusiveDenied.load(),
       ctx.originalFail.load(),ctx.recoverFail.load(),
       ctx.foreignPass.load());
    return 0;
}
}
int main(){return Execute();}
