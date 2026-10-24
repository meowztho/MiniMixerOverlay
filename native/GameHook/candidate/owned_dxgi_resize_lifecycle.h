#pragma once
// Shared resource ownership contract for owned Win32 probes and the
// experimental in-process DLL. Does NOT install an API hook.
// Caller must already own exclusive Present/Resize lifecycle access.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dxgi1_6.h>
#include "live_game_marker.h"
#include "live_repeat_marker.h"

enum class OwnedResizeOutcome {
    BlockedOnGpu,
    ReleaseFailed,
    ForwardedOriginal
};

template<class Original>
HRESULT WithOwnedBackbuffersReleased(
    LiveMarker& single,
    LiveRepeatMarker& repeated,
    DWORD maxGpuWaitMs,
    OwnedResizeOutcome& outcome,
    Original&& original) noexcept {
    // Verify BOTH independent fences before freeing EITHER set of buffers.
    if(!single.WaitForIdle(maxGpuWaitMs) ||
       (repeated.attempted() && !repeated.WaitForIdle(maxGpuWaitMs))) {
        outcome=OwnedResizeOutcome::BlockedOnGpu;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    // Single-shot and repeated marker own separate references to the
    // same underlying game backbuffers. Both must be clean before the
    // application's original DXGI ResizeBuffers/ResizeBuffers1 proceeds.
    if(!single.PrepareForResize() ||
       (repeated.attempted() && !repeated.PrepareForResize())) {
        outcome=OwnedResizeOutcome::ReleaseFailed;
        return DXGI_ERROR_INVALID_CALL;
    }
    outcome=OwnedResizeOutcome::ForwardedOriginal;
    return original();
}

// A Pbgra32 compositor is a THIRD independent backbuffer/queue-fence
// owner. Validate all fences first, release all owners before original DXGI.
// The caller exclusively serializes Present/Resize. The third owner exposes
// WaitIdle and PrepareForResize but owns no additional IPC responsibilities.
template<class Extra,class Original>
HRESULT WithOwnedBackbuffersReleased(
    LiveMarker& single,LiveRepeatMarker& repeated,Extra* extra,
    DWORD maxGpuWaitMs,OwnedResizeOutcome& outcome,
    Original&& original) noexcept {
    if(!single.WaitForIdle(maxGpuWaitMs) ||
       (repeated.attempted() && !repeated.WaitForIdle(maxGpuWaitMs)) ||
       (extra && !extra->WaitIdle(maxGpuWaitMs))){
        outcome=OwnedResizeOutcome::BlockedOnGpu;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    if(!single.PrepareForResize() ||
       (repeated.attempted() && !repeated.PrepareForResize()) ||
       (extra && !extra->PrepareForResize())){
        outcome=OwnedResizeOutcome::ReleaseFailed;
        return DXGI_ERROR_INVALID_CALL;
    }
    outcome=OwnedResizeOutcome::ForwardedOriginal;
    return original();
}

// A separate sphere is a FOURTH GPU backbuffer owner. Do not release
// any owner until all four independent fences are safe. Avoid copying
// CPU pixels into a full-screen union buffer merely to share a slot.
template<class FirstExtra,class SecondExtra,class Original>
HRESULT WithOwnedBackbuffersReleased(
    LiveMarker& single,LiveRepeatMarker& repeated,
    FirstExtra* mainOverlay,SecondExtra* sphereOverlay,
    DWORD maxGpuWaitMs,OwnedResizeOutcome& outcome,
    Original&& original) noexcept {
    if(!single.WaitForIdle(maxGpuWaitMs) ||
       (repeated.attempted() && !repeated.WaitForIdle(maxGpuWaitMs)) ||
       (mainOverlay && !mainOverlay->WaitIdle(maxGpuWaitMs)) ||
       (sphereOverlay && !sphereOverlay->WaitIdle(maxGpuWaitMs))){
        outcome=OwnedResizeOutcome::BlockedOnGpu;
        return DXGI_ERROR_WAS_STILL_DRAWING;
    }
    if(!single.PrepareForResize() ||
       (repeated.attempted() && !repeated.PrepareForResize()) ||
       (mainOverlay && !mainOverlay->PrepareForResize()) ||
       (sphereOverlay && !sphereOverlay->PrepareForResize())){
        outcome=OwnedResizeOutcome::ReleaseFailed;
        return DXGI_ERROR_INVALID_CALL;
    }
    outcome=OwnedResizeOutcome::ForwardedOriginal;
    return original();
}
