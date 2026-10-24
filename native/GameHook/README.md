# MiniMixer Native Graphics Backend

Current implementation: candidate/live_game_present_dll.cpp is an
independently authored DirectX/DXGI Present/Present1/Resize backend
with a verified DX12 swapchain queue and optional MiniMixer WPF IPC.

The backend uses pinned BSD-2-Clause MinHook v1.3.4. It no longer uses
the previous GOverlay DLLs, vendor injector helpers, old USER32 export
or historic vendor-only test probes.

Reusable native components: native/GraphicsHookCore/README.md.
Current adapter: native/GameHook/candidate.
Original candidate headers for D3D11-state, DX12-queue and lifetime
are compatibility facades that delegate to the extracted shared core.
The active DLL itself also uses the reusable MinHookRegistry to install
and safely disable graphics hooks. No second hook owner was created.

Security and behavior: default WPF-only operation does not inject
anything. Native graphics hooks require --native-hook-preview and remain
experimental and fail-closed; protected or unidentified games are not
approved. Overwatch is NOT added to the Native loader allowlist.
Use only approved owned-process GPU/IPC regressions before additional
user-authorized, suitable real-game tests.

The older candidate/README.md is a chronological development record,
not an authorization or current build contract.
Current release policy: Docs/09_RELEASE_INSTALL.md.
