#pragma once
// Reusable, bounded hook ownership. Does not select a process or inject DLLs.
// Consumer MUST fence all active callbacks before calling RemoveAll().
#include <MinHook.h>
#include <array>
namespace graphics_hook_core {
class MinHookRegistry final {
    std::array<void*,16> targets_{};
    unsigned count_{};
public:
    bool Initialize() noexcept {return MH_Initialize()==MH_OK;}
    unsigned Count() const noexcept {return count_;}
    bool Install(void* target,void* replacement,void** original) noexcept {
        if(!target || !replacement || !original || count_>=targets_.size())
            return false;
        // The same vtable entrypoint can be exposed by D3D11 and D3D12.
        // Never overwrite original when a second registration hits it.
        for(unsigned i=0;i<count_;++i)
            if(targets_[i]==target)return false;
        if(MH_CreateHook(target,replacement,original)!=MH_OK)return false;
        if(MH_EnableHook(target)!=MH_OK){
            MH_RemoveHook(target);
            return false;
        }
        targets_[count_++]=target;
        return true;
    }
    bool DisableAll() noexcept {
        bool success=true;
        for(int i=static_cast<int>(count_)-1;i>=0;--i)
            if(MH_DisableHook(targets_[i])!=MH_OK)success=false;
        return success;
    }
    // CALL ONLY after consumer has proven in-flight callbacks quiesced.
    bool RemoveAll() noexcept {
        bool success=true;
        for(int i=static_cast<int>(count_)-1;i>=0;--i)
            if(MH_RemoveHook(targets_[i])!=MH_OK)success=false;
        if(success)count_=0;
        return success;
    }
    bool Uninitialize() noexcept {return MH_Uninitialize()==MH_OK;}
};
} // namespace graphics_hook_core
