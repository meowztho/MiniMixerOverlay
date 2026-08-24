namespace MiniMixerOverlay.Infrastructure.Persistence;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniMixerOverlay.Core.Interfaces;

/// <summary>
/// JSON-basierter Settings Store.
/// </summary>
public class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public AppSettings Settings { get; private set; } = new();

    public JsonSettingsStore(string? customPath = null)
    {
        _filePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMixerOverlay",
            "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                }
            }
            else
            {
                Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsStore Load fehlgeschlagen: {ex.Message}");
            Settings = new AppSettings();
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

            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
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
            System.Diagnostics.Debug.WriteLine($"SettingsStore Save fehlgeschlagen: {ex.Message}");
        }
    }
}
