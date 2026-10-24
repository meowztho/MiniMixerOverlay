namespace MiniMixerOverlay.Core;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

/// <summary>
/// Hauptcontroller - koordiniert Audio, Guard, Persistenz und UI.
/// </summary>
public class MixerController
{
    private static readonly object GuardLogSync = new();
    internal const int DefaultGuardLogMaxBytes = 1024 * 1024;
    private readonly string _guardLogFilePath;
    private readonly int _guardLogMaxBytes;
    private readonly Dictionary<string, string> _lastLoggedGuardDecision = new(StringComparer.OrdinalIgnoreCase);
    // A failed auto-write is NOT applied state. Throttle recovery retries so
    // the session refresh timer never becomes a hot failing-write loop.
    private readonly Dictionary<string, (int Failures, DateTime RetryAfterUtc)> _failedAutoWrites =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _observedSessionKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAudioSessionManager _audioManager;
    private readonly IGuardEngine _guardEngine;
    private readonly ISessionClassifier _classifier;
    private readonly IRuleStore _ruleStore;
    private readonly ISettingsStore _settingsStore;
    // Immutable per-controller boundary: isolated UI reviews must never issue WASAPI writes.
    private readonly bool _allowAudioWrites;
    private bool _isBaselineScan = true;
    private readonly DateTime _controllerStartedUtc = DateTime.UtcNow;
    private SynchronizationContext? _sessionRefreshContext;
    private int _sessionRefreshQueued;
    private volatile bool _isShuttingDown;

    public ObservableCollection<AppEntry> AppEntries { get; } = new();
    public AppSettings Settings => _settingsStore.Settings;

    public event Action? OnSessionsChanged;
    public event Action<string>? OnGuardDecision;

    public MixerController(
        IAudioSessionManager audioManager,
        IGuardEngine guardEngine,
        ISessionClassifier classifier,
        IRuleStore ruleStore,
        ISettingsStore settingsStore,
        string? guardLogFilePath = null,
        int guardLogMaxBytes = DefaultGuardLogMaxBytes,
        bool allowAudioWrites = true)
    {
        _audioManager = audioManager;
        _guardEngine = guardEngine;
        _classifier = classifier;
        _ruleStore = ruleStore;
        _settingsStore = settingsStore;
        _allowAudioWrites = allowAudioWrites;
        _guardLogFilePath = guardLogFilePath ?? BuildGuardLogPath();
        _guardLogMaxBytes = guardLogMaxBytes >= 1024
            ? guardLogMaxBytes
            : throw new ArgumentOutOfRangeException(nameof(guardLogMaxBytes));

        _audioManager.OnSessionCreated(OnNewSession);
        _audioManager.OnSessionDestroyed(OnSessionRemoved);
    }

    /// <summary>
    /// Initialisiert den Controller - laedt Settings, Rules, enumeriert Sessions.
    /// </summary>
    public void Initialize()
    {
        // WPF's DispatcherSynchronizationContext is captured on the owner/UI thread.
        _sessionRefreshContext = SynchronizationContext.Current;
        _isShuttingDown = false;
        try
        {
            _ruleStore.Load();
            _settingsStore.Load();
            RefreshSessions();
            _audioManager.StartMonitoring();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MixerController] Initialize error: {ex.Message}");
        }
    }

    /// <summary>
    /// Laedt alle aktuellen Audio-Sessions neu.
    /// </summary>
    public void RefreshSessions()
    {
        var sessions = _audioManager.EnumerateSessions();
        var appEntries = BuildAppEntries(sessions);
        // Only observed changes are audited; the refresh timer is not an event.
        AuditSessionChanges(sessions, appEntries.Count);

        // Eintraege muessen VOR ApplyGuardIfNeeded in AppEntries sein,
        // weil SetVolume() intern AppEntries.FirstOrDefault() verwendet.
        AppEntries.Clear();
        foreach (var entry in appEntries)
        {
            AppEntries.Add(entry);
        }

        foreach (var entry in appEntries)
        {
            var treatAsBaseline = _isBaselineScan && !WasAppSessionStartedAfterControllerStart(entry);
            ApplyGuardIfNeeded(entry, treatAsBaseline);
        }

        _isBaselineScan = false;
        var activePaths = new HashSet<string>(appEntries.Select(e => e.ExePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in _lastLoggedGuardDecision.Keys.ToArray())
        {
            if (!activePaths.Contains(path)) _lastLoggedGuardDecision.Remove(path);
        }

        OnSessionsChanged?.Invoke();
    }

    // Audit actual snapshot transitions, not potentially duplicated OS callbacks
    // or the polling timer. The initial scan is summarized once.
    private void AuditSessionChanges(List<AudioSessionInfo> sessions, int appCount)
    {
        static string Key(AudioSessionInfo session) =>
            $"{session.ExePath}\u001f{session.SessionIdentifier}\u001f{session.ProcessId}";

        var current = new HashSet<string>(sessions.Select(Key), StringComparer.OrdinalIgnoreCase);
        if (_isBaselineScan)
        {
            WriteGuardMeta($"baseline sessions={sessions.Count} apps={appCount}");
        }
        else
        {
            foreach (var added in current.Except(_observedSessionKeys, StringComparer.OrdinalIgnoreCase))
            {
                WriteGuardMeta($"session_created identity={added.Replace('\u001f', '|')}");
            }
            foreach (var removed in _observedSessionKeys.Except(current, StringComparer.OrdinalIgnoreCase))
            {
                WriteGuardMeta($"session_removed identity={removed.Replace('\u001f', '|')}");
            }
        }
        _observedSessionKeys = current;
    }

    private bool WasAppSessionStartedAfterControllerStart(AppEntry entry)
    {
        if (entry.Sessions == null || entry.Sessions.Count == 0)
        {
            return false;
        }

        foreach (var session in entry.Sessions)
        {
            if (session.ProcessId == 0)
            {
                continue;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)session.ProcessId);
                var startedUtc = process.StartTime.ToUniversalTime();
                if (startedUtc >= _controllerStartedUtc.AddSeconds(-1))
                {
                    return true;
                }
            }
            catch
            {
                // ignore process start-time lookup failures
            }
        }

        return false;
    }

    /// <summary>
    /// Setzt die Lautstaerke einer Anwendung.
    /// Verwendet gecachte AppEntries - keine erneute Enumeration.
    /// </summary>
    public void SetVolume(string exePath, float volume) =>
        _ = TrySetVolume(exePath, volume);

    /// <summary>True only when all owned audio sessions accepted the write.</summary>
    public bool TrySetVolume(string exePath, float volume) =>
        SetVolume(exePath, volume, isManualOverride: true);

    /// <summary>
    /// Setzt den Mute-Status einer Anwendung.
    /// Verwendet gecachte AppEntries - keine erneute Enumeration.
    /// </summary>
    public void SetMute(string exePath, bool mute) =>
        _ = TrySetMute(exePath, mute);

    /// <summary>True only when all owned audio sessions accepted the mute write.</summary>
    public bool TrySetMute(string exePath, bool mute)
    {
        if (!_allowAudioWrites) return false;
        var rule = _ruleStore.GetRule(exePath);
        if (rule != null)
        {
            if (!rule.ManualOverride)
                WriteGuardMeta($"manual_override action=mute exe={exePath}");
            rule.ManualOverride = true;
            rule.LastSeenUtc = DateTime.UtcNow;
            // WASAPI mute does not change SimpleAudioVolume.Volume.
            // Preserve the last audible setting for an eventual unmute.
        }

        var entry = AppEntries.FirstOrDefault(e =>
            string.Equals(e.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Sessions.Count == 0) return false;

        var allSucceeded = true;
        foreach (var session in entry.Sessions)
        {
            try
            {
                if (!_audioManager.TrySetMute(session.SessionIdentifier, mute))
                    allSucceeded = false;
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                System.Diagnostics.Debug.WriteLine($"[MixerController] Mute error: {ex.Message}");
            }
        }
        if (allSucceeded) entry.IsMuted = mute;
        return allSucceeded;
    }

    /// <summary>
    /// Toggle Favorit fuer eine Anwendung.
    /// </summary>
    public void ToggleFavorite(string exePath)
    {
        var rule = _ruleStore.GetRule(exePath);
        if (rule != null)
        {
            rule.Favorite = !rule.Favorite;
            _ruleStore.UpsertRule(exePath, rule);
        }
    }

    /// <summary>
    /// Speichert Settings und Rules auf Disk.
    /// </summary>
    public void SaveState()
    {
        _ruleStore.Save();
        _settingsStore.Save();
    }

    /// <summary>
    /// Faehrt Audio-Monitoring herunter und speichert den Zustand.
    /// </summary>
    public void Shutdown()
    {
        _isShuttingDown = true;
        _audioManager.StopMonitoring();
        SaveState();
    }

    private bool SetVolume(string exePath, float volume, bool isManualOverride)
    {
        if (!_allowAudioWrites) return false;
        var safeVolume = Math.Clamp(volume, 0f, 1f);
        var rule = _ruleStore.GetRule(exePath);
        if (rule != null && isManualOverride)
        {
            if (!rule.ManualOverride)
                WriteGuardMeta($"manual_override action=volume exe={exePath}");
            // Preserve explicit manual intent even when the hardware write fails.
            rule.ManualOverride = true;
            _failedAutoWrites.Remove(exePath);
        }
        var entry = AppEntries.FirstOrDefault(e =>
            string.Equals(e.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Sessions.Count == 0) return false;

        var allSucceeded = true;
        foreach (var session in entry.Sessions)
        {
            try
            {
                if (!_audioManager.TrySetVolume(session.SessionIdentifier, safeVolume))
                    allSucceeded = false;
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                System.Diagnostics.Debug.WriteLine($"[MixerController] Volume error: {ex.Message}");
            }
        }
        // Never publish requested volume or persist LastKnownVolume as measured
        // reality if even one audio session refused or disappeared.
        if (allSucceeded)
        {
            if (rule != null)
            {
                rule.LastKnownVolume = safeVolume;
                rule.LastSeenUtc = DateTime.UtcNow;
            }
            entry.CombinedVolume = safeVolume;
        }
        return allSucceeded;
    }

    private List<AppEntry> BuildAppEntries(List<AudioSessionInfo> sessions)
    {
        var entries = new List<AppEntry>();

        var grouped = sessions
            .Where(s => !string.IsNullOrEmpty(s.ExePath))
            .GroupBy(s => s.ExePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var first = group.First();
            var entry = new AppEntry
            {
                ExePath = group.Key,
                ExeName = first.ExeName,
                DisplayName = first.DisplayName,
                IconBytes = first.IconBytes,
                Sessions = group.ToList(),
                CombinedVolume = group.Average(s => s.Volume),
                IsMuted = group.All(s => s.IsMuted),
                HasActiveAudio = group.Any(s => s.Volume > 0 && !s.IsMuted),
                // True signal is independent of the legacy active/controllable filter.
                SignalPeakLevel = group.Any(s => s.SignalPeakLevel.HasValue)
                    ? group.Where(s => s.SignalPeakLevel.HasValue)
                        .Max(s => s.SignalPeakLevel!.Value)
                    : null,
                IsSystemSound = group.Any(s => s.IsSystemSound),
                Rule = _ruleStore.GetRule(group.Key)
            };
            entries.Add(entry);
        }

        return entries;
    }

    private void ApplyGuardIfNeeded(AppEntry entry, bool isBaselineScan)
    {
        // Includes games, install-date fallback and future settings changes.
        // No guard decisions or provider write commands in an isolated review run.
        if (!_allowAudioWrites) return;
        if (string.IsNullOrEmpty(entry.ExePath) ||
            string.Equals(entry.ExePath, "__system_master__", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.ExePath, "__system_sound__", StringComparison.OrdinalIgnoreCase))
            return; // Neither Windows master nor notification sounds are installable apps.


        var decision = _guardEngine.EvaluateNewSession(
            entry.ExePath,
            entry.DisplayName,
            entry.CombinedVolume,
            isBaselineScan,
            entry.Sessions
                .Select(s => s.ProcessId)
                .Where(pid => pid > 0)
                .Distinct()
                .ToArray());

        var decisionChanged = WriteGuardLog(entry, decision);

        if (decision.AutoApplyAllowed)
        {
            if (_failedAutoWrites.TryGetValue(entry.ExePath, out var previousFailure) &&
                DateTime.UtcNow < previousFailure.RetryAfterUtc)
                return;

            if (!SetVolume(entry.ExePath, decision.TargetVolume, isManualOverride: false))
            {
                var count = _failedAutoWrites.TryGetValue(entry.ExePath, out previousFailure)
                    ? previousFailure.Failures + 1 : 1;
                // Exponential retry backoff: 1, 2, 4...60 seconds;
                // no blind write on every UI polling tick.
                _failedAutoWrites[entry.ExePath] = (count,
                    DateTime.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(count - 1, 6)))));
                if (count == 1)
                    WriteGuardMeta($"auto_apply_failed exe={entry.ExePath} sessions={entry.Sessions.Count}");
                return;
            }
            _failedAutoWrites.Remove(entry.ExePath);
            var rule = _ruleStore.GetRule(entry.ExePath);
            if (rule != null)
            {
                // Ein tatsaechlich ausgefuehrter Auto-Apply ist einmalig, auch im Baseline-Pfad.
                rule.AutoApplied = true;
                rule.AutoVolume = decision.TargetVolume;
                rule.LastKnownVolume = decision.TargetVolume;
                _ruleStore.UpsertRule(entry.ExePath, rule);
            }

            var msg = $"[GUARD] {entry.DisplayName} -> {decision.TargetVolume * 100:F0}% (Auto)";
            System.Diagnostics.Debug.WriteLine(msg);
            OnGuardDecision?.Invoke(msg);
        }
        else if (decisionChanged && !string.IsNullOrEmpty(decision.Reason))
        {
            System.Diagnostics.Debug.WriteLine($"[GUARD] {decision.Reason}");
        }
    }

    private void OnNewSession(AudioSessionInfo session)
    {
        System.Diagnostics.Debug.WriteLine($"[AUDIO] Neue Session: {session.DisplayName} ({session.ExeName})");
        // A callback can arrive after polling already observed this session.
        // Audit the actual snapshot delta when its coalesced refresh runs.
        ScheduleSessionRefresh();
    }

    private void OnSessionRemoved(string sessionId)
    {
        System.Diagnostics.Debug.WriteLine($"[AUDIO] Session entfernt: {sessionId}");
        // Removed session identities are logged only if a snapshot confirms removal.
        ScheduleSessionRefresh();
    }

    private void ScheduleSessionRefresh()
    {
        if (_isShuttingDown) return;

        var context = _sessionRefreshContext;
        if (context == null)
        {
            // Headless/non-UI consumers preserve their existing synchronous behavior.
            RefreshSessions();
            return;
        }

        // Audio callbacks can arrive from worker threads. Never mutate UI-bound AppEntries there.
        // Collapse bursts of notifications into a single owner-thread refresh.
        if (Interlocked.Exchange(ref _sessionRefreshQueued, 1) != 0) return;
        context.Post(_ =>
        {
            Interlocked.Exchange(ref _sessionRefreshQueued, 0);
            if (!_isShuttingDown) RefreshSessions();
        }, null);
    }

    private static string BuildGuardLogPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MiniMixerOverlay",
                "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "guard.log");
        }
        catch
        {
            return Path.Combine(Environment.CurrentDirectory, "guard.log");
        }
    }

    private bool WriteGuardLog(AppEntry entry, GuardDecision decision)
    {
        try
        {
            static string S(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return "-";
                }

                return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            }

            var line =
                $"{DateTime.UtcNow:O} | " +
                $"auto={(decision.AutoApplyAllowed ? 1 : 0)} | " +
                $"target={(decision.TargetVolume * 100f):F0}% | " +
                $"current={(entry.CombinedVolume * 100f):F0}% | " +
                $"baseline={(decision.IsBaselineScan ? 1 : 0)} | " +
                $"class={decision.Classification} | " +
                $"rule_exists={(decision.ExistingRuleFound ? 1 : 0)} | " +
                $"rule_auto={(decision.RuleAutoApplied ? 1 : 0)} | " +
                $"rule_manual={(decision.RuleManualOverride ? 1 : 0)} | " +
                $"age={(decision.InstallAgeDays.HasValue ? decision.InstallAgeDays.Value.ToString() : "-")} | " +
                $"signal={S(decision.InstallSignalSource)} | " +
                $"signal_utc={(decision.InstallSignalUtc.HasValue ? decision.InstallSignalUtc.Value.ToString("O") : "-")} | " +
                $"display={S(decision.DisplayName)} | " +
                $"exe={S(entry.ExeName)} | " +
                $"input={S(decision.InputExePath)} | " +
                $"effective={S(decision.EffectiveExePath)} | " +
                $"reason={S(decision.Reason)}";

            // The timestamp changes each time, but is not part of the decision
            // identity. Auto-apply actions are always logged, even if identical.
            var separator = line.IndexOf(" | ", StringComparison.Ordinal);
            var fingerprint = separator >= 0 ? line[(separator + 3)..] : line;
            lock (GuardLogSync)
            {
                if (!decision.AutoApplyAllowed &&
                    _lastLoggedGuardDecision.TryGetValue(entry.ExePath, out var previous) &&
                    string.Equals(previous, fingerprint, StringComparison.Ordinal))
                {
                    return false;
                }

                AppendBoundedGuardLine(line);
                _lastLoggedGuardDecision[entry.ExePath] = fingerprint;
            }
            return true;
        }
        catch
        {
            // Logging must never break guard evaluation or volume behavior.
            return false;
        }
    }

    private void WriteGuardMeta(string message)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O} | meta=1 | {message}";
            lock (GuardLogSync)
            {
                AppendBoundedGuardLine(line);
            }
        }
        catch
        {
            // Logging must never break runtime behavior.
        }
    }

    // The active log and one rotated backup are independently capped. Even a
    // pre-existing oversized file is truncated to a bounded tail on rotation.
    private void AppendBoundedGuardLine(string line)
    {
        // Exe paths/session identifiers can contain control characters.
        line = line.Replace('\r', ' ').Replace('\n', ' ');
        var maxChars = Math.Min(4096, _guardLogMaxBytes / 8);
        if (line.Length > maxChars)
        {
            line = line[..(maxChars - 3)] + "...";
        }

        var payload = line + Environment.NewLine;
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload) + 3;
        if (payloadBytes > _guardLogMaxBytes)
        {
            return; // Never defeat the disk bound with an oversized diagnostic.
        }

        var directory = Path.GetDirectoryName(_guardLogFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (File.Exists(_guardLogFilePath))
        {
            var length = new FileInfo(_guardLogFilePath).Length;
            if (length + payloadBytes > _guardLogMaxBytes)
            {
                var backup = _guardLogFilePath + ".1";
                if (length > _guardLogMaxBytes)
                {
                    // Avoid preserving an already-unbounded legacy log as backup.
                    using (var old = new FileStream(_guardLogFilePath, FileMode.Open,
                        FileAccess.Read, FileShare.ReadWrite))
                    using (var bounded = new FileStream(backup, FileMode.Create,
                        FileAccess.Write, FileShare.Read))
                    {
                        old.Seek(-_guardLogMaxBytes, SeekOrigin.End);
                        old.CopyTo(bounded);
                    }
                    File.Delete(_guardLogFilePath);
                }
                else
                {
                    File.Move(_guardLogFilePath, backup, overwrite: true);
                }
            }
        }

        File.AppendAllText(_guardLogFilePath, payload, new System.Text.UTF8Encoding(false));
    }
}
