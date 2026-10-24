// Four owned D3D11 flip-swapchain GPU/resize cases. No injection.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <array>
#include <vector>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include "d3d11_swapchain_compositor.h"
using Microsoft::WRL::ComPtr;
static LRESULT CALLBACK WindowProc(HWND w,UINT m,WPARAM a,LPARAM b){
    return DefWindowProcW(w,m,a,b);
}
static unsigned Green(const std::uint8_t* p,DXGI_FORMAT f){
    if(f==DXGI_FORMAT_R10G10B10A2_UNORM){
        std::uint32_t packed{};
        std::memcpy(&packed,p,sizeof(packed));
        return (packed>>10)&1023u;
    }
    return p[1];
}
static int Exercise(ID3D11Device* dev,ID3D11DeviceContext* ctx,
                    IDXGIFactory2* factory,DXGI_FORMAT format,
                    DXGI_SWAP_EFFECT effect,unsigned id){
    HWND window=CreateWindowW(L"MiniMixerOwnFlipAdapter",
        L"Owned D3D11 flip composition test",WS_OVERLAPPEDWINDOW,
        0,0,256,256,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(!window)return 2;
    DXGI_SWAP_CHAIN_DESC1 spec{};
    spec.Width=128;spec.Height=128;spec.Format=format;
    spec.BufferCount=2;spec.SampleDesc.Count=1;
    spec.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    spec.SwapEffect=effect;
    ComPtr<IDXGISwapChain1> v1;
    HRESULT hr=factory->CreateSwapChainForHwnd(
        dev,window,&spec,nullptr,nullptr,v1.GetAddressOf());
    if(FAILED(hr)){
        printf("FAIL create flip %u HRESULT=%08X\n",id,(unsigned)hr);
        DestroyWindow(window);return 3;
    }
    ComPtr<IDXGISwapChain3> flip;
    if(FAILED(v1.As(&flip)))return 4;
    minimixer::D3d11SwapchainCompositor composer;
    DXGI_SWAP_CHAIN_DESC described{};
    flip->GetDesc(&described);
    if(!minimixer::D3d11SwapchainCompositor::Supported(described) ||
       !composer.Init(flip.Get(),window)){
        for(unsigned i=0;i<2;++i){
          ComPtr<ID3D11Texture2D> t;
          const HRESULT lookup=flip->GetBuffer(i,IID_PPV_ARGS(&t));
          printf("BUFFER %u hr=%08X\\n",i,(unsigned)lookup);
        }
        printf("FAIL INIT case=%u swap=%u format=%u w=%u h=%u count=%u sample=%u quality=%u hwnd=%llu expected=%llu supported=%d\\n",
               id,(unsigned)described.SwapEffect,(unsigned)described.BufferDesc.Format,
               described.BufferDesc.Width,described.BufferDesc.Height,
               described.BufferCount,described.SampleDesc.Count,
               described.SampleDesc.Quality,
               (unsigned long long)(UINT_PTR)described.OutputWindow,
               (unsigned long long)(UINT_PTR)window,
               minimixer::D3d11SwapchainCompositor::Supported(described)?1:0);
        return 5;
    }
    std::vector<std::uint8_t> mainPixels(24*24*4),spherePixels(24*24*4);
    for(unsigned y=0;y<24;++y)for(unsigned x=0;x<12;++x){
        const unsigned idx=4*(y*24+x);
        mainPixels[idx+1]=128;mainPixels[idx+3]=128;
        spherePixels[idx+1]=128;spherePixels[idx+3]=128;
    }
    minimixer::FrameView main{mainPixels.data(),24,24,24*4};
    minimixer::FrameView sphere{spherePixels.data(),24,24,24*4};
    std::array<bool,2> seen{};
    for(int frame=0;frame<5;++frame){
        const UINT idx=flip->GetCurrentBackBufferIndex();
        if(idx>=seen.size())return 6;
        seen[idx]=true;
        ComPtr<ID3D11Texture2D> back;
        // D3D11 flip exposes its logical writable backbuffer at index 0.
        if(FAILED(flip->GetBuffer(0,IID_PPV_ARGS(&back))))return 7;
        ComPtr<ID3D11RenderTargetView> gameTarget;
        if(FAILED(dev->CreateRenderTargetView(back.Get(),nullptr,
                                            gameTarget.GetAddressOf())))return 8;
        const float black[4]={0,0,0,1};
        ctx->ClearRenderTargetView(gameTarget.Get(),black);
        ID3D11RenderTargetView* prior=gameTarget.Get();
        ctx->OMSetRenderTargets(1,&prior,nullptr);
        if(!composer.Draw(main,10,15,false) ||
           !composer.Draw(sphere,70,70,true))return 9;
        ComPtr<ID3D11RenderTargetView> restored;
        ctx->OMGetRenderTargets(1,restored.GetAddressOf(),nullptr);
        if(restored.Get()!=gameTarget.Get()){
            puts("FAIL graphics OM state lost");return 10;
        }
        restored.Reset();
        D3D11_TEXTURE2D_DESC stagingDesc{};
        back->GetDesc(&stagingDesc);
        stagingDesc.Usage=D3D11_USAGE_STAGING;
        stagingDesc.BindFlags=0;stagingDesc.MiscFlags=0;
        stagingDesc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;
        if(FAILED(dev->CreateTexture2D(&stagingDesc,nullptr,
                                       staging.GetAddressOf())))return 11;
        ctx->CopyResource(staging.Get(),back.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(ctx->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)))return 12;
        const auto* base=static_cast<const std::uint8_t*>(mapped.pData);
        const auto pixel=[&](UINT x,UINT y){
            return Green(base+static_cast<size_t>(y)*mapped.RowPitch+x*4,format);
        };
        const unsigned mainGreen=pixel(13,22);
        const unsigned sphereGreen=pixel(73,78);
        const unsigned outsideGreen=pixel(45,50);
        ctx->Unmap(staging.Get(),0);
        const unsigned expected=format==DXGI_FORMAT_R10G10B10A2_UNORM?512:128;
        const unsigned tolerance=format==DXGI_FORMAT_R10G10B10A2_UNORM?18:5;
        if(mainGreen+ tolerance<expected || mainGreen>expected+tolerance ||
           sphereGreen+tolerance<expected || sphereGreen>expected+tolerance ||
           outsideGreen>3){
            printf("FAIL pixel case=%u idx=%u main=%u sphere=%u outside=%u expect=%u\n",
                   id,idx,mainGreen,sphereGreen,outsideGreen,expected);
            return 13;
        }
        ctx->OMSetRenderTargets(0,nullptr,nullptr);
        gameTarget.Reset();back.Reset();staging.Reset();
        hr=flip->Present(0,0);
        if(FAILED(hr)){
            printf("FAIL own flip Present case=%u hr=%08X\n",id,(unsigned)hr);
            return 14;
        }
        composer.NotifyPresentResult(hr);
    }
    // D3D11 exposes buffer 0 as the logical render surface; unlike D3D12,
    // no rotating physical buffer index is required or guaranteed here.
    if(!seen[0]||composer.mainFrames!=5||composer.sphereFrames!=5)
        return 15;
    if(!composer.ReleaseForResize())return 16;
    ctx->OMSetRenderTargets(0,nullptr,nullptr);
    ctx->Flush();
    hr=flip->ResizeBuffers(2,160,160,DXGI_FORMAT_UNKNOWN,0);
    if(FAILED(hr)){
        printf("FAIL flip Resize case=%u hr=%08X\n",id,(unsigned)hr);
        return 17;
    }
    if(!composer.Init(flip.Get(),window) ||
       composer.Width()!=160 || composer.Height()!=160 ||
       !composer.Draw(main,12,18,false) ||
       !composer.Draw(sphere,80,80,true) ||
       !composer.ReleaseForResize())return 18;
    ctx->OMSetRenderTargets(0,nullptr,nullptr);
    flip.Reset();v1.Reset();
    DestroyWindow(window);
    printf("PASS OWN_FLIP_GPU case=%u format=%u effect=%u logicalBuffer0=1 bothLayers=1 alpha=1 outside=1 state=1 resize=1\n",
           id,(unsigned)format,(unsigned)effect);
    return 0;
}
int main(){
    // Fail closed before accessing a device or mutating rendering state.
    DXGI_SWAP_CHAIN_DESC rejected{};
    rejected.BufferDesc.Width=128;rejected.BufferDesc.Height=128;
    rejected.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    rejected.BufferCount=2;rejected.SampleDesc.Count=1;
    rejected.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    if(!minimixer::D3d11SwapchainCompositor::Supported(rejected))return 40;
    rejected.BufferCount=1;
    if(minimixer::D3d11SwapchainCompositor::Supported(rejected))return 41;
    rejected.BufferCount=2;rejected.SampleDesc.Count=4;
    if(minimixer::D3d11SwapchainCompositor::Supported(rejected))return 42;
    rejected.SampleDesc.Count=1;rejected.BufferDesc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;
    if(minimixer::D3d11SwapchainCompositor::Supported(rejected))return 43;
    rejected.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    rejected.SwapEffect=DXGI_SWAP_EFFECT_SEQUENTIAL;
    if(minimixer::D3d11SwapchainCompositor::Supported(rejected))return 44;
    WNDCLASSW cls{};
    cls.hInstance=GetModuleHandleW(nullptr);
    cls.lpfnWndProc=WindowProc;
    cls.lpszClassName=L"MiniMixerOwnFlipAdapter";
    if(!RegisterClassW(&cls))return 1;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    const auto hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,
        device.GetAddressOf(),nullptr,context.GetAddressOf());
    if(FAILED(hr))return 2;
    ComPtr<IDXGIDevice> dxgi;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if(FAILED(device.As(&dxgi)) ||
       FAILED(dxgi->GetAdapter(adapter.GetAddressOf())) ||
       FAILED(adapter->GetParent(IID_PPV_ARGS(&factory))))return 3;
    unsigned id=0;
    for(auto effect:{DXGI_SWAP_EFFECT_FLIP_DISCARD,DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL})
        for(auto format:{DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_FORMAT_R10G10B10A2_UNORM}){
            if(const int code=Exercise(device.Get(),context.Get(),factory.Get(),
                                        format,effect,++id))
                return 20+id*20+code;
        }
    puts("PASS D3D11_FLIP_COMPOSITOR all_four_owned_GPU_variants");
    return 0;
}
