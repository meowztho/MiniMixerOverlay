// Separate Win32 process exercising the SAME read-only IPC client used by
// future game renderer. No game hook, no global input, no audio operations.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <cstdint>
#include <thread>
#include <atomic>
#include "live_wpf_ipc_client.h"
int main(int argc,char** argv){
    std::string host="minimixer_graphics_v1";
    DWORD duration=7000;
    bool inputWorker=false,wireInput=false;
    for(int i=1;i<argc;++i){
        const std::string arg(argv[i]);
        if(arg.rfind("--host=",0)==0)host=arg.substr(7);
        else if(arg.rfind("--duration-ms=",0)==0)
            duration=static_cast<DWORD>(atoi(arg.c_str()+14));
        else if(arg=="--sphere-native-input-worker")inputWorker=true;
        else if(arg=="--owned-sphere-input-wire"){
            inputWorker=true;wireInput=true;
        }
        else if(arg=="--headless"||arg.rfind("--stall-ms=",0)==0){}
        else {std::fprintf(stderr,"INVALID_ARG=%s\n",arg.c_str());return 2;}
    }
    minimixer::LiveWpfIpcClient client;
    if(!client.Start(host,inputWorker))return 3;
    const auto start=GetTickCount64();
    std::uint64_t latestGeneration=0,checksum=0,auxGeneration=0;
    unsigned seen=0,contentVariants=0,auxSeen=0,auxMoved=0,
             auxHidden=0,auxAlpha=0;
    bool wireDone=false,wireGood=true;
    int auxLastLeft=0,auxLastTop=0;
    bool everSawAux=false;
    std::uint32_t lastWidth=0,lastHeight=0;
    while(GetTickCount64()-start<duration){
        if(wireInput&&!wireDone&&GetTickCount64()-start>2600 &&
           client.AuxiliaryFrames()>=20 && client.TestAuxiliaryWindowId()!=0){
            const UINT id=client.TestAuxiliaryWindowId();
            const UINT lp=static_cast<UINT>(MAKELPARAM(15,15));
            const minimixer::CursorEvent events[3]={
                {WM_MOUSEMOVE,0,lp,id},
                {WM_LBUTTONDOWN,MK_LBUTTON,lp,id},
                {WM_LBUTTONUP,0,lp,id}
            };
            for(const auto& event:events){
                if(!client.TestSendOwnedSphereInput(event))wireGood=false;
            }
            wireDone=true;
        }
        auto frame=client.Latest();
        if(frame && frame->generation!=latestGeneration){
            if(!frame->View().valid())return 4;
            if(lastWidth && (lastWidth!=frame->width||
                             lastHeight!=frame->height))++contentVariants;
            lastWidth=frame->width;lastHeight=frame->height;
            latestGeneration=frame->generation;++seen;
            auto view=frame->View();
            const auto stride=static_cast<std::size_t>(view.stride);
            if(frame->pixels.size()!=stride*frame->height)return 5;
            checksum=1469598103934665603ULL;
            for(unsigned y=0;y<frame->height;y+=17){
                const auto* row=frame->pixels.data()+stride*y;
                for(unsigned x=0;x<frame->width;x+=31){
                    for(unsigned c=0;c<4;++c)
                        checksum=(checksum^row[x*4+c])*1099511628211ULL;
                }
            }
        }
        auto aux=client.AuxiliaryLatest();
        if(aux&&aux->generation!=auxGeneration){
            if(!aux->View().valid())return 7;
            if(everSawAux && (aux->left!=auxLastLeft ||
                             aux->top!=auxLastTop))++auxMoved;
            auxLastLeft=aux->left;auxLastTop=aux->top;
            auxGeneration=aux->generation;++auxSeen;
            for(size_t j=3;j<aux->pixels.size();j+=4){
                if(aux->pixels[j]>=50){++auxAlpha;break;}
            }
            everSawAux=true;
        } else if(!aux && everSawAux){
            ++auxHidden;
            everSawAux=false;
        }
        Sleep(16);
    }
    const bool stopped=client.Stop();
    std::printf("ACK=%u INIT=%u FRAME_NOTICES=%u COPIED=%u DISPLAYED=0 REJECTED=%u FRAME_MISSES=0 MUTEX_WAITS=0 STALLED=0 STOP=%d HANDSHAKE_TIMEOUTS=%u READONLY_SNAPSHOT=1 READ_FRAMES=%u WIDTH=%u HEIGHT=%u FRAME_VARIANTS=%u LAST_CHECKSUM=%llu AUX_FRAMES=%u AUX_READ=%u AUX_MOVED=%u AUX_HIDDEN=%u AUX_ALPHA=%u INPUT_WORKER=%u INPUT_SAMPLES=%u INPUT_EVENTS=%u INPUT_SENT=%u INPUT_ERRORS=%u OWNED_WIRE_INPUT=%u OWNED_WIRE_OK=%u\n",
         client.acks.load(),client.inits.load(),seen,
         client.Frames(),client.invalid.load(),stopped?1:0,
         client.helloTimeouts.load(),seen,
         lastWidth,lastHeight,contentVariants,
         static_cast<unsigned long long>(checksum),
         client.AuxiliaryFrames(),auxSeen,auxMoved,auxHidden,auxAlpha,
         inputWorker?1u:0u,client.inputSamples.load(),
         client.inputEmitted.load(),client.inputSent.load(),
         client.inputRejected.load(),wireDone?1u:0u,wireGood?1u:0u);
    return stopped&&client.acks>=1&&client.inits>=1&&
           client.Frames()>=10&&seen>=10&&client.invalid==0&&
           client.helloTimeouts==0&&(!wireInput||(wireDone&&wireGood))?0:6;
}
