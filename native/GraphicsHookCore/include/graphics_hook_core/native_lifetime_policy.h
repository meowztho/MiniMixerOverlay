#pragma once
// No fixed shutdown for a healthy interactive game. The existing diagnostic
// worker samples these cumulative, atomically published counters; no new
// polling thread, Present-path filesystem I/O, or GPU synchronization.
#include <cstdint>

namespace graphics_hook_core {
enum class NativeStopReason {
    None,
    MissingStartupFrames,
    ActiveRenderingWithoutOverlay
};

struct NativeLifetimePolicy {
    static constexpr std::uint64_t StartupGraceMs=120000;
    static constexpr std::uint64_t ActiveStallLimitMs=15ull*60ull*1000ull;
    static constexpr std::uint64_t MinimumStartupFrames=100;
    static constexpr std::uint64_t MaxSampleChargeMs=30000; // sleep/resume not counted as active failure

    std::uint64_t previousSampleMs{};
    std::uint64_t previousOverlayFrames{};
    std::uint64_t previousGamePresents{};
    std::uint64_t stalledActiveMs{};
    bool firstSample=true;
    bool admitted=false;

    NativeStopReason Observe(std::uint64_t elapsedMs,
                             std::uint64_t overlayFrames,
                             std::uint64_t gamePresents) noexcept {
        if(firstSample){
            firstSample=false;
            previousSampleMs=elapsedMs;
            previousOverlayFrames=overlayFrames;
            previousGamePresents=gamePresents;
        }
        if(overlayFrames>=MinimumStartupFrames)
            admitted=true;
        if(!admitted && elapsedMs>StartupGraceMs)
            return NativeStopReason::MissingStartupFrames;

        // An elapsed time delta only counts as a lost-overlay interval
        // while the game was *actually presenting*. Paused/minimized
        // games must not be timed out just for having no Present callbacks.
        if(overlayFrames!=previousOverlayFrames){
            stalledActiveMs=0;
        }else if(admitted && gamePresents!=previousGamePresents &&
                 elapsedMs>=previousSampleMs){
            const auto rawDelta=elapsedMs-previousSampleMs;
            const auto delta=rawDelta>MaxSampleChargeMs
                ? MaxSampleChargeMs : rawDelta;
            // Saturate to avoid overflow for malformed/untrusted counters.
            if(stalledActiveMs>=ActiveStallLimitMs ||
               delta>=ActiveStallLimitMs-stalledActiveMs)
                stalledActiveMs=ActiveStallLimitMs;
            else
                stalledActiveMs+=delta;
        }
        previousSampleMs=elapsedMs;
        previousOverlayFrames=overlayFrames;
        previousGamePresents=gamePresents;
        return stalledActiveMs>=ActiveStallLimitMs
             ? NativeStopReason::ActiveRenderingWithoutOverlay
             : NativeStopReason::None;
    }
};
} // namespace graphics_hook_core
