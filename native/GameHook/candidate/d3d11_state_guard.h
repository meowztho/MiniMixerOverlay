#pragma once
// Compatibility facade: MiniMixer owns app-specific rendering and IPC,
// graphics_hook_core is the reusable DirectX state/identity implementation.
#include "../../GraphicsHookCore/include/graphics_hook_core/d3d11_state_guard.h"
namespace minimixer { using ScopedD3d11DrawState=graphics_hook_core::ScopedD3d11DrawState; }
