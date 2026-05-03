namespace MiniMixerOverlay.Core;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

/// <summary>
/// Hauptcontroller - koordiniert Audio, Guard, Persistenz und UI.
/// </summary>
public class MixerController
{
    private static readonly object GuardLogSync = new();
    private static readonly string GuardLogFilePath = BuildGuardLogPath();
    private readonly IAudioSessionManager _audioManager;
    private readonly IGuardEngine _guardEngine;
    private readonly ISessionClassifier _classifier;
    private readonly IRuleStore _ruleStore;
    private readonly ISettingsStore _settingsStore;
    private bool _isBaselineScan = true;
    private readonly DateTime _controllerStartedUtc = DateTime.UtcNow;

    public ObservableCollection<AppEntry> AppEntries { get; } = new();
    public AppSettings Settings => _settingsStore.Settings;

    public event Action? OnSessionsChanged;
    public event Action<string>? OnGuardDecision;

    public MixerController(
        IAudioSessionManager audioManager,
        IGuardEngine guardEngine,
        ISessionClassifier classifier,
        IRuleStore ruleStore,
        ISettingsStore settingsStore)
    {
        _audioManager = audioManager;
        _guardEngine = guardEngine;
        _classifier = classifier;
        _ruleStore = ruleStore;
        _settingsStore = settingsStore;

        _audioManager.OnSessionCreated(OnNewSession);
        _audioManager.OnSessionDestroyed(OnSessionRemoved);
    }

    /// <summary>
    /// Initialisiert den Controller - laedt Settings, Rules, enumeriert Sessions.
    /// </summary>
    public void Initialize()
    {
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
        WriteGuardMeta($"refresh sessions={sessions.Count} apps={appEntries.Count} baseline={(_isBaselineScan ? 1 : 0)}");

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

        OnSessionsChanged?.Invoke();
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
    public void SetVolume(string exePath, float volume)
    {
        SetVolume(exePath, volume, isManualOverride: true);
    }

    /// <summary>
    /// Setzt den Mute-Status einer Anwendung.
    /// Verwendet gecachte AppEntries - keine erneute Enumeration.
    /// </summary>
    public void SetMute(string exePath, bool mute)
    {
        var rule = _ruleStore.GetRule(exePath);
        if (rule != null)
        {
            rule.ManualOverride = true;
            rule.LastSeenUtc = DateTime.UtcNow;
            if (mute)
            {
                rule.LastKnownVolume = 0f;
            }
        }

        var entry = AppEntries.FirstOrDefault(e => e.ExePath == exePath);
        if (entry == null) return;

        foreach (var session in entry.Sessions)
        {
            try { _audioManager.SetMute(session.SessionIdentifier, mute); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MixerController] Mute error: {ex.Message}"); }
        }

        entry.IsMuted = mute;
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
        _audioManager.StopMonitoring();
        SaveState();
    }

    private void SetVolume(string exePath, float volume, bool isManualOverride)
    {
        var safeVolume = Math.Clamp(volume, 0f, 1f);

        var rule = _ruleStore.GetRule(exePath);
        if (rule != null)
        {
            if (isManualOverride)
            {
                rule.ManualOverride = true;
            }

            rule.LastKnownVolume = safeVolume;
            rule.LastSeenUtc = DateTime.UtcNow;
        }

        var entry = AppEntries.FirstOrDefault(e => e.ExePath == exePath);
        if (entry == null) return;

        foreach (var session in entry.Sessions)
        {
            try { _audioManager.SetVolume(session.SessionIdentifier, safeVolume); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MixerController] Volume error: {ex.Message}"); }
        }

        entry.CombinedVolume = safeVolume;
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
                IsSystemSound = group.Any(s => s.IsSystemSound),
                Rule = _ruleStore.GetRule(group.Key)
            };
            entries.Add(entry);
        }

        return entries;
    }

    private void ApplyGuardIfNeeded(AppEntry entry, bool isBaselineScan)
    {
        if (string.IsNullOrEmpty(entry.ExePath)) return;

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

        WriteGuardLog(entry, decision);

        if (decision.AutoApplyAllowed)
        {
            SetVolume(entry.ExePath, decision.TargetVolume, isManualOverride: false);

            var rule = _ruleStore.GetRule(entry.ExePath);
            if (rule != null)
            {
                // Baseline-Autoapply wird nur als "vorbereitet" gespeichert.
                // Das finale "AutoApplied=true" setzen wir erst im regulaeren Durchlauf,
                // damit bereits laufende Spiele nach dem Startup noch einmal sicher auf 5% gehen koennen.
                rule.AutoApplied = !decision.IsBaselineScan;
                rule.AutoVolume = decision.TargetVolume;
                rule.LastKnownVolume = decision.TargetVolume;
                _ruleStore.UpsertRule(entry.ExePath, rule);
            }

            var msg = $"[GUARD] {entry.DisplayName} -> {decision.TargetVolume * 100:F0}% (Auto)";
            System.Diagnostics.Debug.WriteLine(msg);
            OnGuardDecision?.Invoke(msg);
        }
        else if (!string.IsNullOrEmpty(decision.Reason))
        {
            System.Diagnostics.Debug.WriteLine($"[GUARD] {decision.Reason}");
        }
    }

    private void OnNewSession(AudioSessionInfo session)
    {
        System.Diagnostics.Debug.WriteLine($"[AUDIO] Neue Session: {session.DisplayName} ({session.ExeName})");
        WriteGuardMeta($"session_created display={session.DisplayName} exe={session.ExeName} pid={session.ProcessId}");
        RefreshSessions();
    }

    private void OnSessionRemoved(string sessionId)
    {
        System.Diagnostics.Debug.WriteLine($"[AUDIO] Session entfernt: {sessionId}");
        WriteGuardMeta($"session_removed id={sessionId}");
        RefreshSessions();
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

    private static void WriteGuardLog(AppEntry entry, GuardDecision decision)
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

            lock (GuardLogSync)
            {
                File.AppendAllText(GuardLogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never break runtime behavior
        }
    }

    private static void WriteGuardMeta(string message)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O} | meta=1 | {message}";
            lock (GuardLogSync)
            {
                File.AppendAllText(GuardLogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never break runtime behavior
        }
    }
}
