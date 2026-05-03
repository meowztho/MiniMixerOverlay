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
