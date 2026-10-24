using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;

// We do NOT change the user's real audio defaults. Instead inject an
// obsolete *cache identity* backed by an independently owned device wrapper,
// then verify next normal poll withdraws that monitor and leaves real devices.
internal static class AudioMonitorRebindSimulated
{
    internal static int Run()
    {
        var manager=new NAudioSessionManager();
        var monitorField=typeof(NAudioSessionManager).GetField(
            "_monitoredDevices",BindingFlags.Instance|BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_monitoredDevices");
        var scanField=typeof(NAudioSessionManager).GetField(
            "_scanDevices",BindingFlags.Instance|BindingFlags.NonPublic)
            ?? throw new MissingFieldException("_scanDevices");
        var monitors=(Dictionary<string,MMDevice>)monitorField.GetValue(manager)!;
        var scans=(Dictionary<string,MMDevice>)scanField.GetValue(manager)!;
        const string obsolete="__test_obsolete_default_device_not_a_real_endpoint__";
        const string staleScan="__test_stale_scan_device_not_a_real_endpoint__";
        MMDevice? syntheticOwner=null;
        MMDevice? syntheticScan=null;
        try
        {
            manager.EnumerateSessions();
            manager.StartMonitoring();
            if(monitors.Count == 0 || monitors.Count != scans.Count)
                throw new InvalidOperationException("Initial monitor/poll endpoint set mismatch");
            using var enumeration = new MMDeviceEnumerator();
            syntheticOwner=enumeration.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
            monitors.Add(obsolete,syntheticOwner);
            syntheticOwner=null; // ownership transferred to provider
            syntheticScan=enumeration.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console);
            scans.Add(staleScan,syntheticScan);
            syntheticScan=null; // independent wrapper, cached polling owner
            manager.EnumerateSessions();
            if(scans.ContainsKey(staleScan))
                throw new InvalidOperationException("Stale polling endpoint survived default-role refresh");
            if(monitors.ContainsKey(obsolete))
                throw new InvalidOperationException("Stale endpoint-monitor identity survived normal poll");
            if(monitors.Count != scans.Count)
                throw new InvalidOperationException("Changed monitor owner set differs from current scan endpoints");
            if(monitors.Keys.Except(scans.Keys,StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidOperationException("Monitors remain bound to noncurrent devices");
            manager.StopMonitoring();
            if(monitors.Count!=0 || scans.Count!=0)
                throw new InvalidOperationException("Shutdown retained endpoint COM owners");
            Console.WriteLine("PASS OWNED_MONITOR_REBIND_SYNTHETIC_STALE_ID scan_stale_removed=1 monitor_stale_removed=1 current_reused=1 stop_disposes=1 device_settings_unchanged=1");
            return 0;
        }
        finally
        {
            syntheticOwner?.Dispose();
            syntheticScan?.Dispose();
            manager.StopMonitoring();
        }
    }
}
