# MiniMixer Native Candidate – current test and integration entry

The independent native DLL is implemented in live_game_present_dll.cpp
and compiled with build-live-present-dll.ps1. It uses pinned MinHook
v1.3.4 and the production graphics-hook-core building blocks under
native/GraphicsHookCore/include/graphics_hook_core.

There is NO GOverlay runtime in this folder. The old vendor helper,
binary, vendor-probe and USER32 compatibility export were removed.
The active named Win32 WPF-IPC endpoint is minimixer_graphics_v1.
Do not use older vendor IPC captures with the new revision; they are
historical-only evidence under Docs/reviews.

## Reusable versus MiniMixer-specific owners

Reusable: MinHookRegistry, ScopedD3d11DrawState,
VerifiedSwapchainQueues, NativeLifetimePolicy.
Source of truth: native/GraphicsHookCore/README.md.

MiniMixer-specific: qualified game target/HWND, host WPF IPC,
frame mailbox and input policy, marker diagnostics, compositors,
native preview loader and opt-in safety gates. They are not part
of the reusable core and must not silently gain process control.

## Current authorized isolated tests

Run from the repository root on SebPc:
- native/GameHook/candidate/build-live-present-dll.ps1
- native/GameHook/candidate/build-candidate.ps1
- native/GameHook/candidate/build-owned-d3d11-provider-hook-probe.ps1
- native/GameHook/candidate/build-owned-d3d11-tessellation-uavs-probe.ps1
- native/GameHook/candidate/build-owned-d3d12-verified-queue-probe.ps1
- native/GameHook/candidate/build-owned-d3d12-resize-zero-queue-probe.ps1
- native/GameHook/candidate/build-owned-native-lifetime-probe.ps1
- dotnet run -c Release --project
  test/MiniMixerOverlay.NativeCandidateSmoke/MiniMixerOverlay.NativeCandidateSmoke.csproj
  with '--input-probe', '--input-probe-no-forward' and
  '--input-probe-no-capability' options.

The real game binary must never be selected by these fixtures;
native owned tests use their own windows, WARP/hardware devices,
DXGI swapchains and a fake WPF audio source.

Overwatch-specific Native DLL injection is NOT an approved test
and the product allowlist is NOT widened. A game full-screen
surface test may observe Windows Topmost WPF windows read-only,
but cannot claim a real native in-game Present-Pixel result.

## Runtime admission

No normal launch requires a native DLL. The experimental
--native-hook-preview opt-in is permitted only for currently
approved targets and should never be interpreted as broad
anti-cheat compatibility. Existing user-visible WPF sphere and
app mixer may be fully functional without graphics injection.

Prior implementation history is preserved in Git history and
dated Docs/reviews reports; this document is the current entry.
