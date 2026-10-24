// Independent GraphicsHookCore user, never references MiniMixer headers.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <cstdio>
#include <graphics_hook_core/d3d11_state_guard.h>
#include <graphics_hook_core/verified_d3d12_swapchain_queues.h>
#include <graphics_hook_core/native_lifetime_policy.h>
#include <graphics_hook_core/minhook_registry.h>
int main(){
    using namespace graphics_hook_core;
    NativeLifetimePolicy lifetime;
    if(lifetime.Observe(0,0,0)!=NativeStopReason::None ||
       lifetime.Observe(NativeLifetimePolicy::StartupGraceMs+1,0,0)!=NativeStopReason::MissingStartupFrames)return 2;
    VerifiedSwapchainQueues queues;
    if(queues.Has(nullptr) || queues.Lookup(nullptr)!=nullptr)return 3;
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    const D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_1,D3D_FEATURE_LEVEL_11_0};
    D3D_FEATURE_LEVEL got{};
    if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,
             levels,2,D3D11_SDK_VERSION,&device,&got,&context)))return 4;
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    { ScopedD3d11DrawState guard(context.Get());
      context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_POINTLIST); }
    D3D11_PRIMITIVE_TOPOLOGY after{};
    context->IAGetPrimitiveTopology(&after);
    if(after!=D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST)return 5;
    MinHookRegistry registry;
    if(registry.Count()!=0)return 6;
    puts("PASS GRAPHICS_HOOK_CORE_STANDALONE_CONSUMER WARP=1 RAII=1 QUEUE_FAIL_CLOSED=1 LIFETIME=1 MINHOOK_API=1");
    return 0;
}
