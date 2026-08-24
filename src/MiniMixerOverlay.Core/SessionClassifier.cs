namespace MiniMixerOverlay.Core;

using System.IO;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

/// <summary>
/// Heuristic classifier for applications.
/// Conservative: prefer Unknown over wrong classification.
/// </summary>
public class SessionClassifier : ISessionClassifier
{
    // Known non-game apps and launchers.
    private static readonly HashSet<string> NonGameKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "firefox", "edge", "brave", "opera", "vivaldi",
        "discord", "teams", "slack", "zoom", "skype",
        "spotify", "vlc", "obs", "streamlabs",
        "explorer", "shell", "svchost", "runtimebroker",
        "epicgameslauncher", "origin", "uplay", "battlenet",
        "launcher", "updater", "installer",
        "code", "visual studio", "notepad", "calc",
        "teamspeak", "mumble", "nvidia", "amd", "intel",
        "steam.exe"
    };

    // Typical game indicators.
    private static readonly HashSet<string> GameKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "play", "unity", "unreal", "cryengine",
        "directx", "dx11", "dx12", "vulkan",
        "fortnite", "minecraft", "valorant", "csgo", "apex",
        "call of duty", "battlefield", "overwatch", "league",
        "pubg", "genshin", "steamapps",
        "seven deadly sins", "7ds", "grand cross", "sdsgc", "sevendedlysins"
    };

    public AppClassification Classify(string exePath, string displayName)
    {
        var normalizedPath = (exePath ?? string.Empty).Replace('/', '\\');
        var exeFileName = Path.GetFileName(normalizedPath);
        var combined = $"{normalizedPath} {displayName}".ToLowerInvariant();

        // Hard rule: a process from Steam library common path is treated as game.
        if (normalizedPath.IndexOf("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AppClassification.Game;
        }

        // Hard rule: Steam launcher itself is not a game.
        if (string.Equals(exeFileName, "steam.exe", StringComparison.OrdinalIgnoreCase))
        {
            return AppClassification.NonGame;
        }

        if (NonGameKeywords.Any(kw => combined.Contains(kw)))
        {
            return AppClassification.NonGame;
        }

        if (GameKeywords.Any(kw => combined.Contains(kw)))
        {
            return AppClassification.Game;
        }

        return AppClassification.Unknown;
    }
}
