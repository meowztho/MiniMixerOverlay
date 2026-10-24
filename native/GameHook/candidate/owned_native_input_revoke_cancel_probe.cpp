// Own-process regression for review F02/F03: never touches user input,
// games or the actual MiniMixer WPF/audio host.
#define MINIMIXER_OWNED_IPC_TEST_ONLY
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include "live_wpf_ipc_client.h"
using namespace minimixer;
int main(){
  HWND own=CreateWindowExW(0,L"STATIC",L"Own native input probe",
      WS_POPUP,0,0,24,24,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
  if(!own)return 2;
  LiveWpfIpcClient client;
  client.SetGameWindow(own);
  const bool bound=client.TestGameWindow()==own;
  client.SetGameWindow(nullptr);
  const bool cleared=client.TestGameWindow()==nullptr;
  client.SetGameWindow(own);
  const bool rebound=client.TestGameWindow()==own;
  printf("TARGET_REVOKE bound=%d cleared=%d rebound=%d stale_target=%d\n",
         bound,cleared,rebound,client.TestGameWindow()!=own);
  DestroyWindow(own);
  if(!bound||!cleared||!rebound)return 10;
  WpfFrameSnapshot frame;
  frame.width=frame.height=20;frame.left=frame.top=100;
  frame.pixels.assign(20*20*4,255);
  WpfSphereCursorRouter r;
  auto hover=r.Evaluate(&frame,42,true,true,110,110,false);
  auto down=r.Evaluate(&frame,42,true,true,110,110,true);
  const bool captured=r.Captured();
  auto lost=r.Evaluate(nullptr,42,true,true,110,110,true);
  auto repeat=r.Cancel();
  const bool gotCancel=lost.count==2 &&
      lost.events[0].message==WM_CANCELMODE &&
      lost.events[1].message==WM_MOUSELEAVE &&
      lost.events[0].windowId==42 && lost.events[1].windowId==42;
  const bool noDouble=repeat.count==0 && !r.Captured();
  auto held=r.Evaluate(&frame,42,true,true,110,110,true);
  bool noStaleDown=true;
  for(UINT i=0;i<held.count;++i)if(held.events[i].message==WM_LBUTTONDOWN)noStaleDown=false;
  (void)r.Evaluate(&frame,42,true,true,110,110,false);
  const auto newDown=r.Evaluate(&frame,42,true,true,110,110,true);
  const bool rearmed=newDown.count==1&&newDown.events[0].message==WM_LBUTTONDOWN;
  printf("LOST_FRAME down=%u captured_before=%d cancel_on_loss=%d "
         "events_on_loss=%u subsequent_cancel=%u stale_down=%d rearmed=%d\n",
         down.count,captured,gotCancel,lost.count,repeat.count,!noStaleDown,rearmed);
  if(!captured||!gotCancel||!noDouble||!noStaleDown||!rearmed)return 11;
  // Only the captured layer loses its policy. The other layer remains
  // present; cancelled drag must not be replayed onto the other surface.
  WpfSphereCursorRouter sphere,main;
  sphere.Evaluate(&frame,99,true,true,110,110,false);
  sphere.Evaluate(&frame,99,true,true,110,110,true);
  const auto layerLost=sphere.Evaluate(nullptr,99,true,true,110,110,true);
  const auto other=main.Evaluate(&frame,50,true,true,110,110,true);
  bool noOtherDown=true;
  for(UINT i=0;i<other.count;++i)if(other.events[i].message==WM_LBUTTONDOWN)noOtherDown=false;
  const bool policyCancel=layerLost.count==2 &&
     layerLost.events[0].message==WM_CANCELMODE &&
     layerLost.events[1].message==WM_MOUSELEAVE && noOtherDown;
  printf("LAYER_POLICY_LOSS captured_layer_cancel=%d other_layer_did_not_steal=%d\n",
         policyCancel,noOtherDown);
  if(!policyCancel)return 12;
  puts("PASS NATIVE_REVOKE_AND_CURSOR_LOSS owned_window=1 explicit_cancel=1 no_game_or_audio=1");
  return 0;
}
