// Review-only WARP test. Owns all D3D11 objects. No hooks, audio, or other process access.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstdio>
#include <cstring>
#include <vector>
#include "d3d11_renderer.h"
using Microsoft::WRL::ComPtr;

static bool MakeTarget(ID3D11Device* d,ComPtr<ID3D11Texture2D>& t,
                       ComPtr<ID3D11RenderTargetView>& r,
                       ComPtr<ID3D11ShaderResourceView>& s) {
    D3D11_TEXTURE2D_DESC x{};
    x.Width=x.Height=32;x.MipLevels=x.ArraySize=1;
    x.Format=DXGI_FORMAT_B8G8R8A8_UNORM;x.SampleDesc.Count=1;
    x.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    return SUCCEEDED(d->CreateTexture2D(&x,nullptr,&t)) &&
           SUCCEEDED(d->CreateRenderTargetView(t.Get(),nullptr,&r)) &&
           SUCCEEDED(d->CreateShaderResourceView(t.Get(),nullptr,&s));
}
static unsigned Green(ID3D11Device* d,ID3D11DeviceContext* c,ID3D11Texture2D* t) {
    D3D11_TEXTURE2D_DESC x{};t->GetDesc(&x);
    x.Usage=D3D11_USAGE_STAGING;x.BindFlags=0;x.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if(FAILED(d->CreateTexture2D(&x,nullptr,&staging)))return 999;
    c->CopyResource(staging.Get(),t);
    D3D11_MAPPED_SUBRESOURCE m{};
    if(FAILED(c->Map(staging.Get(),0,D3D11_MAP_READ,0,&m)))return 998;
    unsigned g=static_cast<unsigned char*>(m.pData)[16*m.RowPitch+16*4+1];
    c->Unmap(staging.Get(),0);return g;
}
int main() {
    ComPtr<ID3D11Device> d;ComPtr<ID3D11DeviceContext> c;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,
        D3D11_SDK_VERSION,&d,nullptr,&c);
    if(FAILED(hr)){printf("SETUP_FAIL device=%08X\n",(unsigned)hr);return 2;}
    ComPtr<ID3D11Texture2D> t,other;
    ComPtr<ID3D11RenderTargetView> r,otherR;
    ComPtr<ID3D11ShaderResourceView> s,otherS;
    if(!MakeTarget(d.Get(),t,r,s)||!MakeTarget(d.Get(),other,otherR,otherS))return 3;
    std::vector<unsigned char> pixels(32*32*4);
    for(size_t i=0;i<pixels.size();i+=4){pixels[i+1]=128;pixels[i+3]=128;}
    minimixer::FrameView frame{pixels.data(),32,32,128};
    minimixer::D3d11Renderer renderer;
    const float black[4]={0,0,0,1};
    c->ClearRenderTargetView(r.Get(),black);
    const bool baseline=renderer.Draw(d.Get(),c.Get(),r.Get(),frame);
    unsigned baselineG=Green(d.Get(),c.Get(),t.Get());
    printf("BASELINE draw=%d green=%u expected=128\n",baseline,baselineG);

    const char* gsSource=R"(
      struct P { float4 pos:SV_POSITION;float2 uv:TEXCOORD; };
      [maxvertexcount(3)]
      void GS(triangle P v[3],inout TriangleStream<P> stream) {}
    )";
    ComPtr<ID3DBlob> gsBytes,err;
    hr=D3DCompile(gsSource,strlen(gsSource),nullptr,nullptr,nullptr,"GS","gs_5_0",0,0,&gsBytes,&err);
    if(FAILED(hr)){printf("SETUP_FAIL gs=%08X %s\n",(unsigned)hr,err?(char*)err->GetBufferPointer():"");return 4;}
    ComPtr<ID3D11GeometryShader> gs;
    if(FAILED(d->CreateGeometryShader(gsBytes->GetBufferPointer(),gsBytes->GetBufferSize(),nullptr,&gs)))return 5;
    c->GSSetShader(gs.Get(),nullptr,0);
    c->ClearRenderTargetView(r.Get(),black);
    const bool withGS=renderer.Draw(d.Get(),c.Get(),r.Get(),frame);
    unsigned gsG=Green(d.Get(),c.Get(),t.Get()); ComPtr<ID3D11GeometryShader> gsAfter; c->GSGetShader(&gsAfter,nullptr,nullptr); printf("GS_RESTORED=%d\n",gsAfter.Get()==gs.Get());
    printf("INHERITED_GS draw=%d green=%u expected=128 invisible=%d\n",withGS,gsG,withGS&&gsG==0);

    c->ClearState();
    ID3D11RenderTargetView* otherRaw=otherR.Get();
    c->OMSetRenderTargets(1,&otherRaw,nullptr);
    ID3D11ShaderResourceView* source=s.Get();
    c->PSSetShaderResources(0,1,&source);
    ComPtr<ID3D11ShaderResourceView> before,after;
    c->PSGetShaderResources(0,1,&before);
    const bool hazardDraw=renderer.Draw(d.Get(),c.Get(),r.Get(),frame);
    c->PSGetShaderResources(0,1,&after);
    printf("SAVED_SRV0_ALIAS draw=%d initial=%d restored=%d afterNull=%d\n",
        hazardDraw,before.Get()==s.Get(),after.Get()==s.Get(),after.Get()==nullptr);

    c->ClearState();
    c->OMSetRenderTargets(1,&otherRaw,nullptr);
    c->PSSetShaderResources(1,1,&source);
    ComPtr<ID3D11ShaderResourceView> extraBefore,extraAfter;
    c->PSGetShaderResources(1,1,&extraBefore);
    const bool extraDraw=renderer.Draw(d.Get(),c.Get(),r.Get(),frame);
    c->PSGetShaderResources(1,1,&extraAfter);
    printf("SAVED_SRV1_ALIAS draw=%d initial=%d restored=%d afterNull=%d\n",extraDraw,extraBefore.Get()==s.Get(),extraAfter.Get()==s.Get(),extraAfter.Get()==nullptr);
    c->ClearState();
    D3D11_QUERY_DESC predDesc{D3D11_QUERY_OCCLUSION_PREDICATE,0};
    ComPtr<ID3D11Predicate> pred;
    if(FAILED(d->CreatePredicate(&predDesc,&pred)))return 6;
    c->Begin(pred.Get());c->End(pred.Get());c->Flush();
    BOOL predicate=TRUE;hr=S_FALSE;
    for(unsigned i=0;i<1000&&hr==S_FALSE;++i){
        hr=c->GetData(pred.Get(),&predicate,sizeof(predicate),0);
        if(hr==S_FALSE)Sleep(1);
    }
    if(hr!=S_OK)return 7;
    c->ClearRenderTargetView(r.Get(),black);
    c->SetPredication(pred.Get(),FALSE);
    const bool predDraw=renderer.Draw(d.Get(),c.Get(),r.Get(),frame);
    ComPtr<ID3D11Predicate> predAfter; BOOL predValueAfter=TRUE; c->GetPredication(&predAfter,&predValueAfter); printf("PREDICATE_RESTORED=%d value=%d\n",predAfter.Get()==pred.Get(),predValueAfter);
    c->SetPredication(nullptr,FALSE);
    unsigned predG=Green(d.Get(),c.Get(),t.Get());
    printf("INHERITED_PREDICATION value=%d draw=%d green=%u expected=128 invisible=%d\n",
        predicate,predDraw,predG,predDraw&&predG==0);
    c->ClearState();
    renderer.Reset();
    const bool pass=baseline&&baselineG==128&&withGS&&gsG==128&&hazardDraw&&
        before.Get()==s.Get()&&after.Get()==s.Get()&&predDraw&&predG==128;
    printf("PASS D3D11_RESTORED_SRV_GS_PRED=%d\n",pass?1:0);
    printf("ALL_STATE_BINDINGS_RESTORED=%d\n", pass && extraAfter.Get()==s.Get()); return pass && extraAfter.Get()==s.Get() ? 0 : 10;
}
