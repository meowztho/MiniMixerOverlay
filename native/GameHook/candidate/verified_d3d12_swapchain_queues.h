#pragma once
// Compatibility facade: MiniMixer owns app-specific rendering and IPC,
// graphics_hook_core is the reusable DirectX state/identity implementation.
#include "../../GraphicsHookCore/include/graphics_hook_core/verified_d3d12_swapchain_queues.h"
namespace minimixer { using VerifiedSwapchainQueues=graphics_hook_core::VerifiedSwapchainQueues; }
