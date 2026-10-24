#include <windows.h>
#include <cstdio>
#include <vector>
#include "live_wpf_cursor_router.h"
#include "live_overlay_marker_policy.h"
int main(){
    minimixer::WpfFrameSnapshot snap;
    snap.width=20;snap.height=20;snap.left=800;snap.top=400;
    snap.pixels.assign(20*20*4,0);
    for(int y=4;y<16;++y)for(int x=4;x<16;++x)
        snap.pixels[(y*20+x)*4+3]=225;
    minimixer::WpfSphereCursorRouter router;
    unsigned asserts{};
    auto check=[&](bool ok,const char* name){
        ++asserts;
        if(!ok)std::printf("FAIL %s\n",name);
        return ok;
    };
    auto eval=[&](bool focus,bool cursor,int x,int y,bool down,UINT id=701){
        return router.Evaluate(&snap,id,focus,cursor,x,y,down);
    };
    if(!check(eval(true,true,801,401,false).count==0,"transparent"))return 1;
    auto a=eval(true,true,810,410,false);
    if(!check(a.count==1&&a.events[0].message==WM_MOUSEMOVE&&
              LOWORD(a.events[0].lparam)==10&&
              HIWORD(a.events[0].lparam)==10,"enter opaque"))return 2;
    if(!check(eval(true,true,810,410,false).count==0,"stationary no polling spam"))return 3;
    a=eval(true,true,810,410,true);
    if(!check(a.count==1&&a.events[0].message==WM_LBUTTONDOWN,"down once"))return 4;
    if(!check(eval(true,true,810,410,true).count==0,"held no repeats"))return 5;
    a=eval(true,true,815,415,true);
    if(!check(a.count==1&&a.events[0].message==WM_MOUSEMOVE&&
              a.events[0].wparam==MK_LBUTTON,"drag move"))return 6;
    a=eval(true,true,800,400,false);
    if(!check(a.count==2 && a.events[0].message==WM_LBUTTONUP&&
              a.events[1].message==WM_MOUSELEAVE,
              "release outside prevents stuck button"))return 7;
    if(!check(eval(true,true,800,400,false).count==0,"transparent unchanged"))return 8;
    if(!check(eval(false,true,810,410,true).count==0,"other app foreground no input"))return 9;
    a=eval(true,true,810,410,true);
    if(!check(a.count==1&&a.events[0].message==WM_MOUSEMOVE,
              "press held on focus reenter never clicks"))return 10;
    a=eval(true,false,810,410,true);
    if(!check(a.count==1 && a.events[0].message==WM_MOUSELEAVE,
              "cursor hidden terminates hover without a new click"))return 11;
    a=eval(true,true,810,410,false);
    if(!check(a.count==1&&a.events[0].message==WM_MOUSEMOVE,"visible reset"))return 12;
    a=eval(true,true,810,410,true);
    if(!check(a.count==1&&a.events[0].message==WM_LBUTTONDOWN,"new click"))return 13;
    a=eval(true,true,810,410,false);
    if(!check(a.count==1&&a.events[0].message==WM_LBUTTONUP,"release"))return 14;
    snap.pixels.assign(20*20*4,0);
    a=eval(true,true,810,410,false);
    if(!check(a.count==1&&a.events[0].message==WM_MOUSELEAVE,
              "alpha becomes transparent"))return 15;
    snap.pixels.resize(1);
    if(!check(eval(true,true,810,410,false).count==0,"malformed frame denied"))return 16;
    snap.pixels.assign(20*20*4,0);
    for(int y=4;y<16;++y)for(int x=4;x<16;++x)
        snap.pixels[(y*20+x)*4+3]=225;
    router.Reset();
    a=eval(true,true,810,410,true);
    if(!check(a.count==1 && a.events[0].message==WM_MOUSEMOVE &&
              a.events[0].wparam==0,
              "refocus while held may move but must not click"))return 17;
    a=eval(true,true,810,410,false);
    if(!check(a.count==0,"release disarmed click"))return 18;
    a=eval(true,true,810,410,true);
    if(!check(a.count==1 && a.events[0].message==WM_LBUTTONDOWN,
              "new click after sampled release"))return 19;
    router.Reset();
    if(!check(!minimixer::ShowStartupDiagnostic(false,0,false,false) &&
              !minimixer::ShowStartupDiagnostic(false,10,true,true) &&
              !minimixer::ShowStartupDiagnostic(false,200,false,false) &&
              minimixer::ShowStartupDiagnostic(true,0,true,true) &&
              minimixer::ShowStartupDiagnostic(true,100,true,false) &&
              !minimixer::ShowStartupDiagnostic(true,120,true,false) &&
              !minimixer::ShowStartupDiagnostic(true,200,false,true) &&
              minimixer::ShowStartupDiagnostic(true,200,false,false),
              "ordinary preview must never paint a diagnostic marker"))return 20;
    if(!check(!minimixer::MissingFrameProgress(119999,false,0,0,0) &&
              minimixer::MissingFrameProgress(120001,false,0,50,49) &&
              !minimixer::MissingFrameProgress(120001,false,0,80,30) &&
              !minimixer::MissingFrameProgress(120001,true,120,0,0) &&
              minimixer::MissingFrameProgress(120001,true,99,100,100),
              "product watchdog must count WPF frames, diagnostic watchdog marker frames"))return 21;
    std::printf("PASS SPHERE_CURSOR_ALPHA_FOCUS_DRAG_RELEASE_GATE assertions=%u no_hooks=1 no_input_injection=1\n",asserts);
    return 0;
}
