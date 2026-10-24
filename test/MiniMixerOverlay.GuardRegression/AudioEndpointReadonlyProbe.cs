using System;
using System.Linq;
using NAudio.CoreAudioApi;
internal static class AudioEndpointReadonlyProbe
{
    internal static int Run()
    {
        try{
            using var devices = new MMDeviceEnumerator();
            var provider = new MiniMixerOverlay.Infrastructure.Audio.NAudioSessionManager();
            var snapshot = provider.EnumerateSessions();
            using(var defaultOutput = devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){
                var master = snapshot.Where(s=>s.ExePath=="__system_master__").ToArray();
                if(master.Length != 1)
                    throw new InvalidOperationException($"Expected one default master: {master.Length}");
                var entry = master[0];
                if(entry.SessionIdentifier!="__system_master__|"+defaultOutput.ID ||
                   Math.Abs(entry.Volume - defaultOutput.AudioEndpointVolume.MasterVolumeLevelScalar)>0.001f ||
                   entry.IsMuted != defaultOutput.AudioEndpointVolume.Mute)
                    throw new InvalidOperationException("Master UI snapshot did not match real default render endpoint");
                var system = snapshot.Where(s=>s.IsSystemSound).ToArray();
                if(system.Any(s=>s.ExePath=="__system_master__" || s.DisplayName=="Systemlautstärke"))
                    throw new InvalidOperationException("System Sounds still masquerades as master");
                Console.WriteLine($"PASS READONLY_REAL_MASTER_SNAPSHOT master={entry.Volume:F3} mute={entry.IsMuted} systemSoundsCount={system.Length} distinct=1 no_audio_writes=1");
            }
            foreach(var role in new[]{Role.Console,Role.Multimedia,Role.Communications}){
                using var endpoint=devices.GetDefaultAudioEndpoint(DataFlow.Render,role);
                var master=endpoint.AudioEndpointVolume;
                var sounds=Enumerable.Range(0,endpoint.AudioSessionManager.Sessions.Count)
                    .Select(i=>endpoint.AudioSessionManager.Sessions[i])
                    .Where(s=>s.IsSystemSoundsSession).ToArray();
                Console.WriteLine($"READONLY_ENDPOINT role={role} id={endpoint.ID} name={endpoint.FriendlyName} masterVolume={master.MasterVolumeLevelScalar:F3} masterMute={master.Mute} systemSoundsSessions={sounds.Length}");
                foreach(var s in sounds){
                    Console.WriteLine($"READONLY_SYSTEM_SOUNDS sessionVolume={s.SimpleAudioVolume.Volume:F3} sessionMute={s.SimpleAudioVolume.Mute}");
                }
            }
            return 0;
        } catch(Exception e){
            Console.Error.WriteLine($"AUDIO_ENDPOINTS_INCONCLUSIVE {e}");
            return 2;
        }
    }
}
