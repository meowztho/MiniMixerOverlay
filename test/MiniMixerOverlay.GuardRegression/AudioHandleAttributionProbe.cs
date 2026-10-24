using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;

// Isolated read-only WASAPI/GDI process handle attribution.
// Every mode runs in a fresh test process, never a user application.
// No default device changes or real mute/volume writes.
internal static class AudioHandleAttributionProbe
{
    internal static int Run(string mode, int iterations=90)
    {
        iterations=Math.Clamp(iterations,10,300);
        if(!new[]{"idle","endpoints","sessions","master","full","cached-master","cached-sessions","full-monitor"}.Contains(mode,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported handle attribution mode",nameof(mode));
        static string Lifetime(System.Type? t)
        {
            if(t is null) return "none";
            var hasDispose=typeof(IDisposable).IsAssignableFrom(t);
            return $"{t.FullName}:IDisposable={hasDispose} DisposeMethods={string.Join(",",t.GetMethods().Where(m=>m.Name=="Dispose").Select(m=>m.IsPublic?"public":"nonpublic"))}";
        }
        Console.WriteLine("COREAUDIO_TYPE_INFO " +
            "endpointVolume="+Lifetime(typeof(MMDevice).GetProperty("AudioEndpointVolume")?.PropertyType)+
            " sessions="+Lifetime(typeof(NAudio.CoreAudioApi.AudioSessionManager).GetProperty("Sessions")?.PropertyType)+
            " simpleVolume="+Lifetime(typeof(AudioSessionControl).GetProperty("SimpleAudioVolume")?.PropertyType)+
            " audioMeter="+Lifetime(typeof(AudioSessionControl).GetProperty("AudioMeterInformation")?.PropertyType)+
            " manager="+Lifetime(typeof(NAudio.CoreAudioApi.AudioSessionManager)));
        using (var dev = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            var m1=dev.AudioEndpointVolume;
            var m2=dev.AudioEndpointVolume;
            Console.WriteLine($"COREAUDIO_GETTER_IDENTITIES master_same_reference={ReferenceEquals(m1,m2)}");
            if(!ReferenceEquals(m1,m2))m2.Dispose();
            m1.Dispose();
            var sessions=dev.AudioSessionManager.Sessions;
            if(sessions.Count>0)
            {
                using var session=sessions[0];
                var s1=session.SimpleAudioVolume;
                var s2=session.SimpleAudioVolume;
                Console.WriteLine($"COREAUDIO_GETTER_IDENTITIES simple_same_reference={ReferenceEquals(s1,s2)}");
                if(!ReferenceEquals(s1,s2))s2.Dispose();
                s1.Dispose();
                var meter1=session.AudioMeterInformation;
                var meter2=session.AudioMeterInformation;
                Console.WriteLine($"COREAUDIO_GETTER_IDENTITIES meter_same_reference={ReferenceEquals(meter1,meter2)} meter_is_disposable={meter1 is IDisposable}");
                if(!ReferenceEquals(meter1,meter2) && meter2 is IDisposable d2)d2.Dispose();
                if(meter1 is IDisposable d1)d1.Dispose();
            }
        }
        using var me=Process.GetCurrentProcess();
        var deviceEnumerator=new MMDeviceEnumerator();
        var provider=mode=="full" || mode=="full-monitor"?new NAudioSessionManager():null;
        if(mode=="full-monitor")
        {
            provider!.EnumerateSessions();
            provider.StartMonitoring();
        }
        using var retainedDevice=mode=="cached-master" || mode=="cached-sessions" ?
            deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia) : null;
        var now=DateTime.UtcNow;
        var deltas=new List<(int Iteration,int HandleCount,double PrivateMiB,long ManagedBytes)>();
        for(int i=0;i<iterations;++i)
        {
            switch(mode)
            {
                case "idle":break;
                case "endpoints":
                    foreach(var role in new[]{Role.Console,Role.Multimedia,Role.Communications})
                    {
                        try { using var device=deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render,role); _=device.ID; }
                        catch(Exception e) when(e is not OutOfMemoryException) {}
                    }
                    break;
                case "sessions":
                    using (var endpoint=deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console))
                    {
                        var sessions=endpoint.AudioSessionManager.Sessions;
                        for(int k=0;k<sessions.Count;k++)
                        {
                            var audio=sessions[k];
                            try
                            {
                                _ = audio.GetSessionIdentifier;
                                using var volume=audio.SimpleAudioVolume;
                                _ = volume.Mute;
                            }
                            finally{audio.Dispose();}
                        }
                    }
                    break;
                case "master":
                    using(var endpoint=deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))
                    {
                        using var master=endpoint.AudioEndpointVolume;
                        _=master.MasterVolumeLevelScalar;
                        _=master.Mute;
                    }
                    break;
                case "cached-master":
                    using (var current=deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))
                    {
                        if(!string.Equals(current.ID,retainedDevice!.ID,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Device changed during cached-master test");
                        var vol=retainedDevice.AudioEndpointVolume; // intentionally owned by device
                        _=vol.MasterVolumeLevelScalar;_=vol.Mute;
                    }
                    break;
                case "cached-sessions":
                    var sessionManager=retainedDevice!.AudioSessionManager;
                    sessionManager.RefreshSessions();
                    var retainedSessions=sessionManager.Sessions;
                    for(var k=0;k<retainedSessions.Count;k++)
                    {
                        var control=retainedSessions[k];
                        try { _=control.GetSessionIdentifier;_ = control.SimpleAudioVolume.Mute; }
                        finally { control.Dispose(); }
                    }
                    break;
                case "full":
                case "full-monitor":
                    var snap=provider!.EnumerateSessions();
                    if(snap.GroupBy(s=>s.SessionIdentifier,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1))
                        throw new InvalidOperationException("Duplicate audio identity");
                    break;
            }
            Thread.Sleep(60);
            if(i==0 || (i+1)%15==0 || i==iterations-1)
            {
                me.Refresh();
                deltas.Add((i+1,me.HandleCount,me.PrivateMemorySize64/1048576d,GC.GetTotalMemory(false)));
                Console.WriteLine($"HANDLE_ATTRIBUTION mode={mode} scan={i+1} handles={me.HandleCount} private_mib={me.PrivateMemorySize64/1048576d:F2} managed_bytes={GC.GetTotalMemory(false)}");
            }
        }
        if(mode=="full-monitor") provider!.StopMonitoring();
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        me.Refresh();
        Console.WriteLine($"HANDLE_ATTRIBUTION_SUMMARY mode={mode} scans={iterations} " +
            $"initial_handles={deltas[0].HandleCount} peak_pre_gc_handles={deltas.Max(x=>x.HandleCount)} " +
            $"end_pre_gc_handles={deltas[^1].HandleCount} post_gc_handles={me.HandleCount} " +
            $"initial_private_mib={deltas[0].PrivateMiB:F2} last_private_mib={deltas[^1].PrivateMiB:F2} " +
            $"post_gc_private_mib={me.PrivateMemorySize64/1048576d:F2} " +
            "audio_writes=0");
        return 0;
    }
}
