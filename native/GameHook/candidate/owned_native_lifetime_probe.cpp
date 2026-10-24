#include "native_lifetime_policy.h"
#include <cstdio>
#include <cstdint>
using minimixer::NativeLifetimePolicy;
using minimixer::NativeStopReason;

static unsigned assertions=0;
static bool Check(bool ok,const char* why){
    ++assertions;
    if(!ok)std::fprintf(stderr,"FAIL NATIVE_LIFETIME: %s\n",why);
    return ok;
}
int main(){
    {
        NativeLifetimePolicy p;
        if(!Check(p.Observe(3000,0,0)==NativeStopReason::None,"startup wait"))return 1;
        if(!Check(p.Observe(120000,0,0)==NativeStopReason::None,"startup boundary"))return 2;
        if(!Check(p.Observe(150000,0,0)==NativeStopReason::MissingStartupFrames,
                  "no actual output at startup must stop"))return 3;
    }
    {
        // Healthy overlay may run 24h in simulated samples; no 15min cutoff.
        NativeLifetimePolicy p;
        if(!Check(p.Observe(0,0,0)==NativeStopReason::None,"initial healthy"))return 4;
        for(std::uint64_t i=1;i<=2880;++i){
            if(p.Observe(i*30000,i*30,i*3000)!=NativeStopReason::None){
                std::fprintf(stderr,"FAIL 24h at sample %llu\n",
                             static_cast<unsigned long long>(i));
                return 5;
            }
        }
        if(!Check(p.admitted && p.stalledActiveMs==0,"healthy 24h admitted"))return 6;
    }
    {
        NativeLifetimePolicy p;
        p.Observe(0,100,100);
        for(unsigned i=1;i<30;++i){
            if(!Check(p.Observe(i*30000,100,100+50*i)==NativeStopReason::None,
                      "sub-limit active stale interval"))return 7;
        }
        if(!Check(p.Observe(30*30000,100,2000)==
                  NativeStopReason::ActiveRenderingWithoutOverlay,
                  "15 minutes real Present without WPF should fail closed"))return 8;
    }
    {
        NativeLifetimePolicy p;
        p.Observe(0,100,100);
        for(unsigned i=1;i<=20;++i)
            p.Observe(i*30000,100,100+i);
        if(!Check(p.stalledActiveMs==600000,"stale counter before recovery"))return 9;
        if(!Check(p.Observe(630000,101,121)==NativeStopReason::None &&
                  p.stalledActiveMs==0,"fresh WPF frame clears stale duration"))return 10;
        if(!Check(p.Observe(660000,101,122)==NativeStopReason::None,
                  "recovery starts new bounded interval"))return 11;
    }
    {
        NativeLifetimePolicy p;
        p.Observe(0,100,100);
        for(unsigned i=1;i<=120;++i){
            if(p.Observe(i*30000,100,100)!=NativeStopReason::None)
                return 12; // no game Presents for an hour
        }
        if(!Check(p.stalledActiveMs==0,"paused game does not incur timeout"))return 13;
        if(!Check(p.Observe(24ull*3600*1000,100,101)==NativeStopReason::None,
                  "sleep/resume cannot charge 24h of fake active failure"))return 14;
        if(!Check(p.stalledActiveMs==30000,"resume delta capped to sample budget"))return 15;
    }
    {
        NativeLifetimePolicy p;
        p.Observe(0,99,100);
        if(!Check(p.Observe(90000,99,300)==NativeStopReason::None,"low frame startup grace"))return 16;
        if(!Check(p.Observe(121000,100,300)==NativeStopReason::None &&
                  p.admitted,"exact admission threshold"))return 17;
    }
    std::printf("PASS NATIVE_LIFETIME_POLICY assertions=%u simulated_healthy_hours=24"
                " stale_present_stop_minutes=15 paused_game_survives=1\n",assertions);
    return 0;
}
