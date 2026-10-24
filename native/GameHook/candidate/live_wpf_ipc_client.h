#pragma once
// Experimental native MiniMixer GameHookOverlayRuntime IPC adapter.
// One background Win32 message thread owns WM_COPYDATA, mapping/mutex and
// handshake. Present ONLY reads immutable snapshots / publishes game HWND.
// Optional second bounded worker polls cursor ONLY with registered session,
// explicit opt-in, foreground game, visible pointer and host input policy.
// Input messages use a short bounded WM_COPYDATA call from that worker.
// No global USER32 hooks, OS mouse injection or duplicate audio ownership.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <memory>
#include <new>
#include <string>
#include <vector>
#include <cstdlib>
#include <limits>
#include "wpf_frame_mailbox.h"
#include "live_wpf_cursor_router.h"

namespace minimixer {
class LiveWpfIpcClient final {
    static constexpr UINT Connect=0x8201,Ack=0x8202,Close=0x8203;
    static constexpr UINT FrameMessage=WM_APP+12,Hello=WM_APP+11;
    HANDLE worker_{},stop_{},helloWorker_{},cursorWorker_{};
    HWND own_{},host_{};
    DWORD hostPid_{};
    std::atomic<HWND> inputHost_{nullptr},inputGameWindow_{nullptr};
    std::atomic<DWORD> inputHostPid_{0};
    std::atomic<UINT> auxiliaryWindowId_{0},mainWindowId_{0};
    std::atomic<bool> mainInputPolicyAllowed_{false};
    std::atomic<bool> inputSessionReady_{false},inputPolicyAllowed_{false};
    bool allowSphereInput_{false};
    std::string hostName_;
    WpfFrameMailbox mailbox_,auxiliaryMailbox_;
    std::string mutexName_;
    bool initialized_{},pending_{},auxiliaryPending_{};
    ULONGLONG lastConnect_{};
    static bool Int32(const std::uint8_t* p,size_t size,size_t& at,
                      std::int32_t& v) noexcept {
        if(at>size||size-at<4)return false;
        std::memcpy(&v,p+at,4);at+=4;return true;
    }
    static bool String(const std::uint8_t* p,size_t size,size_t& at,
                       std::string& s) {
        std::int32_t len{};
        if(!Int32(p,size,at,len)||len<0||len>65536||
           static_cast<size_t>(len)>size-at)return false;
        s.assign(reinterpret_cast<const char*>(p+at),
                 static_cast<size_t>(len));
        at+=static_cast<size_t>(len);return true;
    }
    static std::string Field(const std::string& json,const char* name){
        const auto pos=json.find(std::string("\"")+name+"\"");
        if(pos==std::string::npos)return {};
        const auto colon=json.find(':',pos);
        if(colon==std::string::npos)return {};
        const auto first=json.find('"',colon+1);
        if(first==std::string::npos)return {};
        const auto last=json.find('"',first+1);
        if(last==std::string::npos ||last-first>1025)return {};
        return json.substr(first+1,last-first-1);
    }
    static UINT Identifier(const std::string& json,const char* name) noexcept {
        const auto pos=json.find(std::string("\"")+name+"\"");
        if(pos==std::string::npos)return 0;
        const auto colon=json.find(':',pos);
        if(colon==std::string::npos)return 0;
        const char* cursor=json.c_str()+colon+1;
        while(*cursor==' '||*cursor=='\t')++cursor;
        if(*cursor<'0'||*cursor>'9')return 0;
        char* end{};
        const unsigned long long val=strtoull(cursor,&end,10);
        if(end==cursor||val==0||val>(std::numeric_limits<UINT>::max)() ||
           (*end!=','&&*end!='}'&&*end!=' '&&*end!='\t'))return 0;
        return static_cast<UINT>(val);
    }
    static int Number(const std::string& json,const char* name,int fallback){
        const auto pos=json.find(std::string("\"")+name+"\"");
        if(pos==std::string::npos)return fallback;
        const auto colon=json.find(':',pos);
        if(colon==std::string::npos)return fallback;
        const char* s=json.c_str()+colon+1;
        char* end{};
        const long v=strtol(s,&end,10);
        return end==s||v<-100000||v>100000?fallback:static_cast<int>(v);
    }
    static void AddInt(std::vector<std::uint8_t>& a,std::int32_t v){
        const auto* data=reinterpret_cast<const std::uint8_t*>(&v);
        a.insert(a.end(),data,data+4);
    }
    static void AddString(std::vector<std::uint8_t>& a,const char* s){
        const auto len=static_cast<std::int32_t>(std::strlen(s));
        AddInt(a,len);
        const auto* bytes=reinterpret_cast<const std::uint8_t*>(s);
        a.insert(a.end(),bytes,bytes+len);
    }
    static bool SendHello(HWND host,DWORD expectedPid,bool sphereInput) noexcept {
        DWORD actualPid{};
        if(!host||!IsWindow(host)||
           !GetWindowThreadProcessId(host,&actualPid)||
           actualPid!=expectedPid)return false;
        std::vector<std::uint8_t> message(12,0);
        AddInt(message,100);
        AddString(message,"game.process");
        AddString(message,sphereInput
            ? R"({"experimentalAuxiliarySurface":true,"experimentalAuxiliaryInput":true,"experimentalMainInput":true})"
            : R"({"experimentalAuxiliarySurface":true})");
        COPYDATASTRUCT data{};
        data.dwData=GetCurrentProcessId();
        data.cbData=static_cast<DWORD>(message.size());
        data.lpData=message.data();
        DWORD_PTR result{};
        return SendMessageTimeoutW(host,WM_COPYDATA,0,
              reinterpret_cast<LPARAM>(&data),
              SMTO_ABORTIFHUNG|SMTO_BLOCK,200,&result)!=0;
    }
    struct HelloArgs {
        HWND host{};DWORD pid{};
        std::atomic<unsigned>* timeoutCounter{};
        bool sphereInput{};
    };
    static DWORD WINAPI SendHelloWorker(void* arg) noexcept {
        std::unique_ptr<HelloArgs> request(static_cast<HelloArgs*>(arg));
        if(!SendHello(request->host,request->pid,request->sphereInput))
            ++(*request->timeoutCounter);
        return 0;
    }
    bool AuthoritativeHost() const noexcept {
        DWORD pid{};
        return host_&&IsWindow(host_)&&
               GetWindowThreadProcessId(host_,&pid)!=0&&pid==hostPid_;
    }
    void Receive(const std::string& type,const std::string& json) noexcept {
        if(type=="overlay.init"){
            const auto mutex=Field(json,"shareMemMutex");
            const auto buffer=Field(json,"bufferName");
            if(mutex.empty()||buffer.empty()){
                ++invalid;return;
            }
            const int x=Number(json,"x",0),y=Number(json,"y",0);
            mailbox_.Configure(mutex,buffer,x,y);
            mutexName_=mutex;
            initialized_=true;
            mainWindowId_.store(Identifier(json,"windowId"),
                                std::memory_order_release);
            inputSessionReady_.store(true,std::memory_order_release);
            ++inits;
        } else if(type=="window.bounds"){
            const auto b=Field(json,"bufferName");
            if(!b.empty())mailbox_.SetBuffer(b);
            mailbox_.SetBounds(Number(json,"x",0),Number(json,"y",0));
        } else if(type=="experimental.main.input.policy"&&initialized_){
            mainInputPolicyAllowed_.store(json.find("\"enabled\":true")!=
                std::string::npos,std::memory_order_release);
        } else if(type=="experimental.aux.input.policy"&&initialized_){
            inputPolicyAllowed_.store(json.find("\"enabled\":true")!=
                                      std::string::npos,
                                      std::memory_order_release);
        } else if(type=="experimental.aux.bounds"&&initialized_){
            const auto buffer=Field(json,"bufferName");
            if(!buffer.empty()&&!mutexName_.empty())
                auxiliaryMailbox_.Configure(mutexName_,buffer,
                          Number(json,"x",0),Number(json,"y",0));
            auxiliaryWindowId_.store(Identifier(json,"windowId"),
                                     std::memory_order_release);
        } else if(type=="experimental.aux.framebuffer"&&initialized_){
            if(!auxiliaryPending_){
                auxiliaryPending_=true;
                PostMessageW(own_,FrameMessage,1,0);
            }
        } else if(type=="experimental.aux.hidden"){
            auxiliaryWindowId_.store(0,std::memory_order_release);
            auxiliaryMailbox_.Reset();
        } else if(type=="window.framebuffer"&&initialized_){
            if(!pending_){
                pending_=true;
                PostMessageW(own_,FrameMessage,0,0);
            }
        }
    }
    LRESULT OnMessage(HWND window,UINT msg,WPARAM w,LPARAM l) noexcept {
        if(msg==Ack){
            if(AuthoritativeHost()&&static_cast<DWORD>(w)==hostPid_){
                ++acks;
                PostMessageW(window,Hello,0,0);
            }
            return 0;
        }
        if(msg==Hello){
            if(!AuthoritativeHost())return 0;
            if(helloWorker_){
                if(WaitForSingleObject(helloWorker_,0)!=WAIT_OBJECT_0)
                    return 0;
                CloseHandle(helloWorker_);helloWorker_=nullptr;
            }
            auto request=std::unique_ptr<HelloArgs>(
                new (std::nothrow) HelloArgs{host_,hostPid_,&helloTimeouts,
                                            allowSphereInput_});
            if(!request){++invalid;return 0;}
            helloWorker_=CreateThread(nullptr,0,SendHelloWorker,
                                      request.get(),0,nullptr);
            if(helloWorker_)request.release();
            else ++invalid;
            return 0;
        }
        if(msg==WM_COPYDATA){
            const auto* data=reinterpret_cast<const COPYDATASTRUCT*>(l);
            if(!AuthoritativeHost()||!data||!data->lpData||
               data->cbData<20||data->cbData>65536||
               static_cast<DWORD>(data->dwData)!=hostPid_){
                ++invalid;return FALSE;
            }
            const auto* bytes=static_cast<const std::uint8_t*>(data->lpData);
            size_t at{};
            std::int32_t direction{},reserved{},id{};
            if(!Int32(bytes,data->cbData,at,direction) ||
               !Int32(bytes,data->cbData,at,reserved) ||
               !Int32(bytes,data->cbData,at,reserved) ||
               !Int32(bytes,data->cbData,at,id) ||
               direction!=1||id!=100){
                ++invalid;return FALSE;
            }
            std::string type,json;
            if(!String(bytes,data->cbData,at,type) ||
               !String(bytes,data->cbData,at,json) ||at!=data->cbData){
                ++invalid;return FALSE;
            }
            Receive(type,json);
            return TRUE;
        }
        if(msg==FrameMessage){
            if(w==1){
                auxiliaryPending_=false;
                if(initialized_&&!auxiliaryMailbox_.Capture())++missed;
            } else {
                pending_=false;
                if(initialized_&&!mailbox_.Capture())++missed;
            }
            return 0;
        }
        if(msg==Close){
            initialized_=false;
            inputSessionReady_.store(false,std::memory_order_release);
            inputPolicyAllowed_.store(false,std::memory_order_release);
            mainInputPolicyAllowed_.store(false,std::memory_order_release);
            mainWindowId_.store(0,std::memory_order_release);
            auxiliaryWindowId_.store(0,std::memory_order_release);
            mainWindowId_.store(0,std::memory_order_release);
            mainInputPolicyAllowed_.store(false,std::memory_order_release);
            mailbox_.Reset();auxiliaryMailbox_.Reset();
            mutexName_.clear();
            return 0;
        }
        return DefWindowProcW(window,msg,w,l);
    }
    static LRESULT CALLBACK WindowProc(HWND window,UINT msg,
                                      WPARAM w,LPARAM l) noexcept {
        if(msg==WM_NCCREATE){
            auto* create=reinterpret_cast<CREATESTRUCTW*>(l);
            SetWindowLongPtrW(window,GWLP_USERDATA,
                 reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto* self=reinterpret_cast<LiveWpfIpcClient*>(
                        GetWindowLongPtrW(window,GWLP_USERDATA));
        return self?self->OnMessage(window,msg,w,l):
                    DefWindowProcW(window,msg,w,l);
    }
    void PollHost() noexcept {
        HWND found=FindWindowA("STATIC",hostName_.c_str());
        DWORD pid{};
        if(found)GetWindowThreadProcessId(found,&pid);
        if(pid==GetCurrentProcessId())found=nullptr;
        if(!found||!pid){
            if(host_){
                host_=nullptr;hostPid_=0;initialized_=false;
                inputSessionReady_.store(false,std::memory_order_release);
            inputPolicyAllowed_.store(false,std::memory_order_release);
                auxiliaryWindowId_.store(0,std::memory_order_release);
                inputHost_.store(nullptr,std::memory_order_release);
                inputHostPid_.store(0,std::memory_order_release);
                mailbox_.Reset();auxiliaryMailbox_.Reset();
                mutexName_.clear();++disconnects;
            }
            return;
        }
        if(found!=host_||pid!=hostPid_){
            mailbox_.Reset();auxiliaryMailbox_.Reset();
            mutexName_.clear();initialized_=false;
            inputSessionReady_.store(false,std::memory_order_release);
            inputPolicyAllowed_.store(false,std::memory_order_release);
            mainInputPolicyAllowed_.store(false,std::memory_order_release);
            mainWindowId_.store(0,std::memory_order_release);
            auxiliaryWindowId_.store(0,std::memory_order_release);
            host_=found;hostPid_=pid;lastConnect_=0;
            inputHostPid_.store(pid,std::memory_order_release);
            inputHost_.store(found,std::memory_order_release);
        }
        const auto now=GetTickCount64();
        if(!initialized_&&(lastConnect_==0||now-lastConnect_>3000)){
            lastConnect_=now;
            if(PostMessageW(host_,Connect,
                            reinterpret_cast<WPARAM>(own_),0))
                ++connects;
        }
    }
    static bool SendCursorInput(HWND host,DWORD expectedPid,
                                const CursorEvent& e,
                                const char* inputType) noexcept {
        DWORD pid{};
        if(!host || !IsWindow(host) ||
           GetWindowThreadProcessId(host,&pid)==0 ||
           pid!=expectedPid || pid==GetCurrentProcessId() ||
           !e.windowId) return false;
        char payload[176]{};
        const int n=snprintf(payload,sizeof(payload),
            "{\"windowId\":%u,\"msg\":%u,\"wparam\":%u,\"lparam\":%u}",
            e.windowId,e.message,e.wparam,e.lparam);
        if(n<=0||static_cast<size_t>(n)>=sizeof(payload))return false;
        std::vector<std::uint8_t> bytes(12,0);
        AddInt(bytes,100);
        AddString(bytes,inputType);
        AddString(bytes,payload);
        COPYDATASTRUCT copy{};
        copy.dwData=GetCurrentProcessId();
        copy.cbData=static_cast<DWORD>(bytes.size());
        copy.lpData=bytes.data();
        DWORD_PTR ignored{};
        return SendMessageTimeoutW(host,WM_COPYDATA,0,
               reinterpret_cast<LPARAM>(&copy),
               SMTO_ABORTIFHUNG|SMTO_BLOCK,40,&ignored)!=0;
    }
    void DeliverCursor(const CursorActions& actions,const char* type) noexcept {
        const HWND host=inputHost_.load(std::memory_order_acquire);
        const DWORD pid=inputHostPid_.load(std::memory_order_acquire);
        for(UINT i=0;i<actions.count;++i){
            ++inputEmitted;
            const UINT message=actions.events[i].message;
            if(inputGameWindow_.load(std::memory_order_acquire)==nullptr &&
                message!=WM_CANCELMODE && message!=WM_MOUSELEAVE){
                ++inputRejected;
                continue;
            }
            if(SendCursorInput(host,pid,actions.events[i],type))++inputSent;
            else ++inputRejected;
        }
    }
    void CancelCursor(WpfSphereCursorRouter& sphere,
                      WpfSphereCursorRouter& main) noexcept {
        DeliverCursor(sphere.Cancel(),"experimental.aux.input");
        DeliverCursor(main.Cancel(),"game.input");
    }
    DWORD PollCursor(WpfSphereCursorRouter& sphere,
                     WpfSphereCursorRouter& main) noexcept {
        // One cursor worker and one exclusive capture owner. The sphere
        // is composited above Main and wins overlapping alpha hits unless
        // Main already owns the gesture.
        const bool sphereAllowed=inputPolicyAllowed_.load(std::memory_order_acquire);
        const bool mainAllowed=mainInputPolicyAllowed_.load(std::memory_order_acquire);
        if(!inputSessionReady_.load(std::memory_order_acquire) ||
           (!sphereAllowed && !mainAllowed)){
            CancelCursor(sphere,main);return 250;
        }
        const HWND game=inputGameWindow_.load(std::memory_order_acquire);
        const HWND host=inputHost_.load(std::memory_order_acquire);
        const DWORD pid=inputHostPid_.load(std::memory_order_acquire);
        const UINT sphereId=auxiliaryWindowId_.load(std::memory_order_acquire);
        const UINT mainId=mainWindowId_.load(std::memory_order_acquire);
        DWORD gamePid{},hostPid{};
        const bool trusted=game && host && pid &&
            GetWindowThreadProcessId(game,&gamePid)!=0 &&
            gamePid==GetCurrentProcessId() &&
            GetWindowThreadProcessId(host,&hostPid)!=0 &&
            hostPid==pid && hostPid!=gamePid &&
            GetForegroundWindow()==game;
        if(!trusted){CancelCursor(sphere,main);return 100;}
        CURSORINFO ci{};ci.cbSize=sizeof(ci);
        if(!GetCursorInfo(&ci) || !(ci.flags&CURSOR_SHOWING)){
            CancelCursor(sphere,main);return 100;
        }
        ++inputSamples;
        const bool pressed=(GetAsyncKeyState(VK_LBUTTON)&0x8000)!=0;
        DWORD hoveredPid{};
        HWND cursorWindow=WindowFromPoint(ci.ptScreenPos);
        if(cursorWindow)GetWindowThreadProcessId(cursorWindow,&hoveredPid);
        if(hoveredPid!=GetCurrentProcessId()){
            // WPF/desktop owns physical cursor: prevent duplicate native
            // clicks and explicitly release any previous native capture.
            CancelCursor(sphere,main);return 100;
        }
        const auto sf=sphereAllowed && sphereId ?
            auxiliaryMailbox_.Latest():nullptr;
        const auto mf=mainAllowed && mainId ? mailbox_.Latest():nullptr;
        // An expired frame or revoked policy must release its own
        // capture BEFORE priority arbitration. Otherwise the stale owner
        // wins and a null Evaluate would silently discard the terminal.
        if(sphere.Captured() && (!sf || !sf->View().valid() || !sphereId))
            DeliverCursor(sphere.Cancel(),"experimental.aux.input");
        if(main.Captured() && (!mf || !mf->View().valid() || !mainId))
            DeliverCursor(main.Cancel(),"game.input");
        const bool sphereHit=WpfSphereCursorRouter::HitTest(
            sf.get(),ci.ptScreenPos.x,ci.ptScreenPos.y);
        const bool mainHit=WpfSphereCursorRouter::HitTest(
            mf.get(),ci.ptScreenPos.x,ci.ptScreenPos.y);
        const auto owner=PickCursorLayer(sphere.Captured(),main.Captured(),
                                         sphereHit,mainHit);
        if(owner==CursorLayer::Sphere){
            DeliverCursor(main.Cancel(),"game.input");
            DeliverCursor(sphere.Evaluate(sf.get(),sphereId,true,true,
                ci.ptScreenPos.x,ci.ptScreenPos.y,pressed),
                "experimental.aux.input");
        }else if(owner==CursorLayer::Main){
            DeliverCursor(sphere.Cancel(),"experimental.aux.input");
            DeliverCursor(main.Evaluate(mf.get(),mainId,true,true,
                ci.ptScreenPos.x,ci.ptScreenPos.y,pressed),"game.input");
        }else{
            CancelCursor(sphere,main);
        }
        return 33;
    }
    static DWORD WINAPI InputEntry(void* opaque) noexcept {
        auto* self=static_cast<LiveWpfIpcClient*>(opaque);
        WpfSphereCursorRouter router,mainRouter;
        while(true){
            const DWORD delay=self->PollCursor(router,mainRouter);
            const auto status=WaitForSingleObject(self->stop_,delay);
            if(status==WAIT_OBJECT_0){
                self->CancelCursor(router,mainRouter);
                return 0;
            }
            if(status!=WAIT_TIMEOUT){
                self->CancelCursor(router,mainRouter);
                return 1;
            }
        }
    }
    DWORD Run() noexcept {
        const HINSTANCE module=GetModuleHandleW(nullptr);
        const wchar_t* cls=L"MiniMixerNativeFrameReadOnlyClient";
        WNDCLASSW c{};
        c.lpfnWndProc=WindowProc;c.hInstance=module;c.lpszClassName=cls;
        RegisterClassW(&c);
        own_=CreateWindowExW(0,cls,L"MiniMixer native read-only",
             0,0,0,0,0,HWND_MESSAGE,nullptr,module,this);
        if(!own_)return 1;
        PollHost(); // Do not wait 1s before first host handshake.
        const HANDLE handles[]{stop_};
        while(true){
            const DWORD result=MsgWaitForMultipleObjects(1,handles,FALSE,
                                                        1000,QS_ALLINPUT);
            if(result==WAIT_OBJECT_0)break;
            if(result==WAIT_OBJECT_0+1){
                MSG message{};
                while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){
                    if(message.message==WM_QUIT)break;
                    TranslateMessage(&message);DispatchMessageW(&message);
                }
            }
            PollHost();
            if(result==WAIT_FAILED)break;
        }
        // SendMessageTimeout is <=200ms; drain before destroying the
        // client state. The receiver thread itself never waits on the host.
        if(helloWorker_){
            const DWORD status=WaitForSingleObject(helloWorker_,500);
            if(status!=WAIT_OBJECT_0)return 2;
            CloseHandle(helloWorker_);helloWorker_=nullptr;
        }
        if(AuthoritativeHost())
            PostMessageW(host_,Close,GetCurrentProcessId(),0);
        inputSessionReady_.store(false,std::memory_order_release);
            inputPolicyAllowed_.store(false,std::memory_order_release);
        auxiliaryWindowId_.store(0,std::memory_order_release);
        inputHost_.store(nullptr,std::memory_order_release);
        inputHostPid_.store(0,std::memory_order_release);
        mailbox_.Reset();auxiliaryMailbox_.Reset();
        DestroyWindow(own_);own_=nullptr;
        host_=nullptr;hostPid_=0;initialized_=false;
        return 0;
    }
    static DWORD WINAPI Entry(void* opaque) noexcept {
        return static_cast<LiveWpfIpcClient*>(opaque)->Run();
    }
public:
    std::atomic<unsigned> acks{},connects{},inits{},missed{},invalid{},
                          disconnects{},helloTimeouts{},inputSamples{},
                          inputEmitted{},inputSent{},inputRejected{};
    void SetGameWindow(HWND target) noexcept {
        // Once validated, the Present path only performs an atomic read.
        // Explicit revocation must clear the shared input target even if
        // the previous HWND is still live; the cursor worker then emits
        // Cancel/Leave rather than continuing a stale native capture.
        if(!target) {
            inputGameWindow_.store(nullptr,std::memory_order_release);
            return;
        }
        if(inputGameWindow_.load(std::memory_order_acquire)==target)return;
        DWORD pid{};
        if(target&&IsWindow(target)&&GetWindowThreadProcessId(target,&pid)
           &&pid==GetCurrentProcessId())
            inputGameWindow_.store(target,std::memory_order_release);
    }
    bool Start(const std::string& hostName,
               bool enableSphereInput=false) noexcept {
        if(worker_||hostName.empty()||hostName.size()>128)return false;
        allowSphereInput_=enableSphereInput;
        hostName_=hostName;
        stop_=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        if(!stop_)return false;
        worker_=CreateThread(nullptr,0,Entry,this,0,nullptr);
        if(!worker_){CloseHandle(stop_);stop_=nullptr;return false;}
        if(allowSphereInput_){
            cursorWorker_=CreateThread(nullptr,0,InputEntry,this,0,nullptr);
            if(!cursorWorker_){
                if(!Stop())return false;
                return false;
            }
        }
        return true;
    }
    std::shared_ptr<const WpfFrameSnapshot> Latest() const noexcept {
        return mailbox_.Latest();
    }
#ifdef MINIMIXER_OWNED_IPC_TEST_ONLY
    // Compiled into owned probes ONLY, not the injected game DLL.
    // Allows synthetic IPC-wire test without touching OS/system cursor.
    bool TestInputPolicyAllowed() const noexcept {
        return inputPolicyAllowed_.load(std::memory_order_acquire);
    }
    bool TestMainInputPolicyAllowed() const noexcept {
        return mainInputPolicyAllowed_.load(std::memory_order_acquire);
    }
    UINT TestMainWindowId() const noexcept {
        return mainWindowId_.load(std::memory_order_acquire);
    }
    HWND TestGameWindow() const noexcept {
        return inputGameWindow_.load(std::memory_order_acquire);
    }
    void TestReceiveOwned(const std::string& type,const std::string& json) noexcept {
        Receive(type,json);
    }
    UINT TestAuxiliaryWindowId() const noexcept {
        return auxiliaryWindowId_.load(std::memory_order_acquire);
    }
    bool TestSendOwnedSphereInput(const CursorEvent& event) noexcept {
        if(!allowSphereInput_ || !inputSessionReady_.load(std::memory_order_acquire) ||
           !inputPolicyAllowed_.load(std::memory_order_acquire) ||
           !event.windowId ||
           event.windowId!=auxiliaryWindowId_.load(std::memory_order_acquire))
            return false;
        return SendCursorInput(inputHost_.load(std::memory_order_acquire),
                               inputHostPid_.load(std::memory_order_acquire),
                               event,"experimental.aux.input");
    }
#endif
    unsigned Frames() const noexcept {return mailbox_.successful;}
    unsigned AuxiliaryFrames() const noexcept {return auxiliaryMailbox_.successful;}
    std::shared_ptr<const WpfFrameSnapshot> AuxiliaryLatest() const noexcept {
        return auxiliaryMailbox_.Latest();
    }
    bool Stop(DWORD maxWaitMs=2500) noexcept {
        if(!worker_)return true;
        SetEvent(stop_);
        if(cursorWorker_){
            // A timed-out sender is left resident; do not free thread
            // state or allow native module unload while it may run.
            if(WaitForSingleObject(cursorWorker_,maxWaitMs)!=WAIT_OBJECT_0)
                return false;
            CloseHandle(cursorWorker_);cursorWorker_=nullptr;
        }
        if(WaitForSingleObject(worker_,maxWaitMs)!=WAIT_OBJECT_0)
            return false;
        CloseHandle(worker_);worker_=nullptr;
        CloseHandle(stop_);stop_=nullptr;
        inputGameWindow_.store(nullptr,std::memory_order_release);
        return true;
    }
    ~LiveWpfIpcClient(){if(worker_)Stop();}
    LiveWpfIpcClient()=default;
    LiveWpfIpcClient(const LiveWpfIpcClient&)=delete;
    LiveWpfIpcClient& operator=(const LiveWpfIpcClient&)=delete;
};
} // namespace minimixer
