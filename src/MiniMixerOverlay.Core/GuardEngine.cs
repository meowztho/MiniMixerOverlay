namespace MiniMixerOverlay.Core;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

/// <summary>
/// Guard-Engine: Verhindert unerwuenschtes Ueberschreiben bestehender Einstellungen.
/// </summary>
public class GuardEngine : IGuardEngine
{
    private readonly IRuleStore _ruleStore;
    private readonly ISessionClassifier _classifier;
    private float _autoVolume = 0.05f;
    private bool _autoApplyToAllNewApps = true;
    private int _autoLimitMaxInstallAgeDays = 7;
    private readonly Dictionary<string, (DateTime CheckedUtc, DateTime InstallSignalUtc, string Source)> _installSignalCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime CheckedUtc, Dictionary<string, DateTime> InstallSignalsUtcByDir)> _steamInstallSignalCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan InstallSignalCacheTtl = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan SteamInstallSignalCacheTtl = TimeSpan.FromMinutes(10);
    private const int ProcessQueryLimitedInformation = 0x1000;

    public GuardEngine(
        IRuleStore ruleStore,
        ISessionClassifier classifier,
        int autoVolumePercent = 5,
        bool autoApplyToAllNewApps = true,
        int autoLimitMaxInstallAgeDays = 7)
    {
        _ruleStore = ruleStore;
        _classifier = classifier;
        Configure(autoVolumePercent, autoApplyToAllNewApps, autoLimitMaxInstallAgeDays);
    }

    public void Configure(int autoVolumePercent, bool autoApplyToAllNewApps, int autoLimitMaxInstallAgeDays)
    {
        _autoVolume = Math.Clamp(autoVolumePercent, 1, 100) / 100f;
        _autoApplyToAllNewApps = autoApplyToAllNewApps;
        _autoLimitMaxInstallAgeDays = Math.Clamp(autoLimitMaxInstallAgeDays, 1, 365);
    }

    public bool ShouldAutoApplyVolume(AppRule rule, DateTime toolFirstRunUtc)
    {
        if (rule.ManualOverride) return false;
        if (rule.AutoApplied) return false;
        if (!_autoApplyToAllNewApps && rule.Classification != AppClassification.Game) return false;
        if (rule.FirstSeenUtc <= toolFirstRunUtc) return false;

        return true;
    }

    public GuardDecision EvaluateNewSession(string exePath, string displayName, float currentVolume, bool isBaselineScan = false, IReadOnlyCollection<uint>? processIds = null)
    {
        var decision = new GuardDecision
        {
            DisplayName = displayName ?? string.Empty,
            InputExePath = exePath ?? string.Empty,
            IsBaselineScan = isBaselineScan
        };
        var nowUtc = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(exePath))
        {
            decision.Reason = "Kein EXE-Pfad verfuegbar - keine Guard-Pruefung moeglich";
            return decision;
        }

        var effectiveExePath = ResolveEffectiveExePath(exePath, processIds);
        decision.EffectiveExePath = effectiveExePath;
        var existingRule = _ruleStore.GetRule(exePath);
        if (existingRule == null && !string.Equals(effectiveExePath, exePath, StringComparison.OrdinalIgnoreCase))
        {
            existingRule = _ruleStore.GetRule(effectiveExePath);
        }
        else if (existingRule != null &&
                 !string.IsNullOrWhiteSpace(effectiveExePath) &&
                 !string.Equals(effectiveExePath, exePath, StringComparison.OrdinalIgnoreCase) &&
                 _ruleStore.GetRule(effectiveExePath) == null)
        {
            _ruleStore.UpsertRule(effectiveExePath, existingRule);
        }

        var classification = _classifier.Classify(effectiveExePath, displayName ?? string.Empty);
        decision.Classification = classification;
        decision.ExistingRuleFound = existingRule != null;

        if (existingRule != null)
        {
            decision.RuleAutoApplied = existingRule.AutoApplied;
            decision.RuleManualOverride = existingRule.ManualOverride;

            if (!string.IsNullOrWhiteSpace(exePath) &&
                !string.Equals(exePath, effectiveExePath, StringComparison.OrdinalIgnoreCase) &&
                _ruleStore.GetRule(exePath) == null)
            {
                _ruleStore.UpsertRule(exePath, existingRule);
            }

            existingRule.LastSeenUtc = nowUtc;

            // Classification nur in Richtung "Game" nachschaerfen.
            if (existingRule.Classification != AppClassification.Game && classification == AppClassification.Game)
            {
                existingRule.Classification = AppClassification.Game;
                decision.Classification = AppClassification.Game;
            }

            if (isBaselineScan)
            {
                decision.Reason = $"Baseline-Erfassung fuer {displayName} - keine Automatik";
                return decision;
            }

            // Wurde die Regel waehrend der Baseline-Erfassung erstellt,
            // aber die Session jetzt live (nicht Baseline) entdeckt,
            // aktualisieren wir FirstSeenUtc auf nowUtc, damit die Auto-Apply-Logik greift.
            if (existingRule.FirstSeenUtc <= _ruleStore.ToolFirstRunUtc &&
                !existingRule.AutoApplied &&
                !existingRule.ManualOverride)
            {
                existingRule.FirstSeenUtc = nowUtc;
                existingRule.LastSeenUtc = nowUtc;
            }

            if (ShouldAutoApplyVolume(existingRule, _ruleStore.ToolFirstRunUtc))
            {
                if (IsAutoLimitAgeEligible(effectiveExePath, out var installAgeDays, out var installSignalSource, out var installSignalUtc))
                {
                    decision.InstallAgeDays = installAgeDays;
                    decision.InstallSignalSource = installSignalSource;
                    decision.InstallSignalUtc = installSignalUtc;
                    decision.AutoApplyAllowed = true;
                    decision.TargetVolume = _autoVolume;
                    decision.Reason = $"Neue App erkannt - Auto-Apply auf {_autoVolume * 100:F0}%";
                    return decision;
                }

                // Fallback: Profil-Alter statt Installationsalter (UWP/Xbox/Epic/GOG).
                if (IsProfileAgeWithinLimit(existingRule, out var profileAgeDays))
                {
                    decision.InstallAgeDays = profileAgeDays;
                    decision.InstallSignalSource = "profile_age";
                    decision.InstallSignalUtc = existingRule.FirstSeenUtc;
                    decision.AutoApplyAllowed = true;
                    decision.TargetVolume = _autoVolume;
                    decision.Reason = $"App-Profil {profileAgeDays}d alt - Auto-Apply auf {_autoVolume * 100:F0}%";
                    return decision;
                }

                decision.InstallAgeDays = installAgeDays ?? profileAgeDays;
                decision.InstallSignalSource = installSignalSource;
                decision.InstallSignalUtc = installSignalUtc;
                decision.Reason = installAgeDays.HasValue
                    ? $"Installationsalter {installAgeDays.Value}d > {_autoLimitMaxInstallAgeDays}d - keine Automatik"
                    : $"Profilalter {profileAgeDays}d > {_autoLimitMaxInstallAgeDays}d - keine Automatik";
                return decision;
            }

            // Fallback fuer "frisch installiert, aber in Baseline erfasst":
            // Wird die EXE/Installationsstruktur erst nach Tool-First-Run erkannt,
            // erlauben wir genau dann ebenfalls eine einmalige Auto-Anwendung.
            if (ShouldAutoApplyFromInstallSignal(existingRule, effectiveExePath))
            {
                decision.AutoApplyAllowed = true;
                decision.TargetVolume = _autoVolume;
                decision.Reason = $"Neue Installation erkannt - Auto-Apply auf {_autoVolume * 100:F0}%";
                return decision;
            }

            if (existingRule.ManualOverride)
            {
                decision.Reason = $"Manueller Override fuer {displayName} - keine Automatik";
                return decision;
            }

            if (existingRule.AutoApplied)
            {
                decision.Reason = $"Auto-Apply bereits durchgefuehrt fuer {displayName}";
                return decision;
            }

            decision.Reason = $"Bekannte Anwendung {displayName} - keine Automatik";
            return decision;
        }

        // Neue Anwendung - Regel anlegen.
        var firstSeenUtc = isBaselineScan ? _ruleStore.ToolFirstRunUtc : nowUtc;
        var newRule = new AppRule
        {
            DisplayName = displayName ?? string.Empty,
            ExeName = Path.GetFileName(exePath),
            Classification = classification,
            FirstSeenUtc = firstSeenUtc,
            LastSeenUtc = nowUtc,
            AutoApplied = false,
            AutoVolume = _autoVolume,
            ManualOverride = false,
            Locked = false,
            Favorite = false,
            LastKnownVolume = currentVolume
        };

        _ruleStore.UpsertRule(effectiveExePath, newRule);
        if (!string.IsNullOrWhiteSpace(exePath) &&
            !string.Equals(exePath, effectiveExePath, StringComparison.OrdinalIgnoreCase))
        {
            _ruleStore.UpsertRule(exePath, newRule);
        }

        if (isBaselineScan)
        {
            if (ShouldAutoApplyFromInstallSignal(newRule, effectiveExePath))
            {
                decision.AutoApplyAllowed = true;
                decision.TargetVolume = _autoVolume;
                decision.Reason = $"Neue Installation erkannt - Auto-Apply auf {_autoVolume * 100:F0}%";
                return decision;
            }

            decision.Reason = $"Baseline-Erfassung fuer {displayName} - keine Automatik";
            return decision;
        }

        if (ShouldAutoApplyVolume(newRule, _ruleStore.ToolFirstRunUtc))
        {
            if (IsAutoLimitAgeEligible(effectiveExePath, out var installAgeDays, out var installSignalSource, out var installSignalUtc))
            {
                decision.InstallAgeDays = installAgeDays;
                decision.InstallSignalSource = installSignalSource;
                decision.InstallSignalUtc = installSignalUtc;
                decision.AutoApplyAllowed = true;
                decision.TargetVolume = _autoVolume;
                decision.Reason = $"Neue App erkannt - Auto-Apply auf {_autoVolume * 100:F0}%";
                return decision;
            }

            // Fallback: Profil-Alter statt Installationsdatum (UWP/Xbox/Epic/GOG).
            // Das Profil schuetzt vor Falscherkennung nach Updates:
            // alte Programme behalten ihr urspruengliches FirstSeenUtc.
            if (IsProfileAgeWithinLimit(newRule, out var profileAgeDays))
            {
                decision.InstallAgeDays = profileAgeDays;
                decision.InstallSignalSource = "profile_age";
                decision.InstallSignalUtc = newRule.FirstSeenUtc;
                decision.AutoApplyAllowed = true;
                decision.TargetVolume = _autoVolume;
                decision.Reason = $"App-Profil {profileAgeDays}d alt - Auto-Apply auf {_autoVolume * 100:F0}%";
                return decision;
            }

            decision.InstallAgeDays = installAgeDays ?? profileAgeDays;
            decision.InstallSignalSource = installSignalSource;
            decision.InstallSignalUtc = installSignalUtc;
            decision.Reason = installAgeDays.HasValue
                ? $"Installationsalter {installAgeDays.Value}d > {_autoLimitMaxInstallAgeDays}d - keine Automatik"
                : $"Profilalter {profileAgeDays}d > {_autoLimitMaxInstallAgeDays}d - keine Automatik";
            return decision;
        }

        decision.Reason = $"Neue Nicht-Spiel-Anwendung {displayName} - keine Automatik";
        return decision;
    }

    private bool ShouldAutoApplyFromInstallSignal(AppRule rule, string exePath)
    {
        if (rule.ManualOverride || rule.AutoApplied)
        {
            return false;
        }

        if (!_autoApplyToAllNewApps && rule.Classification != AppClassification.Game)
        {
            return false;
        }

        // Nur fuer Regeln, die initial auf Tool-First-Run standen (Baseline-Fall).
        if (rule.FirstSeenUtc > _ruleStore.ToolFirstRunUtc.AddSeconds(2))
        {
            return false;
        }

        return IsLikelyInstalledAfterFirstRun(exePath, _ruleStore.ToolFirstRunUtc) &&
               IsAutoLimitAgeEligible(exePath, out _);
    }

    private bool IsLikelyInstalledAfterFirstRun(string exePath, DateTime toolFirstRunUtc)
    {
        if (!TryGetInstallSignalUtc(exePath, out var installSignalUtc))
        {
            return false;
        }

        return installSignalUtc > toolFirstRunUtc.AddMinutes(2);
    }

    /// <summary>
    /// Prueft, ob das Profil-Alter innerhalb des Auto-Limits liegt.
    /// Dient als Fallback, wenn kein Installationssignal verfuegbar ist
    /// (UWP/Xbox/Epic/GOG). Das Profil (FirstSeenUtc) schuetzt vor
    /// falscher Erkennung nach Updates: alte Programme behalten ihr
    /// urspruengliches FirstSeenUtc und werden nicht erneut angepasst.
    /// </summary>
    private bool IsProfileAgeWithinLimit(AppRule rule, out int profileAgeDays)
    {
        profileAgeDays = (int)Math.Floor((DateTime.UtcNow - rule.FirstSeenUtc).TotalDays);
        if (profileAgeDays < 0) profileAgeDays = 0;

        return profileAgeDays <= _autoLimitMaxInstallAgeDays;
    }

    private bool IsAutoLimitAgeEligible(string exePath, out int? installAgeDays)
    {
        return IsAutoLimitAgeEligible(exePath, out installAgeDays, out _, out _);
    }

    private bool IsAutoLimitAgeEligible(
        string exePath,
        out int? installAgeDays,
        out string installSignalSource,
        out DateTime? installSignalUtcValue)
    {
        installAgeDays = null;
        installSignalSource = string.Empty;
        installSignalUtcValue = null;
        if (!TryGetInstallSignalUtc(exePath, out var installSignalUtc, out installSignalSource))
        {
            return false;
        }

        installSignalUtcValue = installSignalUtc;
        installAgeDays = (int)Math.Floor((DateTime.UtcNow - installSignalUtc).TotalDays);
        if (installAgeDays.Value < 0)
        {
            installAgeDays = 0;
        }

        return installAgeDays.Value <= _autoLimitMaxInstallAgeDays;
    }

    private bool TryGetInstallSignalUtc(string exePath, out DateTime installSignalUtc)
    {
        return TryGetInstallSignalUtc(exePath, out installSignalUtc, out _);
    }

    private bool TryGetInstallSignalUtc(string exePath, out DateTime installSignalUtc, out string installSignalSource)
    {
        installSignalUtc = DateTime.MinValue;
        installSignalSource = string.Empty;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        if (_installSignalCache.TryGetValue(exePath, out var cached) &&
            (nowUtc - cached.CheckedUtc) <= InstallSignalCacheTtl)
        {
            installSignalUtc = cached.InstallSignalUtc;
            installSignalSource = cached.Source;
            return installSignalUtc != DateTime.MinValue;
        }

        DateTime signalUtc = DateTime.MinValue;
        var signalSource = string.Empty;

        var rootedPath = string.Empty;
        if (Path.IsPathRooted(exePath) && File.Exists(exePath))
        {
            rootedPath = exePath;
        }

        if (!string.IsNullOrWhiteSpace(rootedPath))
        {
            try
            {
                var exeCreatedUtc = File.GetCreationTimeUtc(rootedPath);
                var directory = Path.GetDirectoryName(rootedPath);
                var dirCreatedUtc = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                    ? Directory.GetCreationTimeUtc(directory)
                    : DateTime.MinValue;

                signalUtc = exeCreatedUtc > dirCreatedUtc ? exeCreatedUtc : dirCreatedUtc;
                if (signalUtc != DateTime.MinValue)
                {
                    signalSource = "file_timestamps";
                }
            }
            catch
            {
                signalUtc = DateTime.MinValue;
            }
        }

        // Fallback 1: vorhandene Rule-Historie fuer gleiche EXE-Namen nutzen.
        if (signalUtc == DateTime.MinValue)
        {
            var exeName = NormalizeExeName(exePath);
            if (!string.IsNullOrWhiteSpace(exeName))
            {
                foreach (var knownPath in _ruleStore.Apps.Keys)
                {
                    if (!Path.IsPathRooted(knownPath) || !File.Exists(knownPath))
                    {
                        continue;
                    }

                    if (!string.Equals(NormalizeExeName(knownPath), exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        var candidate = File.GetCreationTimeUtc(knownPath);
                        if (signalUtc == DateTime.MinValue || candidate < signalUtc)
                        {
                            signalUtc = candidate;
                            signalSource = "rule_history";
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        // Fallback 2: Windows Uninstall-Registry
        if (signalUtc == DateTime.MinValue)
        {
            var exeName = NormalizeExeName(exePath);
            if (OperatingSystem.IsWindows() &&
                !string.IsNullOrWhiteSpace(exeName) &&
                TryGetInstallDateFromUninstallRegistry(exeName, rootedPath, out var uninstallInstallUtc))
            {
                signalUtc = uninstallInstallUtc;
                signalSource = "windows_apps_registry";
            }
        }

        // Fallback 3: Steam appmanifest (wichtig fuer viele Steam-Demos/Indie-Spiele)
        if (signalUtc == DateTime.MinValue &&
            OperatingSystem.IsWindows() &&
            !string.IsNullOrWhiteSpace(rootedPath) &&
            TryGetInstallSignalFromSteamManifest(rootedPath, out var steamInstallUtc))
        {
            signalUtc = steamInstallUtc;
            signalSource = "steam_appmanifest";
        }

        _installSignalCache[exePath] = (nowUtc, signalUtc, signalSource);
        installSignalUtc = signalUtc;
        installSignalSource = signalSource;
        return installSignalUtc != DateTime.MinValue;
    }

    private bool TryGetInstallSignalFromSteamManifest(string rootedExePath, out DateTime installSignalUtc)
    {
        installSignalUtc = DateTime.MinValue;
        if (!TryGetSteamLibraryContext(rootedExePath, out var steamAppsDir, out var installDirName))
        {
            return false;
        }

        var installSignals = LoadSteamInstallSignalsByDir(steamAppsDir);
        if (installSignals.Count == 0)
        {
            return false;
        }

        if (!installSignals.TryGetValue(installDirName, out var signalUtc))
        {
            return false;
        }

        installSignalUtc = signalUtc;
        return installSignalUtc != DateTime.MinValue;
    }

    private Dictionary<string, DateTime> LoadSteamInstallSignalsByDir(string steamAppsDir)
    {
        var nowUtc = DateTime.UtcNow;
        if (_steamInstallSignalCache.TryGetValue(steamAppsDir, out var cached) &&
            (nowUtc - cached.CheckedUtc) <= SteamInstallSignalCacheTtl)
        {
            return cached.InstallSignalsUtcByDir;
        }

        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(steamAppsDir))
            {
                foreach (var manifestPath in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                {
                    if (!TryParseSteamManifestInstallSignal(manifestPath, out var installDirName, out var installSignalUtc))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(installDirName))
                    {
                        continue;
                    }

                    if (!map.TryGetValue(installDirName, out var existing) || installSignalUtc > existing)
                    {
                        map[installDirName] = installSignalUtc;
                    }
                }
            }
        }
        catch
        {
            // ignore IO/permissions and keep empty map
        }

        _steamInstallSignalCache[steamAppsDir] = (nowUtc, map);
        return map;
    }

    private static bool TryGetSteamLibraryContext(string rootedExePath, out string steamAppsDir, out string installDirName)
    {
        steamAppsDir = string.Empty;
        installDirName = string.Empty;

        if (string.IsNullOrWhiteSpace(rootedExePath) || !Path.IsPathRooted(rootedExePath))
        {
            return false;
        }

        var exeDirPath = Path.GetDirectoryName(rootedExePath);
        if (string.IsNullOrWhiteSpace(exeDirPath))
        {
            return false;
        }

        DirectoryInfo? cursor;
        try
        {
            cursor = new DirectoryInfo(exeDirPath);
        }
        catch
        {
            return false;
        }

        while (cursor != null)
        {
            var parent = cursor.Parent;
            if (string.Equals(cursor.Name, "common", StringComparison.OrdinalIgnoreCase) &&
                parent != null &&
                string.Equals(parent.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(cursor.FullName, exeDirPath);
                if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return false;
                }

                var segments = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    return false;
                }

                steamAppsDir = parent.FullName;
                installDirName = segments[0];
                return true;
            }

            cursor = parent;
        }

        return false;
    }

    private static bool TryParseSteamManifestInstallSignal(string manifestPath, out string installDirName, out DateTime installSignalUtc)
    {
        installDirName = string.Empty;
        installSignalUtc = DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(manifestPath);
            var rawInstallDir = ReadSteamVdfValue(text, "installdir");
            if (string.IsNullOrWhiteSpace(rawInstallDir))
            {
                return false;
            }

            installDirName = UnescapeSteamValue(rawInstallDir).Trim();
            if (string.IsNullOrWhiteSpace(installDirName))
            {
                return false;
            }

            var rawLastUpdated = ReadSteamVdfValue(text, "LastUpdated");
            if (long.TryParse(rawLastUpdated, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds) &&
                unixSeconds > 0)
            {
                installSignalUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            }
            else
            {
                installSignalUtc = File.GetLastWriteTimeUtc(manifestPath);
            }

            return installSignalUtc != DateTime.MinValue;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadSteamVdfValue(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            text,
            $"\"{Regex.Escape(key)}\"\\s*\"([^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string UnescapeSteamValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string ResolveEffectiveExePath(string exePath, IReadOnlyCollection<uint>? processIds)
    {
        if (!string.IsNullOrWhiteSpace(exePath) && Path.IsPathRooted(exePath) && File.Exists(exePath))
        {
            return exePath;
        }

        if (!OperatingSystem.IsWindows() || processIds == null || processIds.Count == 0)
        {
            return exePath;
        }

        foreach (var pid in processIds)
        {
            if (pid == 0)
            {
                continue;
            }

            if (TryQueryFullProcessImagePath(pid, out var resolvedPath) &&
                Path.IsPathRooted(resolvedPath) &&
                File.Exists(resolvedPath))
            {
                return resolvedPath;
            }
        }

        return exePath;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryQueryFullProcessImagePath(uint pid, out string fullPath)
    {
        fullPath = string.Empty;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            if (!QueryFullProcessImageName(handle, 0, buffer, ref capacity))
            {
                return false;
            }

            var path = buffer.ToString();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            fullPath = path.Trim();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static string NormalizeExeName(string value)
    {
        var fileName = Path.GetFileName((value ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.exe";
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetInstallDateFromUninstallRegistry(string exeName, string rootedExePath, out DateTime installUtc)
    {
        installUtc = DateTime.MinValue;
        var normalizedExe = NormalizeExeName(exeName);
        var normalizedRootedExePath = NormalizePath(rootedExePath);
        if (string.IsNullOrWhiteSpace(normalizedExe))
        {
            return false;
        }

        var candidates = new List<DateTime>();

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallKey == null)
                    {
                        continue;
                    }

                    foreach (var subName in uninstallKey.GetSubKeyNames())
                    {
                        using var appKey = uninstallKey.OpenSubKey(subName);
                        if (appKey == null)
                        {
                            continue;
                        }

                        var displayIcon = NormalizeDisplayIconPath((appKey.GetValue("DisplayIcon") as string ?? string.Empty).Trim());
                        var installLocation = NormalizePath((appKey.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"'));
                        var displayName = (appKey.GetValue("DisplayName") as string ?? string.Empty).Trim();
                        var installDateRaw = (appKey.GetValue("InstallDate") as string ?? string.Empty).Trim();

                        var iconName = NormalizeExeName(displayIcon);
                        var locationContainsExe = !string.IsNullOrWhiteSpace(installLocation) &&
                                                  installLocation.IndexOf(Path.GetFileNameWithoutExtension(normalizedExe), StringComparison.OrdinalIgnoreCase) >= 0;
                        var displayMentionsExe = !string.IsNullOrWhiteSpace(displayName) &&
                                                 displayName.IndexOf(Path.GetFileNameWithoutExtension(normalizedExe), StringComparison.OrdinalIgnoreCase) >= 0;
                        var iconPathMatchesExe = !string.IsNullOrWhiteSpace(displayIcon) &&
                                                 !string.IsNullOrWhiteSpace(normalizedRootedExePath) &&
                                                 string.Equals(NormalizePath(displayIcon), normalizedRootedExePath, StringComparison.OrdinalIgnoreCase);
                        var installLocationContainsExePath = !string.IsNullOrWhiteSpace(installLocation) &&
                                                             !string.IsNullOrWhiteSpace(normalizedRootedExePath) &&
                                                             normalizedRootedExePath.StartsWith(AppendDirectorySeparator(installLocation), StringComparison.OrdinalIgnoreCase);

                        var match = string.Equals(iconName, normalizedExe, StringComparison.OrdinalIgnoreCase) ||
                                    locationContainsExe ||
                                    displayMentionsExe ||
                                    iconPathMatchesExe ||
                                    installLocationContainsExePath;
                        if (!match)
                        {
                            continue;
                        }

                        if (TryParseUninstallDate(installDateRaw, out var parsed))
                        {
                            candidates.Add(parsed);
                        }
                    }
                }
                catch
                {
                    // ignore registry access/view errors
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        installUtc = candidates.Min();
        return true;
    }

    private static string NormalizeDisplayIconPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim().Trim('"');
        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex > 1 && trimmed.IndexOf(':') >= 1)
        {
            trimmed = trimmed[..commaIndex];
        }

        return trimmed.Trim().Trim('"');
    }

    private static string NormalizePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(raw.Trim().Trim('"'));
        }
        catch
        {
            return raw.Trim().Trim('"');
        }
    }

    private static string AppendDirectorySeparator(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return string.Empty;
        }

        return directoryPath.EndsWith(Path.DirectorySeparatorChar) || directoryPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? directoryPath
            : directoryPath + Path.DirectorySeparatorChar;
    }

    private static bool TryParseUninstallDate(string value, out DateTime parsedUtc)
    {
        parsedUtc = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ymd))
        {
            parsedUtc = ymd.ToUniversalTime();
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var generic))
        {
            parsedUtc = generic.ToUniversalTime();
            return true;
        }

        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, uint processId);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder text, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
