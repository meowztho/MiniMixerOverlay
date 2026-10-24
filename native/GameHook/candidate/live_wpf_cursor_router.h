#pragma once
// Pure native input decision logic for the experimental independent WPF
// sphere. No USER32 hook, no desktop input, no game mouse interception.
// A trusted caller samples cursor state only when an IPC frame wakes it.
// Every event is bounded to the immutable actual Pbgra32 alpha mask.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <cstdint>
#include "wpf_frame_mailbox.h"

namespace minimixer {
struct CursorEvent {
    UINT message{};
    UINT wparam{};
    UINT lparam{};
    UINT windowId{};
};
struct CursorActions {
    CursorEvent events[3]{};
    UINT count{};
    void Add(UINT m,UINT w,UINT x,UINT y,UINT id) noexcept {
        if(count<3)events[count++]={m,w,static_cast<UINT>(MAKELPARAM(x,y)),id};
    }
};
enum class CursorLayer { None, Sphere, Main };
// One deterministic owner: a gesture keeps the initial capture; absent
// capture the sphere is topmost (drawn after Main).
inline CursorLayer PickCursorLayer(bool sphereCaptured,bool mainCaptured,
                                  bool sphereHit,bool mainHit) noexcept {
    if(sphereCaptured)return CursorLayer::Sphere;
    if(mainCaptured)return CursorLayer::Main;
    if(sphereHit)return CursorLayer::Sphere;
    if(mainHit)return CursorLayer::Main;
    return CursorLayer::None;
}
class WpfSphereCursorRouter final {
    bool hovered_{},buttonWasDown_{},captured_{},armed_{};
    UINT lastX_{},lastY_{},lastId_{};
public:
    bool Captured() const noexcept {return captured_;}
    static bool HitTest(const WpfFrameSnapshot* image,LONG screenX,LONG screenY) noexcept {
        if(!image || !image->View().valid())return false;
        const auto x=static_cast<std::int64_t>(screenX)-image->left;
        const auto y=static_cast<std::int64_t>(screenY)-image->top;
        if(x<0||y<0||x>=image->width||y>=image->height)return false;
        const auto index=(static_cast<std::size_t>(y)*image->width+
                          static_cast<std::size_t>(x))*4+3;
        return index<image->pixels.size() && image->pixels[index]>32;
    }
    // WM_CANCELMODE releases WPF capture without manufacturing a click-up.
    // A transparent/hidden owner must also send LEAVE, not silently Reset.
    CursorActions Cancel() noexcept {
        CursorActions a{};
        if(captured_ && lastId_)a.Add(WM_CANCELMODE,0,0,0,lastId_);
        if(hovered_ && lastId_)a.Add(WM_MOUSELEAVE,0,0,0,lastId_);
        Reset();
        return a;
    }
    // Caller must enforce foreground process/game HWND and live consent.
    // This pure helper *never* sends input or changes system cursor state.
    CursorActions Evaluate(const WpfFrameSnapshot* image,UINT windowId,
                           bool gameForeground,bool visibleCursor,
                           LONG screenX,LONG screenY,bool leftDown) noexcept {
        CursorActions result{};
        if(!image||!image->View().valid()||!windowId ||
           !gameForeground||!visibleCursor){
            // A captured gesture MUST receive its terminal cancel/leave
            // before state is forgotten (expired frame, revoked layer
            // policy, focus or windowId change). Never forge a click-up.
            return Cancel();
        }
        // After foreground/session recovery, require a sampled button-UP
        // before arming a new click: no surprise DOWN on Alt+Tab return.
        if(!leftDown)armed_=true;
        const std::int64_t x=static_cast<std::int64_t>(screenX)-image->left;
        const std::int64_t y=static_cast<std::int64_t>(screenY)-image->top;
        const bool inBounds=x>=0 && y>=0 &&
                            static_cast<std::uint64_t>(x)<image->width &&
                            static_cast<std::uint64_t>(y)<image->height;
        bool hit=false;
        if(inBounds) {
            const std::size_t offset=(static_cast<std::size_t>(y)*image->width+
                                     static_cast<std::size_t>(x))*4+3;
            hit=offset<image->pixels.size() && image->pixels[offset]>32;
        }
        const bool matchingCapture=captured_&&lastId_==windowId;
        if(!hit){
            // During capture continue MOVE even outside the alpha hit.
            // Clamp to valid WPF client bounds (the IPC host rejects
            // arbitrary negative/unbounded pixel coordinates).
            if(matchingCapture && image->width && image->height){
                const auto cx=static_cast<UINT>(x<0?0:
                    x>=image->width?image->width-1:x);
                const auto cy=static_cast<UINT>(y<0?0:
                    y>=image->height?image->height-1:y);
                if(leftDown && (cx!=lastX_||cy!=lastY_)){
                    result.Add(WM_MOUSEMOVE,MK_LBUTTON,cx,cy,windowId);
                }
                if(!leftDown){
                    result.Add(WM_LBUTTONUP,0,cx,cy,windowId);
                    captured_=false;
                }
                lastX_=cx;lastY_=cy;
            }
            if(!captured_ && hovered_ && lastId_==windowId)
                result.Add(WM_MOUSELEAVE,0,0,0,windowId);
            hovered_=captured_;
            buttonWasDown_=leftDown;
            return result;
        }
        const UINT localX=static_cast<UINT>(x),localY=static_cast<UINT>(y);
        const bool moved=!hovered_ || lastId_!=windowId ||
                         lastX_!=localX||lastY_!=localY;
        if(moved)
            result.Add(WM_MOUSEMOVE,leftDown&&captured_?MK_LBUTTON:0,
                       localX,localY,windowId);
        if(leftDown&&!buttonWasDown_&&!captured_&&armed_){
            result.Add(WM_LBUTTONDOWN,MK_LBUTTON,localX,localY,windowId);
            captured_=true;
        }
        if(!leftDown&&buttonWasDown_&&matchingCapture){
            result.Add(WM_LBUTTONUP,0,localX,localY,windowId);
            captured_=false;
        }
        lastX_=localX;lastY_=localY;lastId_=windowId;
        hovered_=true;buttonWasDown_=leftDown;
        return result;
    }
    void Reset() noexcept {
        hovered_=buttonWasDown_=captured_=armed_=false;
        lastX_=lastY_=lastId_=0;
    }
};
} // namespace minimixer
