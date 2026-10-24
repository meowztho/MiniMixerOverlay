// Exercises real owned-Win32-WndProc dispatch only. No global input injection.
// This is an input policy/IPC event shape test, not an actual game hook.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <windowsx.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>
#include "owned_input_router.h"

using minimixer::OwnedInputRouter;
using minimixer::InputFrame;
using minimixer::InputDecision;
namespace {
OwnedInputRouter router;
InputFrame frame;
InputDecision last;
int forwarded{},unhandled{},enters{},leaves{},downs{},ups{};
std::uint32_t lastWindowId{},lastMessage{},lastParam{};
LRESULT CALLBACK WindowProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp) {
    if(msg!=WM_MOUSEMOVE && msg!=WM_LBUTTONDOWN && msg!=WM_LBUTTONUP &&
       msg!=WM_MOUSELEAVE && msg!=WM_MOUSEWHEEL && msg!=WM_KEYDOWN)
        return DefWindowProcW(hwnd,msg,wp,lp);
    // In a real owned window, use ClientToScreen for client-coordinate
    // messages. WM_MOUSEWHEEL is already screen-coordinate input.
    POINT pt{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
    if(msg!=WM_MOUSEWHEEL)ClientToScreen(hwnd,&pt);
    last=router.Route(frame,msg,wp,lp,pt.x,pt.y);
    if(last.entered)++enters;
    if(last.left)++leaves;
    if(last.forwarded){
        ++forwarded;
        lastWindowId=last.windowId;
        lastMessage=last.msg;
        lastParam=last.lparam;
        if(msg==WM_LBUTTONDOWN)++downs;
        if(msg==WM_LBUTTONUP)++ups;
    }else ++unhandled;
    // Only explicitly authorized hit pixels may consume the event.
    return last.consumed ? 0 : 0x3456;
}
bool Assert(bool ok,const char* label){
    if(!ok)std::printf("FAIL owned native input: %s\n",label);
    return ok;
}
LRESULT Pointer(HWND h,UINT msg,int x,int y,WPARAM wp=0) {
    return SendMessageW(h,msg,wp,MAKELPARAM(x,y));
}
} // namespace
int main() {
    WNDCLASSW wc{};wc.hInstance=GetModuleHandleW(nullptr);
    wc.lpfnWndProc=WindowProc;wc.lpszClassName=L"MiniMixerOwnedInputProbe";
    if(!RegisterClassW(&wc))return 2;
    HWND window=CreateWindowExW(0,wc.lpszClassName,L"Owned input test",
                         WS_POPUP,100,200,64,64,nullptr,nullptr,
                         wc.hInstance,nullptr);
    if(!window)return 3;
    constexpr unsigned width=32,height=32,stride=width*4+16;
    std::vector<std::uint8_t> pixels(height*stride,0);
    for(unsigned y=0;y<16;++y)for(unsigned x=0;x<16;++x)
        pixels[static_cast<size_t>(y)*stride+x*4+3]=210;
    // x=16 is under alpha threshold, despite being inside the frame.
    pixels[8*stride+16*4+3]=24;
    frame={pixels.data(),width,height,stride,pixels.size(),100,200,123456789u,true,true};
    bool ok=true;
    // Win32 synthetic dispatch uses an owned HWND and real Win32 messages.
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,8,8)==0 &&
                 last.forwarded && last.entered &&
                 lastWindowId==123456789u &&
                 lastMessage==WM_MOUSEMOVE,"opaque hover forwarding and ID");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,16,8)==0x3456 &&
                 !last.forwarded && last.left,"low alpha passes through");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,50,8)==0x3456 &&
                 !last.forwarded,"outside bounds passes through");
    ok &= Assert(Pointer(window,WM_LBUTTONDOWN,20,20)==0x3456 &&
                 !router.Pressed(),"transparent button down passes through");
    ok &= Assert(Pointer(window,WM_LBUTTONDOWN,7,7,MK_LBUTTON)==0 &&
                 last.captureStarted && router.Pressed(),"button capture starts");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,50,50,MK_LBUTTON)==0 &&
                 last.forwarded && router.Pressed(),
                 "drag outside hit region still forwarded");
    ok &= Assert(Pointer(window,WM_LBUTTONUP,50,50)==0 &&
                 last.captureEnded && !router.Pressed(),
                 "release outside ends captured drag");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,50,50)==0x3456 &&
                 !last.forwarded,"no stale mouse capture");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0 &&
                 last.entered,"hover can reenter");
    ok &= Assert(SendMessageW(window,WM_MOUSELEAVE,0,0)==0x3456 &&
                 last.left && !router.Inside(),
                 "leave clears hover without intercept");
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0 &&
                 last.entered,"hover recovers after leave");
    ok &= Assert(Pointer(window,WM_KEYDOWN,VK_RETURN,0)==0x3456 &&
                 !last.forwarded,"keyboard never intercepted by mouse router");
    frame.forward=false;
    ok &= Assert(Pointer(window,WM_LBUTTONDOWN,9,9)==0x3456 &&
                 !router.Inside()&&!router.Pressed(),"forwarding is opt-in");
    frame.forward=true;frame.intercept=false;
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0x3456 &&
                 !last.forwarded,"input interception must be enabled");
    frame.intercept=true;
    frame.windowId=0;
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0x3456 &&
                 !last.forwarded,"missing window ID fails closed");
    frame.windowId=123456789u;
    frame.stride=4;
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0x3456 &&
                 !last.forwarded,"invalid frame stride fails closed");
    frame.stride=stride;
    frame.bufferBytes=1;
    ok &= Assert(Pointer(window,WM_MOUSEMOVE,9,9)==0x3456 &&
                 !last.forwarded,"truncated frame buffer fails closed");
    frame.bufferBytes=pixels.size();
    ok &= Assert(forwarded==6 && downs==1 && ups==1 &&
                 enters==4 && leaves==3,"event/capture counters");
    DestroyWindow(window);
    if(!ok)return 4;
    std::printf("PASS OWNED_INPUT_ROUTER forwarded=%d downs=%d ups=%d enter=%d leave=%d "
                "transparentIgnored=1 offscreenIgnored=1 leaveCleared=1 "
                "optIn=1 noKeyboardHook=1 noGlobalHook=1\n",
                forwarded,downs,ups,enters,leaves);
    return 0;
}
