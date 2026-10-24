// Own-process D3D11 compositor probe; never hooks/injects an external process.
// Exercises BGRA frame upload, alpha blending, GPU readback, swap-chain Present.
// Shares API-neutral FrameView with future explicit API adapters.
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>
#include <atomic>
#include <thread>
#include "graphics_contract.h"
#define MINIMIXER_D3D11_PROBE_HOOKS 1
#include "d3d11_renderer.h"
#include "d3d11_swapchain_compositor.h"
#include "owned_present_attachment.h"
using Microsoft::WRL::ComPtr;
using minimixer::FrameView;
using minimixer::GraphicsApi;
using minimixer::Status;
using minimixer::D3d11Renderer;
using minimixer::D3d11OverlayShader;
using Vertex = minimixer::D3d11Vertex;
namespace {
constexpr unsigned Size = 64;
// One owned swapchain only. Native production providers must not use this
// private COM-vptr test technique as a substitute for a validated hook API.
minimixer::OwnedPresentAttachment presentAttachment;
D3d11Renderer* interceptedRenderer{};
ID3D11Device* interceptedDevice{};
ID3D11DeviceContext* interceptedContext{};
ID3D11RenderTargetView* interceptedTarget{};
const FrameView* interceptedFrame{};
int presentInterceptions=0, renderedInPresent=0;
bool resizedPixelsVerified=false,requestNestedPresent=false;
bool nestedDetachRejected=false;
HRESULT nestedPresentResult=E_FAIL;
bool ReadResizedFrameBeforePresent(IDXGISwapChain* chain) {
    ComPtr<ID3D11Texture2D> buffer;
    if(FAILED(chain->GetBuffer(0,__uuidof(ID3D11Texture2D),
             reinterpret_cast<void**>(buffer.GetAddressOf()))))return false;
    D3D11_TEXTURE2D_DESC desc{};
    buffer->GetDesc(&desc);
    if(desc.Width!=96||desc.Height!=96)return false;
    desc.BindFlags=0;desc.MiscFlags=0;
    desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> stage;
    if(FAILED(interceptedDevice->CreateTexture2D(&desc,nullptr,stage.GetAddressOf())))
        return false;
    interceptedContext->CopyResource(stage.Get(),buffer.Get());
    D3D11_MAPPED_SUBRESOURCE read{};
    if(FAILED(interceptedContext->Map(stage.Get(),0,D3D11_MAP_READ,0,&read)))
        return false;
    const auto* ptr=static_cast<const std::uint8_t*>(read.pData);
    int left=ptr[32*read.RowPitch+12*4+1];
    int outside=ptr[32*read.RowPitch+80*4+1];
    interceptedContext->Unmap(stage.Get(),0);
    return left>=115&&left<=140&&outside==0;
}
void BeforeOwnedPresent(IDXGISwapChain* chain,void*) noexcept {
    ++presentInterceptions;
    if(requestNestedPresent) {
        requestNestedPresent=false;
        nestedDetachRejected=!presentAttachment.Detach();
        // Recursive Present must delegate to the original function without
        // recursively drawing the overlay or detaching an in-flight callback.
        nestedPresentResult=chain->Present(0,0);
    }
    if(interceptedRenderer&&interceptedDevice&&interceptedContext&&
       interceptedTarget&&interceptedFrame &&
       interceptedRenderer->Draw(interceptedDevice,interceptedContext,
                                 interceptedTarget,*interceptedFrame)) {
        ++renderedInPresent;
        if(presentInterceptions==6)
            resizedPixelsVerified=ReadResizedFrameBeforePresent(chain);
    }
}
void AfterOwnedPresent(HRESULT result,void*) noexcept {
    if(interceptedRenderer&&interceptedDevice)
        interceptedRenderer->NotifyPresentResult(interceptedDevice,result);
}

// Validation intentionally compares both pointers and scalar state. A successful
// screenshot alone would not detect a broken pipeline after overlay drawing.
struct StateSentinels {
    ComPtr<ID3D11RasterizerState> rasterizer;
    ComPtr<ID3D11BlendState> blend;
    ComPtr<ID3D11InputLayout> layout;
    ComPtr<ID3D11Buffer> vertex;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11ShaderResourceView> srv;
    ComPtr<ID3D11SamplerState> sampler;
    D3D11_VIEWPORT viewport{};
    ID3D11RenderTargetView* target{};
};
bool ConfigureSentinels(ID3D11Device* device,ID3D11DeviceContext* ctx,
                        ID3D11RenderTargetView* target,StateSentinels& sent) {
    D3D11_RASTERIZER_DESC rd{};rd.FillMode=D3D11_FILL_WIREFRAME;rd.CullMode=D3D11_CULL_BACK;
    rd.DepthClipEnable=TRUE;rd.ScissorEnable=FALSE;
    if(FAILED(device->CreateRasterizerState(&rd,sent.rasterizer.GetAddressOf())))return false;
    D3D11_BLEND_DESC bd{};bd.RenderTarget[0].RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    if(FAILED(device->CreateBlendState(&bd,sent.blend.GetAddressOf())))return false;
    // Non-null sentinel shaders, layout, VB, SRV and sampler make forgotten
    // restoration observable; null/default state is insufficient evidence.
    ComPtr<ID3DBlob> vsBytes,psBytes;
    if(FAILED(D3DCompile(D3d11OverlayShader,strlen(D3d11OverlayShader),nullptr,nullptr,nullptr,
        "VS","vs_4_0",0,0,vsBytes.GetAddressOf(),nullptr)) ||
       FAILED(D3DCompile(D3d11OverlayShader,strlen(D3d11OverlayShader),nullptr,nullptr,nullptr,
        "PS","ps_4_0",0,0,psBytes.GetAddressOf(),nullptr)))return false;
    if(FAILED(device->CreateVertexShader(vsBytes->GetBufferPointer(),
        vsBytes->GetBufferSize(),nullptr,sent.vs.GetAddressOf())) ||
       FAILED(device->CreatePixelShader(psBytes->GetBufferPointer(),
        psBytes->GetBufferSize(),nullptr,sent.ps.GetAddressOf())))return false;
    D3D11_INPUT_ELEMENT_DESC fields[]={
        {"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,8,D3D11_INPUT_PER_VERTEX_DATA,0}
    };
    if(FAILED(device->CreateInputLayout(fields,2,vsBytes->GetBufferPointer(),
        vsBytes->GetBufferSize(),sent.layout.GetAddressOf())))return false;
    Vertex verts[]={{-0.5f,0.5f,0,0},{0.5f,0.5f,1,0},{0,-0.5f,0.5f,1}};
    D3D11_BUFFER_DESC buffer{};buffer.ByteWidth=sizeof(verts);
    buffer.BindFlags=D3D11_BIND_VERTEX_BUFFER;buffer.Usage=D3D11_USAGE_IMMUTABLE;
    D3D11_SUBRESOURCE_DATA payload{};payload.pSysMem=verts;
    if(FAILED(device->CreateBuffer(&buffer,&payload,sent.vertex.GetAddressOf())))return false;
    D3D11_TEXTURE2D_DESC texture{};
    texture.Width=2;texture.Height=2;texture.MipLevels=1;texture.ArraySize=1;
    texture.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    texture.SampleDesc.Count=1;texture.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    if(FAILED(device->CreateTexture2D(&texture,nullptr,sent.texture.GetAddressOf())) ||
       FAILED(device->CreateShaderResourceView(sent.texture.Get(),nullptr,
             sent.srv.GetAddressOf())))return false;
    D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_WRAP;
    sampler.MaxLOD=D3D11_FLOAT32_MAX;
    if(FAILED(device->CreateSamplerState(&sampler,sent.sampler.GetAddressOf())))return false;
    sent.target=target;
    sent.viewport.TopLeftX=3.0f;sent.viewport.TopLeftY=5.0f;
    sent.viewport.Width=44.f;sent.viewport.Height=47.f;
    sent.viewport.MinDepth=0.f;sent.viewport.MaxDepth=1.f;
    ctx->IASetInputLayout(sent.layout.Get());
    ID3D11Buffer* vb=sent.vertex.Get();
    UINT stride=sizeof(Vertex),offset=0;
    ctx->IASetVertexBuffers(0,1,&vb,&stride,&offset);
    ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_LINELIST);
    ctx->VSSetShader(sent.vs.Get(),nullptr,0);
    ctx->PSSetShader(sent.ps.Get(),nullptr,0);
    ID3D11ShaderResourceView* srv=sent.srv.Get();
    ID3D11SamplerState* psSampler=sent.sampler.Get();
    ctx->PSSetShaderResources(0,1,&srv);
    ctx->PSSetSamplers(0,1,&psSampler);
    ctx->RSSetViewports(1,&sent.viewport);
    ctx->RSSetState(sent.rasterizer.Get());
    const float blendFactor[4]={0.2f,0.3f,0.4f,0.5f};
    ctx->OMSetBlendState(sent.blend.Get(),blendFactor,0xA55AA55A);
    ctx->OMSetRenderTargets(1,&target,nullptr);
    return true;
}
bool ValidateSentinels(ID3D11DeviceContext* ctx,const StateSentinels& sent){
    D3D11_PRIMITIVE_TOPOLOGY topology{};
    ctx->IAGetPrimitiveTopology(&topology);
    if(topology!=D3D11_PRIMITIVE_TOPOLOGY_LINELIST)return false;
    ComPtr<ID3D11InputLayout> layout;ctx->IAGetInputLayout(layout.GetAddressOf());
    if(layout.Get()!=sent.layout.Get())return false;
    ComPtr<ID3D11Buffer> vertex;UINT stride{},offset{};
    ctx->IAGetVertexBuffers(0,1,vertex.GetAddressOf(),&stride,&offset);
    if(vertex.Get()!=sent.vertex.Get()||stride!=sizeof(Vertex)||offset!=0)return false;
    ComPtr<ID3D11VertexShader> vs;ComPtr<ID3D11PixelShader> ps;
    UINT n=0;ctx->VSGetShader(vs.GetAddressOf(),nullptr,&n);
    n=0;ctx->PSGetShader(ps.GetAddressOf(),nullptr,&n);
    if(vs.Get()!=sent.vs.Get()||ps.Get()!=sent.ps.Get())return false;
    ComPtr<ID3D11ShaderResourceView> srv;
    ComPtr<ID3D11SamplerState> sampler;
    ctx->PSGetShaderResources(0,1,srv.GetAddressOf());
    ctx->PSGetSamplers(0,1,sampler.GetAddressOf());
    if(srv.Get()!=sent.srv.Get()||sampler.Get()!=sent.sampler.Get())return false;
    ComPtr<ID3D11RasterizerState> rs;ctx->RSGetState(rs.GetAddressOf());
    if(rs.Get()!=sent.rasterizer.Get())return false;
    UINT count=D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
    D3D11_VIEWPORT viewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE]{};
    ctx->RSGetViewports(&count,viewports);
    if(count!=1||memcmp(viewports,&sent.viewport,sizeof(D3D11_VIEWPORT))!=0)return false;
    ComPtr<ID3D11BlendState> blend;
    float factors[4]{};UINT mask=0;
    ctx->OMGetBlendState(blend.GetAddressOf(),factors,&mask);
    if(blend.Get()!=sent.blend.Get()||mask!=0xA55AA55A||
       factors[0]!=0.2f||factors[1]!=0.3f||factors[2]!=0.4f||factors[3]!=0.5f)return false;
    ComPtr<ID3D11RenderTargetView> target;
    ctx->OMGetRenderTargets(1,target.GetAddressOf(),nullptr);
    return target.Get()==sent.target;
}

struct NestedAdmissionTest {
    D3d11Renderer* renderer{};
    ID3D11Device* device{};
    ID3D11DeviceContext* context{};
    ID3D11RenderTargetView* target{};
    const FrameView* frame{};
    bool drawDenied=false,resetDenied=false;
};
void NestedDrawProbe(void* state) {
    auto& t=*static_cast<NestedAdmissionTest*>(state);
    t.drawDenied=!t.renderer->Draw(t.device,t.context,t.target,*t.frame);
    t.resetDenied=!t.renderer->Reset();
}
struct ConcurrentAdmissionTest {
    HANDLE entered{}, release{};
};
void BlockingDrawProbe(void* state) {
    auto& t=*static_cast<ConcurrentAdmissionTest*>(state);
    SetEvent(t.entered);
    WaitForSingleObject(t.release,5000);
}
LRESULT CALLBACK WindowProc(HWND w,UINT m,WPARAM a,LPARAM b){
 return m==WM_CLOSE?0:DefWindowProc(w,m,a,b);
}
}
int main(int argc,char** argv){
    const bool forceWarp=argc>1&&strcmp(argv[1],"--warp")==0;
    using namespace minimixer;
    if(SupportedRenderer(GraphicsApi::D3D9)!=Status::UnsupportedApi ||
       SupportedRenderer(GraphicsApi::D3D10)!=Status::UnsupportedApi ||
       SupportedRenderer(GraphicsApi::D3D12)!=Status::UnsupportedApi ||
       SupportedRenderer(GraphicsApi::OpenGL)!=Status::UnsupportedApi ||
       SupportedRenderer(GraphicsApi::Vulkan)!=Status::UnsupportedApi ||
       SupportedRenderer(GraphicsApi::D3D11)!=Status::Ok){
        puts("FAIL capability routing");return 2;
    }
    WNDCLASSA wc{};wc.lpfnWndProc=WindowProc;wc.hInstance=GetModuleHandle(nullptr);
    wc.lpszClassName="MiniMixerOwnProcessDx11Test";
    if(!RegisterClassA(&wc)){puts("FAIL window class");return 3;}
    HWND hwnd=CreateWindowA(wc.lpszClassName,"MiniMixer D3D11 isolated test",
       WS_OVERLAPPEDWINDOW,0,0,128,128,nullptr,nullptr,wc.hInstance,nullptr);
    if(!hwnd){puts("FAIL hwnd");return 4;}
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=Size;desc.BufferDesc.Height=Size;
    desc.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount=2;desc.OutputWindow=hwnd;desc.Windowed=TRUE;
    desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    ComPtr<IDXGISwapChain> chain;ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> ctx;
    D3D_FEATURE_LEVEL level{};
    D3D_FEATURE_LEVEL wanted[]={D3D_FEATURE_LEVEL_11_0,D3D_FEATURE_LEVEL_10_1};
    HRESULT hr=E_FAIL;const char* driver="none";
    for(D3D_DRIVER_TYPE type:{D3D_DRIVER_TYPE_HARDWARE,D3D_DRIVER_TYPE_WARP}){
        if(forceWarp&&type!=D3D_DRIVER_TYPE_WARP)continue;
        hr=D3D11CreateDeviceAndSwapChain(nullptr,type,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            wanted,2,D3D11_SDK_VERSION,&desc,chain.ReleaseAndGetAddressOf(),
            device.ReleaseAndGetAddressOf(),&level,ctx.ReleaseAndGetAddressOf());
        if(SUCCEEDED(hr)){driver=type==D3D_DRIVER_TYPE_HARDWARE?"HARDWARE":"WARP";break;}
    }
    if(FAILED(hr)){printf("FAIL D3D11 device 0x%08X\n",(unsigned)hr);return 5;}
    ComPtr<ID3D11Texture2D> back;
    hr=chain->GetBuffer(0,__uuidof(ID3D11Texture2D),(void**)back.GetAddressOf());
    if(FAILED(hr))return 6;
    ComPtr<ID3D11RenderTargetView> rtv;
    if(FAILED(device->CreateRenderTargetView(back.Get(),nullptr,rtv.GetAddressOf())))return 7;
    float black[4]={0.f,0.f,0.f,1.f};ctx->ClearRenderTargetView(rtv.Get(),black);
    std::vector<std::uint8_t> data(Size*Size*4);
    for(unsigned y=0;y<Size;++y)for(unsigned x=0;x<Size/2;++x){
        size_t pos=(y*Size+x)*4;
        data[pos]=0;data[pos+1]=128;data[pos+2]=0;data[pos+3]=128; // premultiplied green
    }
    FrameView frame{data.data(),Size,Size,Size*4};
    D3d11Renderer renderer;
    StateSentinels sentinel;
    if(!ConfigureSentinels(device.Get(),ctx.Get(),rtv.Get(),sentinel) ||
       !ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL initial render-pipeline sentinel configuration");return 8;
    }
    if(!renderer.Draw(device.Get(),ctx.Get(),rtv.Get(),frame)){
        puts("FAIL drawing");return 8;
    }
    if(!ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL render pipeline state changed after direct draw");return 16;
    }
    FrameView invalid{data.data(),5000,Size,Size*4};
    if(renderer.Draw(device.Get(),ctx.Get(),rtv.Get(),invalid) ||
       !ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL invalid frame rejected or state altered");return 24;
    }
    // Many successive frames must reuse compiled shaders and GPU resources.
    // This intentionally catches accidental allocations per Present.
    for(int i=0;i<120;++i) {
        ctx->ClearRenderTargetView(rtv.Get(),black);
        if(!renderer.Draw(device.Get(),ctx.Get(),rtv.Get(),frame) ||
           !ValidateSentinels(ctx.Get(),sentinel)) {
            puts("FAIL repeated cached draw/state restoration");return 25;
        }
    }
    auto cached=renderer.Stats();
    if(cached.staticBuilds!=1||cached.textureBuilds!=1||cached.uploadedFrames!=121) {
        printf("FAIL cache reuse static=%u texture=%u frames=%u\n",
               cached.staticBuilds,cached.textureBuilds,cached.uploadedFrames);
        return 26;
    }
    D3D11_TEXTURE2D_DESC staging{};
    back->GetDesc(&staging);staging.BindFlags=0;staging.MiscFlags=0;
    staging.Usage=D3D11_USAGE_STAGING;staging.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> readback;
    if(FAILED(device->CreateTexture2D(&staging,nullptr,readback.GetAddressOf())))return 9;
    ctx->CopyResource(readback.Get(),back.Get());
    D3D11_MAPPED_SUBRESOURCE map{};
    if(FAILED(ctx->Map(readback.Get(),0,D3D11_MAP_READ,0,&map)))return 10;
    auto left=static_cast<const std::uint8_t*>(map.pData)+32*map.RowPitch+12*4;
    auto right=static_cast<const std::uint8_t*>(map.pData)+32*map.RowPitch+52*4;
    int gLeft=left[1],gRight=right[1];
    ctx->Unmap(readback.Get(),0);
    if(gLeft<115||gLeft>140||gRight!=0){
        printf("FAIL colors left=%d right=%d\n",gLeft,gRight);return 11;
    }
    // This attachment is scoped to the single known-owned test swapchain.
    interceptedRenderer=&renderer;
    interceptedDevice=device.Get();interceptedContext=ctx.Get();
    interceptedTarget=rtv.Get();interceptedFrame=&frame;
    HRESULT present=S_OK;
    minimixer::OwnedPresentAttachment rejectedConcurrentOwner;
    for(int cycle=0;cycle<5;++cycle){
        if(!presentAttachment.Attach(chain.Get(),&BeforeOwnedPresent,&AfterOwnedPresent,nullptr))
            return 13;
        if(presentAttachment.Attach(chain.Get(),&BeforeOwnedPresent,&AfterOwnedPresent,nullptr) ||
           rejectedConcurrentOwner.Attach(chain.Get(),&BeforeOwnedPresent,&AfterOwnedPresent,nullptr))
            return 44;
        present=chain->Present(0,0);
        if(!presentAttachment.Detach() || presentAttachment.Attached())
            return 45;
        if(!ValidateSentinels(ctx.Get(),sentinel)){
            puts("FAIL render pipeline state changed in Present callback");return 17;
        }
        if(FAILED(present)||presentInterceptions!=cycle+1||renderedInPresent!=cycle+1){
            printf("FAIL hooked Present cycle=%d hr=0x%08X calls=%d draw=%d\n",
                cycle,(unsigned)present,presentInterceptions,renderedInPresent);
            return 14;
        }
        HRESULT normalPresent=chain->Present(0,0);
        if(FAILED(normalPresent)||presentInterceptions!=cycle+1){
            puts("FAIL original Present after detaching owned vtable");return 15;
        }
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=1||cached.textureBuilds!=1||cached.uploadedFrames!=126) {
        puts("FAIL cache reused across hooked Present cycles");return 27;
    }
    // This is the same per-swapchain adapter that the native provider will
    // call. Its retained RTV must be dropped *before* our real ResizeBuffers.
    D3d11SwapchainCompositor liveAdapter;
    if(!liveAdapter.Init(chain.Get(),hwnd) ||
       !liveAdapter.Draw(frame,0,0,false) ||
       !liveAdapter.ReleaseForResize() ||
       liveAdapter.Ready() || !ValidateSentinels(ctx.Get(),sentinel)) {
        puts("FAIL live D3D11 adapter initial draw/release before resize");return 56;
    }
    // Resize must release the previous backbuffer and unbind render targets.
    // An extra AddRef held by a stale state guard would make this fail.
    ctx->OMSetRenderTargets(0,nullptr,nullptr);
    rtv.Reset();back.Reset();readback.Reset();
    ctx->Flush();
    HRESULT resized=chain->ResizeBuffers(2,96,96,DXGI_FORMAT_UNKNOWN,0);
    if(FAILED(resized)){
        printf("FAIL ResizeBuffers 0x%08X\n",(unsigned)resized);return 18;
    }
    if(FAILED(chain->GetBuffer(0,__uuidof(ID3D11Texture2D),(void**)back.GetAddressOf())) ||
       FAILED(device->CreateRenderTargetView(back.Get(),nullptr,rtv.GetAddressOf()))){
        puts("FAIL recreate render target after ResizeBuffers");return 19;
    }
    D3D11_TEXTURE2D_DESC resizedDesc{};
    back->GetDesc(&resizedDesc);
    if(resizedDesc.Width!=96||resizedDesc.Height!=96){
        puts("FAIL new backbuffer dimensions");return 20;
    }
    sentinel=StateSentinels{};
    if(!ConfigureSentinels(device.Get(),ctx.Get(),rtv.Get(),sentinel)){
        puts("FAIL configure after resize");return 21;
    }
    ctx->ClearRenderTargetView(rtv.Get(),black);
    // A resized overlay frame forces one new texture/SRV, not a shader recompile.
    constexpr unsigned Smaller=48;
    std::vector<std::uint8_t> resizedPixels(Smaller*Smaller*4);
    for(unsigned y=0;y<Smaller;++y)for(unsigned x=0;x<Smaller/2;++x){
        size_t p=(y*Smaller+x)*4;
        resizedPixels[p+1]=128;resizedPixels[p+3]=128;
    }
    FrameView resizedFrame{resizedPixels.data(),Smaller,Smaller,Smaller*4};

    // The production overlay has independent screen-relative WPF surfaces.
    // Verify a real D3D11 region at a nonzero target offset; neither the
    // original state nor pixels outside the region may change.
    {
        D3d11Renderer positioned;
        if (positioned.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame,
                            60,13,96,96) ||
            positioned.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame,
                            9,60,96,96) ||
            positioned.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame,
                            0,0,47,96) ||
            !ValidateSentinels(ctx.Get(),sentinel)) {
            puts("FAIL region bounds must reject without touching state");
            return 50;
        }
        ctx->ClearRenderTargetView(rtv.Get(),black);
        if (!positioned.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame,
                             9,13,96,96) ||
            !ValidateSentinels(ctx.Get(),sentinel)) {
            puts("FAIL positioned D3D11 render or pipeline state");
            return 51;
        }
        D3D11_TEXTURE2D_DESC desc{};
        back->GetDesc(&desc);
        desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;
        desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> stage;
        if (FAILED(device->CreateTexture2D(&desc,nullptr,stage.GetAddressOf())))
            return 52;
        ctx->CopyResource(stage.Get(),back.Get());
        D3D11_MAPPED_SUBRESOURCE sample{};
        if (FAILED(ctx->Map(stage.Get(),0,D3D11_MAP_READ,0,&sample)))
            return 53;
        const auto* bytes=static_cast<const std::uint8_t*>(sample.pData);
        const int inside=bytes[32*sample.RowPitch+12*4+1];
        const int outside=bytes[32*sample.RowPitch+40*4+1];
        const int above=bytes[4*sample.RowPitch+12*4+1];
        ctx->Unmap(stage.Get(),0);
        if (inside<115||inside>140||outside!=0||above!=0 ||
            positioned.Stats().uploadedFrames!=1) {
            printf("FAIL region GPU pixels: inside=%d outside=%d above=%d\n",
                   inside,outside,above);
            return 54;
        }
        if (!positioned.Reset())return 55;
        ctx->ClearRenderTargetView(rtv.Get(),black);
        puts("PASS D3D11 positioned region/alpha/outside/state/guarded bounds");
    }
    // Exercise a second real backbuffer lifetime with two independently
    // positioned translucent WPF surfaces and no per-Draw target rebuild.
    {
        constexpr unsigned SphereSize=24;
        std::vector<std::uint8_t> spherePixels(SphereSize*SphereSize*4);
        for(unsigned y=0;y<SphereSize;++y)for(unsigned x=0;x<SphereSize/2;++x) {
            const auto p=(y*SphereSize+x)*4;
            spherePixels[p+1]=128;spherePixels[p+3]=128;
        }
        FrameView aux{spherePixels.data(),SphereSize,SphereSize,SphereSize*4};
        ctx->ClearRenderTargetView(rtv.Get(),black);
        // Wrong target identity must not provision a compositor or GPU cache.
        if(liveAdapter.Init(chain.Get(),GetDesktopWindow()) || liveAdapter.Ready())
            return 62;
        if(!liveAdapter.Init(chain.Get(),hwnd) ||
           liveAdapter.Draw(resizedFrame,60,13,false) ||
           !liveAdapter.Draw(resizedFrame,9,13,false) ||
           !liveAdapter.Draw(aux,58,51,true) ||
           !ValidateSentinels(ctx.Get(),sentinel) ||
           liveAdapter.mainFrames!=2 || liveAdapter.sphereFrames!=1) {
            puts("FAIL live D3D11 adapter repositioned two surfaces");return 57;
        }
        D3D11_TEXTURE2D_DESC d{};
        back->GetDesc(&d);d.BindFlags=0;d.MiscFlags=0;
        d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;
        if(FAILED(device->CreateTexture2D(&d,nullptr,staging.GetAddressOf())))
            return 58;
        ctx->CopyResource(staging.Get(),back.Get());
        D3D11_MAPPED_SUBRESOURCE pixels{};
        if(FAILED(ctx->Map(staging.Get(),0,D3D11_MAP_READ,0,&pixels)))return 59;
        const auto* ptr=static_cast<const std::uint8_t*>(pixels.pData);
        const int mainGreen=ptr[32*pixels.RowPitch+12*4+1];
        const int sphereGreen=ptr[55*pixels.RowPitch+60*4+1];
        const int untouched=ptr[4*pixels.RowPitch+85*4+1];
        ctx->Unmap(staging.Get(),0);
        if(mainGreen<115 || mainGreen>140 ||
           sphereGreen<115 || sphereGreen>140 || untouched!=0) {
            printf("FAIL live D3D11 adapter pixel result main=%d sphere=%d untouched=%d\n",
                   mainGreen,sphereGreen,untouched);return 60;
        }
        liveAdapter.NotifyPresentResult(S_OK);
        if(!liveAdapter.ReleaseForResize() || liveAdapter.Ready())return 61;
        ctx->ClearRenderTargetView(rtv.Get(),black);
        puts("PASS live D3D11 adapter two WPF surfaces/alpha/resize release");
    }
    interceptedFrame=&resizedFrame;
    interceptedTarget=rtv.Get();
    if(!presentAttachment.Attach(chain.Get(),&BeforeOwnedPresent,&AfterOwnedPresent,nullptr))
        return 46;
    present=chain->Present(0,0);
    if(!presentAttachment.Detach())return 47;
    interceptedRenderer=nullptr;
    interceptedDevice=nullptr;interceptedContext=nullptr;
    interceptedTarget=nullptr;interceptedFrame=nullptr;
    if(FAILED(present)||presentInterceptions!=6||renderedInPresent!=6 ||
       !resizedPixelsVerified || !ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL hooked Present/StateRestore after ResizeBuffers");return 22;
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=1||cached.textureBuilds!=2||cached.uploadedFrames!=127) {
        puts("FAIL overlay frame dimension cache invalidation");return 28;
    }
    if(FAILED(chain->Present(0,0))||presentInterceptions!=6){
        puts("FAIL original Present after resized detach");return 23;
    }
    // Explicit reset is required for device teardown/loss. Rebuild lazily.
    renderer.Reset();
    if(!renderer.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame) ||
       !ValidateSentinels(ctx.Get(),sentinel)) {
        puts("FAIL renderer reset/recreate");return 29;
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=2||cached.textureBuilds!=3||cached.uploadedFrames!=128) {
        puts("FAIL cache did not rebuild after explicit reset");return 30;
    }
    // A distinct owned WARP device demonstrates automatic cache replacement.
    ComPtr<ID3D11Device> nextDevice;
    ComPtr<ID3D11DeviceContext> nextContext;
    D3D_FEATURE_LEVEL nextLevel{};
    HRESULT nextHr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,wanted,2,D3D11_SDK_VERSION,
        nextDevice.GetAddressOf(),&nextLevel,nextContext.GetAddressOf());
    if(FAILED(nextHr)){puts("FAIL second D3D11 device");return 31;}
    D3D11_TEXTURE2D_DESC nextDesc{};
    nextDesc.Width=96;nextDesc.Height=96;nextDesc.MipLevels=1;
    nextDesc.ArraySize=1;nextDesc.SampleDesc.Count=1;
    nextDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
    nextDesc.Usage=D3D11_USAGE_DEFAULT;
    nextDesc.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> nextTexture;
    ComPtr<ID3D11RenderTargetView> nextTarget;
    if(FAILED(nextDevice->CreateTexture2D(&nextDesc,nullptr,nextTexture.GetAddressOf())) ||
       FAILED(nextDevice->CreateRenderTargetView(nextTexture.Get(),nullptr,nextTarget.GetAddressOf()))) {
        puts("FAIL replacement render target");return 32;
    }
    // Rejected mismatch must not swap cache or touch the original context.
    if(renderer.Draw(nextDevice.Get(),ctx.Get(),rtv.Get(),resizedFrame) ||
       renderer.Draw(device.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame) ||
       renderer.Draw(device.Get(),ctx.Get(),nextTarget.Get(),resizedFrame) ||
       !ValidateSentinels(ctx.Get(),sentinel)) {
        puts("FAIL mismatched device/context/RTV accepted");return 33;
    }
    StateSentinels newSentinel;
    if(!ConfigureSentinels(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),newSentinel) ||
       !renderer.Draw(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame) ||
       !ValidateSentinels(nextContext.Get(),newSentinel)) {
        puts("FAIL replacement device draw/state");return 34;
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=3||cached.textureBuilds!=4||cached.uploadedFrames!=129) {
        puts("FAIL implicit device migration cache");return 35;
    }
    // A nested Draw and Reset must be rejected without modifying the outer Draw.
    NestedAdmissionTest nested{&renderer,nextDevice.Get(),nextContext.Get(),
                               nextTarget.Get(),&resizedFrame};
    renderer.SetBeforeGpuWorkForTest(&NestedDrawProbe,&nested);
    bool nestedOuter=renderer.Draw(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame);
    renderer.SetBeforeGpuWorkForTest(nullptr,nullptr);
    if(!nestedOuter||!nested.drawDenied||!nested.resetDenied||
       !ValidateSentinels(nextContext.Get(),newSentinel)) {
        puts("FAIL nested draw/reset admission");return 36;
    }
    // Deterministically hold an actual Draw after admission but before GPU work.
    // A concurrent caller cannot acquire a renderer slot or touch the context.
    ConcurrentAdmissionTest concurrent{
        CreateEventA(nullptr,TRUE,FALSE,nullptr),
        CreateEventA(nullptr,TRUE,FALSE,nullptr)};
    if(!concurrent.entered||!concurrent.release){
        puts("FAIL events");return 37;
    }
    renderer.SetBeforeGpuWorkForTest(&BlockingDrawProbe,&concurrent);
    std::atomic<bool> backgroundDraw{false};
    std::thread worker([&](){
        backgroundDraw.store(renderer.Draw(nextDevice.Get(),nextContext.Get(),
                                           nextTarget.Get(),resizedFrame));
    });
    DWORD waitResult=WaitForSingleObject(concurrent.entered,3000);
    bool concurrentlyDenied=false;
    if(waitResult==WAIT_OBJECT_0){
        concurrentlyDenied=
          !renderer.Draw(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame) &&
          !renderer.Reset() &&
          !renderer.NotifyPresentResult(nextDevice.Get(),DXGI_ERROR_DEVICE_REMOVED);
    }
    SetEvent(concurrent.release);
    worker.join();
    renderer.SetBeforeGpuWorkForTest(nullptr,nullptr);
    CloseHandle(concurrent.entered);CloseHandle(concurrent.release);
    if(!concurrentlyDenied||!backgroundDraw.load()||
       !ValidateSentinels(nextContext.Get(),newSentinel)){
        puts("FAIL cross-thread draw/reset/present admission");return 38;
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=3||cached.textureBuilds!=4||
       cached.uploadedFrames!=131||cached.busyRejects!=5||cached.deviceLosses!=0){
        printf("FAIL admission counters draw=%u busy=%u loss=%u\n",
               cached.uploadedFrames,cached.busyRejects,cached.deviceLosses);
        return 39;
    }
    // Synthetic DXGI failure fed through the actual owner-facing callback API.
    // This does NOT cause real device removal on hardware.
    if(!renderer.NotifyPresentResult(nextDevice.Get(),DXGI_ERROR_DEVICE_RESET) ||
       renderer.Draw(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame) ||
       !renderer.NotifyPresentResult(nextDevice.Get(),S_OK) ||
       renderer.Draw(nextDevice.Get(),nextContext.Get(),nextTarget.Get(),resizedFrame)) {
        puts("FAIL device-loss quarantine");return 40;
    }
    cached=renderer.Stats();
    if(cached.deviceLosses!=1||cached.uploadedFrames!=131) {
        puts("FAIL quarantined device still rendering");return 41;
    }
    // Recovery is automatic only on a distinct healthy device, never on the
    // same quarantined device. Original owned device remains valid here.
    if(!renderer.Draw(device.Get(),ctx.Get(),rtv.Get(),resizedFrame)||
       !ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL healthy-device recovery");return 42;
    }
    cached=renderer.Stats();
    if(cached.staticBuilds!=4||cached.textureBuilds!=5||
       cached.uploadedFrames!=132||cached.deviceLosses!=1){
        puts("FAIL healthy replacement cache/counters");return 43;
    }
    // Challenge live detach and recursive Present while still on the owned
    // swapchain. It must leave the original function callable after detach.
    interceptedRenderer=&renderer;
    interceptedDevice=device.Get();interceptedContext=ctx.Get();
    interceptedTarget=rtv.Get();interceptedFrame=&resizedFrame;
    requestNestedPresent=true;
    if(!presentAttachment.Attach(chain.Get(),&BeforeOwnedPresent,&AfterOwnedPresent,nullptr))
        return 48;
    HRESULT nestedResult=chain->Present(0,0);
    if(FAILED(nestedResult)||FAILED(nestedPresentResult)||
       !nestedDetachRejected||presentAttachment.InFlight()!=0||
       presentAttachment.NestedSkipped()!=1||
       presentAttachment.CallbackCount()!=7||!ValidateSentinels(ctx.Get(),sentinel)){
        puts("FAIL recursive Present or in-flight Detach isolation");return 49;
    }
    // Mutation: an unexpected vptr owner must NOT be overwritten by Detach.
    void*** ownSlot=reinterpret_cast<void***>(chain.Get());
    void** currentVtable=*ownSlot;
    {
        // Simulate unexpected external vptr ownership with a different,
        // temporary table at the address of this owned COM test object.
        // No Present call occurs while that temporary table is installed.
        std::array<void*,18> foreignTable{};
        std::memcpy(foreignTable.data(),currentVtable,sizeof(foreignTable));
        InterlockedExchangePointer(reinterpret_cast<void* volatile*>(ownSlot),foreignTable.data());
        const bool wronglyDetached=presentAttachment.Detach();
        InterlockedExchangePointer(reinterpret_cast<void* volatile*>(ownSlot),currentVtable);
        if(wronglyDetached || !presentAttachment.Attached()){
            puts("FAIL Detach overwrote changed vptr owner");return 50;
        }
    }
    if(!presentAttachment.Detach() || FAILED(chain->Present(0,0))){
        puts("FAIL final detach or original Present");return 51;
    }
    interceptedRenderer=nullptr;interceptedDevice=nullptr;
    interceptedContext=nullptr;interceptedTarget=nullptr;interceptedFrame=nullptr;
    if(presentInterceptions!=7||renderedInPresent!=7){
        puts("FAIL callback accounting after nested Present");return 52;
    }
    cached=renderer.Stats(); // Include the final nested-Present outer draw.
    printf("PASS UNIVERSAL_FRAME_D3D11 driver=%s overlayGreen=%d transparentGreen=%d present=0x%08X ownPresentHook=%d ownHookDraw=%d unhooked=1 stateRestored=1 resized=1 resizePixels=1 cycles=7 attachDetach=7 nestedSkipped=1 inFlightDetachDenied=1 foreignVtableDetachDenied=1 cachedStatic=%u cachedTextures=%u uploadedFrames=%u busyRejects=%u deviceLosses=%u nestedDenied=1 concurrentDenied=1 recoveredDevice=1 unsupportedApisRejected=1\n",
        driver,gLeft,gRight,(unsigned)present,presentInterceptions,renderedInPresent,
        cached.staticBuilds,cached.textureBuilds,cached.uploadedFrames,
        cached.busyRejects,cached.deviceLosses);
    chain.Reset();ctx.Reset();rtv.Reset();back.Reset();readback.Reset();device.Reset();
    DestroyWindow(hwnd);
    return 0;
}
