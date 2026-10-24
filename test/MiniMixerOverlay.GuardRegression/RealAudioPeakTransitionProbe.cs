using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// Own WASAPI signal → real production NAudioSessionManager snapshots.
// No volume/mute APIs, foreign sessions, application settings or native hooks.
internal static class RealAudioPeakTransitionProbe
{
    private sealed class ControlledSample : ISampleProvider
    {
        private volatile int _phase; // 0=silence, 1=very quiet sine wave
        private double _cursor;
        public WaveFormat WaveFormat { get; }
        public ControlledSample(int rate, int channels)
            => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public void SetSignal(bool on) => _phase = on ? 1 : 0;
        public int Read(float[] buffer, int offset, int count)
        {
            var channels = WaveFormat.Channels;
            for (var i = 0; i < count; i += channels)
            {
                var sample = _phase == 0 ? 0f : (float)(0.025 * Math.Sin(_cursor * 2 * Math.PI * 440 / WaveFormat.SampleRate));
                for (var ch = 0; ch < channels && i + ch < count; ch++)
                    buffer[offset + i + ch] = sample;
                _cursor++;
            }
            return count;
        }
    }

    public static int Run()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            var format = endpoint.AudioClient.MixFormat;
            var floatFormat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                (format is WaveFormatExtensible extensible &&
                 extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            if (!floatFormat || format.BitsPerSample != 32)
            {
                Console.WriteLine($"AUDIO_PEAK_INCONCLUSIVE mix={format.Encoding}/{format.BitsPerSample}; no output started");
                return 2;
            }
            var source = new ControlledSample(format.SampleRate, format.Channels);
            using var player = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 100);
            var manager = new NAudioSessionManager();
            player.Init(source.ToWaveProvider());
            player.Play();
            try
            {
                var ownedPid = (uint)Environment.ProcessId;
                var before = new List<float>();
                var during = new List<float>();
                var after = new List<float>();
                List<float> Read(string phase, int samples)
                {
                    var observed = new List<float>();
                    for(var i = 0; i < samples; i++)
                    {
                        var matches = manager.EnumerateSessions()
                            .Where(s => s.ProcessId == ownedPid &&
                              s.SessionIdentifier.StartsWith(endpoint.ID + "|", StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        if(matches.Length != 1)
                            throw new InvalidOperationException($"{phase}: expected one own session, got {matches.Length}");
                        var peak = matches[0].SignalPeakLevel;
                        if(peak.HasValue) observed.Add(peak.Value);
                        Thread.Sleep(65);
                    }
                    if (observed.Count < samples * 0.8)
                        throw new InvalidOperationException($"{phase}: meter unavailable on most samples");
                    Console.WriteLine($"OWN_PEAK_PHASE {phase}: samples={observed.Count} min={observed.Min():F5} max={observed.Max():F5}");
                    return observed;
                }

                // Warm-up allows audio engine to reach a stable settled silence.
                Thread.Sleep(340);
                before = Read("silence_before", 14);
                source.SetSignal(true);
                during = Read("signal", 21);
                source.SetSignal(false);
                Thread.Sleep(220); // flush buffered frames before checking silence
                after = Read("silence_after", 14);
                if (before.TakeLast(6).Max() > 0.005f ||
                    during.Max() < 0.012f ||
                    during.Count(x => x > 0.008f) < 10 ||
                    after.TakeLast(6).Max() > 0.005f)
                {
                    Console.Error.WriteLine("AUDIO_PEAK_FAIL: silence → signal → silence was not observable");
                    return 1;
                }
                Console.WriteLine("AUDIO_PEAK_PASS: owned Windows WASAPI signal observed in production provider; foreign_audio_writes=0");
                return 0;
            }
            finally { source.SetSignal(false); player.Stop(); }
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"AUDIO_PEAK_INCONCLUSIVE: {ex}");
            return 2;
        }
    }
}
