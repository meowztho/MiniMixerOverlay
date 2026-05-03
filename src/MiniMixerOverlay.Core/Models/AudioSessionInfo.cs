namespace MiniMixerOverlay.Core.Models;

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
/// Repräsentiert eine einzelne Audio-Session einer Anwendung.
/// </summary>
public class AudioSessionInfo
{
    public string SessionIdentifier { get; set; } = string.Empty;
    public uint ProcessId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public float Volume { get; set; }
    public bool IsMuted { get; set; }
    public string ExePath { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public byte[]? IconBytes { get; set; }
    public bool IsSystemSound { get; set; }
}

/// <summary>
/// Aggregiertes Modell für die UI – eine Anwendung kann mehrere Sessions haben.
/// </summary>
public class AppEntry
{
    public string ExePath { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte[]? IconBytes { get; set; }
    public List<AudioSessionInfo> Sessions { get; set; } = new();
    public float CombinedVolume { get; set; }
    public bool IsMuted { get; set; }
    public bool HasActiveAudio { get; set; }
    public bool IsSystemSound { get; set; }
    public AppRule? Rule { get; set; }
}

/// <summary>
/// Persistierte Regel pro Anwendung.
/// </summary>
public class AppRule
{
    public string DisplayName { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public AppClassification Classification { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public bool AutoApplied { get; set; }
    public float AutoVolume { get; set; }
    public bool ManualOverride { get; set; }
    public bool Locked { get; set; }
    public bool Favorite { get; set; }
    public float LastKnownVolume { get; set; }
}
