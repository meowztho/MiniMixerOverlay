#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <cstdio>
#include <vector>
#include "d3d11_renderer.h"
using Microsoft::WRL::ComPtr;
int main(){
 ComPtr<ID3D11Device>d;ComPtr<ID3D11DeviceContext>c;
 if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,nullptr,&c)))return 2;
 D3D11_TEXTURE2D_DESC td{};td.Width=td.Height=32;td.MipLevels=td.ArraySize=1;td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_UNORDERED_ACCESS;
 ComPtr<ID3D11Texture2D>target,other,uavTex;ComPtr<ID3D11RenderTargetView>rt,otherRt;ComPtr<ID3D11UnorderedAccessView>uav;
 if(FAILED(d->CreateTexture2D(&td,nullptr,&target))||FAILED(d->CreateTexture2D(&td,nullptr,&other))||FAILED(d->CreateTexture2D(&td,nullptr,&uavTex)))return 3;
 if(FAILED(d->CreateRenderTargetView(target.Get(),nullptr,&rt))||FAILED(d->CreateRenderTargetView(other.Get(),nullptr,&otherRt))||FAILED(d->CreateUnorderedAccessView(uavTex.Get(),nullptr,&uav)))return 4;
 auto original=otherRt.Get();auto raw=uav.Get();UINT keep=UINT(-1);
 c->OMSetRenderTargetsAndUnorderedAccessViews(1,&original,nullptr,1,1,&raw,&keep);
 ComPtr<ID3D11UnorderedAccessView>before,after;
 c->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,1,1,&before);
 std::vector<unsigned char>px(32*32*4,128);minimixer::FrameView f{px.data(),32,32,128};minimixer::D3d11Renderer renderer;
 bool draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);
 c->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,1,1,&after);
 printf("OM_UAV_SLOT1 draw=%d before_matches=%d after_matches=%d after_null=%d\n",draw,before.Get()==raw,after.Get()==raw,after.Get()==nullptr);
 c->ClearState();renderer.Reset();
 const bool pass=draw&&before.Get()==raw&&after.Get()==raw;
 puts(pass?"PASS OWN_D3D11_OM_UAV_SLOT1_PRESERVED":
           "FAIL OWN_D3D11_OM_UAV_SLOT1_PRESERVED");
 return pass?0:10;
}
