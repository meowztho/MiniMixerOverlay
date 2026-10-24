#pragma once
// Shared nonblocking Present-path recovery after game swapchain resize.
// The caller owns exclusive Present/Resize and controls COM lifetime.
// GPU pixel validation occurs only after the submitted fence is signalled.
#include "live_game_marker.h"

inline bool AdvanceVerifiedOneShotOnPresent(
    LiveMarker& marker, IDXGISwapChain* swapchain,
    ID3D12CommandQueue* queue) noexcept {
    if (!marker.tried.load(std::memory_order_acquire)) {
        marker.DrawOnce(swapchain, queue);
    }
    if (marker.result.load(std::memory_order_acquire) == 1) {
        // CheckReadback does not wait: it returns if GPU work is unfinished.
        marker.CheckReadback();
    }
    return marker.result.load(std::memory_order_acquire) == 2;
}
