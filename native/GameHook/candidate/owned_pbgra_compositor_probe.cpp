// Owned-process DXGI swapchain GPU test of premultiplied WPF/Pbgra32
// compositing. NOT an injected hook; no game/client/audio access.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <cstdio>
#include <cmath>
#include <cstdint>
#include <vector>
#include <cstring>
#include "graphics_contract.h"
#include "live_repeat_marker.h"
#include "d3d12_pbgra_compositor.h"

namespace {
using Microsoft::WRL::ComPtr;
static LRESULT CALLBACK Window(HWND w,UINT m,WPARAM a,LPARAM b){
    return DefWindowProcW(w,m,a,b);
}
D3D12_HEAP_PROPERTIES Heap(D3D12_HEAP_TYPE t) {
    D3D12_HEAP_PROPERTIES h{};h.Type=t;h.CreationNodeMask=h.VisibleNodeMask=1;return h;
}
D3D12_RESOURCE_DESC Buffer(UINT64 bytes){
    D3D12_RESOURCE_DESC r{};r.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
    r.Width=bytes;r.Height=1;r.DepthOrArraySize=1;r.MipLevels=1;
    r.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;r.SampleDesc.Count=1;return r;
}
D3D12_RESOURCE_BARRIER Barrier(ID3D12Resource* resource,
                               D3D12_RESOURCE_STATES before,
                               D3D12_RESOURCE_STATES after){
    D3D12_RESOURCE_BARRIER b{};b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource=resource;b.Transition.StateBefore=before;
    b.Transition.StateAfter=after;
    b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return b;
}
struct Color {float r{},g{},b{};};
Color Pixel(const unsigned char* p,DXGI_FORMAT fmt) {
    if(fmt==DXGI_FORMAT_R10G10B10A2_UNORM){
        std::uint32_t n{};std::memcpy(&n,p,4);
        return {static_cast<float>(n&1023u)/1023.f,
                static_cast<float>((n>>10)&1023u)/1023.f,
                static_cast<float>((n>>20)&1023u)/1023.f};
    }
    return {p[2]/255.f,p[1]/255.f,p[0]/255.f};
}
bool Near(Color observed,Color expected,float tolerance=0.06f) {
    return std::fabs(observed.r-expected.r)<tolerance &&
           std::fabs(observed.g-expected.g)<tolerance &&
           std::fabs(observed.b-expected.b)<tolerance;
}
struct Sample {
    Color background{},transparent{},green{},blueHalf{},red{},sphereGreen{};
};
bool Capture(const ComPtr<ID3D12Device>& dev,
             const ComPtr<ID3D12CommandQueue>& q,
             const ComPtr<IDXGISwapChain3>& sc,Sample& out) {
    const UINT idx=sc->GetCurrentBackBufferIndex();
    ComPtr<ID3D12Resource> target;
    if(FAILED(sc->GetBuffer(idx,IID_PPV_ARGS(&target))))return false;
    auto desc=target->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT layout{};
    UINT64 bytes{};
    dev->GetCopyableFootprints(&desc,0,1,0,&layout,nullptr,nullptr,&bytes);
    const auto h=Heap(D3D12_HEAP_TYPE_READBACK);
    const auto b=Buffer(bytes);
    ComPtr<ID3D12Resource> readback;
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> cmd;
    ComPtr<ID3D12Fence> fence;
    if(FAILED(dev->CreateCommittedResource(&h,D3D12_HEAP_FLAG_NONE,&b,
         D3D12_RESOURCE_STATE_COPY_DEST,nullptr,IID_PPV_ARGS(&readback))) ||
       FAILED(dev->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
          IID_PPV_ARGS(&allocator))) ||
       FAILED(dev->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
          allocator.Get(),nullptr,IID_PPV_ARGS(&cmd))) ||
       FAILED(dev->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence))))
        return false;
    auto from=Barrier(target.Get(),D3D12_RESOURCE_STATE_PRESENT,
                     D3D12_RESOURCE_STATE_COPY_SOURCE);
    cmd->ResourceBarrier(1,&from);
    D3D12_TEXTURE_COPY_LOCATION src{},dst{};
    src.pResource=target.Get();src.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dst.pResource=readback.Get();dst.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    dst.PlacedFootprint=layout;
    cmd->CopyTextureRegion(&dst,0,0,0,&src,nullptr);
    auto back=Barrier(target.Get(),D3D12_RESOURCE_STATE_COPY_SOURCE,
                     D3D12_RESOURCE_STATE_PRESENT);
    cmd->ResourceBarrier(1,&back);
    if(FAILED(cmd->Close()))return false;
    ID3D12CommandList* work[]{cmd.Get()};
    q->ExecuteCommandLists(1,work);
    if(FAILED(q->Signal(fence.Get(),1)))return false;
    if(fence->GetCompletedValue()<1){
        HANDLE complete=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!complete)return false;
        const bool waited=SUCCEEDED(fence->SetEventOnCompletion(1,complete)) &&
                 WaitForSingleObject(complete,3000)==WAIT_OBJECT_0;
        CloseHandle(complete);
        if(!waited)return false;
    }
    std::uint8_t* data{};
    D3D12_RANGE range{0,static_cast<SIZE_T>(bytes)};
    if(FAILED(readback->Map(0,&range,reinterpret_cast<void**>(&data)))||!data)
        return false;
    const auto at=[&](UINT x,UINT y) {
        return Pixel(data+layout.Offset+static_cast<size_t>(y)*
                    layout.Footprint.RowPitch+x*4,desc.Format);
    };
    out.background=at(65,65);
    out.transparent=at(84,74);
    out.green=at(104,74);
    out.blueHalf=at(120,74);
    out.red=at(137,74);
    out.sphereGreen=at(212,98);
    D3D12_RANGE written{0,0};
    readback->Unmap(0,&written);
    return true;
}
bool CheckSample(const Sample& s,DXGI_FORMAT format) {
    const Color cyan{0.02f,0.85f,0.90f};
    const Color green{0.f,1.f,0.f};
    const Color halfBlue{0.01f,0.423f,0.95f};
    const Color red{1.f,0.f,0.f};
    const Color sphereGreen{0.01f,0.925f,0.449f};
    const bool good=Near(s.background,cyan)&&Near(s.transparent,cyan)&&
                    Near(s.green,green)&&Near(s.blueHalf,halfBlue)&&Near(s.red,red)&&
                    Near(s.sphereGreen,sphereGreen);
    if(!good)
        std::printf("FAIL BLEND format=%u base=(%.2f %.2f %.2f) transparent=(%.2f %.2f %.2f) green=(%.2f %.2f %.2f) blue50=(%.2f %.2f %.2f) red=(%.2f %.2f %.2f) sphere=(%.2f %.2f %.2f)\n",
           static_cast<unsigned>(format),s.background.r,s.background.g,s.background.b,
           s.transparent.r,s.transparent.g,s.transparent.b,
           s.green.r,s.green.g,s.green.b,s.blueHalf.r,s.blueHalf.g,s.blueHalf.b,
           s.red.r,s.red.g,s.red.b,
           s.sphereGreen.r,s.sphereGreen.g,s.sphereGreen.b);
    return good;
}
bool Test(DXGI_FORMAT format) {
    WNDCLASSW wc{};wc.hInstance=GetModuleHandleW(nullptr);wc.lpfnWndProc=Window;
    wc.lpszClassName=L"MiniMixerOwnedPbgraCompositeTest";
    RegisterClassW(&wc);
    HWND hwnd=CreateWindowExW(0,wc.lpszClassName,L"Own compositing test",
                   WS_POPUP,0,0,512,288,nullptr,nullptr,wc.hInstance,nullptr);
    if(!hwnd)return false;
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<IDXGIFactory2> factory;
    ComPtr<IDXGISwapChain1> first;
    ComPtr<IDXGISwapChain3> chain;
    if(FAILED(D3D12CreateDevice(nullptr,D3D_FEATURE_LEVEL_11_0,
                IID_PPV_ARGS(&device))))return false;
    D3D12_COMMAND_QUEUE_DESC q{};q.Type=D3D12_COMMAND_LIST_TYPE_DIRECT;
    if(FAILED(device->CreateCommandQueue(&q,IID_PPV_ARGS(&queue)))||
       FAILED(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory))))return false;
    DXGI_SWAP_CHAIN_DESC1 sd{};
    sd.Width=512;sd.Height=288;sd.Format=format;sd.BufferCount=2;
    sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
    if(FAILED(factory->CreateSwapChainForHwnd(queue.Get(),hwnd,&sd,
                                nullptr,nullptr,&first))||
       FAILED(first.As(&chain)))return false;
    LiveRepeatMarker backdrop;
    minimixer::D3d12PbgraCompositor compositor,sphereComposer;
    std::vector<std::uint8_t> spherePixels(24*24*4);
    for(std::size_t i=0;i<spherePixels.size();i+=4){
        spherePixels[i]=0;spherePixels[i+1]=128;
        spherePixels[i+2]=0;spherePixels[i+3]=128;
    }
    minimixer::FrameView sphereFrame{spherePixels.data(),24,24,24*4};
    if(!backdrop.Init(chain.Get(),queue.Get(),hwnd) ||
       !compositor.Init(chain.Get(),queue.Get(),hwnd) ||
       !sphereComposer.Init(chain.Get(),queue.Get(),hwnd))return false;
    // Real MiniMixer FrameView contract including a padded row stride.
    constexpr UINT width=64,height=32,stride=272;
    std::vector<std::uint8_t> pixels(stride*height,0xE9);
    for(UINT y=0;y<height;++y)
       for(UINT x=0;x<width;++x){
           auto* p=pixels.data()+static_cast<size_t>(y)*stride+x*4;
           if(x<16){p[0]=p[1]=p[2]=p[3]=0;}
           else if(x<32){p[0]=0;p[1]=255;p[2]=0;p[3]=255;}
           else if(x<48){p[0]=128;p[1]=0;p[2]=0;p[3]=128;}
           else {p[0]=0;p[1]=0;p[2]=255;p[3]=255;}
       }
    minimixer::FrameView view{pixels.data(),width,height,stride};
    minimixer::FrameView wrong{pixels.data(),width,height,4};
    for(int frame=0;frame<8;++frame){
        if(!backdrop.Draw() || !compositor.Draw(view,80,70) ||
           !sphereComposer.Draw(sphereFrame,200,88))
            return false;
        if(frame==0){
            Sample firstSample{};
            if(!Capture(device,queue,chain,firstSample) ||
               !CheckSample(firstSample,format))return false;
        }
        if(FAILED(chain->Present(0,0)))return false;
        Sleep(3);
    }
    const auto before=compositor.submitted.load();
    if(compositor.Draw(wrong,80,70) ||
       compositor.Draw(view,500,270) ||
       compositor.submitted.load()!=before)return false;
    if(!compositor.WaitIdle(2000)||!sphereComposer.WaitIdle(2000)||
       !backdrop.WaitForIdle(2000)||
       !compositor.PrepareForResize()||!sphereComposer.PrepareForResize()||
       !backdrop.PrepareForResize())return false;
    first.Reset(); // DXGI resize cannot retain stale swapchain1 reference to buffers
    if(FAILED(chain->ResizeBuffers(2,640,360,DXGI_FORMAT_UNKNOWN,0)))return false;
    if(!SetWindowPos(hwnd,nullptr,0,0,640,360,
           SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE))return false;
    if(!backdrop.Init(chain.Get(),queue.Get(),hwnd)||
       !compositor.Init(chain.Get(),queue.Get(),hwnd)||
       !sphereComposer.Init(chain.Get(),queue.Get(),hwnd))return false;
    for(int frame=0;frame<8;++frame){
        if(!backdrop.Draw()||!compositor.Draw(view,80,70)||
           !sphereComposer.Draw(sphereFrame,200,88))return false;
        if(frame==0){
            Sample second{};
            if(!Capture(device,queue,chain,second) ||
               !CheckSample(second,format))return false;
        }
        if(FAILED(chain->Present(0,0)))return false;
        Sleep(3);
    }
    if(!compositor.WaitIdle(2000)||!sphereComposer.WaitIdle(2000)||
       !backdrop.WaitForIdle(2000)||!compositor.Cleanup()||
       !sphereComposer.Cleanup()||!backdrop.Cleanup())return false;
    if(compositor.submitted.load()!=16 ||
       compositor.skipped.load()!=0 || compositor.errors.load()!=0||
       sphereComposer.submitted.load()!=16 ||
       sphereComposer.skipped.load()!=0 ||
       sphereComposer.errors.load()!=0)return false;
    chain.Reset();factory.Reset();queue.Reset();device.Reset();
    DestroyWindow(hwnd);
    std::printf("PASS OWNED_PBGRA_D3D12_BLEND format=%u frames=16 resizes=1 transparentPreserved=1 premultipliedAlpha=1 secondarySphere=1 sphereOffset=200x88 offset=80x70 malformedRejected=1 fencesClean=1\n",static_cast<unsigned>(format));
    return true;
}
}
int main(){
    if(!Test(DXGI_FORMAT_R10G10B10A2_UNORM))return 1;
    if(!Test(DXGI_FORMAT_B8G8R8A8_UNORM))return 2;
    puts("PASS TWO_GPU_FORMATS_PBGRA_COMPOSITION");
    return 0;
}
