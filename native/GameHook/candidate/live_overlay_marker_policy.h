#pragma once
// Visible magenta/cyan marker is only for an explicitly instrumented build.
// Interactive NativePreview always keeps game pixels clear, including startup,
// IPC reconnect and display-mode switches.
#include <cstdint>
namespace minimixer {
inline bool ShowStartupDiagnostic(
    bool diagnosticEnabled,std::uint64_t verifiedMarkerFrames,
    bool freshMain,bool freshSphere) noexcept {
    constexpr std::uint64_t RequiredAdmissionMargin=120;
    return diagnosticEnabled &&
           (verifiedMarkerFrames<RequiredAdmissionMargin ||
            (!freshMain && !freshSphere));
}

// Without the visible diagnostic, real WPF draws (main plus sphere) become
// the 2-minute progress watchdog. Otherwise 0 repeat-marker frames would
// incorrectly stop an otherwise healthy overlay.
inline bool MissingFrameProgress(
    std::uint64_t elapsedMs,bool diagnosticEnabled,
    std::uint64_t diagnosticFrames,std::uint64_t mainFrames,
    std::uint64_t sphereFrames) noexcept {
    return elapsedMs>120000 &&
           (diagnosticEnabled ? diagnosticFrames : mainFrames+sphereFrames)<100;
}
} // namespace minimixer
