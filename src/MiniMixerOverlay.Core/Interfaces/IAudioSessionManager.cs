namespace MiniMixerOverlay.Core.Interfaces;

using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Models;
using System;
using System.Collections.Generic;

/// <summary>
/// Interface für den Zugriff auf Windows Audio-Sessions.
/// </summary>
public interface IAudioSessionManager
{
    /// <summary>
    /// Enumeriert alle aktuellen Audio-Sessions.
    /// </summary>
    List<AudioSessionInfo> EnumerateSessions();

    /// <summary>
    /// Setzt die Lautstärke einer Session (0.0 - 1.0).
    /// </summary>
    void SetVolume(string sessionIdentifier, float volume);

    /// <summary>
    /// Setzt den Mute-Status einer Session.
    /// </summary>
    void SetMute(string sessionIdentifier, bool mute);

    /// <summary>
    /// Registriert einen Callback für neue Sessions.
    /// </summary>
    void OnSessionCreated(Action<AudioSessionInfo> callback);

    /// <summary>
    /// Registriert einen Callback für entfernte Sessions.
    /// </summary>
    void OnSessionDestroyed(Action<string> callback);

    /// <summary>
    /// Startet die Überwachung.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stoppt die Überwachung.
    /// </summary>
    void StopMonitoring();
}

/// <summary>
/// Interface für Klassifikation von Anwendungen.
/// </summary>
public interface ISessionClassifier
{
    AppClassification Classify(string exePath, string displayName);
}

/// <summary>
/// Interface für die Guard-Engine.
/// </summary>
public interface IGuardEngine
{
    /// <summary>
    /// Prüft, ob eine neue Anwendung automatisch auf die konfigurierte Ziel-Lautstaerke gesetzt werden darf.
    /// </summary>
    bool ShouldAutoApplyVolume(AppRule rule, DateTime toolFirstRunUtc);

    /// <summary>
    /// Wendet die Guard-Logik auf eine neue Session an.
    /// </summary>
    GuardDecision EvaluateNewSession(
        string appIdentityKey,
        string exePath,
        string displayName,
        float currentVolume,
        string outputDeviceId,
        string outputDeviceName,
        bool isBaselineScan = false,
        IReadOnlyCollection<uint>? processIds = null);
}

public class GuardDecision
{
    public bool AutoApplyAllowed { get; set; }
    public float TargetVolume { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AppIdentityKey { get; set; } = string.Empty;
    public string InputExePath { get; set; } = string.Empty;
    public string EffectiveExePath { get; set; } = string.Empty;
    public bool IsBaselineScan { get; set; }
    public bool ExistingRuleFound { get; set; }
    public bool RuleAutoApplied { get; set; }
    public bool RuleManualOverride { get; set; }
    public AppClassification Classification { get; set; } = AppClassification.Unknown;
    public int? PresenceAgeDays { get; set; }
    public DateTime? PresenceSignalUtc { get; set; }
    public string PresenceSignalSource { get; set; } = string.Empty;
    public string OutputDeviceId { get; set; } = string.Empty;
    public string OutputDeviceName { get; set; } = string.Empty;
    public AppDiscoveryStatus DiscoveryStatus { get; set; } = AppDiscoveryStatus.Unknown;
    public bool DeviceProfileExisted { get; set; }
    public bool DeviceProfileAutoApplied { get; set; }
    public bool DeviceProfileManualOverride { get; set; }
    public bool ApplyVolume { get; set; }
    public bool ProfileRestore { get; set; }
}

/// <summary>
/// Interface für den Rule Store (JSON-Persistenz).
/// </summary>
public interface IRuleStore
{
    DateTime ToolFirstRunUtc { get; }
    Dictionary<string, AppRule> Apps { get; }

    void Load();
    void Save();
    AppRule? GetRule(string appIdentityKey);
    AppRule? ResolveRule(string appIdentityKey, string exePath, string exeName, string displayName);
    void UpsertRule(string appIdentityKey, AppRule rule);
}

/// <summary>
/// Interface für den Settings Store.
/// </summary>
public interface ISettingsStore
{
    AppSettings Settings { get; }
    void Load();
    void Save();
}

public class AppSettings
{
    public WindowSettings Window { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public GameHookSettings GameHook { get; set; } = new();
}

public class WindowSettings
{
    public double X { get; set; } = 1580;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 680;
    public bool IsDocked { get; set; }
    public string DockSide { get; set; } = "right";
    public int DockVisibleWidth { get; set; } = 72;
    public int DockRevealZoneWidth { get; set; } = 72;
    public string CornerSnapAnchor { get; set; } = "top-right";
    public bool AlwaysOnTop { get; set; } = true;

    // Optional manual anchor for the visible corner hint / hover target.
    // Coordinates use the Windows virtual desktop, so secondary monitors are supported.
    public bool CornerHintManualPosition { get; set; }
    public double? CornerHintX { get; set; }
    public double? CornerHintY { get; set; }
}

public class UiSettings
{
    public string Language { get; set; } = "de";
    public string OverlayMode { get; set; } = "desktop";
    public bool CornerHintShowDot { get; set; } = true;
    public bool CornerHintShowValue { get; set; } = true;
    public string CornerHintValueColor { get; set; } = "Auto";
    public bool CornerHintUseCustomValueColor { get; set; }
    public string CornerHintCustomValueColorHex { get; set; } = "#F5FCFF";
    public int VisibleApps { get; set; } = 5;
    public int RefreshIntervalMs { get; set; } = 1800;
    public bool BringToFrontOnHover { get; set; } = true;
    public bool CornerRevealEnabled { get; set; } = true;
    public bool AutoApplyToAllNewApps { get; set; } = true;
    public int AutoVolumePercent { get; set; } = GuardDefaults.AutoVolumePercent;
    public bool UseWindowsAccentForGlass { get; set; } = true;
    public string GlassPalette { get; set; } = "Cyan";
    public bool GlassUseCustomColor { get; set; }
    public string GlassCustomColorHex { get; set; } = "#00D4FF";
    public int GlassStrength { get; set; } = 74;
    public int GlassTransparency { get; set; } = 72;
    public bool GlassBorderUseCustomColor { get; set; }
    public string GlassBorderColorHex { get; set; } = "#9CD2E8";
    public int GlassBorderThickness { get; set; } = 1;
    public int GlassBorderSmudge { get; set; } = 38;
}

public class GameHookSettings
{
    public string AssetsPath { get; set; } = @"goverlay-master\goverlay-master\game-overlay\prebuilt";
    public string TargetWindowTitle { get; set; } = string.Empty;
    public bool ForwardInputToOverlay { get; set; } = false;
    public bool AutoStartRuntime { get; set; } = true;
}

