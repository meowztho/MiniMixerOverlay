// Owned F05 policy + main WPF cursor semantics: no game/injection/audio.
#define MINIMIXER_OWNED_IPC_TEST_ONLY
#include "live_wpf_ipc_client.h"
#include <windows.h>
#include <cstdio>
int main(){
    minimixer::LiveWpfIpcClient client;
    // A Main-Input policy before overlay.init MUST NOT authorize anything.
    client.TestReceiveOwned("experimental.main.input.policy",
        R"({"enabled":true})");
    if(client.TestMainInputPolicyAllowed())return 1;
    client.TestReceiveOwned("overlay.init",
        R"({"shareMemMutex":"OWNED_FIXTURE_MUTEX","bufferName":"OWNED_FIXTURE_BUFFER","windows":[{"windowId":12345}],"windowId":12345,"x":10,"y":20})");
    if(client.TestMainWindowId()!=12345)return 2;
    client.TestReceiveOwned("experimental.main.input.policy",
        R"({"enabled":true})");
    if(!client.TestMainInputPolicyAllowed() ||
       client.TestInputPolicyAllowed())return 3;
    client.TestReceiveOwned("experimental.main.input.policy",
        R"({"enabled":false})");
    if(client.TestMainInputPolicyAllowed())return 4;
    minimixer::WpfFrameSnapshot snap;
    snap.width=24;snap.height=24;snap.left=100;snap.top=200;
    snap.pixels.assign(24*24*4,0);
    for(unsigned y=5;y<20;++y)for(unsigned x=5;x<20;++x)
        snap.pixels[(y*24+x)*4+3]=255;
    minimixer::WpfSphereCursorRouter main,aux;
    auto a=main.Evaluate(&snap,12345,true,true,110,210,false);
    if(a.count!=1||a.events[0].message!=WM_MOUSEMOVE)return 5;
    a=main.Evaluate(&snap,12345,true,true,110,210,true);
    if(a.count!=1||a.events[0].message!=WM_LBUTTONDOWN)return 6;
    a=main.Evaluate(&snap,12345,true,true,110,210,false);
    if(a.count!=1||a.events[0].message!=WM_LBUTTONUP)return 7;
    a=aux.Evaluate(&snap,444,true,true,110,210,false);
    if(a.count!=1||a.events[0].windowId!=444)return 8;
    // Focus loss must reject new input but terminate the earlier hover
    // exactly once; silently dropping it would leave WPF hover stuck.
    a=main.Evaluate(&snap,12345,false,true,110,210,true);
    if(a.count!=1 || a.events[0].message!=WM_MOUSELEAVE ||
       a.events[0].windowId!=12345)return 9;
    if(main.Cancel().count!=0)return 10;
    puts("PASS OWN_MAIN_INPUT_POLICY_AND_CURSOR gated_before_init=1"
         " policy_toggle=1 main_id=1 main_click=1 sphere_isolated=1"
         " focus_reject=1 no_game_or_audio=1");
    return 0;
}
