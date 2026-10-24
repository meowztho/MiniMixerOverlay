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
    private bool _saveBlocked;

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
        _saveBlocked = false;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<RuleData>(json, _jsonOptions)
                    ?? throw new JsonException("RuleStore root must be a JSON object");
                loaded.Apps = NormalizeExeKeys(loaded.Apps, out var hadCollisions);
                if (hadCollisions)
                {
                    // Preserve the raw conflicting records before any later save can merge them.
                    _saveBlocked = !JsonFileRecovery.TryPreserve(_filePath, "case-conflicts");
                }
                _data = loaded;
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
            // Keep the damaged file recoverable; block writes if preservation failed.
            _saveBlocked = !JsonFileRecovery.TryPreserve(_filePath, "corrupt");
            _data = new RuleData { ToolFirstRunUtc = DateTime.UtcNow };
        }
    }

    public void Save()
    {
        if (_saveBlocked)
        {
            System.Diagnostics.Debug.WriteLine("RuleStore Save skipped: original JSON could not be backed up");
            return;
        }

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
            // A normal shutdown often saves unchanged rules. Compare the current
            // on-disk bytes, not a stale in-memory "last saved" flag: external
            // edits/deletions must still go through the atomic write path.
            if (File.Exists(_filePath) &&
                string.Equals(File.ReadAllText(_filePath), json, StringComparison.Ordinal))
                return;
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

    private static Dictionary<string, AppRule> NormalizeExeKeys(Dictionary<string, AppRule>? source, out bool hadCollisions)
    {
        var result = new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);
        hadCollisions = false;
        if (source == null) return result;

        foreach (var (exePath, candidate) in source)
        {
            if (!result.TryGetValue(exePath, out var existing))
            {
                result[exePath] = candidate;
                continue;
            }

            hadCollisions = true;
            // Keep the latest manual choice over automatic state and never clear safety flags.
            var preferred = existing.ManualOverride != candidate.ManualOverride
                ? (existing.ManualOverride ? existing : candidate)
                : (existing.LastSeenUtc >= candidate.LastSeenUtc ? existing : candidate);

            result[exePath] = new AppRule
            {
                DisplayName = preferred.DisplayName,
                ExeName = preferred.ExeName,
                Classification = preferred.Classification,
                FirstSeenUtc = existing.FirstSeenUtc <= candidate.FirstSeenUtc ? existing.FirstSeenUtc : candidate.FirstSeenUtc,
                LastSeenUtc = existing.LastSeenUtc >= candidate.LastSeenUtc ? existing.LastSeenUtc : candidate.LastSeenUtc,
                AutoApplied = existing.AutoApplied || candidate.AutoApplied,
                ManualOverride = existing.ManualOverride || candidate.ManualOverride,
                Locked = existing.Locked || candidate.Locked,
                Favorite = existing.Favorite || candidate.Favorite,
                AutoVolume = preferred.AutoVolume,
                LastKnownVolume = preferred.LastKnownVolume
            };
        }

        return result;
    }

    public AppRule? GetRule(string exePath)
    {
        return _data.Apps.TryGetValue(exePath, out var rule) ? rule : null;
    }

    public void UpsertRule(string exePath, AppRule rule)
    {
        _data.Apps[exePath] = rule;
        Save();
    }
}

internal class RuleData
{
    public DateTime ToolFirstRunUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, AppRule> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
