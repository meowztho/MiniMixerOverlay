// Reviewer-only WARP consumer. Product headers unchanged. Own GPU objects only.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11_1.h>
#include <d3d11sdklayers.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstdio>
#include <cstring>
#include <vector>
#include "d3d11_renderer.h"
using Microsoft::WRL::ComPtr;
static LONG WINAPI Crash(EXCEPTION_POINTERS* e){HMODULE mod{};wchar_t path[MAX_PATH]{};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(e->ExceptionRecord->ExceptionAddress),&mod);if(mod)GetModuleFileNameW(mod,path,MAX_PATH);printf("EXCEPTION code=%08X address=%p module=%ls offset=%llu\n",e->ExceptionRecord->ExceptionCode,e->ExceptionRecord->ExceptionAddress,path,static_cast<unsigned long long>(reinterpret_cast<UINT_PTR>(e->ExceptionRecord->ExceptionAddress)-reinterpret_cast<UINT_PTR>(mod)));return EXCEPTION_EXECUTE_HANDLER;}
static unsigned Green(ID3D11Device* d,ID3D11DeviceContext* c,ID3D11Texture2D* t){
 D3D11_TEXTURE2D_DESC td{};t->GetDesc(&td);td.Usage=D3D11_USAGE_STAGING;td.BindFlags=0;td.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
 ComPtr<ID3D11Texture2D>s;if(FAILED(d->CreateTexture2D(&td,nullptr,&s)))return 999;
 c->CopyResource(s.Get(),t);D3D11_MAPPED_SUBRESOURCE m{};
 if(FAILED(c->Map(s.Get(),0,D3D11_MAP_READ,0,&m)))return 998;
 unsigned g=static_cast<unsigned char*>(m.pData)[16*m.RowPitch+16*4+1];c->Unmap(s.Get(),0);return g;
}
static void Messages(ID3D11InfoQueue* q){
 if(!q)return;
 for(UINT64 i=0;i<q->GetNumStoredMessages();++i){SIZE_T n{};q->GetMessage(i,nullptr,&n);std::vector<char>b(n);auto*m=reinterpret_cast<D3D11_MESSAGE*>(b.data());q->GetMessage(i,m,&n);if(m->Severity<=D3D11_MESSAGE_SEVERITY_WARNING)printf("D3D_DEBUG id=%u severity=%u %s\n",m->ID,m->Severity,m->pDescription);}
 q->ClearStoredMessages();
}
int main(int argc,char** argv){
 setvbuf(stdout,nullptr,_IONBF,0);
 SetUnhandledExceptionFilter(Crash);const bool clearTess=argc>1&&strcmp(argv[1],"--clear-tess")==0;
 ComPtr<ID3D11Device>d;ComPtr<ID3D11DeviceContext>c;D3D_FEATURE_LEVEL level{};D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_1};
 HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_DEBUG,levels,1,D3D11_SDK_VERSION,&d,&level,&c);
 if(FAILED(hr)){printf("SETUP_FAIL debug11_1=%08X\n",unsigned(hr));return 2;}
 ComPtr<ID3D11InfoQueue>q;d.As(&q);printf("DEVICE feature=%04X debug=%d\n",level,q!=nullptr);
 D3D11_TEXTURE2D_DESC td{};td.Width=td.Height=32;td.MipLevels=td.ArraySize=1;td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_UNORDERED_ACCESS;
 ComPtr<ID3D11Texture2D>target,other,uavTex;ComPtr<ID3D11RenderTargetView>rt,otherRt;ComPtr<ID3D11UnorderedAccessView>targetUav,uav;
 if(FAILED(d->CreateTexture2D(&td,nullptr,&target))||FAILED(d->CreateTexture2D(&td,nullptr,&other))||FAILED(d->CreateTexture2D(&td,nullptr,&uavTex)))return 3;
 if(FAILED(d->CreateRenderTargetView(target.Get(),nullptr,&rt))||FAILED(d->CreateRenderTargetView(other.Get(),nullptr,&otherRt))||FAILED(d->CreateUnorderedAccessView(target.Get(),nullptr,&targetUav))||FAILED(d->CreateUnorderedAccessView(uavTex.Get(),nullptr,&uav)))return 4;
 std::vector<unsigned char>px(32*32*4);for(size_t i=0;i<px.size();i+=4){px[i+1]=128;px[i+3]=128;}
 minimixer::FrameView f{px.data(),32,32,128};minimixer::D3d11Renderer renderer;const float black[]{0,0,0,1};
 c->ClearRenderTargetView(rt.Get(),black);bool draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);auto baseline=Green(d.Get(),c.Get(),target.Get());printf("BASELINE draw=%d green=%u\n",draw,baseline);Messages(q.Get());
 auto original=otherRt.Get();auto raw=uav.Get();UINT keep=UINT(-1);
 c->ClearState();c->OMSetRenderTargetsAndUnorderedAccessViews(1,&original,nullptr,9,1,&raw,&keep);
 ComPtr<ID3D11UnorderedAccessView>before,after;c->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,9,1,&before);
 draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);c->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,9,1,&after);
 bool omPass=before.Get()==raw&&after.Get()==raw;printf("OM_UAV_SLOT9 draw=%d before_matches=%d after_matches=%d after_null=%d\n",draw,before.Get()==raw,after.Get()==raw,after==nullptr);Messages(q.Get());
 c->ClearState();c->OMSetRenderTargets(1,&original,nullptr);auto alias=targetUav.Get();c->CSSetUnorderedAccessViews(0,1,&alias,&keep);
 before.Reset();after.Reset();c->CSGetUnorderedAccessViews(0,1,&before);draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);c->CSGetUnorderedAccessViews(0,1,&after);
 bool csPass=before.Get()==alias&&after.Get()==alias;printf("CS_UAV_TARGET_ALIAS draw=%d before_matches=%d after_matches=%d after_null=%d\n",draw,before.Get()==alias,after.Get()==alias,after==nullptr);Messages(q.Get());
 const char* hlsl=R"(
 struct P {float4 pos:SV_POSITION;float2 uv:TEXCOORD;};
 struct T {float edge[3]:SV_TessFactor;float inside:SV_InsideTessFactor;};
 T Patch(InputPatch<P,3> p){T o;o.edge[0]=o.edge[1]=o.edge[2]=1;o.inside=1;return o;}
 [domain("tri")][partitioning("integer")][outputtopology("triangle_cw")][outputcontrolpoints(3)][patchconstantfunc("Patch")]
 P HS(InputPatch<P,3> p,uint i:SV_OutputControlPointID){return p[i];}
 [domain("tri")]
 P DS(T t,float3 b:SV_DomainLocation,const OutputPatch<P,3> p){P o;o.pos=b.x*p[0].pos+b.y*p[1].pos+b.z*p[2].pos;o.uv=b.x*p[0].uv+b.y*p[1].uv+b.z*p[2].uv;return o;}
 )";
 ComPtr<ID3DBlob>hsb,dsb,err;hr=D3DCompile(hlsl,strlen(hlsl),nullptr,nullptr,nullptr,"HS","hs_5_0",0,0,&hsb,&err);if(FAILED(hr)){printf("HS_FAIL %s\n",err?(char*)err->GetBufferPointer():"");return 5;}
 hr=D3DCompile(hlsl,strlen(hlsl),nullptr,nullptr,nullptr,"DS","ds_5_0",0,0,&dsb,&err);if(FAILED(hr))return 6;
 ComPtr<ID3D11HullShader>hs;ComPtr<ID3D11DomainShader>ds;if(FAILED(d->CreateHullShader(hsb->GetBufferPointer(),hsb->GetBufferSize(),nullptr,&hs))||FAILED(d->CreateDomainShader(dsb->GetBufferPointer(),dsb->GetBufferSize(),nullptr,&ds)))return 7;
 c->ClearState();c->HSSetShader(hs.Get(),nullptr,0);c->DSSetShader(ds.Get(),nullptr,0);c->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_3_CONTROL_POINT_PATCHLIST);
 // Prove the inherited tessellation pipeline is valid BEFORE the overlay.
 const char* gameShader=R"(
 struct P {float4 pos:SV_POSITION;float2 uv:TEXCOORD;};
 P VS(uint id:SV_VertexID){float2 xy[3]={float2(-1,-1),float2(-1,3),float2(3,-1)};P p;p.pos=float4(xy[id],0,1);p.uv=float2(0,0);return p;}
 float4 PS(P p):SV_TARGET{return float4(0,0.5,0,1);})";
 ComPtr<ID3DBlob>vsb,psb;
 if(FAILED(D3DCompile(gameShader,strlen(gameShader),nullptr,nullptr,nullptr,"VS","vs_5_0",0,0,&vsb,&err))||FAILED(D3DCompile(gameShader,strlen(gameShader),nullptr,nullptr,nullptr,"PS","ps_5_0",0,0,&psb,&err)))return 8;
 ComPtr<ID3D11VertexShader>gameVS;ComPtr<ID3D11PixelShader>gamePS;
 if(FAILED(d->CreateVertexShader(vsb->GetBufferPointer(),vsb->GetBufferSize(),nullptr,&gameVS))||FAILED(d->CreatePixelShader(psb->GetBufferPointer(),psb->GetBufferSize(),nullptr,&gamePS)))return 9;
 D3D11_RASTERIZER_DESC rd{};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;ComPtr<ID3D11RasterizerState>rs;if(FAILED(d->CreateRasterizerState(&rd,&rs)))return 11;
 D3D11_VIEWPORT vp{0,0,32,32,0,1};c->RSSetState(rs.Get());c->RSSetViewports(1,&vp);auto rraw=rt.Get();c->OMSetRenderTargets(1,&rraw,nullptr);c->VSSetShader(gameVS.Get(),nullptr,0);c->PSSetShader(gamePS.Get(),nullptr,0);
 c->ClearRenderTargetView(rt.Get(),black);c->Draw(3,0);unsigned gameGreen=Green(d.Get(),c.Get(),target.Get());printf("VALID_GAME_TESSELLATION green=%u expected=128\n",gameGreen);Messages(q.Get());if(gameGreen!=128)return 12;
 if(clearTess){c->HSSetShader(nullptr,nullptr,0);c->DSSetShader(nullptr,nullptr,0);}
 puts(clearTess?"BEFORE_TESS_DRAW control_cleared=1":"BEFORE_TESS_DRAW hs_ds_bound=1");Messages(q.Get());
 c->ClearRenderTargetView(rt.Get(),black);draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);printf("AFTER_TESS_DRAW returned=%d\n",draw);Messages(q.Get());unsigned withTess=Green(d.Get(),c.Get(),target.Get());
 ComPtr<ID3D11HullShader>hsAfter;ComPtr<ID3D11DomainShader>dsAfter;c->HSGetShader(&hsAfter,nullptr,nullptr);c->DSGetShader(&dsAfter,nullptr,nullptr);
 printf("INHERITED_HS_DS draw=%d green=%u expected=128 hs_restored=%d ds_restored=%d\n",draw,withTess,hsAfter.Get()==hs.Get(),dsAfter.Get()==ds.Get());Messages(q.Get());
 c->HSSetShader(nullptr,nullptr,0);c->DSSetShader(nullptr,nullptr,0);c->ClearRenderTargetView(rt.Get(),black);draw=renderer.Draw(d.Get(),c.Get(),rt.Get(),f);unsigned cleared=Green(d.Get(),c.Get(),target.Get());printf("CONTROL_HS_DS_CLEARED draw=%d green=%u\n",draw,cleared);Messages(q.Get());
 c->ClearState();renderer.Reset();
 printf("RESULT om64=%d cs_alias=%d tessellation=%d\n",omPass,csPass,withTess==128);
 const bool restored=clearTess || (hsAfter.Get()==hs.Get() && dsAfter.Get()==ds.Get());
 const bool pass=baseline==128&&omPass&&csPass&&withTess==128&&cleared==128&&restored;
 printf("PASS_OWNED_EXTENDED_PIPELINE=%d hs_ds_restored=%d\n",pass,restored);
 return pass?0:10;
}
