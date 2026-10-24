// Own WARP-only microbenchmark of the actual scoped D3D11 state guard.
// Does not load MiniMixer into any game, process or driver hook.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_1.h>
#include <wrl/client.h>
#include <chrono>
#include <algorithm>
#include <array>
#include <vector>
#include <cstdio>
#include <cstring>
#include "d3d11_state_guard.h"
using Microsoft::WRL::ComPtr;
int main(int argc,char** argv){
  const bool hw=argc>1 && strcmp(argv[1],"--hardware")==0;
  ComPtr<ID3D11Device> dev;
  ComPtr<ID3D11DeviceContext> ctx;
  D3D_FEATURE_LEVEL level{};
  const D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_1,D3D_FEATURE_LEVEL_11_0};
  HRESULT hr=D3D11CreateDevice(nullptr,hw?D3D_DRIVER_TYPE_HARDWARE:D3D_DRIVER_TYPE_WARP,nullptr,0,
       levels,2,D3D11_SDK_VERSION,&dev,&level,&ctx);
  if(FAILED(hr))return 2;
  D3D11_TEXTURE2D_DESC td{};
  td.Width=128;td.Height=128;td.MipLevels=1;td.ArraySize=1;td.SampleDesc.Count=1;
  td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
  td.BindFlags=D3D11_BIND_RENDER_TARGET;
  ComPtr<ID3D11Texture2D> texture;
  ComPtr<ID3D11RenderTargetView> rtv;
  if(FAILED(dev->CreateTexture2D(&td,nullptr,&texture)) ||
     FAILED(dev->CreateRenderTargetView(texture.Get(),nullptr,&rtv)))return 3;
  ID3D11RenderTargetView* ptr=rtv.Get();
  ctx->OMSetRenderTargets(1,&ptr,nullptr);
  constexpr int rounds=600;
  std::vector<double> samples;
  samples.reserve(rounds);
  for(int i=0;i<rounds+80;++i){
    auto start=std::chrono::steady_clock::now();
    {minimixer::ScopedD3d11DrawState scoped(ctx.Get());}
    auto duration=std::chrono::duration<double,std::micro>(
       std::chrono::steady_clock::now()-start).count();
    if(i>=80)samples.push_back(duration);
  }
  std::sort(samples.begin(),samples.end());
  auto sum=0.0;
  for(double us:samples)sum+=us;
  const auto median=samples[rounds/2],p95=samples[rounds*95/100];
  printf("D3D11_GUARD driver=%s feature_level=%x samples=%d mean_us=%.2f median_us=%.2f p95_us=%.2f max_us=%.2f\n",
         hw?"HARDWARE":"WARP",(unsigned)level,rounds,sum/rounds,median,p95,samples.back());
  return 0;
}
