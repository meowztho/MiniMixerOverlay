using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// One owned, silent WASAPI session; manual volume commands race read-only scans.
// No master controls or foreign app sessions may be written by this fixture.
internal static class OwnAudioConcurrentRefreshSmoke
{
    internal static int Run()
    {
        using var devices = new MMDeviceEnumerator();
        using var endpoint=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console);
        var silence=new BufferedWaveProvider(endpoint.AudioClient.MixFormat){ReadFully=true};
        using var player=new WasapiOut(endpoint,AudioClientShareMode.Shared,true,120);
        player.Init(silence);
        player.Play();
        var manager=new NAudioSessionManager();
        AudioSessionControl? direct=null;
        string key="";
        float initialVolume=0;
        bool initialMute=false;
        try
        {
            var until=DateTime.UtcNow.AddSeconds(8);
            while(DateTime.UtcNow<until)
            {
                var all=manager.EnumerateSessions();
                var own=all.Where(s=>s.ProcessId==(uint)Environment.ProcessId &&
                    s.SessionIdentifier.StartsWith(endpoint.ID+"|",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(own.Length==1)
                {
                    var directCandidates=Enumerable.Range(0,endpoint.AudioSessionManager.Sessions.Count)
                        .Select(i=>endpoint.AudioSessionManager.Sessions[i])
                        .Where(s=>s.GetProcessID==(uint)Environment.ProcessId).ToArray();
                    if(directCandidates.Length==1)
                    {
                        key=own[0].SessionIdentifier;
                        direct=directCandidates[0];
                        initialVolume=direct.SimpleAudioVolume.Volume;
                        initialMute=direct.SimpleAudioVolume.Mute;
                        break;
                    }
                }
                Thread.Sleep(100);
            }
            if(direct==null)
                throw new InvalidOperationException("Could not uniquely bind own silent audio session");
            manager.StartMonitoring();
            var scans=0;
            var writes=0;
            var scanner=Task.Run(()=>
            {
                for(var i=0;i<120;i++)
                {
                    var snapshot=manager.EnumerateSessions();
                    if(snapshot.Count(s=>s.SessionIdentifier==key)!=1)
                        throw new InvalidOperationException("Own session disappeared during refresh race");
                    Interlocked.Increment(ref scans);
                    Thread.Sleep(7);
                }
            });
            var writer=Task.Run(()=>
            {
                for(var i=0;i<100;i++)
                {
                    var target=(i%2==0)?0.37f:0.61f;
                    manager.SetVolume(key,target);
                    manager.SetMute(key,false);
                    Interlocked.Increment(ref writes);
                    Thread.Sleep(9);
                }
            });
            Task.WaitAll(scanner,writer);
            manager.SetVolume(key,0.54f);
            manager.SetMute(key,false);
            Thread.Sleep(60);
            if(Math.Abs(direct.SimpleAudioVolume.Volume-0.54f)>0.025f ||
               direct.SimpleAudioVolume.Mute)
                throw new InvalidOperationException(
                    $"Concurrent refresh invalidated own session control: got {direct.SimpleAudioVolume.Volume:F2}");
            var final=manager.EnumerateSessions().Single(s=>s.SessionIdentifier==key);
            if(Math.Abs(final.Volume-0.54f)>0.025f || final.IsMuted)
                throw new InvalidOperationException("Final canonical session snapshot is stale");
            Console.WriteLine($"PASS OWN_WASAPI_PARALLEL_REFRESH_AND_WRITES scans={scans} writes={writes} final_volume=0.54 mute=false foreign_sessions_written=0");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"FAIL OWN_WASAPI_PARALLEL_REFRESH_AND_WRITES {ex}");
            return 1;
        }
        finally
        {
            manager.StopMonitoring();
            if(direct!=null)
            {
                try
                {
                    direct.SimpleAudioVolume.Volume=initialVolume;
                    direct.SimpleAudioVolume.Mute=initialMute;
                }
                catch(Exception ex)
                {
                    Console.Error.WriteLine($"OWN_WASAPI_RESTORE_WARNING {ex.Message}");
                }
            }
            player.Stop();
        }
    }
}
