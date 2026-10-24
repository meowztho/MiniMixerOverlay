#pragma once
// Experimental Pbgra32 premultiplied-alpha compositor for a caller-owned
// DXGI/D3D12 swapchain. No game injection, no IPC host and no input hooks.
// The caller serializes Present, ResizeBuffers, Draw and Cleanup.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstdint>
#include <cstring>
#include <atomic>
#include "graphics_contract.h"

namespace minimixer {
class D3d12PbgraCompositor final {
    template<class T> using Ptr=Microsoft::WRL::ComPtr<T>;
    struct Slot {
        Ptr<ID3D12Resource> buffer,texture,upload;
        Ptr<ID3D12CommandAllocator> allocator;
        Ptr<ID3D12GraphicsCommandList> list;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT layout{};
        D3D12_CPU_DESCRIPTOR_HANDLE rtv{};
        UINT texWidth{},texHeight{};
        UINT64 signal{};
    };
    static constexpr UINT Capacity=8;
    Slot slots_[Capacity]{};
    Ptr<IDXGISwapChain3> chain_;
    Ptr<ID3D12Device> device_;
    Ptr<ID3D12CommandQueue> queue_;
    Ptr<ID3D12Fence> fence_;
    Ptr<ID3D12DescriptorHeap> rtvHeap_,srvHeap_;
    Ptr<ID3D12RootSignature> root_;
    Ptr<ID3D12PipelineState> pipeline_;
    UINT count_{},surfaceWidth_{},surfaceHeight_{};
    DXGI_FORMAT targetFormat_{DXGI_FORMAT_UNKNOWN};
    UINT64 nextSignal_{};
    bool ready_{},untracked_{};
    HWND hwnd_{};
    static D3D12_RESOURCE_DESC Buffer(UINT64 bytes) noexcept {
        D3D12_RESOURCE_DESC r{};r.Dimension=D3D12_RESOURCE_DIMENSION_BUFFER;
        r.Width=bytes;r.Height=1;r.DepthOrArraySize=1;r.MipLevels=1;
        r.SampleDesc.Count=1;r.Layout=D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        return r;
    }
    static D3D12_HEAP_PROPERTIES Heap(D3D12_HEAP_TYPE type) noexcept {
        D3D12_HEAP_PROPERTIES p{};p.Type=type;p.CreationNodeMask=1;
        p.VisibleNodeMask=1;return p;
    }
    static D3D12_RESOURCE_BARRIER Barrier(ID3D12Resource* r,
                            D3D12_RESOURCE_STATES from,
                            D3D12_RESOURCE_STATES to) noexcept {
        D3D12_RESOURCE_BARRIER b{};
        b.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition.pResource=r;
        b.Transition.StateBefore=from;b.Transition.StateAfter=to;
        b.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        return b;
    }
    static constexpr const char* Shader=R"(
struct Vertex {float4 position:SV_POSITION;float2 uv:TEXCOORD;};
Vertex VS(uint id:SV_VertexID) {
    float2 p=float2(id==2?3.0:-1.0,id==1?3.0:-1.0);
    Vertex v;v.position=float4(p,0,1);
    v.uv=float2((p.x+1.0)*0.5,(1.0-p.y)*0.5);return v;
}
Texture2D frame:register(t0);
SamplerState pointSampler:register(s0);
float4 PS(Vertex v):SV_TARGET {return frame.Sample(pointSampler,v.uv);}
)";
    bool Pipeline() noexcept {
        Ptr<ID3DBlob> vs,ps,sig,err;
        const auto n=std::strlen(Shader);
        if(FAILED(D3DCompile(Shader,n,nullptr,nullptr,nullptr,"VS","vs_5_0",
                             0,0,&vs,nullptr)) ||
           FAILED(D3DCompile(Shader,n,nullptr,nullptr,nullptr,"PS","ps_5_0",
                             0,0,&ps,nullptr)))return false;
        D3D12_DESCRIPTOR_RANGE range{};
        range.RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        range.NumDescriptors=1;
        D3D12_ROOT_PARAMETER parm{};
        parm.ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        parm.DescriptorTable.NumDescriptorRanges=1;
        parm.DescriptorTable.pDescriptorRanges=&range;
        parm.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
        D3D12_STATIC_SAMPLER_DESC sampler{};
        sampler.Filter=D3D12_FILTER_MIN_MAG_MIP_POINT;
        sampler.AddressU=sampler.AddressV=sampler.AddressW=
            D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.MaxLOD=D3D12_FLOAT32_MAX;
        sampler.ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL;
        D3D12_ROOT_SIGNATURE_DESC rs{};
        rs.NumParameters=1;rs.pParameters=&parm;
        rs.NumStaticSamplers=1;rs.pStaticSamplers=&sampler;
        rs.Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
        if(FAILED(D3D12SerializeRootSignature(&rs,D3D_ROOT_SIGNATURE_VERSION_1,
                                              &sig,&err)) ||
           FAILED(device_->CreateRootSignature(0,sig->GetBufferPointer(),
                          sig->GetBufferSize(),IID_PPV_ARGS(&root_))))return false;
        D3D12_GRAPHICS_PIPELINE_STATE_DESC desc{};
        desc.pRootSignature=root_.Get();
        desc.VS={vs->GetBufferPointer(),vs->GetBufferSize()};
        desc.PS={ps->GetBufferPointer(),ps->GetBufferSize()};
        desc.BlendState.RenderTarget[0].BlendEnable=TRUE;
        auto& bl=desc.BlendState.RenderTarget[0];
        bl.SrcBlend=D3D12_BLEND_ONE;
        bl.DestBlend=D3D12_BLEND_INV_SRC_ALPHA;
        bl.BlendOp=D3D12_BLEND_OP_ADD;
        bl.SrcBlendAlpha=D3D12_BLEND_ONE;
        bl.DestBlendAlpha=D3D12_BLEND_INV_SRC_ALPHA;
        bl.BlendOpAlpha=D3D12_BLEND_OP_ADD;
        bl.RenderTargetWriteMask=D3D12_COLOR_WRITE_ENABLE_ALL;
        desc.SampleMask=UINT_MAX;
        desc.RasterizerState.FillMode=D3D12_FILL_MODE_SOLID;
        desc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
        desc.RasterizerState.DepthClipEnable=TRUE;
        desc.DepthStencilState.DepthEnable=FALSE;
        desc.DepthStencilState.StencilEnable=FALSE;
        desc.PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        desc.NumRenderTargets=1;
        desc.RTVFormats[0]=targetFormat_;
        desc.SampleDesc.Count=1;
        return SUCCEEDED(device_->CreateGraphicsPipelineState(
                         &desc,IID_PPV_ARGS(&pipeline_)));
    }
    bool Texture(Slot& slot,UINT index,const FrameView& f) noexcept {
        if(slot.texture && slot.texWidth==f.width && slot.texHeight==f.height)
            return true;
        // Caller must ensure slot.signal fence is complete before reuse.
        slot.texture.Reset();slot.upload.Reset();
        D3D12_RESOURCE_DESC tex{};
        tex.Dimension=D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        tex.Width=f.width;tex.Height=f.height;
        tex.DepthOrArraySize=1;tex.MipLevels=1;
        tex.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        tex.SampleDesc.Count=1;
        const auto gpu=Heap(D3D12_HEAP_TYPE_DEFAULT);
        if(FAILED(device_->CreateCommittedResource(&gpu,D3D12_HEAP_FLAG_NONE,
                 &tex,D3D12_RESOURCE_STATE_COPY_DEST,nullptr,
                 IID_PPV_ARGS(&slot.texture))))return false;
        UINT64 bytes{};
        device_->GetCopyableFootprints(&tex,0,1,0,&slot.layout,
                                       nullptr,nullptr,&bytes);
        const auto cpu=Heap(D3D12_HEAP_TYPE_UPLOAD);
        const auto buf=Buffer(bytes);
        if(FAILED(device_->CreateCommittedResource(&cpu,D3D12_HEAP_FLAG_NONE,
                &buf,D3D12_RESOURCE_STATE_GENERIC_READ,nullptr,
                IID_PPV_ARGS(&slot.upload))))return false;
        D3D12_SHADER_RESOURCE_VIEW_DESC srv{};
        srv.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        srv.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D;
        srv.Texture2D.MipLevels=1;
        auto descriptor=srvHeap_->GetCPUDescriptorHandleForHeapStart();
        descriptor.ptr+=static_cast<SIZE_T>(index)*
            device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        device_->CreateShaderResourceView(slot.texture.Get(),&srv,descriptor);
        slot.texWidth=f.width;slot.texHeight=f.height;
        return true;
    }
public:
    std::atomic<unsigned> submitted{0},skipped{0},errors{0};
    bool Ready() const noexcept{return ready_;}
    bool Init(IDXGISwapChain* swapchain,ID3D12CommandQueue* queue,HWND hwnd) noexcept {
        if(ready_||!swapchain||!queue||!hwnd)return false;
        DXGI_SWAP_CHAIN_DESC desc{};
        if(FAILED(swapchain->GetDesc(&desc))||desc.OutputWindow!=hwnd||
           desc.BufferCount<2||desc.BufferCount>Capacity ||
           desc.BufferDesc.Width<128||desc.BufferDesc.Height<120||
           (desc.BufferDesc.Format!=DXGI_FORMAT_R10G10B10A2_UNORM &&
            desc.BufferDesc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM &&
            desc.BufferDesc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM) ||
           queue->GetDesc().Type!=D3D12_COMMAND_LIST_TYPE_DIRECT)return false;
        Ptr<ID3D12Device> a,b;
        if(FAILED(swapchain->GetDevice(IID_PPV_ARGS(&a))) ||
           FAILED(queue->GetDevice(IID_PPV_ARGS(&b)))||a.Get()!=b.Get())
            return false;
        if(FAILED(swapchain->QueryInterface(IID_PPV_ARGS(&chain_))))return false;
        device_=a;queue_=queue;
        count_=desc.BufferCount;
        surfaceWidth_=desc.BufferDesc.Width;surfaceHeight_=desc.BufferDesc.Height;
        targetFormat_=desc.BufferDesc.Format;hwnd_=hwnd;
        D3D12_DESCRIPTOR_HEAP_DESC rtv{};rtv.NumDescriptors=count_;
        rtv.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        if(FAILED(device_->CreateDescriptorHeap(&rtv,IID_PPV_ARGS(&rtvHeap_))))return false;
        D3D12_DESCRIPTOR_HEAP_DESC srv{};
        srv.Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        srv.NumDescriptors=count_;srv.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if(FAILED(device_->CreateDescriptorHeap(&srv,IID_PPV_ARGS(&srvHeap_)))||
           FAILED(device_->CreateFence(0,D3D12_FENCE_FLAG_NONE,
                                      IID_PPV_ARGS(&fence_))) ||
           !Pipeline())return false;
        const auto increment=device_->GetDescriptorHandleIncrementSize(
                                       D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        auto base=rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        for(UINT i=0;i<count_;++i){
            auto& slot=slots_[i];
            if(FAILED(chain_->GetBuffer(i,IID_PPV_ARGS(&slot.buffer))))return false;
            const auto bd=slot.buffer->GetDesc();
            if(bd.Width!=surfaceWidth_||bd.Height!=surfaceHeight_||
               bd.Format!=targetFormat_||bd.SampleDesc.Count!=1)return false;
            slot.rtv.ptr=base.ptr+static_cast<SIZE_T>(i)*increment;
            device_->CreateRenderTargetView(slot.buffer.Get(),nullptr,slot.rtv);
            if(FAILED(device_->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
                       IID_PPV_ARGS(&slot.allocator)))||
               FAILED(device_->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,
                       slot.allocator.Get(),nullptr,IID_PPV_ARGS(&slot.list)))||
               FAILED(slot.list->Close()))return false;
        }
        ready_=true;return true;
    }
    bool Draw(const FrameView& frame,UINT left,UINT top) noexcept {
        if(!ready_||untracked_||!frame.valid()||
           frame.width>surfaceWidth_ || frame.height>surfaceHeight_ ||
           left>surfaceWidth_-frame.width||top>surfaceHeight_-frame.height)
            return false;
        DXGI_SWAP_CHAIN_DESC desc{};
        if(FAILED(chain_->GetDesc(&desc))||desc.OutputWindow!=hwnd_||
           desc.BufferCount!=count_||desc.BufferDesc.Width!=surfaceWidth_||
           desc.BufferDesc.Height!=surfaceHeight_||
           desc.BufferDesc.Format!=targetFormat_)return false;
        const auto idx=chain_->GetCurrentBackBufferIndex();
        if(idx>=count_){++errors;return false;}
        auto& slot=slots_[idx];
        if(slot.signal&&fence_->GetCompletedValue()<slot.signal){
            ++skipped;return false;
        }
        if(!Texture(slot,idx,frame)){++errors;return false;}
        std::uint8_t* mapped{};
        const D3D12_RANGE readNone{0,0};
        if(FAILED(slot.upload->Map(0,&readNone,
                                  reinterpret_cast<void**>(&mapped)))||!mapped){
            ++errors;return false;
        }
        for(UINT y=0;y<frame.height;++y)
            std::memcpy(mapped+slot.layout.Offset+
                          static_cast<size_t>(y)*slot.layout.Footprint.RowPitch,
                        frame.bgra+static_cast<size_t>(y)*frame.stride,
                        static_cast<size_t>(frame.width)*4);
        slot.upload->Unmap(0,nullptr);
        if(FAILED(slot.allocator->Reset())||
           FAILED(slot.list->Reset(slot.allocator.Get(),pipeline_.Get()))){
            ++errors;return false;
        }
        auto* cmds=slot.list.Get();
        D3D12_TEXTURE_COPY_LOCATION dst{},src{};
        dst.pResource=slot.texture.Get();
        dst.Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        src.pResource=slot.upload.Get();
        src.Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        src.PlacedFootprint=slot.layout;
        cmds->CopyTextureRegion(&dst,0,0,0,&src,nullptr);
        auto textureToShader=Barrier(slot.texture.Get(),
                    D3D12_RESOURCE_STATE_COPY_DEST,
                    D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        auto backToTarget=Barrier(slot.buffer.Get(),
                    D3D12_RESOURCE_STATE_PRESENT,
                    D3D12_RESOURCE_STATE_RENDER_TARGET);
        cmds->ResourceBarrier(1,&textureToShader);
        cmds->ResourceBarrier(1,&backToTarget);
        cmds->OMSetRenderTargets(1,&slot.rtv,FALSE,nullptr);
        D3D12_VIEWPORT viewport{
            static_cast<float>(left),static_cast<float>(top),
            static_cast<float>(frame.width),static_cast<float>(frame.height),0,1};
        const D3D12_RECT clip{
            static_cast<LONG>(left),static_cast<LONG>(top),
            static_cast<LONG>(left+frame.width),
            static_cast<LONG>(top+frame.height)};
        cmds->RSSetViewports(1,&viewport);
        cmds->RSSetScissorRects(1,&clip);
        cmds->SetGraphicsRootSignature(root_.Get());
        cmds->SetPipelineState(pipeline_.Get());
        ID3D12DescriptorHeap* heaps[]{srvHeap_.Get()};
        cmds->SetDescriptorHeaps(1,heaps);
        auto handle=srvHeap_->GetGPUDescriptorHandleForHeapStart();
        handle.ptr+=static_cast<UINT64>(idx)*
            device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        cmds->SetGraphicsRootDescriptorTable(0,handle);
        cmds->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        cmds->DrawInstanced(3,1,0,0);
        auto texBack=Barrier(slot.texture.Get(),
                D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                D3D12_RESOURCE_STATE_COPY_DEST);
        auto backPresent=Barrier(slot.buffer.Get(),
                D3D12_RESOURCE_STATE_RENDER_TARGET,
                D3D12_RESOURCE_STATE_PRESENT);
        cmds->ResourceBarrier(1,&texBack);
        cmds->ResourceBarrier(1,&backPresent);
        if(FAILED(cmds->Close())){++errors;return false;}
        ID3D12CommandList* work[]{cmds};
        queue_->ExecuteCommandLists(1,work);
        untracked_=true;
        const UINT64 signal=++nextSignal_;
        if(FAILED(queue_->Signal(fence_.Get(),signal))){++errors;return false;}
        slot.signal=signal;
        untracked_=false;
        ++submitted;return true;
    }
    bool CanRelease() const noexcept {
        if(untracked_)return false;
        if(!fence_)return true;
        const UINT64 done=fence_->GetCompletedValue();
        for(UINT i=0;i<count_;++i)
            if(slots_[i].signal>done)return false;
        return true;
    }
    bool WaitIdle(DWORD timeoutMs) const noexcept {
        if(CanRelease())return true;
        if(untracked_||!fence_)return false;
        HANDLE ev=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!ev)return false;
        const HRESULT hr=fence_->SetEventOnCompletion(nextSignal_,ev);
        const DWORD result=SUCCEEDED(hr) ?
            WaitForSingleObject(ev,timeoutMs):WAIT_FAILED;
        CloseHandle(ev);
        return result==WAIT_OBJECT_0&&CanRelease();
    }
    bool Cleanup() noexcept {
        if(!CanRelease())return false;
        ready_=false;
        for(auto& slot:slots_)slot=Slot{};
        pipeline_.Reset();root_.Reset();
        srvHeap_.Reset();rtvHeap_.Reset();fence_.Reset();
        chain_.Reset();queue_.Reset();device_.Reset();
        count_=surfaceWidth_=surfaceHeight_=0;
        targetFormat_=DXGI_FORMAT_UNKNOWN;
        nextSignal_=0;untracked_=false;hwnd_=nullptr;
        return true;
    }
    bool PrepareForResize() noexcept {
        return Cleanup();
    }
};
} // namespace minimixer
