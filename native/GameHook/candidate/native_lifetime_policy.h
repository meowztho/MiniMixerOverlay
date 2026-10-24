#pragma once
// Compatibility facade: MiniMixer owns app-specific rendering and IPC,
// graphics_hook_core is the reusable DirectX state/identity implementation.
#include "../../GraphicsHookCore/include/graphics_hook_core/native_lifetime_policy.h"
namespace minimixer { using NativeStopReason=graphics_hook_core::NativeStopReason; using NativeLifetimePolicy=graphics_hook_core::NativeLifetimePolicy; }
