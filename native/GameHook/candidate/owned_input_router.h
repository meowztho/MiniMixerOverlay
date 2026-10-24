#pragma once
// Input policy for an explicitly owned test window. No global hooks,
// SendInput, low-level keyboard hooks or foreign HWND subclasses.
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace minimixer {
struct InputFrame {
    const std::uint8_t* bgra{};
    unsigned width{},height{},stride{};
    std::size_t bufferBytes{};
    int screenX{},screenY{};
    std::uint32_t windowId{};
    bool intercept{};
    bool forward{};
    bool valid() const noexcept {
        return bgra && windowId && width && height && width<=4096 && height<=4096 &&
               stride>=static_cast<std::uint64_t>(width)*4u &&
               stride<=static_cast<std::uint64_t>(4096)*4u &&
               bufferBytes >= (static_cast<std::size_t>(height)-1u)*stride+
                              static_cast<std::size_t>(width)*4u;
    }
    bool Hit(int screenXValue,int screenYValue) const noexcept {
        if(!valid())return false;
        const std::int64_t x=static_cast<std::int64_t>(screenXValue)-screenX;
        const std::int64_t y=static_cast<std::int64_t>(screenYValue)-screenY;
        if(x<0||y<0||x>=width||y>=height)return false;
        const auto at=static_cast<std::size_t>(y)*stride+
                      static_cast<std::size_t>(x)*4u+3u;
        return bgra[at]>24; // Low-alpha pixels remain pointer-transparent.
    }
};
struct InputDecision {
    bool consumed{},forwarded{},entered{},left{},captureStarted{},captureEnded{};
    std::uint32_t windowId{},msg{},wparam{},lparam{};
};
class OwnedInputRouter final {
    bool inside_{},pressed_{};
public:
    bool Inside() const noexcept{return inside_;}
    bool Pressed() const noexcept{return pressed_;}
    void Reset() noexcept {inside_=pressed_=false;}
    // Coordinates must be screen pixels, obtained by ClientToScreen in the
    // owned window's WndProc. Never trust arbitrary window IDs from input.
    InputDecision Route(const InputFrame& frame,UINT msg,WPARAM wp,LPARAM lp,
                        int screenX,int screenY) noexcept {
        InputDecision result{};
        if(!frame.valid() || !frame.intercept || !frame.forward){
            Reset();return result;
        }
        const bool isMove=msg==WM_MOUSEMOVE;
        const bool isDown=msg==WM_LBUTTONDOWN;
        const bool isUp=msg==WM_LBUTTONUP;
        const bool isWheel=msg==WM_MOUSEWHEEL;
        const bool isLeave=msg==WM_MOUSELEAVE;
        if(!isMove&&!isDown&&!isUp&&!isWheel&&!isLeave)return result;
        const bool hit=!isLeave&&frame.Hit(screenX,screenY);
        const bool wasInside=inside_;
        // Hold input capture for a started drag, even when pointer leaves
        // the widget, then always forward the matching button release.
        const bool active=hit||pressed_;
        if(isLeave){
            inside_=false;
            result.left=wasInside;
            return result;
        }
        inside_=hit;
        result.entered=hit&&!wasInside;
        result.left=!hit&&wasInside;
        if(isDown && hit && !pressed_) {
            pressed_=true;
            result.captureStarted=true;
        }
        result.consumed=active || (isUp && pressed_);
        if(isUp && pressed_) {
            pressed_=false;
            result.captureEnded=true;
        }
        if(!result.consumed) return result;
        result.forwarded=true;
        result.windowId=frame.windowId;
        result.msg=static_cast<std::uint32_t>(msg);
        result.wparam=static_cast<std::uint32_t>(wp);
        result.lparam=static_cast<std::uint32_t>(lp);
        return result;
    }
};
} // namespace minimixer
