#pragma once
#include <d3d11_1.h>
#include <wrl/client.h>
#include <array>
#include <cstdint>

namespace graphics_hook_core {
// Save every pipeline binding that an OM RTV switch can implicitly unbind.
// Restore the original RTV/DSV/UAV occupancy together, preserving UAV counters.
// This is NOT a substitute for synchronizing with a foreign render thread.
// No hook may keep this guard alive across ResizeBuffers or device loss.
class ScopedD3d11DrawState final {
    template<typename T> using ComPtr=Microsoft::WRL::ComPtr<T>;
    static constexpr UINT MaxClassInstances=256;
    ID3D11DeviceContext* ctx_;
    ComPtr<ID3D11InputLayout> layout_;
    ComPtr<ID3D11Buffer> vertex_;
    UINT stride_{}, offset_{};
    D3D11_PRIMITIVE_TOPOLOGY topology_{D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED};
    ComPtr<ID3D11VertexShader> vertexShader_;
    ComPtr<ID3D11PixelShader> pixelShader_;
    ComPtr<ID3D11GeometryShader> geometryShader_;
    ComPtr<ID3D11HullShader> hullShader_;
    ComPtr<ID3D11DomainShader> domainShader_;
    ComPtr<ID3D11Predicate> predicate_;
    BOOL predicateValue_{};
    std::array<ID3D11ClassInstance*,MaxClassInstances> vsClasses_{};
    std::array<ID3D11ClassInstance*,MaxClassInstances> psClasses_{};
    std::array<ID3D11ClassInstance*,MaxClassInstances> gsClasses_{};
    std::array<ID3D11ClassInstance*,MaxClassInstances> hsClasses_{};
    std::array<ID3D11ClassInstance*,MaxClassInstances> dsClasses_{};
    UINT vsCount_{MaxClassInstances}, psCount_{MaxClassInstances},
         gsCount_{MaxClassInstances},hsCount_{MaxClassInstances},
         dsCount_{MaxClassInstances};
    // OMSetRenderTargets silently unbinds every overlapping SRV, even if
    // the draw itself only edits PS SRV0. Snapshot every shader-stage SRV
    // slot before switching the output target; restore outputs first.
    static constexpr UINT Srvs=D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT;
    using ShaderViews=std::array<ID3D11ShaderResourceView*,Srvs>;
    ShaderViews vsSrvs_{},psSrvs_{},gsSrvs_{},hsSrvs_{},dsSrvs_{},csSrvs_{};
    ComPtr<ID3D11SamplerState> samplerSlot0_;
    ComPtr<ID3D11RasterizerState> rasterizer_;
    std::array<D3D11_VIEWPORT,D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE> viewports_{};
    UINT viewportCount_{static_cast<UINT>(viewports_.size())};
    ComPtr<ID3D11BlendState> blend_;
    FLOAT blendFactor_[4]{};
    UINT sampleMask_{};
    std::array<ID3D11RenderTargetView*,D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT> renderTargets_{};
    ComPtr<ID3D11DepthStencilView> depthTarget_;
    // D3D11.1 Feature Level 11_1 exposes 64 OM/CS UAV slots; 11_0
    // exposes eight. Query only the slots valid on this device.
    static constexpr UINT MaxUavSlots=D3D11_1_UAV_SLOT_COUNT;
    UINT uavSlots_{D3D11_PS_CS_UAV_REGISTER_COUNT};
    std::array<ID3D11UnorderedAccessView*,MaxUavSlots> omUavs_{};
    std::array<ID3D11UnorderedAccessView*,MaxUavSlots> csUavs_{};
    // Original valid RTVs occupy 0..(rtvCount-1); UAVs occupy the
    // remaining OM slots. Restoring all eight RTVs would erase UAV1.
    UINT rtvCount_{};
public:
    explicit ScopedD3d11DrawState(ID3D11DeviceContext* ctx):ctx_(ctx) {
        ctx_->IAGetInputLayout(layout_.GetAddressOf());
        ctx_->IAGetVertexBuffers(0,1,vertex_.GetAddressOf(),&stride_,&offset_);
        ctx_->IAGetPrimitiveTopology(&topology_);
        ctx_->VSGetShader(vertexShader_.GetAddressOf(),vsClasses_.data(),&vsCount_);
        ctx_->PSGetShader(pixelShader_.GetAddressOf(),psClasses_.data(),&psCount_);
        ctx_->GSGetShader(geometryShader_.GetAddressOf(),gsClasses_.data(),&gsCount_);
        ctx_->HSGetShader(hullShader_.GetAddressOf(),hsClasses_.data(),&hsCount_);
        ctx_->DSGetShader(domainShader_.GetAddressOf(),dsClasses_.data(),&dsCount_);
        ctx_->GetPredication(predicate_.GetAddressOf(),&predicateValue_);
        ctx_->VSGetShaderResources(0,Srvs,vsSrvs_.data());
        ctx_->PSGetShaderResources(0,Srvs,psSrvs_.data());
        ctx_->GSGetShaderResources(0,Srvs,gsSrvs_.data());
        ctx_->HSGetShaderResources(0,Srvs,hsSrvs_.data());
        ctx_->DSGetShaderResources(0,Srvs,dsSrvs_.data());
        ctx_->CSGetShaderResources(0,Srvs,csSrvs_.data());
        ctx_->PSGetSamplers(0,1,samplerSlot0_.GetAddressOf());
        ctx_->RSGetState(rasterizer_.GetAddressOf());
        ctx_->RSGetViewports(&viewportCount_,viewports_.data());
        ctx_->OMGetBlendState(blend_.GetAddressOf(),blendFactor_,&sampleMask_);
        ComPtr<ID3D11Device> device;
        ctx_->GetDevice(device.GetAddressOf());
        if(device && device->GetFeatureLevel()>=D3D_FEATURE_LEVEL_11_1)
            uavSlots_=MaxUavSlots;
        ctx_->OMGetRenderTargetsAndUnorderedAccessViews(
            static_cast<UINT>(renderTargets_.size()),
            renderTargets_.data(),depthTarget_.GetAddressOf(),
            0,uavSlots_,omUavs_.data());
        // The overlay RTV may overlap a valid CS UAV and force DXGI to
        // unbind it. Restore CS outputs only AFTER the original OM state.
        ctx_->CSGetUnorderedAccessViews(0,uavSlots_,csUavs_.data());
        for(UINT slot=0;slot<renderTargets_.size();++slot)
            if(renderTargets_[slot])rtvCount_=slot+1;
    }
    ScopedD3d11DrawState(const ScopedD3d11DrawState&)=delete;
    ScopedD3d11DrawState& operator=(const ScopedD3d11DrawState&)=delete;
    ~ScopedD3d11DrawState() {
        ctx_->IASetInputLayout(layout_.Get());
        ID3D11Buffer* vb=vertex_.Get();
        ctx_->IASetVertexBuffers(0,1,&vb,&stride_,&offset_);
        ctx_->IASetPrimitiveTopology(topology_);
        ctx_->VSSetShader(vertexShader_.Get(),vsClasses_.data(),vsCount_);
        ctx_->PSSetShader(pixelShader_.Get(),psClasses_.data(),psCount_);
        ctx_->GSSetShader(geometryShader_.Get(),gsClasses_.data(),gsCount_);
        ctx_->HSSetShader(hullShader_.Get(),hsClasses_.data(),hsCount_);
        ctx_->DSSetShader(domainShader_.Get(),dsClasses_.data(),dsCount_);
        ctx_->SetPredication(predicate_.Get(),predicateValue_);
        ID3D11SamplerState* sampler=samplerSlot0_.Get();
        // Restore outputs before all shader stages: the overlay RTV may
        // overlap any saved SRV, not just the first pixel-shader slot.
        // OMSetRenderTargets drops all original OM UAVs. Restore the
        // *occupied RTV range* and nonoverlapping UAV range atomically.
        // KEEP counters instead of resetting append/consume UAV state.
        std::array<UINT,MaxUavSlots> keepCounts{};
        keepCounts.fill(static_cast<UINT>(-1));
        ctx_->OMSetRenderTargetsAndUnorderedAccessViews(
            rtvCount_,rtvCount_?renderTargets_.data():nullptr,
            depthTarget_.Get(),rtvCount_,uavSlots_-rtvCount_,
            rtvCount_<uavSlots_?omUavs_.data()+rtvCount_:nullptr,
            rtvCount_<uavSlots_?keepCounts.data()+rtvCount_:nullptr);
        // OMSetRenderTargets also unbinds overlapping CS UAVs. Rebinding
        // those before OM would immediately lose them again.
        ctx_->CSSetUnorderedAccessViews(0,uavSlots_,csUavs_.data(),keepCounts.data());
        ctx_->VSSetShaderResources(0,Srvs,vsSrvs_.data());
        ctx_->PSSetShaderResources(0,Srvs,psSrvs_.data());
        ctx_->GSSetShaderResources(0,Srvs,gsSrvs_.data());
        ctx_->HSSetShaderResources(0,Srvs,hsSrvs_.data());
        ctx_->DSSetShaderResources(0,Srvs,dsSrvs_.data());
        ctx_->CSSetShaderResources(0,Srvs,csSrvs_.data());
        ctx_->PSSetSamplers(0,1,&sampler);
        ctx_->RSSetState(rasterizer_.Get());
        ctx_->RSSetViewports(viewportCount_,viewportCount_?viewports_.data():nullptr);
        ctx_->OMSetBlendState(blend_.Get(),blendFactor_,sampleMask_);
        for(UINT i=0;i<vsCount_;++i)if(vsClasses_[i])vsClasses_[i]->Release();
        for(UINT i=0;i<psCount_;++i)if(psClasses_[i])psClasses_[i]->Release();
        for(UINT i=0;i<gsCount_;++i)if(gsClasses_[i])gsClasses_[i]->Release();
        for(UINT i=0;i<hsCount_;++i)if(hsClasses_[i])hsClasses_[i]->Release();
        for(UINT i=0;i<dsCount_;++i)if(dsClasses_[i])dsClasses_[i]->Release();
        for(auto& rtv:renderTargets_)if(rtv)rtv->Release();
        for(auto& uav:omUavs_)if(uav)uav->Release();
        for(auto& uav:csUavs_)if(uav)uav->Release();
        // GetShaderResources AddRefs every nonnull slot.
        auto release=[](ShaderViews& views) noexcept {
            for(auto*& view:views){if(view){view->Release();view=nullptr;}}
        };
        release(vsSrvs_);release(psSrvs_);release(gsSrvs_);
        release(hsSrvs_);release(dsSrvs_);release(csSrvs_);
    }
};
} // namespace graphics_hook_core
