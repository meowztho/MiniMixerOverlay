#include <windows.h>
#include <cstdio>
#include "live_wpf_cursor_router.h"
using namespace minimixer;
int main(){
 WpfFrameSnapshot image;
 image.width=20;image.height=20;image.left=100;image.top=200;
 image.pixels.assign(20*20*4,0);
 for(int y=2;y<18;++y)for(int x=2;x<18;++x)
   image.pixels[(y*20+x)*4+3]=255;
 WpfSphereCursorRouter sphere,main;
 const bool sh=WpfSphereCursorRouter::HitTest(&image,110,210);
 const bool mh=WpfSphereCursorRouter::HitTest(&image,110,210);
 if(!sh||!mh||PickCursorLayer(false,false,sh,mh)!=CursorLayer::Sphere)return 1;
 // Same physical down must go to one and only one surface.
 auto downSphere=sphere.Evaluate(&image,42,true,true,110,210,false);
 auto downMain=main.Cancel();
 auto down=sphere.Evaluate(&image,42,true,true,110,210,true);
 if(down.count!=1||down.events[0].message!=WM_LBUTTONDOWN||
    downMain.count!=0)return 2;
 if(PickCursorLayer(sphere.Captured(),main.Captured(),false,true)!=CursorLayer::Sphere)
   return 3;
 // Keep capture and continue moving against bounds when leaving alpha.
 auto drag=sphere.Evaluate(&image,42,true,true,160,260,true);
 if(drag.count!=1||drag.events[0].message!=WM_MOUSEMOVE||
    LOWORD(drag.events[0].lparam)!=19||
    HIWORD(drag.events[0].lparam)!=19)return 4;
 auto released=sphere.Evaluate(&image,42,true,true,160,260,false);
 if(released.count<1||released.events[0].message!=WM_LBUTTONUP||
    LOWORD(released.events[0].lparam)!=19)return 5;
 if(sphere.Captured())return 6;
 // Explicit cancel is not a synthetic mouse click/up.
 sphere.Evaluate(&image,42,true,true,110,210,false);
 sphere.Evaluate(&image,42,true,true,110,210,true);
 auto cancelled=sphere.Cancel();
 if(cancelled.count!=2||cancelled.events[0].message!=WM_CANCELMODE||
    cancelled.events[1].message!=WM_MOUSELEAVE)return 7;
 auto stillHeld=sphere.Evaluate(&image,42,true,true,110,210,true);
 for(unsigned i=0;i<stillHeld.count;++i)
    if(stillHeld.events[i].message==WM_LBUTTONDOWN)return 8;
 sphere.Evaluate(&image,42,true,true,110,210,false);
 auto normalDown=sphere.Evaluate(&image,42,true,true,110,210,true);
 if(normalDown.count!=1||normalDown.events[0].message!=WM_LBUTTONDOWN)return 9;
 puts("PASS OWN_LAYER_CAPTURE_PRIORITY_CANCEL sphere_over_main=1"
      " duplicate_down=0 drag_outside=1 clipped_release=1"
      " cancel_mode=1 no_synthetic_up=1 no_refocus_click=1");
 return 0;
}
