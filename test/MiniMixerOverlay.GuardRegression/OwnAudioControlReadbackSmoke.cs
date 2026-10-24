using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static class OwnAudioControlReadbackSmoke
{
    // Real Windows audio controls, but ONLY against a newly created silent
    // WASAPI session in this disposable test process. No third-party writes.
    internal static int Run()
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        var provider = new BufferedWaveProvider(device.AudioClient.MixFormat) { ReadFully = true };
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 120);
        output.Init(provider);
        output.Play();

        var manager = new NAudioSessionManager();
        var expectedPid = (uint)Environment.ProcessId;
        AudioSessionControl? original = null;
        string controlKey = string.Empty;
        float baselineVolume = 0f;
        bool baselineMute = false;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = manager.EnumerateSessions();
                var ours = snapshot.Where(s => s.ProcessId == expectedPid &&
                             s.SessionIdentifier.StartsWith(device.ID + "|",
                                   StringComparison.OrdinalIgnoreCase)).ToArray();

                // Do not write audio if we cannot identify EXACTLY one
                // newly-created own process session for the chosen endpoint.
                if (ours.Length != 1)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var match = Enumerable.Range(0, device.AudioSessionManager.Sessions.Count)
                    .Select(i => device.AudioSessionManager.Sessions[i])
                    .Where(s => s.GetProcessID == expectedPid)
                    .ToArray();
                if (match.Length != 1)
                {
                    Thread.Sleep(100);
                    continue;
                }
                original = match[0];
                controlKey = ours[0].SessionIdentifier;
                baselineVolume = original.SimpleAudioVolume.Volume;
                baselineMute = original.SimpleAudioVolume.Mute;
                break;
            }

            if (original == null)
                throw new InvalidOperationException("Own WASAPI test session could not be uniquely resolved");

            // Real backend writes + independent direct CoreAudio reads:
            // muted audio MUST retain its volume so unmute does not jump.
            manager.SetVolume(controlKey, 0.37f);
            AssertNear(original.SimpleAudioVolume.Volume, 0.37f, "volume .37");
            manager.SetMute(controlKey, true);
            if (!original.SimpleAudioVolume.Mute)
                throw new InvalidOperationException("Direct WASAPI readback did not show Mute=true");
            AssertNear(original.SimpleAudioVolume.Volume, 0.37f, "volume after mute");
            manager.SetMute(controlKey, false);
            if (original.SimpleAudioVolume.Mute)
                throw new InvalidOperationException("Direct WASAPI readback did not show Mute=false");
            AssertNear(original.SimpleAudioVolume.Volume, 0.37f, "volume after unmute");

            manager.SetVolume(controlKey, 0.61f);
            AssertNear(original.SimpleAudioVolume.Volume, 0.61f, "volume .61");

            var updated = manager.EnumerateSessions().Single(s => s.SessionIdentifier == controlKey);
            AssertNear(updated.Volume, 0.61f, "re-enumerated volume .61");
            if (updated.IsMuted)
                throw new InvalidOperationException("Enumerated test session incorrectly stayed muted");

            Console.WriteLine(
                $"PASS OWN_WASAPI_CONTROL_READBACK own_pid={expectedPid} endpoint_bound=1 " +
                $"volume_37=1 mute=1 mute_preserves_volume=1 unmute=1 volume_61=1 rescan=1 " +
                $"foreign_sessions_changed=0");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL OWN_WASAPI_CONTROL_READBACK {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            if (original != null)
            {
                try
                {
                    // Restore only the single own-process, test-created
                    // session before its process/stream exits.
                    original.SimpleAudioVolume.Volume = baselineVolume;
                    original.SimpleAudioVolume.Mute = baselineMute;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"OWN_SESSION_RESTORE_WARNING {ex.Message}");
                }
            }
            output.Stop();
        }
    }

    private static void AssertNear(float actual, float target, string stage)
    {
        if (Math.Abs(actual - target) > 0.025f)
            throw new InvalidOperationException(
                $"Own WASAPI {stage}: got {actual:F3}, expected {target:F3}");
    }
}
