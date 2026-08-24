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
/// Application identity is global, while volume ownership is App x OutputDevice.
/// </summary>
public class MixerController
{
    private readonly IAudioSessionManager _audioManager;
    private readonly IGuardEngine _guardEngine;
    private readonly ISessionClassifier _classifier;
    private readonly IRuleStore _ruleStore;
    private readonly ISettingsStore _settingsStore;
    private readonly HashSet<string> _profileInitializedSessionIds = new(StringComparer.OrdinalIgnoreCase);
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

    public void Initialize()
    {
        try
        {
            _ruleStore.Load();
            _settingsStore.Load();

            // Baseline exists only on the tool's first-ever run. Earlier versions started a
            // new baseline on every MiniMixer launch, which could exempt a genuinely new app
            // simply because it was already running when MiniMixer restarted.
            var firstRunAge = _controllerStartedUtc - _ruleStore.ToolFirstRunUtc;
            _isBaselineScan = firstRunAge >= TimeSpan.FromMinutes(-1) &&
                              firstRunAge <= TimeSpan.FromMinutes(2);

            RefreshSessions();
            _audioManager.StartMonitoring();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MixerController] Initialize error: {ex.Message}");
        }
    }

    public void RefreshSessions()
    {
        var sessions = _audioManager.EnumerateSessions();
        var appEntries = BuildAppEntries(sessions);
        AppEntries.Clear();
        foreach (var entry in appEntries)
        {
            AppEntries.Add(entry);
        }

        foreach (var entry in appEntries)
        {
            RestoreKnownDeviceProfileIfNeeded(entry);

            var treatAsBaseline = _isBaselineScan && !WasAppSessionStartedAfterControllerStart(entry);
            ApplyGuardIfNeeded(entry, treatAsBaseline);
        }

        // A session that disappears must be eligible for profile restore when it comes back.
        var currentSessionIds = sessions
            .Select(s => s.SessionIdentifier)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _profileInitializedSessionIds.IntersectWith(currentSessionIds);

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

    public void SetVolume(string entryKey, float volume)
    {
        SetVolume(entryKey, volume, isManualOverride: true);
    }

    public void SetMute(string entryKey, bool mute)
    {
        var entry = FindEntry(entryKey);
        if (entry == null)
        {
            return;
        }

        var rule = _ruleStore.GetRule(entry.AppIdentityKey);
        if (rule != null)
        {
            var profile = GetOrCreateDeviceProfile(rule, entry, entry.CombinedVolume);
            profile.ManualOverride = true;
            profile.GuardEvaluated = true;
            profile.LastSeenUtc = DateTime.UtcNow;

            // Mute is not a volume value. Keep LastKnownVolume so unmute/device restore
            // does not silently turn the profile into 0%.
            rule.ManualOverride = rule.DeviceProfiles.Values.Any(p => p.ManualOverride);
            rule.LastSeenUtc = DateTime.UtcNow;
            _ruleStore.UpsertRule(entry.AppIdentityKey, rule);
        }

        foreach (var session in entry.Sessions)
        {
            try { _audioManager.SetMute(session.SessionIdentifier, mute); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MixerController] Mute error: {ex.Message}"); }
        }

        entry.IsMuted = mute;
    }

    public void ToggleFavorite(string entryKey)
    {
        var entry = FindEntry(entryKey);
        var ruleKey = entry?.AppIdentityKey ?? entryKey;
        var rule = _ruleStore.GetRule(ruleKey);
        if (rule != null)
        {
            rule.Favorite = !rule.Favorite;
            _ruleStore.UpsertRule(ruleKey, rule);
        }
    }

    public void SaveState()
    {
        _ruleStore.Save();
        _settingsStore.Save();
    }

    public void Shutdown()
    {
        _audioManager.StopMonitoring();
        SaveState();
    }

    private void SetVolume(string entryKey, float volume, bool isManualOverride)
    {
        var safeVolume = Math.Clamp(volume, 0f, 1f);
        var entry = FindEntry(entryKey);
        if (entry == null)
        {
            return;
        }

        var rule = _ruleStore.GetRule(entry.AppIdentityKey);
        if (rule != null)
        {
            var profile = GetOrCreateDeviceProfile(rule, entry, safeVolume);
            if (isManualOverride)
            {
                profile.ManualOverride = true;
                profile.GuardEvaluated = true;
            }

            profile.LastKnownVolume = safeVolume;
            profile.LastSeenUtc = DateTime.UtcNow;

            // Legacy mirrors are non-authoritative from v10 onward, but keep them fresh
            // so downgrades/old diagnostics do not lose the last touched value.
            rule.ManualOverride = rule.DeviceProfiles.Values.Any(p => p.ManualOverride);
            rule.AutoApplied = rule.DeviceProfiles.Values.Any(p => p.AutoApplied);
            rule.LastKnownVolume = safeVolume;
            rule.LastSeenUtc = DateTime.UtcNow;
            _ruleStore.UpsertRule(entry.AppIdentityKey, rule);
        }

        foreach (var session in entry.Sessions)
        {
            try
            {
                _audioManager.SetVolume(session.SessionIdentifier, safeVolume);
                _profileInitializedSessionIds.Add(session.SessionIdentifier);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MixerController] Volume error: {ex.Message}");
            }
        }

        entry.CombinedVolume = safeVolume;
    }

    private DeviceVolumeProfile GetOrCreateDeviceProfile(AppRule rule, AppEntry entry, float initialVolume)
    {
        rule.DeviceProfiles ??= new Dictionary<string, DeviceVolumeProfile>(StringComparer.OrdinalIgnoreCase);
        var deviceId = OutputDeviceIdentity.Normalize(entry.OutputDeviceId);
        if (!rule.DeviceProfiles.TryGetValue(deviceId, out var profile))
        {
            profile = new DeviceVolumeProfile
            {
                DeviceId = deviceId,
                DeviceName = entry.OutputDeviceName,
                FirstSeenUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                LastKnownVolume = Math.Clamp(initialVolume, 0f, 1f)
            };
            rule.DeviceProfiles[deviceId] = profile;
        }
        else
        {
            profile.DeviceId = deviceId;
            if (!string.IsNullOrWhiteSpace(entry.OutputDeviceName))
            {
                profile.DeviceName = entry.OutputDeviceName;
            }
        }

        return profile;
    }

    private void RestoreKnownDeviceProfileIfNeeded(AppEntry entry)
    {
        if (entry.IsSystemSound || entry.Sessions.Count == 0)
        {
            return;
        }

        var rule = entry.Rule ?? _ruleStore.GetRule(entry.AppIdentityKey);
        if (rule?.DeviceProfiles == null)
        {
            return;
        }

        var deviceId = OutputDeviceIdentity.Normalize(entry.OutputDeviceId);
        if (!rule.DeviceProfiles.TryGetValue(deviceId, out var profile) || !profile.GuardEvaluated)
        {
            return;
        }

        var hasFreshSession = entry.Sessions.Any(s =>
            !string.IsNullOrWhiteSpace(s.SessionIdentifier) &&
            !_profileInitializedSessionIds.Contains(s.SessionIdentifier));
        if (!hasFreshSession)
        {
            return;
        }

        var target = Math.Clamp(profile.LastKnownVolume, 0f, 1f);
        SetVolume(entry.ControlKey, target, isManualOverride: false);
    }

    private AppEntry? FindEntry(string keyOrPath)
    {
        var byControl = AppEntries.FirstOrDefault(e =>
            string.Equals(e.ControlKey, keyOrPath, StringComparison.OrdinalIgnoreCase));
        if (byControl != null)
        {
            return byControl;
        }

        // Compatibility fallback for callers from older UI code. Prefer the default endpoint
        // if the same logical app currently has sessions on multiple output devices.
        return AppEntries
            .Where(e =>
                string.Equals(e.AppIdentityKey, keyOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.ExePath, keyOrPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.IsDefaultOutputDevice)
            .FirstOrDefault();
    }

    private List<AppEntry> BuildAppEntries(List<AudioSessionInfo> sessions)
    {
        var entries = new List<AppEntry>();

        var grouped = sessions
            .Where(s => !string.IsNullOrEmpty(s.ExePath))
            .GroupBy(
                s =>
                {
                    var appKey = string.IsNullOrWhiteSpace(s.AppIdentityKey)
                        ? AppIdentity.CreateKey(s.ExePath, s.ExeName, s.DisplayName)
                        : s.AppIdentityKey;
                    return OutputDeviceIdentity.CreateControlKey(appKey, s.OutputDeviceId);
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var first = group.First();
            var appIdentityKey = string.IsNullOrWhiteSpace(first.AppIdentityKey)
                ? AppIdentity.CreateKey(first.ExePath, first.ExeName, first.DisplayName)
                : first.AppIdentityKey;
            var currentPath = group
                .Select(s => s.ExePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;

            var entry = new AppEntry
            {
                ControlKey = group.Key,
                AppIdentityKey = appIdentityKey,
                ExePath = currentPath,
                ExeName = first.ExeName,
                DisplayName = first.DisplayName,
                IconBytes = first.IconBytes,
                Sessions = group.ToList(),
                CombinedVolume = group.Average(s => s.Volume),
                IsMuted = group.All(s => s.IsMuted),
                HasActiveAudio = group.Any(s => s.Volume > 0 && !s.IsMuted),
                IsSystemSound = group.Any(s => s.IsSystemSound),
                OutputDeviceId = OutputDeviceIdentity.Normalize(first.OutputDeviceId),
                OutputDeviceName = first.OutputDeviceName,
                IsDefaultOutputDevice = group.Any(s => s.IsDefaultOutputDevice),
                Rule = _ruleStore.ResolveRule(appIdentityKey, currentPath, first.ExeName, first.DisplayName)
            };
            entries.Add(entry);
        }

        // Keep the system-volume fallback singular. If Windows exposes system-sound sessions
        // on several endpoints, prefer the current default output device.
        var systemEntries = entries.Where(e => e.IsSystemSound).ToList();
        if (systemEntries.Count > 1)
        {
            var keep = systemEntries.FirstOrDefault(e => e.IsDefaultOutputDevice) ?? systemEntries[0];
            entries.RemoveAll(e => e.IsSystemSound && !ReferenceEquals(e, keep));
        }

        return entries
            .OrderByDescending(e => e.IsDefaultOutputDevice)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void ApplyGuardIfNeeded(AppEntry entry, bool isBaselineScan)
    {
        if (string.IsNullOrEmpty(entry.ExePath) || entry.IsSystemSound)
        {
            return;
        }

        var decision = _guardEngine.EvaluateNewSession(
            entry.AppIdentityKey,
            entry.ExePath,
            entry.DisplayName,
            entry.CombinedVolume,
            entry.OutputDeviceId,
            entry.OutputDeviceName,
            isBaselineScan,
            entry.Sessions
                .Select(s => s.ProcessId)
                .Where(pid => pid > 0)
                .Distinct()
                .ToArray());

        if (decision.ApplyVolume)
        {
            SetVolume(entry.ControlKey, decision.TargetVolume, isManualOverride: false);
            var action = decision.AutoApplyAllowed ? "Auto" : "Profile";
            var msg = $"[GUARD] {entry.DisplayName} @ {entry.OutputDeviceName} -> {decision.TargetVolume * 100:F0}% ({action})";
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
        System.Diagnostics.Debug.WriteLine(
            $"[AUDIO] Neue Session: {session.DisplayName} ({session.ExeName}) @ {session.OutputDeviceName}");
        RefreshSessions();
    }

    private void OnSessionRemoved(string sessionId)
    {
        _profileInitializedSessionIds.Remove(sessionId);
        System.Diagnostics.Debug.WriteLine($"[AUDIO] Session entfernt: {sessionId}");
        RefreshSessions();
    }


}
