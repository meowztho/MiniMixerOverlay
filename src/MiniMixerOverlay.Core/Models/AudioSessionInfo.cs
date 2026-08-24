namespace MiniMixerOverlay.Core.Models;

using System.Text;

/// <summary>
/// Klassifikation einer Anwendung.
/// </summary>
public enum AppClassification
{
    Unknown,
    Game,
    NonGame
}

/// <summary>
/// Persisted result of the one-time "was this app already present when MiniMixer first ran?" check.
/// Unknown deliberately remains retryable when the first audio session did not expose
/// enough process/presence metadata yet.
/// </summary>
public enum AppDiscoveryStatus
{
    Unknown = 0,
    Baseline = 1,

    // Legacy v10 value retained only so existing rules.json files deserialize safely.
    InstalledBeforeTool = 2,

    NewEligible = 3,
    PresentBeforeFirstRun = 4
}

/// <summary>
/// Repräsentiert eine einzelne Audio-Session einer Anwendung.
/// </summary>
public class AudioSessionInfo
{
    public string SessionIdentifier { get; set; } = string.Empty;
    public uint ProcessId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public float Volume { get; set; }
    public bool IsMuted { get; set; }
    public string AppIdentityKey { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public byte[]? IconBytes { get; set; }
    public bool IsSystemSound { get; set; }
    public string OutputDeviceId { get; set; } = string.Empty;
    public string OutputDeviceName { get; set; } = string.Empty;
    public bool IsDefaultOutputDevice { get; set; }
}

/// <summary>
/// Aggregiertes Modell für die UI – eine Anwendung kann mehrere Sessions haben.
/// </summary>
public class AppEntry
{
    /// <summary>
    /// Runtime key for exactly one logical app on exactly one output endpoint.
    /// UI actions should use this key instead of ExePath.
    /// </summary>
    public string ControlKey { get; set; } = string.Empty;
    public string AppIdentityKey { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte[]? IconBytes { get; set; }
    public List<AudioSessionInfo> Sessions { get; set; } = new();
    public float CombinedVolume { get; set; }
    public bool IsMuted { get; set; }
    public bool HasActiveAudio { get; set; }
    public bool IsSystemSound { get; set; }
    public string OutputDeviceId { get; set; } = string.Empty;
    public string OutputDeviceName { get; set; } = string.Empty;
    public bool IsDefaultOutputDevice { get; set; }
    public AppRule? Rule { get; set; }
}

/// <summary>
/// Persistierte Regel pro Anwendung.
/// </summary>
public class AppRule
{
    public string AppIdentityKey { get; set; } = string.Empty;
    public string LastKnownExePath { get; set; } = string.Empty;
    public bool BaselineAtFirstRun { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public AppClassification Classification { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    // Global application discovery is separate from per-output-device volume state.
    public AppDiscoveryStatus DiscoveryStatus { get; set; } = AppDiscoveryStatus.Unknown;

    // Legacy JSON/property names retained for backwards compatibility. Semantically these
    // fields now store the earliest reliable local presence/arrival evidence, not only
    // installer data. Sources can be registry, Steam manifest, or portable-file creation.
    public DateTime? InstallSignalUtc { get; set; }
    public string InstallSignalSource { get; set; } = string.Empty;
    public Dictionary<string, DeviceVolumeProfile> DeviceProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Legacy fields retained for lossless migration from pre-device-profile rule files.
    // New runtime code does not use these as the owner of device volume state.
    public bool AutoApplied { get; set; }
    public float AutoVolume { get; set; }
    public bool ManualOverride { get; set; }
    public bool Locked { get; set; }
    public bool Favorite { get; set; }
    public float LastKnownVolume { get; set; }
}

/// <summary>
/// Independent persisted volume profile for one logical application on one Windows
/// render endpoint. This prevents e.g. speaker volume from overwriting headphone volume.
/// </summary>
public class DeviceVolumeProfile
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool GuardEvaluated { get; set; }
    public bool AutoApplied { get; set; }
    public float AutoVolume { get; set; }
    public bool ManualOverride { get; set; }
    public float LastKnownVolume { get; set; }
}

public static class OutputDeviceIdentity
{
    public const string UnknownDeviceId = "__unknown_output_device__";

    public static string Normalize(string? deviceId)
        => string.IsNullOrWhiteSpace(deviceId) ? UnknownDeviceId : deviceId.Trim();

    public static string CreateControlKey(string appIdentityKey, string? deviceId)
        => $"{appIdentityKey}\u001f{Normalize(deviceId)}";
}


/// <summary>
/// Builds a durable logical application key. The key intentionally avoids versioned
/// install paths so self-updating applications keep the same persisted rule.
/// </summary>
public static class AppIdentity
{
    public static string CreateKey(
        string? exePath,
        string? exeName,
        string? displayName,
        string? companyName = null,
        string? productName = null,
        string? originalFilename = null)
    {
        var rawExeName = exeName;
        if (string.IsNullOrWhiteSpace(rawExeName) && !string.IsNullOrWhiteSpace(exePath))
        {
            rawExeName = System.IO.Path.GetFileName(exePath);
        }

        var normalizedExe = Normalize(System.IO.Path.GetFileNameWithoutExtension(rawExeName ?? string.Empty));
        var normalizedDisplay = Normalize(displayName);
        if (normalizedExe == "systemsound" || string.Equals(exePath, "__system_sound__", StringComparison.OrdinalIgnoreCase))
        {
            return "system:audio";
        }

        // Prefer publisher/product metadata. This keeps updater/version-folder changes stable
        // without merging unrelated applications that happen to share an exe filename.
        var company = Normalize(companyName);
        var product = Normalize(productName);
        var original = Normalize(System.IO.Path.GetFileNameWithoutExtension(originalFilename ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(product))
        {
            var filePart = !string.IsNullOrWhiteSpace(original) ? original : normalizedExe;
            return $"product:{company}|{product}|{filePart}";
        }

        // Stable install-root fallback: remove common version-only directory segments but
        // retain enough path context to distinguish same-named executables from different apps.
        var installKey = NormalizeStableInstallPath(exePath);
        if (!string.IsNullOrWhiteSpace(installKey) && !string.IsNullOrWhiteSpace(normalizedExe))
        {
            return $"install:{installKey}|{normalizedExe}";
        }

        if (!string.IsNullOrWhiteSpace(normalizedExe) || !string.IsNullOrWhiteSpace(normalizedDisplay))
        {
            return $"app:{normalizedExe}|{normalizedDisplay}";
        }

        return $"path:{Normalize(exePath)}";
    }

    private static string NormalizeStableInstallPath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return string.Empty;
        try
        {
            var full = System.IO.Path.GetFullPath(exePath);
            var dir = System.IO.Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(dir)) return string.Empty;

            var parts = dir.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            while (parts.Count > 0 && IsVersionLikeDirectory(parts[^1]))
            {
                parts.RemoveAt(parts.Count - 1);
            }

            return Normalize(string.Join("|", parts));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsVersionLikeDirectory(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v.StartsWith("app-", StringComparison.Ordinal) || v.StartsWith("version-", StringComparison.Ordinal))
        {
            v = v[(v.IndexOf('-') + 1)..];
        }

        var hasDigit = false;
        foreach (var ch in v)
        {
            if (char.IsDigit(ch)) { hasDigit = true; continue; }
            if (ch is '.' or '-' or '_' or 'v') continue;
            return false;
        }
        return hasDigit;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
