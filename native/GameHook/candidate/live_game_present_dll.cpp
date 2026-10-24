// Experimental x64 DXGI/D3D12 overlay provider: NOT production.
// Intercepts Present/Execute to composite WPF frames; visible diagnostic
// pixels are reserved for explicitly instrumented marker builds.
// A prior game crash occurred inside a user32 DLL-hook callback after repeated
// builds/injections. Root cause unproven; use immutable DLL and one-run gating.
// No audio modification, global input capture, or game-specific offsets.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <MinHook.h>
#include "../../GraphicsHookCore/include/graphics_hook_core/minhook_registry.h"
#include "live_game_marker.h"
#include "live_repeat_marker.h"
#include "owned_dxgi_resize_lifecycle.h"
#include "live_frame_resume.h"
#include "d3d12_pbgra_compositor.h"
#include "d3d11_swapchain_compositor.h"
#include "verified_d3d12_swapchain_queues.h"
#include "qualified_game_window.h"
#include "live_wpf_ipc_client.h"
#include "live_overlay_marker_policy.h"
#include "native_lifetime_policy.h"
#include <atomic>
#include <cstdio>
#include <cwchar>
#include <cstring>
#include <cstdint>

// Built without this define in all regular and packaged NativePreview paths.
// Explicit diagnostic builds may retain their historic 120-frame marker test.
#if defined(MINIMIXER_VISIBLE_MARKER_DIAGNOSTIC_BUILD)
static constexpr bool kVisualMarkerDiagnostic=true;
#else
static constexpr bool kVisualMarkerDiagnostic=false;
#endif

using Present=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT);
using Present1=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain1*,UINT,UINT,const DXGI_PRESENT_PARAMETERS*);
using Execute=void (STDMETHODCALLTYPE*)(ID3D12CommandQueue*,UINT,ID3D12CommandList* const*);
using Resize=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*,UINT,UINT,UINT,DXGI_FORMAT,UINT);
using Resize1=HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain3*,UINT,UINT,UINT,DXGI_FORMAT,UINT,const UINT*,IUnknown* const*);
using CreateChain=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory*,IUnknown*,DXGI_SWAP_CHAIN_DESC*,IDXGISwapChain**);
using CreateChainHwnd=HRESULT (STDMETHODCALLTYPE*)(IDXGIFactory2*,IUnknown*,HWND,
    const DXGI_SWAP_CHAIN_DESC1*,const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*,IDXGIOutput*,IDXGISwapChain1**);
static graphics_hook_core::MinHookRegistry g_hooks;
static HMODULE g_module{};
static std::atomic<unsigned> g_present11{},g_present12{},g_present1_11{},g_present1_12{},g_inFlight{},g_executeCalls{};
struct InFlight {
    InFlight() noexcept {++g_inFlight;}
    ~InFlight() noexcept {--g_inFlight;}
};
static std::atomic<void*> g_foreignChain{nullptr};
static minimixer::QualifiedGameWindow g_qualifiedGame;
static ULONGLONG g_lastAuthorizedPresent{};
static std::atomic<unsigned> g_windowDenied{},g_chainRebindings{};

static std::atomic<HWND> g_foreignHwnd{nullptr};
static std::atomic<int> g_foreign11Hr{0x7fffffff},g_foreign12Hr{0x7fffffff},g_foreignQueueHr{0x7fffffff};
static IDXGISwapChain* g_dummy11{};
static IDXGISwapChain* g_dummy12{};
static Present g_orig11{},g_orig12{};
static Present1 g_orig1_11{},g_orig1_12{};
static Resize g_origResize11{},g_origResize12{};
static Resize1 g_origResize1_11{},g_origResize1_12{};
static std::atomic<unsigned> g_resizeCalls{},g_resize1Calls{},
    g_resizeBlocked{},g_resizePrepared{},g_resizeOriginalFailures{};

static Execute g_origExecute{};
static CreateChain g_origCreateChain{};
static CreateChainHwnd g_origCreateChainHwnd{};
static minimixer::VerifiedSwapchainQueues g_verifiedQueues;
static std::atomic<unsigned> g_unverifiedQueuePresents{},g_verifiedQueuePresents{};
static ID3D12CommandQueue* g_dummyQueue{};
// Read-only diagnostic for OWNED synthetic swapchains; game build never
// calls this export. In particular a same-device foreign queue is rejected.
extern "C" __declspec(dllexport) unsigned WINAPI
MiniMixerProbeQueueMatches(IDXGISwapChain* swapchain,
                           ID3D12CommandQueue* suppliedQueue) noexcept {
    ID3D12CommandQueue* verified=g_verifiedQueues.Lookup(swapchain);
    if(!verified)return 0;
    const unsigned matches=verified==suppliedQueue?1:0;
    verified->Release();
    return matches;
}
static LiveMarker g_marker;
static LiveRepeatMarker g_repeat;
static minimixer::D3d11SwapchainCompositor g_d3d11Composer;
// Bounded diagnostics: updated only during an initial D3D11 adapter setup,
// consumed by the existing low-frequency worker. No Present-path file I/O.
static std::atomic<unsigned> g_d3d11SwapEffect{},g_d3d11SwapFormat{},
    g_d3d11SwapWidth{},g_d3d11SwapHeight{},g_d3d11InitRejects{};
static minimixer::D3d12PbgraCompositor g_wpfComposer;
static minimixer::D3d12PbgraCompositor g_sphereComposer;
static minimixer::LiveWpfIpcClient g_wpfIpc;
static std::atomic<unsigned> g_wpfComposed{},g_wpfSkippedBounds{},
    g_wpfDrawErrors{},g_sphereComposed{},g_sphereSkippedBounds{},
    g_sphereDrawErrors{};
static HANDLE g_stopEvent{};
static std::atomic<bool> g_running{};
static std::atomic<bool> g_stopping{};
static std::atomic_flag g_rendererBusy=ATOMIC_FLAG_INIT;
struct TryExclusivePresent {
    const bool acquired;
    TryExclusivePresent() noexcept:
        acquired(!g_rendererBusy.test_and_set(std::memory_order_acquire)) {}
    ~TryExclusivePresent() noexcept {
        if(acquired)g_rendererBusy.clear(std::memory_order_release);
    }
};

static std::atomic<unsigned> g_hookEntryCalls{};
extern "C" __declspec(dllexport) unsigned WINAPI MiniMixerProbeHookCalls() noexcept {
    return g_hookEntryCalls.load(std::memory_order_relaxed);
}
// Read-only owned-process diagnostic; not an input or rendering control.
extern "C" __declspec(dllexport) unsigned WINAPI MiniMixerProbeD3D11PresentCalls() noexcept {
    return g_present11.load(std::memory_order_relaxed)+
           g_present1_11.load(std::memory_order_relaxed);
}

// Factory hooks provide a proven swapchain -> presentation queue identity.
static HRESULT STDMETHODCALLTYPE OnCreateChain(IDXGIFactory* factory,
    IUnknown* device,DXGI_SWAP_CHAIN_DESC* desc,IDXGISwapChain** chain) noexcept {
    InFlight active;
    if(!g_origCreateChain)return DXGI_ERROR_INVALID_CALL;
    const auto hr=g_origCreateChain(factory,device,desc,chain);
    if(SUCCEEDED(hr) && chain && *chain)
        g_verifiedQueues.Record(*chain,device);
    return hr;
}
static HRESULT STDMETHODCALLTYPE OnCreateChainHwnd(IDXGIFactory2* factory,
    IUnknown* device,HWND hwnd,const DXGI_SWAP_CHAIN_DESC1* desc,
    const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreen,IDXGIOutput* restrictOutput,
    IDXGISwapChain1** chain) noexcept {
    InFlight active;
    if(!g_origCreateChainHwnd)return DXGI_ERROR_INVALID_CALL;
    const auto hr=g_origCreateChainHwnd(factory,device,hwnd,desc,fullscreen,restrictOutput,chain);
    if(SUCCEEDED(hr) && chain && *chain)
        g_verifiedQueues.Record(*chain,device);
    return hr;
}

// The renderer gate is REQUIRED. Never promote the first arbitrary foreign
// Present. The host's exact HWND/PID/process-start/title is the only target.
static bool ObserveForeign(IDXGISwapChain* sc,std::atomic<unsigned>& counter) noexcept {
    if(sc==g_dummy11 || sc==g_dummy12)return false;
    ++counter;
    if(!g_running.load(std::memory_order_acquire) ||
       g_stopping.load(std::memory_order_acquire) ||
       (!g_qualifiedGame.Load() ||
        !g_qualifiedGame.Accepts(sc,
            sc!=g_foreignChain.load(std::memory_order_acquire)))){
        ++g_windowDenied;return false;
    }
    DXGI_SWAP_CHAIN_DESC desc{};
    if(FAILED(sc->GetDesc(&desc)))return false;
    ID3D11Device* dd11{};
    ID3D12Device* dd12{};
    ID3D12CommandQueue* q12{};
    const HRESULT hr11=sc->GetDevice(__uuidof(ID3D11Device),
                                     reinterpret_cast<void**>(&dd11));
    const HRESULT hr12=sc->GetDevice(__uuidof(ID3D12Device),
                                     reinterpret_cast<void**>(&dd12));
    const HRESULT hrq=sc->GetDevice(__uuidof(ID3D12CommandQueue),
                                   reinterpret_cast<void**>(&q12));
    if(dd11)dd11->Release();
    if(dd12)dd12->Release();
    if(q12)q12->Release();
    const bool supported=(hr11==S_OK &&
            minimixer::D3d11SwapchainCompositor::Supported(desc)) ||
         (hr12==S_OK && desc.BufferCount>=2 &&
          desc.BufferCount<=3 && desc.SampleDesc.Count==1 &&
          desc.BufferDesc.Width>=128 && desc.BufferDesc.Height>=120 &&
          (desc.BufferDesc.Format==DXGI_FORMAT_B8G8R8A8_UNORM ||
           desc.BufferDesc.Format==DXGI_FORMAT_R8G8B8A8_UNORM ||
           desc.BufferDesc.Format==DXGI_FORMAT_R10G10B10A2_UNORM));
    if(!supported){++g_windowDenied;return false;}
    const auto now=GetTickCount64();
    IDXGISwapChain* old=static_cast<IDXGISwapChain*>(
        g_foreignChain.load(std::memory_order_acquire));
    if(old && old!=sc){
        // Both swapchains might be alive concurrently (menus/cutscenes).
        // Never oscillate between them. Require old-chain inactivity,
        // identical qualified HWND, and complete GPU-idle release.
        if(now-g_lastAuthorizedPresent<1500){++g_windowDenied;return false;}
        if(!g_d3d11Composer.ReleaseForResize() ||
           !g_wpfComposer.WaitIdle(0) ||
           !g_sphereComposer.WaitIdle(0) ||
           !g_repeat.WaitForIdle(0) ||
           !g_wpfComposer.Cleanup() || !g_sphereComposer.Cleanup() ||
           (g_repeat.attempted() && !g_repeat.PrepareForResize()) ||
           (g_marker.tried.load() && !g_marker.PrepareForResize())){
            ++g_windowDenied;return false;
        }
        g_verifiedQueues.Forget(old);
        ++g_chainRebindings;
    }
    g_lastAuthorizedPresent=now;
    g_foreignHwnd.store(g_qualifiedGame.Hwnd(),std::memory_order_release);
    if(old!=sc){
        g_foreign11Hr.store(hr11);g_foreign12Hr.store(hr12);
        g_foreignQueueHr.store(hrq);
        g_foreignChain.store(sc,std::memory_order_release);
    }
    return true;
}
// Owned-process visibility only; no effect on rendering or permission.
extern "C" __declspec(dllexport) UINT_PTR WINAPI
MiniMixerProbeQualifiedWindow() noexcept {
    return reinterpret_cast<UINT_PTR>(
        g_foreignHwnd.load(std::memory_order_acquire));
}
extern "C" __declspec(dllexport) UINT_PTR WINAPI
MiniMixerProbeBoundChain() noexcept {
    return reinterpret_cast<UINT_PTR>(
        g_foreignChain.load(std::memory_order_acquire));
}
extern "C" __declspec(dllexport) unsigned WINAPI
MiniMixerProbePresent1Calls() noexcept {
    return g_present1_11.load(std::memory_order_relaxed)+
           g_present1_12.load(std::memory_order_relaxed);
}

// The same hook boundary owns both the one-shot readback marker and the
// repeated renderer's strong backbuffer COM references. It MUST be invoked
// BEFORE forwarding real DXGI ResizeBuffers / ResizeBuffers1. No background
// thread may concurrently inspect the markers without g_rendererBusy.
template<class Forward>
static HRESULT BeforeRealResize(IDXGISwapChain* sc,Forward&& original) noexcept {
    if(!g_running.load(std::memory_order_acquire) ||
       g_stopping.load(std::memory_order_acquire) ||
       sc!=g_foreignChain.load(std::memory_order_acquire))
        return original();
    TryExclusivePresent exclusive;
    if(!exclusive.acquired){
        ++g_resizeBlocked;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    // The D3D11 adapter also retains a backbuffer RTV. It must release
    // this reference on the same exclusive Resize boundary before the
    // game's original ResizeBuffers can run.
    if(g_d3d11Composer.Ready() && !g_d3d11Composer.ReleaseForResize()){
        ++g_resizeBlocked;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    // Without prior D3D12 rendering there are no D3D12 backbuffer refs.
    if(!g_marker.tried.load(std::memory_order_acquire) &&
       !g_repeat.attempted() && !g_wpfComposer.Ready() &&
       !g_sphereComposer.Ready())
        return original();
    OwnedResizeOutcome outcome=OwnedResizeOutcome::BlockedOnGpu;
    const HRESULT hr=WithOwnedBackbuffersReleased(
        g_marker,g_repeat,&g_wpfComposer,&g_sphereComposer,180,
        outcome,original);
    if(outcome!=OwnedResizeOutcome::ForwardedOriginal){
        ++g_resizeBlocked;
        return hr;
    }
    ++g_resizePrepared;
    if(FAILED(hr))++g_resizeOriginalFailures;
    // Both marker owners are disarmed and re-arm at the next Present.
    // Even after an original Resize failure, it may still be possible to
    // recover from the surviving previous swapchain (owned test verified).
    return hr;
}
static HRESULT STDMETHODCALLTYPE OnResize11(
    IDXGISwapChain* sc,UINT count,UINT width,UINT height,
    DXGI_FORMAT format,UINT flags) noexcept {
    InFlight inFlight;
    ++g_resizeCalls;
    if(!g_origResize11)return DXGI_ERROR_INVALID_CALL;
    return BeforeRealResize(sc,[&]() noexcept {
        return g_origResize11(sc,count,width,height,format,flags);
    });
}
static HRESULT STDMETHODCALLTYPE OnResize12(
    IDXGISwapChain* sc,UINT count,UINT width,UINT height,
    DXGI_FORMAT format,UINT flags) noexcept {
    InFlight inFlight;
    ++g_resizeCalls;
    if(!g_origResize12)return DXGI_ERROR_INVALID_CALL;
    return BeforeRealResize(sc,[&]() noexcept {
        return g_origResize12(sc,count,width,height,format,flags);
    });
}
// A successful ResizeBuffers1 may replace the queue supplied at creation.
// All per-buffer queue slots must agree; heterogeneous queues are not
// supported by this compositor and must never be guessed from Execute.
static void RebindVerifiedQueueAfterResize1(IDXGISwapChain3* sc,UINT count,
                                             IUnknown* const* queues) noexcept {
    if(!sc || !queues)return;
    UINT effective=count;
    if(effective==0){
        // DXGI ResizeBuffers1(BufferCount=0) keeps the old count. The
        // original has already returned S_OK, so query its actual count.
        DXGI_SWAP_CHAIN_DESC actual{};
        if(FAILED(sc->GetDesc(&actual)))return;
        effective=actual.BufferCount;
    }
    if(effective==0 || effective>16 || !queues[0])return;
    for(UINT i=1;i<effective;++i)
        if(!queues[i] || queues[i]!=queues[0])return;
    g_verifiedQueues.Record(sc,queues[0]);
}
static HRESULT STDMETHODCALLTYPE OnResize1_11(
    IDXGISwapChain3* sc,UINT count,UINT width,UINT height,
    DXGI_FORMAT format,UINT flags,const UINT* masks,
    IUnknown* const* queues) noexcept {
    InFlight inFlight;
    ++g_resize1Calls;
    if(!g_origResize1_11)return DXGI_ERROR_INVALID_CALL;
    g_verifiedQueues.Forget(sc);
    const HRESULT hr=BeforeRealResize(sc,[&]() noexcept {
        return g_origResize1_11(sc,count,width,height,format,flags,masks,queues);
    });
    if(SUCCEEDED(hr))RebindVerifiedQueueAfterResize1(sc,count,queues);
    return hr;
}
static HRESULT STDMETHODCALLTYPE OnResize1_12(
    IDXGISwapChain3* sc,UINT count,UINT width,UINT height,
    DXGI_FORMAT format,UINT flags,const UINT* masks,
    IUnknown* const* queues) noexcept {
    InFlight inFlight;
    ++g_resize1Calls;
    if(!g_origResize1_12)return DXGI_ERROR_INVALID_CALL;
    g_verifiedQueues.Forget(sc);
    const HRESULT hr=BeforeRealResize(sc,[&]() noexcept {
        return g_origResize1_12(sc,count,width,height,format,flags,masks,queues);
    });
    if(SUCCEEDED(hr))RebindVerifiedQueueAfterResize1(sc,count,queues);
    return hr;
}
// Called only under the one existing g_rendererBusy Present/Resize gate.
// This adapter consumes immutable frames from the same WPF/IPC owner as D3D12.
// Supported D3D11 discard/flip models only; others fail closed.
static void ComposeD3d11(IDXGISwapChain* sc) noexcept {
    HWND hwnd=g_foreignHwnd.load(std::memory_order_acquire);
    if(!hwnd)return;
    const auto main=g_wpfIpc.Latest();
    const auto sphere=g_wpfIpc.AuxiliaryLatest();
    if((!main || !main->View().valid()) &&
       (!sphere || !sphere->View().valid()))return;
    if(!g_d3d11Composer.Ready()){
        DXGI_SWAP_CHAIN_DESC descriptor{};
        if(SUCCEEDED(sc->GetDesc(&descriptor))){
            g_d3d11SwapEffect.store(static_cast<unsigned>(descriptor.SwapEffect));
            g_d3d11SwapFormat.store(static_cast<unsigned>(descriptor.BufferDesc.Format));
            g_d3d11SwapWidth.store(descriptor.BufferDesc.Width);
            g_d3d11SwapHeight.store(descriptor.BufferDesc.Height);
        }
        if(!g_d3d11Composer.Init(sc,hwnd)){
            ++g_d3d11InitRejects;
            return;
        }
    }
    const UINT sw=g_d3d11Composer.Width();
    const UINT sh=g_d3d11Composer.Height();
    RECT gameRect{};
    const bool boundsKnown=GetWindowRect(hwnd,&gameRect)!=0;
    g_wpfIpc.SetGameWindow(hwnd);

    auto compose=[&](const auto& shot,bool auxiliary) noexcept {
        if(!shot || !shot->View().valid())return;
        const UINT w=shot->width,h=shot->height;
        if(w>sw || h>sh)return;
        long x=36,y=48; // Same fallback as the D3D12 main overlay.
        if(boundsKnown){
            const long requestedX=static_cast<long>(shot->left)-gameRect.left;
            const long requestedY=static_cast<long>(shot->top)-gameRect.top;
            if(requestedX>=0 && requestedY>=0 &&
               static_cast<unsigned long>(requestedX)<=sw-w &&
               static_cast<unsigned long>(requestedY)<=sh-h){
                x=requestedX;y=requestedY;
            } else if(auxiliary)return; // sphere must never jump to fallback
        } else if(auxiliary)return;
        if(x<0 || y<0 || static_cast<unsigned long>(x)>sw-w ||
           static_cast<unsigned long>(y)>sh-h){
            if(auxiliary)++g_sphereSkippedBounds;
            else ++g_wpfSkippedBounds;
            return;
        }
        if(g_d3d11Composer.Draw(shot->View(),static_cast<UINT>(x),
                                static_cast<UINT>(y),auxiliary)){
            if(auxiliary)++g_sphereComposed;
            else ++g_wpfComposed;
        }else{
            if(auxiliary)++g_sphereDrawErrors;
            else ++g_wpfDrawErrors;
        }
    };
    compose(main,false);
    compose(sphere,true);
}
// One canonical D3D12 render path, regardless of which DXGI Present
// entrypoint is hooked. Caller owns g_rendererBusy (non-reentrant).
static void ComposeD3d12(IDXGISwapChain* sc) noexcept {
    if(g_running.load(std::memory_order_acquire) &&
       !g_stopping.load(std::memory_order_acquire) &&
       sc!=g_dummy11 && sc!=g_dummy12 &&
       sc==g_foreignChain.load(std::memory_order_acquire) &&
       g_foreign12Hr.load()==S_OK && g_foreignHwnd.load()!=nullptr){
        // DO NOT use the first unrelated ExecuteCommandLists queue.
        // Only the factory's queue-to-swapchain association is trusted.
        if(auto* q=g_verifiedQueues.Lookup(sc)){
            ++g_verifiedQueuePresents;
            g_wpfIpc.SetGameWindow(g_foreignHwnd.load(std::memory_order_acquire));
            // After any ResizeBuffers, reverify the GPU pixel on the
            // earliest fence-completed Present, without waiting for the
            // sleeping diagnostic worker's 30-second status snapshot.
            // Interactive previews never paint a debug rectangle, even on
            // the first frame or after IPC loss. Keep the old GPU-marker
            // admission only for explicitly instrumented diagnostic builds.
            auto snapshot=g_wpfIpc.Latest();
            auto sphere=g_wpfIpc.AuxiliaryLatest();
            const bool drawMarker=minimixer::ShowStartupDiagnostic(
                kVisualMarkerDiagnostic,g_repeat.submitted.load(),
                snapshot && snapshot->View().valid(),
                sphere && sphere->View().valid());
            if(!drawMarker || AdvanceVerifiedOneShotOnPresent(g_marker,sc,q)){
                // Existing swapchain-size/resize owner also provides the
                // bounded geometry for both independent WPF compositors.
                if(!g_repeat.attempted())
                    g_repeat.Init(sc,q,g_foreignHwnd.load());
                if(drawMarker)g_repeat.Draw();
                // Only immutable WPF snapshots cross into Present.
                // All WM_COPYDATA/mutex/host I/O stays on the IPC worker.
                if(snapshot && snapshot->View().valid()){
                    const UINT surfaceW=g_repeat.width();
                    const UINT surfaceH=g_repeat.height();
                    const auto w=snapshot->width,h=snapshot->height;
                    RECT gameRect{};
                    int left=36,top=48; // bounded fallback for off-screen host UI
                    if(GetWindowRect(g_foreignHwnd.load(),&gameRect)){
                        const long originX=static_cast<long>(snapshot->left)-gameRect.left;
                        const long originY=static_cast<long>(snapshot->top)-gameRect.top;
                        if(originX>=0&&originY>=0&&
                           static_cast<unsigned long>(originX)+w<=surfaceW&&
                           static_cast<unsigned long>(originY)+h<=surfaceH){
                            left=static_cast<int>(originX);
                            top=static_cast<int>(originY);
                        }
                    }
                    if(left>=0&&top>=0&&w<=surfaceW&&h<=surfaceH&&
                       static_cast<unsigned>(left)<=surfaceW-w&&
                       static_cast<unsigned>(top)<=surfaceH-h){
                        if(!g_wpfComposer.Ready())
                            g_wpfComposer.Init(sc,q,g_foreignHwnd.load());
                        if(g_wpfComposer.Ready()){
                            const auto prevSkipped=g_wpfComposer.skipped.load();
                            if(g_wpfComposer.Draw(snapshot->View(),
                                 static_cast<UINT>(left),static_cast<UINT>(top)))
                                ++g_wpfComposed;
                            else if(g_wpfComposer.skipped.load()==prevSkipped)
                                ++g_wpfDrawErrors;
                        }
                    } else ++g_wpfSkippedBounds;
                }
                // Independent WPF sphere, opt-in auxiliary surface. Its
                // exact screen position is preserved; never substitute a
                // fallback near the game edge when it is outside this HWND.
                if(sphere && sphere->View().valid()){
                    RECT bounds{};
                    const bool knownBounds=GetWindowRect(
                        g_foreignHwnd.load(),&bounds)!=0;
                    const UINT sw=g_repeat.width(),sh=g_repeat.height();
                    const long x=static_cast<long>(sphere->left)-bounds.left;
                    const long y=static_cast<long>(sphere->top)-bounds.top;
                    if(knownBounds&&x>=0&&y>=0&&
                       sphere->width<=sw&&sphere->height<=sh&&
                       static_cast<unsigned long>(x)<=sw-sphere->width&&
                       static_cast<unsigned long>(y)<=sh-sphere->height){
                        if(!g_sphereComposer.Ready())
                            g_sphereComposer.Init(sc,q,g_foreignHwnd.load());
                        if(g_sphereComposer.Ready()){
                            const auto skips=g_sphereComposer.skipped.load();
                            if(g_sphereComposer.Draw(sphere->View(),
                                 static_cast<UINT>(x),static_cast<UINT>(y)))
                                ++g_sphereComposed;
                            else if(g_sphereComposer.skipped.load()==skips)
                                ++g_sphereDrawErrors;
                        }
                    } else ++g_sphereSkippedBounds;
                }
            }
            q->Release();
        } else ++g_unverifiedQueuePresents;
    }
}
static HRESULT STDMETHODCALLTYPE OnPresent11(IDXGISwapChain* sc,UINT interval,UINT flags) noexcept {
    InFlight inFlight;
    // COM swapchains may receive overlapping Present calls. One render
    // owner at a time protects the cached allocators and init lifecycle.
    TryExclusivePresent exclusive;
    const bool authorized=exclusive.acquired && ObserveForeign(sc,g_present11);
    if(exclusive.acquired && !authorized &&
       sc==g_foreignChain.load(std::memory_order_acquire)){
        g_foreignHwnd.store(nullptr,std::memory_order_release);
        g_wpfIpc.SetGameWindow(nullptr);
    }
    if(authorized)ComposeD3d12(sc);
    // D3D11 is a separate API adapter, never driven through D3D12 queues.
    if(authorized && g_running.load(std::memory_order_acquire) &&
       !g_stopping.load(std::memory_order_acquire) &&
       sc==g_foreignChain.load(std::memory_order_acquire) &&
       g_foreign11Hr.load()==S_OK && g_foreign12Hr.load()!=S_OK)
        ComposeD3d11(sc);
    const HRESULT result=g_orig11(sc,interval,flags);
    if(exclusive.acquired && g_d3d11Composer.Ready())
        g_d3d11Composer.NotifyPresentResult(result);
    return result;
}
static HRESULT STDMETHODCALLTYPE OnPresent12(IDXGISwapChain* sc,UINT interval,UINT flags) noexcept {
    InFlight inFlight;
    // The D3D12-specific DXGI Present implementation can differ from the
    // D3D11 dummy hook target. Serialize its forwarding against Resize too.
    TryExclusivePresent exclusive;
    const bool authorized=exclusive.acquired && ObserveForeign(sc,g_present12);
    if(exclusive.acquired && !authorized &&
       sc==g_foreignChain.load(std::memory_order_acquire)){
        g_foreignHwnd.store(nullptr,std::memory_order_release);
        g_wpfIpc.SetGameWindow(nullptr);
    }
    if(authorized)ComposeD3d12(sc);
    return g_orig12(sc,interval,flags);
}
static HRESULT STDMETHODCALLTYPE OnPresent1_11(IDXGISwapChain1* sc,UINT interval,UINT flags,
                                                 const DXGI_PRESENT_PARAMETERS* params) noexcept {
    InFlight inFlight;
    TryExclusivePresent exclusive;
    const bool authorized=exclusive.acquired && ObserveForeign(sc,g_present1_11);
    if(exclusive.acquired && !authorized &&
       sc==g_foreignChain.load(std::memory_order_acquire)){
        g_foreignHwnd.store(nullptr,std::memory_order_release);
        g_wpfIpc.SetGameWindow(nullptr);
    }
    // This vtable address can be shared by D3D11 and D3D12 swapchains.
    if(authorized)ComposeD3d12(sc);
    if(authorized && g_running.load(std::memory_order_acquire) &&
       !g_stopping.load(std::memory_order_acquire) &&
       sc==g_foreignChain.load(std::memory_order_acquire) &&
       g_foreign11Hr.load()==S_OK && g_foreign12Hr.load()!=S_OK)
        ComposeD3d11(sc);
    const HRESULT result=g_orig1_11(sc,interval,flags,params);
    if(exclusive.acquired && g_d3d11Composer.Ready())
        g_d3d11Composer.NotifyPresentResult(result);
    return result;
}
static HRESULT STDMETHODCALLTYPE OnPresent1_12(IDXGISwapChain1* sc,UINT interval,UINT flags,
                                                 const DXGI_PRESENT_PARAMETERS* params) noexcept {
    InFlight inFlight;
    TryExclusivePresent exclusive;
    const bool authorized=exclusive.acquired && ObserveForeign(sc,g_present1_12);
    if(exclusive.acquired && !authorized &&
       sc==g_foreignChain.load(std::memory_order_acquire)){
        g_foreignHwnd.store(nullptr,std::memory_order_release);
        g_wpfIpc.SetGameWindow(nullptr);
    }
    if(authorized)ComposeD3d12(sc);
    return g_orig1_12(sc,interval,flags,params);
}

static void STDMETHODCALLTYPE OnExecute(ID3D12CommandQueue* q,UINT count,ID3D12CommandList* const* lists) noexcept {
    InFlight active;
    if(q!=g_dummyQueue){
        ++g_executeCalls;
    }
    g_origExecute(q,count,lists);
}
static void LogLine(const char* line) noexcept {
    wchar_t temp[MAX_PATH]{}, file[MAX_PATH]{};
    if(!GetTempPathW(MAX_PATH,temp))return;
    swprintf_s(file,L"%sMiniMixerLivePresent_%lu.txt",temp,GetCurrentProcessId());
    HANDLE h=CreateFileW(file,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,
                         nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(h==INVALID_HANDLE_VALUE)return;
    DWORD written{};
    WriteFile(h,line,static_cast<DWORD>(strlen(line)),&written,nullptr);
    CloseHandle(h);
}
static void Snapshot(const char* phase) noexcept {
    // The diagnostic thread must not read non-atomic dimensions during
    // concurrent Present/Resize. Skip this low-frequency status if busy.
    TryExclusivePresent exclusive;
    if(!exclusive.acquired)return;
    char s[1536]{};
    sprintf_s(s,"PID=%lu %s HOOKS=%d D3D11_PRESENT=%u D3D12_PRESENT=%u D3D11_PRESENT1=%u D3D12_PRESENT1=%u FOREIGN_HWND=%llu GAME_D3D11_HR=0x%08X GAME_D3D12_HR=0x%08X GAME_QUEUE_HR=0x%08X EXECUTE_CALLS=%u QUEUE_CAPTURED=%d QUEUE_VERIFIED_PRESENTS=%u QUEUE_DENIED_PRESENTS=%u VISUAL_MARKER_ENABLED=%d MARKER_STATUS=%d MARKER_PIXEL=0x%08X MARKER_FORMAT=%u MARKER_SIZE=%ux%u REPEAT_READY=%d REPEAT_FRAMES=%u REPEAT_SKIPPED=%u REPEAT_ERRORS=%u REPEAT_RESIZE=%u REPEAT_FENCE=%llu RESIZE_CALLS=%u RESIZE1_CALLS=%u RESIZE_DENIED=%u RESIZE_PREPARED=%u RESIZE_FAILURES=%u WPF_SNAPSHOTS=%u WPF_COMPOSED=%u WPF_COMPOSE_BOUNDS=%u WPF_COMPOSE_ERRORS=%u D3D11_MAIN=%u D3D11_SPHERE=%u D3D11_ERRORS=%u D3D11_SWAP_EFFECT=%u D3D11_SWAP_FORMAT=%u D3D11_SWAP_SIZE=%ux%u D3D11_INIT_DENIED=%u WPF_IPC_ACK=%u WPF_IPC_INIT=%u WPF_IPC_TIMEOUT=%u SPHERE_SNAPSHOTS=%u SPHERE_COMPOSED=%u SPHERE_OUTSIDE=%u SPHERE_ERRORS=%u INPUT_SAMPLES=%u INPUT_EMITTED=%u INPUT_SENT=%u INPUT_SEND_ERRORS=%u\n",
              GetCurrentProcessId(),phase,g_hooks.Count(),
              g_present11.load(),g_present12.load(),g_present1_11.load(),g_present1_12.load(),
              static_cast<unsigned long long>(reinterpret_cast<UINT_PTR>(g_foreignHwnd.load())),
              static_cast<unsigned>(g_foreign11Hr.load()),static_cast<unsigned>(g_foreign12Hr.load()),static_cast<unsigned>(g_foreignQueueHr.load()),
              g_executeCalls.load(),g_verifiedQueues.Has(static_cast<IDXGISwapChain*>(g_foreignChain.load()))?1:0,
              g_verifiedQueuePresents.load(),g_unverifiedQueuePresents.load(),
              kVisualMarkerDiagnostic?1:0,g_marker.result.load(),g_marker.observedPixel.load(),
              static_cast<unsigned>(g_marker.format),g_marker.width,g_marker.height,
              g_repeat.ready()?1:0,g_repeat.submitted.load(),g_repeat.skipped.load(),
              g_repeat.deviceErrors.load(),g_repeat.resizeGuard.load(),
              static_cast<unsigned long long>(g_repeat.fenceCompleted()),
              g_resizeCalls.load(),g_resize1Calls.load(),
              g_resizeBlocked.load(),g_resizePrepared.load(),
              g_resizeOriginalFailures.load(),
              g_wpfIpc.Frames(),g_wpfComposed.load(),
              g_wpfSkippedBounds.load(),g_wpfDrawErrors.load(),
              g_d3d11Composer.mainFrames,g_d3d11Composer.sphereFrames,
              g_d3d11Composer.errors,g_d3d11SwapEffect.load(),
              g_d3d11SwapFormat.load(),g_d3d11SwapWidth.load(),
              g_d3d11SwapHeight.load(),g_d3d11InitRejects.load(),
              g_wpfIpc.acks.load(),g_wpfIpc.inits.load(),
              g_wpfIpc.helloTimeouts.load(),
              g_wpfIpc.AuxiliaryFrames(),g_sphereComposed.load(),
              g_sphereSkippedBounds.load(),g_sphereDrawErrors.load(),
              g_wpfIpc.inputSamples.load(),g_wpfIpc.inputEmitted.load(),
              g_wpfIpc.inputSent.load(),g_wpfIpc.inputRejected.load());
    LogLine(s);
}
static LRESULT CALLBACK ProbeWindowProc(HWND h,UINT m,WPARAM w,LPARAM l){
    return DefWindowProcW(h,m,w,l);
}

static void Install(void* target,void* replacement,void** trampoline) noexcept {
    (void)g_hooks.Install(target,replacement,trampoline);
}
static DWORD WINAPI DiagnosticThread(void*) noexcept {
    // Pin the DLL until *all* inline hooks have been disabled.
    HMODULE pinned{};
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
         reinterpret_cast<LPCWSTR>(&DiagnosticThread),&pinned))return 1;
    // Game identity and anti-cheat eligibility belong to the host's
    // GameHookBridge/native loader. A graphics provider must not hardcode
    // one game's executable name; owned-process tests use the same DLL.
    LogLine("NATIVE_PROVIDER_START\n");
    if(!g_qualifiedGame.Load())
        LogLine("QUALIFIED_TARGET_UNAVAILABLE_NO_RENDER\n");
    WNDCLASSW wc{};wc.hInstance=g_module;wc.lpfnWndProc=ProbeWindowProc;
    wc.lpszClassName=L"MiniMixerTransientSwapchainProbe";
    RegisterClassW(&wc);
    HWND dummyWindow=CreateWindowExW(0,wc.lpszClassName,L"Owned DXGI Target",
           WS_POPUP,0,0,64,64,nullptr,nullptr,wc.hInstance,nullptr);

    IDXGISwapChain* sc11{};
    IDXGISwapChain1* sc11Extended{};
    IDXGISwapChain3* sc11Extended3{};
    ID3D11Device* d11{};
    ID3D11DeviceContext* context11{};
    DXGI_SWAP_CHAIN_DESC sd{};
    sd.BufferDesc.Width=64;sd.BufferDesc.Height=64;
    sd.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.BufferCount=2;sd.OutputWindow=dummyWindow;sd.Windowed=TRUE;
    sd.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    HRESULT hr11=dummyWindow?D3D11CreateDeviceAndSwapChain(
       nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,
       D3D11_SDK_VERSION,&sd,&sc11,&d11,nullptr,&context11):E_FAIL;
    if(FAILED(hr11) && dummyWindow)
       hr11=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_WARP,
             nullptr,0,nullptr,0,D3D11_SDK_VERSION,&sd,
             &sc11,&d11,nullptr,&context11);
    g_dummy11=sc11;
    if(sc11){
        sc11->QueryInterface(IID_PPV_ARGS(&sc11Extended));
        // Fallback probe target when dummy D3D12 swapchain creation is
        // denied by the game process environment (seen previously).
        sc11->QueryInterface(IID_PPV_ARGS(&sc11Extended3));
    }

    IDXGIFactory2* factory{};
    // Factory creation is independent of whether D3D12 dummy-device
    // setup succeeds in the target process. This enables queue tracing
    // when the DLL loads before an actual target creates its swapchain.
    const HRESULT factoryHr=CreateDXGIFactory2(0,IID_PPV_ARGS(&factory));
    ID3D12Device* d12{};
    ID3D12CommandQueue* queue{};
    IDXGISwapChain1* sc12{};
    IDXGISwapChain3* sc12Extended{};
    HRESULT hr12=D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,
                                   IID_PPV_ARGS(&d12));
    if(SUCCEEDED(hr12)){
        D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
        hr12=d12->CreateCommandQueue(&q,IID_PPV_ARGS(&queue));
    }
    if(SUCCEEDED(hr12) && SUCCEEDED(factoryHr)){
        DXGI_SWAP_CHAIN_DESC1 d{};
        d.Width=64;d.Height=64;d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        d.SampleDesc.Count=1;d.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        d.BufferCount=2;d.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
        hr12=factory->CreateSwapChainForHwnd(queue,dummyWindow,&d,nullptr,nullptr,&sc12);
    }
    g_dummy12=sc12;
    if(sc12)sc12->QueryInterface(IID_PPV_ARGS(&sc12Extended));
    g_dummyQueue=queue;
    char creation[180]{};
    sprintf_s(creation,"DUMMY11_HR=0x%08X DUMMY12_HR=0x%08X\n",
              static_cast<unsigned>(hr11),static_cast<unsigned>(hr12));
    LogLine(creation);

    bool hooksInitialized=g_hooks.Initialize();
    if(hooksInitialized){
        if(factory){
            void** fv=*reinterpret_cast<void***>(factory);
            // IDXGIFactory vtable 10, IDXGIFactory2 vtable 15.
            Install(fv[10],reinterpret_cast<void*>(&OnCreateChain),
                    reinterpret_cast<void**>(&g_origCreateChain));
            Install(fv[15],reinterpret_cast<void*>(&OnCreateChainHwnd),
                    reinterpret_cast<void**>(&g_origCreateChainHwnd));
        }
        if(queue){
            void** qv=*reinterpret_cast<void***>(queue);
            Install(qv[10],reinterpret_cast<void*>(&OnExecute),
                    reinterpret_cast<void**>(&g_origExecute));
        }
        if(sc11){
            void** v=*reinterpret_cast<void***>(sc11);
            Install(v[8],reinterpret_cast<void*>(&OnPresent11),
                    reinterpret_cast<void**>(&g_orig11));
            Install(v[13],reinterpret_cast<void*>(&OnResize11),
                    reinterpret_cast<void**>(&g_origResize11));
            if(sc11Extended3){
                void** v3=*reinterpret_cast<void***>(sc11Extended3);
                Install(v3[39],reinterpret_cast<void*>(&OnResize1_11),
                        reinterpret_cast<void**>(&g_origResize1_11));
            }
            if(sc11Extended){
                void** v1=*reinterpret_cast<void***>(sc11Extended);
                Install(v1[22],reinterpret_cast<void*>(&OnPresent1_11),
                        reinterpret_cast<void**>(&g_orig1_11));
            }
        }
        if(sc12){
            void** v=*reinterpret_cast<void***>(sc12);
            Install(v[8],reinterpret_cast<void*>(&OnPresent12),
                    reinterpret_cast<void**>(&g_orig12));
            Install(v[13],reinterpret_cast<void*>(&OnResize12),
                    reinterpret_cast<void**>(&g_origResize12));
            Install(v[22],reinterpret_cast<void*>(&OnPresent1_12),
                    reinterpret_cast<void**>(&g_orig1_12));
            if(sc12Extended){
                void** v3=*reinterpret_cast<void***>(sc12Extended);
                Install(v3[39],reinterpret_cast<void*>(&OnResize1_12),
                        reinterpret_cast<void**>(&g_origResize1_12));
            }
        }
    }
    // Manual-reset named stop event. External controller can request
    // shutdown, but the Render/USER32 DLL stays resident until game exit.
    wchar_t name[110]{};
    swprintf_s(name,L"Local\\MiniMixerOverlayStop_%lu",GetCurrentProcessId());
    g_stopEvent=CreateEventW(nullptr,TRUE,FALSE,name);
    // D3D11 requires Present and Resize, not the unrelated D3D12 Execute
    // callback. A missing D3D12 device must not disable a valid D3D11 path.
    // D3D12 composition additionally requires a captured command queue.
    const bool d3d11HookReady=g_orig11 && g_origResize11;
    const bool d3d12HookReady=g_orig12 && g_origResize12 &&
        (g_origCreateChain || g_origCreateChainHwnd);
    const bool requiredHooks=d3d11HookReady || d3d12HookReady;
    if(!requiredHooks){
        LogLine("REQUIRED_DXGI_HOOKS_MISSING_AUTO_STOP\n");
        if(g_stopEvent)SetEvent(g_stopEvent);
    }
    if(!g_stopEvent){
        LogLine("STOP_EVENT_CREATION_FAILED; HOOKS_NOT_PERMITTED\n");
    } else if(requiredHooks) {
        g_running.store(true,std::memory_order_release);
        LogLine("REQUIRED_DXGI_HOOKS_READY\n");
        if(!g_wpfIpc.Start("minimixer_graphics_v1",true))
            LogLine("OPTIONAL_WPF_IPC_WORKER_UNAVAILABLE\n");
    }
    Snapshot("INSTALLED");
    if(g_stopEvent){
        // Wait for manual stop with low-frequency diagnostics only.
        // The Present hook itself has no polling or logging.
        const ULONGLONG begin=GetTickCount64();
        minimixer::NativeLifetimePolicy lifetime;
        for(unsigned checkpoint=0;;++checkpoint){
            // Two initial snapshots to prove real frame progress, then one
            // small status line every 30 seconds (no per-frame logging).
            DWORD status=WaitForSingleObject(g_stopEvent,
                                             checkpoint<2 ? 3000 : 30000);
            // The GPU readback and resize both touch non-atomic fields.
            // Only the exclusive render lifecycle owner may inspect them.
            {
                TryExclusivePresent readbackOwner;
                if(readbackOwner.acquired)g_marker.CheckReadback();
            }
            if(status==WAIT_OBJECT_0){
                LogLine("STOP_SIGNAL_RECEIVED\n");
                break;
            }
            if(status!=WAIT_TIMEOUT){
                LogLine("STOP_WAIT_FAILED\n");
                break;
            }
            // Sample health every 30 s, but only persist a full telemetry
            // snapshot about every five minutes after two startup samples.
            if(checkpoint<2 || (checkpoint-2)%10==0)
                Snapshot("PERSISTENT_ACTIVE");
            // A healthy WPF overlay must not disappear after 15 minutes.
            // Monitor cumulative real render progress without a second poller
            // and without treating paused/minimized games as stalled.
            const std::uint64_t overlayFrames=kVisualMarkerDiagnostic
                ? static_cast<std::uint64_t>(g_repeat.submitted.load())
                : static_cast<std::uint64_t>(g_wpfComposed.load())+
                  static_cast<std::uint64_t>(g_sphereComposed.load());
            const std::uint64_t gamePresents=
                static_cast<std::uint64_t>(g_present11.load())+
                static_cast<std::uint64_t>(g_present12.load())+
                static_cast<std::uint64_t>(g_present1_11.load())+
                static_cast<std::uint64_t>(g_present1_12.load());
            const auto progress=lifetime.Observe(
                GetTickCount64()-begin,overlayFrames,gamePresents);
            if(g_repeat.failed() || g_repeat.resizeGuard.load() ||
               (kVisualMarkerDiagnostic &&
                g_marker.result.load(std::memory_order_acquire)<0)){
                LogLine("PERSISTENT_RENDER_GUARD_STOP\n");
                break;
            }
            if(progress==minimixer::NativeStopReason::MissingStartupFrames){
                LogLine("NO_WPF_PROGRESS_STARTUP_STOP\n");
                break;
            }
            if(progress==minimixer::NativeStopReason::ActiveRenderingWithoutOverlay){
                LogLine("ACTIVE_GAME_WITHOUT_WPF_PROGRESS_STOP\n");
                break;
            }
        }
    }
    g_stopping.store(true,std::memory_order_release);
    g_running.store(false,std::memory_order_release);
    const bool wpfWorkerStopped=g_wpfIpc.Stop(2500);
    if(!wpfWorkerStopped)
        LogLine("WPF_IPC_WORKER_DID_NOT_STOP_MODULE_PINNED\n");
    // A failed IPC worker stop must not prevent us from removing graphics
    // inline hooks. Keep the DLL pinned afterward if IPC is still active.
    bool removed=g_hooks.DisableAll();
    if(removed){
        for(int retry=0;retry<40 && g_inFlight.load()!=0;++retry)
            Sleep(25);
        if(g_inFlight.load()!=0)removed=false;
    }
    if(removed){
        removed=g_hooks.RemoveAll();
        if(removed && hooksInitialized && !g_hooks.Uninitialize())
            removed=false;
    }
    if(removed){
        g_marker.CheckReadback();
        // Only release retained backbuffers after the queue fence has
        // completed; never release live GPU allocations after timeout.
        for(int retry=0;retry<40 && !g_repeat.CanRelease();++retry)
            Sleep(25);
        if(!g_d3d11Composer.ReleaseForResize() ||
           !g_wpfComposer.WaitIdle(1000) ||
           !g_sphereComposer.WaitIdle(1000) ||
           !g_wpfComposer.Cleanup() || !g_sphereComposer.Cleanup() ||
           !g_repeat.Cleanup() ||
           !g_marker.Cleanup())removed=false;
    }
    if(!wpfWorkerStopped)removed=false;
    if(g_stopEvent){CloseHandle(g_stopEvent);g_stopEvent=nullptr;}
    if(removed){
        Snapshot("UNHOOKED");
        if(sc12Extended)sc12Extended->Release();
        if(sc12)sc12->Release();
        g_verifiedQueues.Clear();
        if(queue)queue->Release();
        if(factory)factory->Release();
        if(d12)d12->Release();
        if(sc11Extended3)sc11Extended3->Release();
        if(sc11Extended)sc11Extended->Release();
        if(sc11)sc11->Release();
        if(context11)context11->Release();
        if(d11)d11->Release();
        if(dummyWindow)DestroyWindow(dummyWindow);
        // Retain a strong DLL reference until normal process exit. Async
        // callbacks and stop handoff need a separately proven safe unload.
        LogLine("NATIVE_MODULE_PINNED_UNTIL_GAME_EXIT\n");
        return 0;
    }
    // Never unload an active hook handler; keep the extra module reference.
    LogLine("UNHOOK_FAILED_PINNED -- no unload attempted\n");
    return 2;
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){
        g_module=module;
        DisableThreadLibraryCalls(module);
        HANDLE thread=CreateThread(nullptr,0,DiagnosticThread,nullptr,0,nullptr);
        if(thread)CloseHandle(thread);
        else return FALSE;
    }
    return TRUE;
}
