// Independent native IPC/frame consumer for MiniMixer, NOT a game injector.
// Defaults to headless; --show uses Windows layered desktop compositor only.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <string>
#include <vector>
#include "d3d11_ipc_sink.h"
#include "d3d12_owned_gpu_sink.h"
#include "owned_pbgra_ipc_sink.h"
#include "wpf_frame_mailbox.h"
#include "owned_input_router.h"
#include <windowsx.h>
#include <limits>
namespace {
constexpr UINT CONNECT=0x8201, ACK=0x8202, CLOSE=0x8203, HELLO=WM_APP+11, RENDER=WM_APP+12;
struct Runtime {
 HWND host{}, ipc{}, panel{},gpuWindow{},inputWindow{}; DWORD hostPid{}; HANDLE map{}, mutex{}; const unsigned char* view{};
 std::string mapName, mutexName; std::vector<unsigned char> frame,inputPixels; int width{},height{},x{},y{}; int inputX{INT_MIN},inputY{INT_MIN},inputWidth{},inputHeight{}; unsigned inputWindowId{};
 bool show{},gpu{},gpuWarp{},gpuHook{},gpuD3d12{},gpuOverlay{},frameMailbox{},inputProbe{},inputIntercept{},inputDone{},pending{},stop{},stalled{}; int inputCommands{},inputMaskUpdates{},inputRectUpdates{},acks{},inits{},notices{},copies{},draws{},errors{},misses{},waits{},stallMs{},inputForwarded{},inputPassThrough{};
} r;
minimixer::OwnedD3d11IpcSink gpuSink;
minimixer::OwnedD3d12GpuSink d3d12Sink;
minimixer::OwnedPbgraIpcSink pbgraOverlay;
minimixer::WpfFrameMailbox frameMailbox;
minimixer::OwnedInputRouter inputRouter;
bool i32(const unsigned char* p,size_t n,size_t& at,int32_t& v) {
 if(at>n||n-at<4)return false;memcpy(&v,p+at,4);at+=4;return true;
}
bool str(const unsigned char* p,size_t n,size_t& at,std::string& v) {
 int32_t len{};if(!i32(p,n,at,len)||len<0||(size_t)len>n-at)return false;
 v.assign((const char*)(p+at),len);at+=len;return true;
}
std::string field(const std::string& s,const char* key) {
 auto at=s.find(std::string("\"")+key+"\"");
 if(at==std::string::npos)return {};at=s.find(':',at);if(at==std::string::npos)return {};
 at=s.find('"',at+1);if(at==std::string::npos)return {};
 auto end=s.find('"',++at);return end==std::string::npos?std::string():s.substr(at,end-at);
}
int number(const std::string& s,const char* key,int defaultValue) {
 auto at=s.find(std::string("\"")+key+"\"");
 if(at==std::string::npos)return defaultValue;at=s.find(':',at);if(at==std::string::npos)return defaultValue;
 const char* p=s.c_str()+at+1;char* end{};long n=strtol(p,&end,10);
 return end==p||n<-30000||n>30000?defaultValue:(int)n;
}
unsigned uintField(const std::string& json,const char* key) {
 auto at=json.find(std::string("\"")+key+"\"");
 if(at==std::string::npos)return 0;
 at=json.find(':',at);if(at==std::string::npos)return 0;
 const char* p=json.c_str()+at+1;
 while(*p==' '||*p=='\t')++p;
 if(*p<'0'||*p>'9')return 0;
 char* end{};
 unsigned long long value=strtoull(p,&end,10);
 if(end==p || value==0 || value>UINT32_MAX ||
    (*end!=',' && *end!='}' && *end!=' '))return 0;
 return static_cast<unsigned>(value);
}
bool sendInputEvent(const minimixer::InputDecision& e) {
 if(!e.forwarded || !r.inputProbe || !r.inputIntercept || !IsWindow(r.host))
    return false;
 char json[160]{};
 int n=snprintf(json,sizeof(json),
    "{\"windowId\":%u,\"msg\":%u,\"wparam\":%u,\"lparam\":%u}",
    e.windowId,e.msg,e.wparam,e.lparam);
 if(n<=0 || static_cast<size_t>(n)>=sizeof(json))return false;
 std::vector<unsigned char> bytes(12,0);
 auto add32=[&](int32_t v){const auto* ptr=reinterpret_cast<const unsigned char*>(&v);
                          bytes.insert(bytes.end(),ptr,ptr+4);};
 auto addString=[&](const char* v){auto len=static_cast<int32_t>(strlen(v));
                         add32(len);bytes.insert(bytes.end(),v,v+len);};
 add32(100);addString("game.input");addString(json);
 COPYDATASTRUCT cds{};
 cds.dwData=GetCurrentProcessId();
 cds.cbData=static_cast<DWORD>(bytes.size());
 cds.lpData=bytes.data();
 DWORD_PTR result{};
 if(!SendMessageTimeoutA(r.host,WM_COPYDATA,0,
    reinterpret_cast<LPARAM>(&cds),SMTO_ABORTIFHUNG|SMTO_BLOCK,40,&result))
    return false;
 ++r.inputForwarded;return true;
}
LRESULT CALLBACK inputWindowProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){
 if(msg!=WM_MOUSEMOVE && msg!=WM_LBUTTONDOWN && msg!=WM_LBUTTONUP &&
    msg!=WM_MOUSELEAVE && msg!=WM_KEYDOWN)
    return DefWindowProcA(hwnd,msg,wp,lp);
 POINT pos{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
 ClientToScreen(hwnd,&pos);
 minimixer::InputFrame frame{
    r.inputPixels.data(),static_cast<unsigned>(r.width),
    static_cast<unsigned>(r.height),static_cast<unsigned>(r.width)*4,
    r.inputPixels.size(),r.x,r.y,r.inputWindowId,r.inputIntercept,r.inputProbe};
 auto decision=inputRouter.Route(frame,msg,wp,lp,pos.x,pos.y);
 if(decision.forwarded && !sendInputEvent(decision))++r.errors;
 if(!decision.consumed)++r.inputPassThrough;
 return decision.consumed?0:DefWindowProcA(hwnd,msg,wp,lp);
}
void RunOwnedInputRoundtrip(){
 if(!r.inputProbe || !r.inputWindow || r.frame.empty() ||
    r.width<4 || r.height<4)return;
 // Keep the hit-test alpha mask synchronized with EVERY rendered frame,
 // not just the first synthetic test. Source IPC bytes remain immutable.
 r.inputPixels=r.frame;
 ++r.inputMaskUpdates;
 r.inputPixels[3]=0; // One explicit transparent test pixel in local copy.
 if(r.inputX!=r.x || r.inputY!=r.y ||
    r.inputWidth!=r.width || r.inputHeight!=r.height){
  SetWindowPos(r.inputWindow,nullptr,r.x,r.y,r.width,r.height,
     SWP_NOACTIVATE|SWP_NOZORDER|SWP_NOOWNERZORDER);
  r.inputX=r.x;r.inputY=r.y;
  r.inputWidth=r.width;r.inputHeight=r.height;
  ++r.inputRectUpdates;
 }
 if(r.inputDone || !r.inputIntercept || !r.inputWindowId)return;
 int hitX=-1,hitY=-1;
 for(int y=0;y<r.height && hitX<0;++y)
  for(int x=1;x<r.width;++x)
   if(r.inputPixels[(static_cast<size_t>(y)*r.width+x)*4+3]>24){
    hitX=x;hitY=y;break;
   }
 if(hitX<0){++r.errors;return;}
 // SendMessage only targets our own private test HWND. No pointer motion or
 // OS-global synthetic input occurs. Captured drag release is exercised.
 SendMessageA(r.inputWindow,WM_MOUSEMOVE,0,MAKELPARAM(hitX,hitY));
 SendMessageA(r.inputWindow,WM_LBUTTONDOWN,MK_LBUTTON,MAKELPARAM(hitX,hitY));
 SendMessageA(r.inputWindow,WM_MOUSEMOVE,MK_LBUTTON,MAKELPARAM(0,0));
 SendMessageA(r.inputWindow,WM_LBUTTONUP,0,MAKELPARAM(0,0));
 SendMessageA(r.inputWindow,WM_MOUSEMOVE,0,MAKELPARAM(0,0));
 SendMessageA(r.inputWindow,WM_KEYDOWN,VK_RETURN,0);
 r.inputDone=true;
 if(r.inputForwarded!=4 || r.errors>0)++r.errors;
}
void closeMap() {
 if(r.view)UnmapViewOfFile(r.view);if(r.map)CloseHandle(r.map);if(r.mutex)CloseHandle(r.mutex);
 r.view=nullptr;r.map=nullptr;r.mutex=nullptr;
}
bool openMap() {
 closeMap();if(r.mapName.empty()||r.mutexName.empty())return false;
 r.map=OpenFileMappingA(FILE_MAP_READ,FALSE,r.mapName.c_str());
 r.mutex=OpenMutexA(SYNCHRONIZE|MUTEX_MODIFY_STATE,FALSE,r.mutexName.c_str());
 if(!r.map||!r.mutex){closeMap();return false;}
 r.view=(const unsigned char*)MapViewOfFile(r.map,FILE_MAP_READ,0,0,0);
 if(!r.view){closeMap();return false;}return true;
}
bool capture() {
 if(!r.view&&!openMap())return false;
 DWORD state=WaitForSingleObject(r.mutex,8);
 if(state!=WAIT_OBJECT_0){if(state==WAIT_ABANDONED)ReleaseMutex(r.mutex);++r.waits;return false;}
 bool ok=false;
 do {
  MEMORY_BASIC_INFORMATION info{};
  if(!VirtualQuery(r.view,&info,sizeof(info))||info.RegionSize<8)break;
  int32_t w{},h{};memcpy(&w,r.view,4);memcpy(&h,r.view+4,4);
  uint64_t bytes=(uint64_t)std::max(0,w)*(uint64_t)std::max(0,h)*4;
  if(w<2||h<2||bytes>4096ull*4096ull*4||bytes+8>info.RegionSize)break;
  r.width=w;r.height=h;r.frame.assign(r.view+8,r.view+8+bytes);ok=true;
 }while(false);
 ReleaseMutex(r.mutex);if(ok)++r.copies;return ok;
}
bool showFrame() {
 if(!r.show||!r.panel||r.frame.empty())return false;
 HDC dc=CreateCompatibleDC(nullptr);if(!dc)return false;
 BITMAPINFO bi{};bi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
 bi.bmiHeader.biWidth=r.width;bi.bmiHeader.biHeight=-r.height;bi.bmiHeader.biPlanes=1;
 bi.bmiHeader.biBitCount=32;bi.bmiHeader.biCompression=BI_RGB;
 void* pixels{};HBITMAP bmp=CreateDIBSection(dc,&bi,DIB_RGB_COLORS,&pixels,nullptr,0);
 if(!bmp||!pixels){if(bmp)DeleteObject(bmp);DeleteDC(dc);return false;}
 HGDIOBJ old=SelectObject(dc,bmp);memcpy(pixels,r.frame.data(),r.frame.size());
 POINT dst{r.x,r.y},origin{};SIZE size{r.width,r.height};
 BLENDFUNCTION blend{AC_SRC_OVER,0,255,AC_SRC_ALPHA};
 BOOL rendered=UpdateLayeredWindow(r.panel,nullptr,&dst,&size,dc,&origin,0,&blend,ULW_ALPHA);
 SelectObject(dc,old);DeleteObject(bmp);DeleteDC(dc);
 if(rendered){ShowWindow(r.panel,SW_SHOWNOACTIVATE);++r.draws;}return rendered!=0;
}
void process(const std::string& type,const std::string& json) {
 if(type=="command.input.intercept") {
  ++r.inputCommands;
  r.inputIntercept=json.find("\"intercept\":true")!=std::string::npos;
  if(!r.inputIntercept)inputRouter.Reset();return;
 }
 if(type=="overlay.init") {
  ++r.inits;auto m=field(json,"shareMemMutex"),b=field(json,"bufferName");
  if(m.empty()||b.empty()){++r.errors;return;}
  if(m!=r.mutexName||b!=r.mapName){closeMap();r.mutexName=m;r.mapName=b;}
  r.x=number(json,"x",r.x);r.y=number(json,"y",r.y);
  if(r.frameMailbox)frameMailbox.Configure(m,b,r.x,r.y);
  r.inputWindowId=uintField(json,"windowId");openMap();return;
 }
 if(type=="window.bounds") {
  auto b=field(json,"bufferName");if(!b.empty()&&b!=r.mapName){closeMap();r.mapName=b;}
  r.x=number(json,"x",r.x);r.y=number(json,"y",r.y);
  if(r.frameMailbox){if(!b.empty())frameMailbox.SetBuffer(b);
                     frameMailbox.SetBounds(r.x,r.y);}return;
 }
 if(type=="window.framebuffer") {
  ++r.notices;if(!r.pending){r.pending=true;PostMessage(r.ipc,RENDER,0,0);}
 }
}
bool hello() {
 if(!IsWindow(r.host))return false;
 std::vector<unsigned char> message(12,0);
 auto add32=[&](int32_t n){auto p=(unsigned char*)&n;message.insert(message.end(),p,p+4);};
 auto addstr=[&](const char* text){int32_t n=(int32_t)strlen(text);add32(n);message.insert(message.end(),text,text+n);};
 add32(100);addstr("game.process");addstr(R"({"inputStateReplay":true,"experimentalMainInput":true})");
 COPYDATASTRUCT data{};data.dwData=GetCurrentProcessId();data.cbData=(DWORD)message.size();data.lpData=message.data();
 DWORD_PTR out{};return SendMessageTimeoutA(r.host,WM_COPYDATA,0,(LPARAM)&data,
  SMTO_ABORTIFHUNG|SMTO_BLOCK,200,&out)!=0;
}
LRESULT CALLBACK windowProc(HWND h,UINT msg,WPARAM w,LPARAM l) {
 switch(msg) {
 case ACK:
  if((DWORD)w==r.hostPid){++r.acks;PostMessage(h,HELLO,0,0);}return 0;
 case HELLO:
  if(!hello())++r.errors;return 0;
 case WM_COPYDATA: {
  auto data=(const COPYDATASTRUCT*)l;
  if(!data||!data->lpData||data->cbData<20||data->cbData>1048576||
     (DWORD)data->dwData!=r.hostPid){++r.errors;return FALSE;}
  auto p=(const unsigned char*)data->lpData;size_t at=0;int32_t dir{},reserved{},id{};
  if(!i32(p,data->cbData,at,dir)||!i32(p,data->cbData,at,reserved)||
     !i32(p,data->cbData,at,reserved)||!i32(p,data->cbData,at,id)||dir!=1||id!=100)
   {++r.errors;return FALSE;}
  std::string type,json;
  if(!str(p,data->cbData,at,type)||!str(p,data->cbData,at,json))
   {++r.errors;return FALSE;}
  process(type,json);return TRUE;
 }
 case RENDER: {
  r.pending=false;
  // An unavailable map/frame during resize or re-init is a dropped frame,
  // not a malformed IPC message. Never render stale pixels on a miss.
  const bool accepted=r.frameMailbox?frameMailbox.Capture():capture();
  if(!accepted)++r.misses;
  else {
   // The opt-in input test uses the SAME owned HWND as the GPU swapchain
   // when a GPU mode is active; there is no second input proxy window.
   if(r.inputProbe)RunOwnedInputRoundtrip();
   if(r.show&&!showFrame())++r.errors;
   if(r.gpu) {
   auto latest=r.frameMailbox?frameMailbox.Latest():std::shared_ptr<const minimixer::WpfFrameSnapshot>{};
   minimixer::FrameView frame=r.frameMailbox ? latest->View() :
       minimixer::FrameView{r.frame.data(),static_cast<unsigned>(r.width),
                            static_cast<unsigned>(r.height),
                            static_cast<unsigned>(r.width)*4};
   if(r.gpuOverlay) {
    if(!pbgraOverlay.Prepare(r.gpuWindow,frame) ||
       !pbgraOverlay.Render(frame)) {++pbgraOverlay.gpuErrors;++r.errors;}
   } else if(r.gpuD3d12) {
    if(!d3d12Sink.Prepare(r.gpuWindow,frame,r.gpuWarp) ||
       !d3d12Sink.Render(frame,r.gpuHook)) {++d3d12Sink.gpuErrors;++r.errors;}
   } else if(!gpuSink.Render(r.gpuWindow,frame,r.gpuWarp,r.gpuHook))++r.errors;
   }
  }
  return 0;
 }
 case CLOSE: r.stop=true;PostQuitMessage(0);return 0;
 }
 return DefWindowProcA(h,msg,w,l);
}
}
int main(int argc,char** argv) {
 std::string hostName="minimixer_graphics_v1";int duration=0;
 for(int i=1;i<argc;++i){
  std::string arg=argv[i];
  if(arg=="--show")r.show=true;
  else if(arg=="--gpu")r.gpu=true;
  else if(arg=="--gpu-hook"){r.gpu=true;r.gpuHook=true;}
  else if(arg=="--gpu-hook-warp"){r.gpu=true;r.gpuHook=true;r.gpuWarp=true;}
  else if(arg=="--gpu-warp"){r.gpu=true;r.gpuWarp=true;}
  else if(arg=="--gpu-d3d12"){r.gpu=true;r.gpuD3d12=true;}
  else if(arg=="--gpu-d3d12-overlay"){r.gpu=true;r.gpuD3d12=true;r.gpuOverlay=true;}
  else if(arg=="--gpu-d3d12-overlay-mailbox"){
    r.gpu=true;r.gpuD3d12=true;r.gpuOverlay=true;r.frameMailbox=true;
  }
  else if(arg=="--gpu-d3d12-hook"){r.gpu=true;r.gpuD3d12=true;r.gpuHook=true;}
  else if(arg=="--gpu-d3d12-warp"){r.gpu=true;r.gpuD3d12=true;r.gpuWarp=true;}
  else if(arg=="--gpu-d3d12-hook-warp"){r.gpu=true;r.gpuD3d12=true;r.gpuHook=true;r.gpuWarp=true;}
  else if(arg=="--input-probe")r.inputProbe=true;
  else if(arg=="--headless")r.show=false;
  else if(arg.rfind("--host=",0)==0)hostName=arg.substr(7);
  else if(arg.rfind("--duration-ms=",0)==0)duration=std::max(0,atoi(arg.c_str()+14));
  else if(arg.rfind("--stall-ms=",0)==0)r.stallMs=std::max(0,atoi(arg.c_str()+11));
  else{fprintf(stderr,"INVALID_ARG %s\n",arg.c_str());return 3;}
 }
 if(r.gpu&&r.show){fputs("GPU_AND_LAYERED_MODES_ARE_EXCLUSIVE\n",stderr);return 3;}
 if(r.inputProbe && r.show){fputs("INPUT_AND_LAYERED_ARE_EXCLUSIVE\n",stderr);return 3;}
 r.host=FindWindowA("STATIC",hostName.c_str());
 if(!r.host){fprintf(stderr,"HOST_NOT_FOUND\n");return 4;}
 GetWindowThreadProcessId(r.host,&r.hostPid);if(!r.hostPid)return 5;
 WNDCLASSA cls{};cls.lpfnWndProc=windowProc;cls.hInstance=GetModuleHandleA(nullptr);
 cls.lpszClassName="MiniMixerIndependentCandidateIpc";if(!RegisterClassA(&cls))return 6;
 r.ipc=CreateWindowExA(0,cls.lpszClassName,"Candidate IPC",0,0,0,0,0,HWND_MESSAGE,
                      nullptr,cls.hInstance,nullptr);if(!r.ipc)return 7;
 if(r.show) {
  WNDCLASSA panel=cls;panel.lpszClassName="MiniMixerIndependentCandidatePanel";
  panel.lpfnWndProc=DefWindowProcA;if(!RegisterClassA(&panel))return 8;
  r.panel=CreateWindowExA(WS_EX_LAYERED|WS_EX_TOPMOST|WS_EX_TOOLWINDOW|
       WS_EX_NOACTIVATE|WS_EX_TRANSPARENT,panel.lpszClassName,"MiniMixer candidate",
       WS_POPUP,0,0,1,1,nullptr,nullptr,cls.hInstance,nullptr);
  if(!r.panel)return 9;
 }
 const char* ownedClass=cls.lpszClassName;
 if(r.inputProbe) {
  WNDCLASSA inputClass=cls;
  inputClass.lpszClassName="MiniMixerOwnedInputTest";
  inputClass.lpfnWndProc=inputWindowProc;
  if(!RegisterClassA(&inputClass))return 13;
  ownedClass=inputClass.lpszClassName;
 }
 if(r.gpu || r.inputProbe){
  // One HWND owns both GPU swapchain and mouse input for combined tests.
  // Without --input-probe, the original GPU-only WndProc is unchanged.
  HWND owned=CreateWindowExA(0,ownedClass,"Candidate owned renderer",
                     WS_POPUP,0,0,320,240,nullptr,nullptr,cls.hInstance,nullptr);
  if(!owned)return r.gpu?11:14;
  if(r.gpu)r.gpuWindow=owned;
  if(r.inputProbe)r.inputWindow=owned;
 }
 if(!PostMessage(r.host,CONNECT,(WPARAM)r.ipc,0))return 10;
 ULONGLONG begin=GetTickCount64();MSG message{};
 while(!r.stop){
  while(PeekMessage(&message,nullptr,0,0,PM_REMOVE)){
   if(message.message==WM_QUIT){r.stop=true;break;}
   TranslateMessage(&message);DispatchMessage(&message);
  }
  // Offline stall test deliberately blocks this candidate's IPC HWND once.
  if(r.stallMs>0&&!r.stalled&&(r.frameMailbox?frameMailbox.successful.load():r.copies)>=6){r.stalled=true;Sleep((DWORD)r.stallMs);}
  if(duration>0&&GetTickCount64()-begin>=(ULONGLONG)duration)break;
  if(!IsWindow(r.host))break;
  Sleep(3);
 }
 if(IsWindow(r.host))PostMessage(r.host,CLOSE,GetCurrentProcessId(),0);
 closeMap();gpuSink.Reset();d3d12Sink.Reset();pbgraOverlay.Reset();
 frameMailbox.Reset();
 if(r.inputWindow && r.inputWindow!=r.gpuWindow)DestroyWindow(r.inputWindow);
 if(r.gpuWindow)DestroyWindow(r.gpuWindow);
 else if(r.inputWindow)DestroyWindow(r.inputWindow);
 if(r.panel)DestroyWindow(r.panel);DestroyWindow(r.ipc);
 printf("ACK=%d INIT=%d FRAME_NOTICES=%d COPIED=%d DISPLAYED=%d REJECTED=%d FRAME_MISSES=%d MUTEX_WAITS=%d STALLED=%d STOP=%d",
 r.acks,r.inits,r.notices,r.frameMailbox?static_cast<int>(frameMailbox.successful.load()):r.copies,r.draws,r.errors,r.misses,r.waits,r.stalled?1:0,r.stop?1:0);
 if(r.gpuOverlay)printf(" GPU_API=D3D12 GPU_FRAMES=%u GPU_MATCHED=%u GPU_PRESENTS=%u GPU_RESIZES=%u GPU_VARIANTS=%u GPU_ERRORS=%u GPU_DRIVER=%s GPU_OVERLAY_COMPOSITE=1 GPU_FRAME_MAILBOX=%d",
     pbgraOverlay.gpuFrames,pbgraOverlay.gpuMatches,pbgraOverlay.gpuPresents,
     pbgraOverlay.gpuResizes,pbgraOverlay.gpuVariants,pbgraOverlay.gpuErrors,
     pbgraOverlay.Driver(),r.frameMailbox?1:0);
 else if(r.gpuD3d12)printf(" GPU_API=D3D12 GPU_FRAMES=%u GPU_MATCHED=%u GPU_PRESENTS=%u GPU_RESIZES=%u GPU_VARIANTS=%u GPU_ERRORS=%u GPU_DRIVER=%s GPU_PIPELINE_BUILDS=%u GPU_TEXTURE_BUILDS=%u",
     d3d12Sink.gpuFrames,d3d12Sink.gpuMatches,d3d12Sink.gpuPresents,
     d3d12Sink.gpuResizes,d3d12Sink.gpuVariants,d3d12Sink.gpuErrors,d3d12Sink.Driver(),
     d3d12Sink.PipelineBuilds(),d3d12Sink.TextureBuilds());
 else if(r.gpu)printf(" GPU_API=D3D11 GPU_FRAMES=%u GPU_MATCHED=%u GPU_PRESENTS=%u GPU_RESIZES=%u GPU_VARIANTS=%u GPU_ERRORS=%u GPU_DRIVER=%s",
     gpuSink.gpuFrames,gpuSink.gpuMatches,gpuSink.gpuPresents,
     gpuSink.gpuResizes,gpuSink.gpuVariants,gpuSink.gpuErrors,gpuSink.Driver());
 if(r.gpuHook){
  if(r.gpuD3d12)printf(" GPU_HOOK_CALLBACKS=%u GPU_HOOK_ATTACHES=%u GPU_HOOK_DETACHES=%u",
      d3d12Sink.hookCallbacks,d3d12Sink.hookAttaches,d3d12Sink.hookDetaches);
  else printf(" GPU_HOOK_CALLBACKS=%u GPU_HOOK_ATTACHES=%u GPU_HOOK_DETACHES=%u",
      gpuSink.hookCallbacks,gpuSink.hookAttaches,gpuSink.hookDetaches);
 }
 if(r.inputProbe)printf(" INPUT_FORWARDED=%d INPUT_PASSTHROUGH=%d INPUT_DONE=%d INPUT_GPU_SAME_HWND=%d INPUT_INTERCEPT=%d INPUT_ID_VALID=%d INPUT_COMMANDS=%d INPUT_MASK_UPDATES=%d INPUT_RECT_UPDATES=%d",r.inputForwarded,r.inputPassThrough,r.inputDone?1:0,(r.gpu&&r.inputWindow==r.gpuWindow)?1:0,r.inputIntercept?1:0,r.inputWindowId?1:0,r.inputCommands,r.inputMaskUpdates,r.inputRectUpdates);
 puts("");
 return (r.acks>0&&r.inits>0&&(r.frameMailbox?frameMailbox.successful.load():r.copies)>0&&
          (!r.inputProbe || (r.inputDone && r.inputForwarded==4 &&
              (!r.gpu || r.inputWindow==r.gpuWindow))) &&
          (!r.gpu || (r.gpuOverlay
              ? (pbgraOverlay.gpuFrames>=1&&pbgraOverlay.gpuMatches>=1&&pbgraOverlay.gpuErrors==0)
              : r.gpuD3d12
              ? (d3d12Sink.gpuFrames>=1&&d3d12Sink.gpuMatches>=1&&
                 d3d12Sink.gpuErrors==0)
              : (gpuSink.gpuFrames>=1&&gpuSink.gpuMatches>=1&&
                 gpuSink.gpuErrors==0))))?0:12;
}
