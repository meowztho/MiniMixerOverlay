# GraphicsHookCore: reusable C++17 DXGI/DirectX functionality

This module is extracted from the independent MiniMixer graphics
backend. It contains NO external process selector, injector, input
capture, application UI, audio owner or WPF IPC transport.

Public include tree: include/graphics_hook_core/

- minhook_registry.h: bounded 16-target hook registry. Installs a
  requested address/replacement with MinHook and captures trampoline;
  prevents duplicate ownership. Disables in reverse registration order.
  Removal is explicitly dependent on the consumer proving all in-flight
  callbacks have quiesced. MinHook stays a pinned external dependency.
- d3d11_state_guard.h: RAII capture/restoration of D3D11 pipeline
  state including tessellation stages, OM/CS UAVs, SRVs and predication.
- verified_d3d12_swapchain_queues.h: DXGI swapchain-to-queue evidence
  with private-generation stamp; bounded 16-entry LRU. Unknown/evicted
  queues fail-closed. Never guess from an unrelated ExecuteCommandLists.
- native_lifetime_policy.h: pure no-I/O admission and stall evaluator.

Canonical implementation lives here. Previous names in
native/GameHook/candidate are thin aliases and remain supported for
the existing MiniMixer native adapter and isolated probe code. The
native product DLL directly uses the registry from this module.
This makes actual shared code available to independent consumers.

A consumer must provide: explicit process eligibility and consent,
DXGI vtable addresses, callback implementations/trampolines, WPF or
other image sources, target window qualification, render synchronization,
resize and device-loss recovery, stop-event coordination, and test
instrumentation. These are NOT inferred by GraphicsHookCore and must
not be implemented as generic automatic game injection.

Use MinHook v1.3.4 (BSD-2-Clause, includes local license) and an
MSVC x64 C++17 Windows-SDK toolchain. Example evidence runs:
  native/GameHook/candidate/build-owned-d3d11-provider-hook-probe.ps1
  native/GameHook/candidate/build-owned-d3d12-verified-queue-probe.ps1
  native/GameHook/candidate/build-owned-d3d11-tessellation-uavs-probe.ps1
  native/GameHook/candidate/build-owned-native-lifetime-probe.ps1

Scope: standalone DXGI-state/hook building blocks, NOT an injectable
universal overlay DLL or proof of anti-cheat compatibility.
