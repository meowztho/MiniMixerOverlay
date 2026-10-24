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
    private bool _saveBlocked;

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
        _saveBlocked = false;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                    ?? throw new JsonException("SettingsStore root must be a JSON object");
                // Explicit JSON null overrides constructor defaults. Recover these
                // required sections before consumers access them; preserve valid
                // sibling preferences and the exact original bytes on disk.
                if (loaded.Window == null || loaded.Ui == null ||
                    loaded.GameHook == null || loaded.System == null)
                {
                    _saveBlocked = !JsonFileRecovery.TryPreserve(_filePath, "corrupt");
                    loaded.Window ??= new WindowSettings();
                    loaded.Ui ??= new UiSettings();
                    loaded.GameHook ??= new GameHookSettings();
                    loaded.System ??= new SystemSettings();
                }
                Settings = loaded;
            }
            else
            {
                Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsStore Load fehlgeschlagen: {ex.Message}");
            // Do not overwrite the damaged original unless its exact bytes were backed up.
            _saveBlocked = !JsonFileRecovery.TryPreserve(_filePath, "corrupt");
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        if (_saveBlocked)
        {
            System.Diagnostics.Debug.WriteLine("SettingsStore Save skipped: original JSON could not be backed up");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            // Keep the existing atomic save/recovery contract, but avoid
            // rewriting identical data after a harmless window close.
            // Reading the actual file also detects external changes/deletion.
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
            System.Diagnostics.Debug.WriteLine($"SettingsStore Save fehlgeschlagen: {ex.Message}");
        }
    }
}
