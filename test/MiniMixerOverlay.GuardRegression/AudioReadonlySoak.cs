using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MiniMixerOverlay.Infrastructure.Audio;

// Bounded, in-process repeated REAL provider scan: no audio setters,
// app window, injected renderer, or user settings are loaded.
internal static class AudioReadonlySoak
{
    internal static int Run(int repeats = 80, int intervalMs = 100)
    {
        repeats=Math.Clamp(repeats,10,300);
        intervalMs=Math.Clamp(intervalMs,25,1000);
        Console.WriteLine($"NAUDIO_DISPOSABLE MMDevice={typeof(NAudio.CoreAudioApi.MMDevice).GetInterfaces().Contains(typeof(IDisposable))} AudioSessionControl={typeof(NAudio.CoreAudioApi.AudioSessionControl).GetInterfaces().Contains(typeof(IDisposable))}");
        var manager=new NAudioSessionManager();
        using var process=Process.GetCurrentProcess();
        long initialPrivate=0;
        int initialHandles=0;
        long highestPrivate=0;
        int highestHandles=0;
        long lastPrivate=0;
        int lastHandles=0;
        int sessionChanges=0;
        string[]? previous=null;
        for(var i=0;i<repeats;i++)
        {
            var snapshot=manager.EnumerateSessions();
            if(snapshot.Any(s=>string.IsNullOrWhiteSpace(s.SessionIdentifier)) ||
               snapshot.Select(s=>s.SessionIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=snapshot.Count)
                throw new InvalidOperationException("Audio enumeration identity corrupted in soak scan " + i);
            var ids=snapshot.Select(s=>s.SessionIdentifier).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
            if(previous!=null && !ids.SequenceEqual(previous)) ++sessionChanges;
            previous=ids;
            process.Refresh();
            var priv=process.PrivateMemorySize64;
            var handles=process.HandleCount;
            if(i==0){initialPrivate=priv;initialHandles=handles;}
            lastPrivate=priv;lastHandles=handles;
            highestPrivate=Math.Max(highestPrivate,priv);
            highestHandles=Math.Max(highestHandles,handles);
            if((i+1)%20==0)
                Console.WriteLine($"AUDIO_READONLY_SOAK_PROGRESS scans={i+1} private_mib={priv/1048576d:F2} handles={handles} sessions={snapshot.Count}");
            Thread.Sleep(intervalMs);
        }
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        process.Refresh();
        Console.WriteLine($"AUDIO_READONLY_SOAK_AFTER_GC private_mib={process.PrivateMemorySize64/1048576d:F2} handles={process.HandleCount}");
        Console.WriteLine($"AUDIO_READONLY_SOAK_RESULT scans={repeats} interval_ms={intervalMs} session_changes={sessionChanges} " +
             $"private_start_mib={initialPrivate/1048576d:F2} private_end_mib={lastPrivate/1048576d:F2} " +
             $"private_peak_mib={highestPrivate/1048576d:F2} handles_start={initialHandles} " +
             $"handles_end={lastHandles} handles_peak={highestHandles} volume_writes=0 mute_writes=0");
        return 0;
    }
}
