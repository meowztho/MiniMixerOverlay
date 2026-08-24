namespace MiniMixerOverlay.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

/// <summary>
/// JSON-basierter Rule Store.
/// </summary>
public class JsonRuleStore : IRuleStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private RuleData _data;

    public DateTime ToolFirstRunUtc => _data.ToolFirstRunUtc;
    public Dictionary<string, AppRule> Apps => _data.Apps;

    public JsonRuleStore(string? customPath = null)
    {
        _filePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMixerOverlay",
            "rules.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        _data = new RuleData();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<RuleData>(json, _jsonOptions);
                if (loaded != null)
                {
                    loaded.Apps ??= new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);
                    _data = loaded;
                    MigrateLegacyRuleKeys();
                }
            }
            else
            {
                // Neue Datei – ToolFirstRun setzen
                _data.ToolFirstRunUtc = DateTime.UtcNow;
                Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RuleStore Load fehlgeschlagen: {ex.Message}");
            // Bei Fehler: Defaults verwenden
            _data = new RuleData { ToolFirstRunUtc = DateTime.UtcNow };
        }
    }


    private void MigrateLegacyRuleKeys()
    {
        var migrated = new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _data.Apps)
        {
            var rule = pair.Value ?? new AppRule();
            EnsureRuleShape(rule);
            var key = !string.IsNullOrWhiteSpace(rule.AppIdentityKey)
                ? rule.AppIdentityKey
                : AppIdentity.CreateKey(pair.Key, rule.ExeName, rule.DisplayName);

            rule.AppIdentityKey = key;
            if (!rule.BaselineAtFirstRun && rule.FirstSeenUtc != default &&
                rule.FirstSeenUtc <= _data.ToolFirstRunUtc.AddSeconds(2))
            {
                rule.BaselineAtFirstRun = true;
            }

            if (rule.BaselineAtFirstRun)
            {
                rule.DiscoveryStatus = AppDiscoveryStatus.Baseline;
            }
            else if (rule.DiscoveryStatus == AppDiscoveryStatus.InstalledBeforeTool)
            {
                // v10 wording was installer-centric. Preserve the decision but migrate the
                // semantic owner to "present before MiniMixer first run".
                rule.DiscoveryStatus = AppDiscoveryStatus.PresentBeforeFirstRun;
            }
            else if (rule.DiscoveryStatus == AppDiscoveryStatus.Unknown && rule.DeviceProfiles.Count == 0)
            {
                if (rule.AutoApplied)
                {
                    // Legacy AutoApplied rules already passed the previous new-app guard.
                    rule.DiscoveryStatus = AppDiscoveryStatus.NewEligible;
                }
                else if (rule.FirstSeenUtc != default)
                {
                    // Conservative migration: a pre-v10 persisted rule is already a known app.
                    // Treat it as present before the MiniMixer baseline; do not retroactively
                    // auto-limit it merely because older versions lacked presence evidence.
                    rule.DiscoveryStatus = AppDiscoveryStatus.PresentBeforeFirstRun;
                }
            }
            if (string.IsNullOrWhiteSpace(rule.LastKnownExePath) && Path.IsPathRooted(pair.Key))
            {
                rule.LastKnownExePath = pair.Key;
            }

            if (!migrated.TryGetValue(key, out var existing))
            {
                migrated[key] = rule;
                continue;
            }

            // Prefer explicit user intent. If both rules are equally strong, keep the
            // most recently seen one as the source for LastKnownVolume/current metadata.
            var existingPriority = RulePriority(existing);
            var incomingPriority = RulePriority(rule);
            var incomingPreferred = incomingPriority > existingPriority ||
                (incomingPriority == existingPriority && rule.LastSeenUtc > existing.LastSeenUtc);

            migrated[key] = incomingPreferred
                ? MergeRules(existing, rule)
                : MergeRules(rule, existing);
        }

        _data.Apps = migrated;
        Save();
    }

    private static int RulePriority(AppRule rule)
    {
        var score = 0;
        if (rule.ManualOverride) score += 8;
        if (rule.Locked) score += 4;
        if (rule.Favorite) score += 2;
        if (rule.AutoApplied) score += 1;
        return score;
    }

    private static AppRule MergeRules(AppRule secondary, AppRule preferred)
    {
        preferred.FirstSeenUtc = MinNonDefault(preferred.FirstSeenUtc, secondary.FirstSeenUtc);
        preferred.LastSeenUtc = preferred.LastSeenUtc >= secondary.LastSeenUtc ? preferred.LastSeenUtc : secondary.LastSeenUtc;
        preferred.ManualOverride |= secondary.ManualOverride;
        preferred.AutoApplied |= secondary.AutoApplied;
        preferred.Locked |= secondary.Locked;
        preferred.Favorite |= secondary.Favorite;
        preferred.BaselineAtFirstRun |= secondary.BaselineAtFirstRun;
        if (string.IsNullOrWhiteSpace(preferred.LastKnownExePath)) preferred.LastKnownExePath = secondary.LastKnownExePath;
        if (string.IsNullOrWhiteSpace(preferred.ExeName)) preferred.ExeName = secondary.ExeName;
        if (string.IsNullOrWhiteSpace(preferred.DisplayName)) preferred.DisplayName = secondary.DisplayName;

        EnsureRuleShape(preferred);
        EnsureRuleShape(secondary);
        foreach (var pair in secondary.DeviceProfiles)
        {
            if (!preferred.DeviceProfiles.TryGetValue(pair.Key, out var existingProfile) ||
                pair.Value.LastSeenUtc > existingProfile.LastSeenUtc)
            {
                preferred.DeviceProfiles[pair.Key] = pair.Value;
            }
        }

        if (preferred.DiscoveryStatus == AppDiscoveryStatus.Unknown &&
            secondary.DiscoveryStatus != AppDiscoveryStatus.Unknown)
        {
            preferred.DiscoveryStatus = secondary.DiscoveryStatus;
            preferred.InstallSignalUtc = secondary.InstallSignalUtc;
            preferred.InstallSignalSource = secondary.InstallSignalSource;
        }

        return preferred;
    }

    private static void EnsureRuleShape(AppRule rule)
    {
        rule.DeviceProfiles = rule.DeviceProfiles == null
            ? new Dictionary<string, DeviceVolumeProfile>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DeviceVolumeProfile>(rule.DeviceProfiles, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in rule.DeviceProfiles)
        {
            pair.Value.DeviceId = string.IsNullOrWhiteSpace(pair.Value.DeviceId)
                ? pair.Key
                : pair.Value.DeviceId;
        }
    }

    private static DateTime MinNonDefault(DateTime a, DateTime b)
    {
        if (a == default) return b;
        if (b == default) return a;
        return a <= b ? a : b;
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Atomar speichern: zuerst in temporäre Datei, dann umbenennen
            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(_data, _jsonOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RuleStore Save fehlgeschlagen: {ex.Message}");
        }
    }

    public AppRule? GetRule(string appIdentityKey)
    {
        if (!_data.Apps.TryGetValue(appIdentityKey, out var rule))
        {
            return null;
        }

        EnsureRuleShape(rule);
        return rule;
    }

    public AppRule? ResolveRule(string appIdentityKey, string exePath, string exeName, string displayName)
    {
        if (_data.Apps.TryGetValue(appIdentityKey, out var direct))
        {
            EnsureRuleShape(direct);
            return direct;
        }

        // Lazy migration for identity-scheme upgrades. Only adopt an old rule when the
        // executable name match is unique; display name is used as an additional guard.
        var normalizedExe = Path.GetFileName(exeName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedExe))
        {
            normalizedExe = Path.GetFileName(exePath ?? string.Empty);
        }

        var candidates = new List<KeyValuePair<string, AppRule>>();
        foreach (var pair in _data.Apps)
        {
            var rule = pair.Value;
            var ruleExe = !string.IsNullOrWhiteSpace(rule.ExeName)
                ? Path.GetFileName(rule.ExeName)
                : Path.GetFileName(rule.LastKnownExePath);
            if (!string.Equals(ruleExe, normalizedExe, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(rule.DisplayName) &&
                !string.Equals(rule.DisplayName.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(pair);
        }

        if (candidates.Count != 1)
        {
            return null;
        }

        var adopted = candidates[0].Value;
        adopted.AppIdentityKey = appIdentityKey;
        if (!string.IsNullOrWhiteSpace(exePath)) adopted.LastKnownExePath = exePath;
        if (!string.IsNullOrWhiteSpace(exeName)) adopted.ExeName = exeName;
        if (!string.IsNullOrWhiteSpace(displayName)) adopted.DisplayName = displayName;
        _data.Apps[appIdentityKey] = adopted;
        Save();
        return adopted;
    }

    public void UpsertRule(string appIdentityKey, AppRule rule)
    {
        rule.AppIdentityKey = appIdentityKey;
        EnsureRuleShape(rule);
        _data.Apps[appIdentityKey] = rule;
        Save();
    }
}

internal class RuleData
{
    public DateTime ToolFirstRunUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, AppRule> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
