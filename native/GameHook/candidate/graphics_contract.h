#pragma once
#include <cstdint>
#include <cstddef>

// Universal frame semantics; independent of any game title/window title.
// API adapters are implementation choices, not different IPC authorities.
namespace minimixer {
enum class GraphicsApi { D3D9, D3D10, D3D11, D3D12, OpenGL, Vulkan };
enum class Status { Ok, UnsupportedApi, InvalidFrame, GraphicsFailure, DeviceLost };
struct FrameView {
    const std::uint8_t* bgra = nullptr;
    std::uint32_t width = 0, height = 0, stride = 0;
    bool valid() const {
        return bgra && width >= 2 && height >= 2 &&
               width <= 4096 && height <= 4096 &&
               stride >= static_cast<std::uint64_t>(width) * 4 &&
               static_cast<std::uint64_t>(stride) * height <= 64ull * 1024 * 1024;
    }
};
inline Status SupportedRenderer(GraphicsApi api) {
    // Only the independently exercised D3D11 rendering module is available.
    // Do not route other APIs here, even if the reference DLL supports them.
    return api == GraphicsApi::D3D11 ? Status::Ok : Status::UnsupportedApi;
}
} // namespace minimixer
