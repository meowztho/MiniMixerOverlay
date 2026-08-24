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
    private float _autoVolume = GuardDefaults.AutoVolumePercent / 100f;
    private bool _autoApplyToAllNewApps = true;
    private readonly Dictionary<string, (DateTime CheckedUtc, DateTime PresenceSignalUtc, string Source)> _presenceSignalCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime CheckedUtc, Dictionary<string, DateTime> PresenceSignalsUtcByDir)> _steamPresenceSignalCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PresenceSignalCacheTtl = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan MissingPresenceSignalRetryTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SteamPresenceSignalCacheTtl = TimeSpan.FromMinutes(10);
    private const int ProcessQueryLimitedInformation = 0x1000;

    public GuardEngine(
        IRuleStore ruleStore,
        ISessionClassifier classifier,
        int autoVolumePercent = GuardDefaults.AutoVolumePercent,
        bool autoApplyToAllNewApps = true)
    {
        _ruleStore = ruleStore;
        _classifier = classifier;
        Configure(autoVolumePercent, autoApplyToAllNewApps);
    }

    public void Configure(int autoVolumePercent, bool autoApplyToAllNewApps)
    {
        _autoVolume = Math.Clamp(autoVolumePercent, 1, 100) / 100f;
        _autoApplyToAllNewApps = autoApplyToAllNewApps;
    }

    public bool ShouldAutoApplyVolume(AppRule rule, DateTime toolFirstRunUtc)
    {
        if (rule.BaselineAtFirstRun) return false;
        if (rule.DiscoveryStatus != AppDiscoveryStatus.NewEligible) return false;
        if (!_autoApplyToAllNewApps && rule.Classification != AppClassification.Game) return false;
        return true;
    }

    public GuardDecision EvaluateNewSession(
        string appIdentityKey,
        string exePath,
        string displayName,
        float currentVolume,
        string outputDeviceId,
        string outputDeviceName,
        bool isBaselineScan = false,
        IReadOnlyCollection<uint>? processIds = null)
    {
        var nowUtc = DateTime.UtcNow;
        var effectiveExePath = ResolveEffectiveExePath(exePath, processIds);
        var identityKey = string.IsNullOrWhiteSpace(appIdentityKey)
            ? AppIdentity.CreateKey(effectiveExePath, Path.GetFileName(effectiveExePath), displayName)
            : appIdentityKey;
        var deviceId = OutputDeviceIdentity.Normalize(outputDeviceId);

        var decision = new GuardDecision
        {
            AppIdentityKey = identityKey,
            DisplayName = displayName ?? string.Empty,
            InputExePath = exePath ?? string.Empty,
            EffectiveExePath = effectiveExePath,
            IsBaselineScan = isBaselineScan,
            OutputDeviceId = deviceId,
            OutputDeviceName = outputDeviceName ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(identityKey))
        {
            decision.Reason = "Keine stabile App-Identitaet verfuegbar - keine Guard-Pruefung moeglich";
            return decision;
        }

        var classification = _classifier.Classify(effectiveExePath, displayName ?? string.Empty);
        decision.Classification = classification;

        var rule = _ruleStore.ResolveRule(
            identityKey,
            effectiveExePath,
            Path.GetFileName(effectiveExePath),
            displayName ?? string.Empty);

        var ruleAlreadyExisted = rule != null;
        decision.ExistingRuleFound = ruleAlreadyExisted;

        if (rule == null)
        {
            rule = new AppRule
            {
                AppIdentityKey = identityKey,
                LastKnownExePath = effectiveExePath,
                BaselineAtFirstRun = isBaselineScan,
                DisplayName = displayName ?? string.Empty,
                ExeName = Path.GetFileName(effectiveExePath),
                Classification = classification,
                FirstSeenUtc = isBaselineScan ? _ruleStore.ToolFirstRunUtc : nowUtc,
                LastSeenUtc = nowUtc,
                DiscoveryStatus = isBaselineScan ? AppDiscoveryStatus.Baseline : AppDiscoveryStatus.Unknown,
                AutoApplied = false,
                AutoVolume = _autoVolume,
                ManualOverride = false,
                Locked = false,
                Favorite = false,
                LastKnownVolume = currentVolume
            };
        }
        else
        {
            rule.AppIdentityKey = identityKey;
            rule.LastKnownExePath = effectiveExePath;
            rule.LastSeenUtc = nowUtc;
            rule.DeviceProfiles ??= new Dictionary<string, DeviceVolumeProfile>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(rule.ExeName))
            {
                rule.ExeName = Path.GetFileName(effectiveExePath);
            }

            if (string.IsNullOrWhiteSpace(rule.DisplayName))
            {
                rule.DisplayName = displayName ?? string.Empty;
            }

            // Classification is only strengthened towards Game.
            if (rule.Classification != AppClassification.Game && classification == AppClassification.Game)
            {
                rule.Classification = AppClassification.Game;
            }

            if (rule.BaselineAtFirstRun)
            {
                rule.DiscoveryStatus = AppDiscoveryStatus.Baseline;
            }
            else if (rule.DiscoveryStatus == AppDiscoveryStatus.Unknown && rule.AutoApplied)
            {
                // A legacy AutoApplied rule necessarily passed the old install-newness guard.
                rule.DiscoveryStatus = AppDiscoveryStatus.NewEligible;
            }
        }

        decision.Classification = rule.Classification;
        rule.DeviceProfiles ??= new Dictionary<string, DeviceVolumeProfile>(StringComparer.OrdinalIgnoreCase);

        // The initial baseline is a property of the logical application, not of an audio device.
        if (isBaselineScan && rule.DiscoveryStatus == AppDiscoveryStatus.Unknown)
        {
            rule.BaselineAtFirstRun = true;
            rule.DiscoveryStatus = AppDiscoveryStatus.Baseline;
        }

        // Resolve application newness once, but keep Unknown retryable. This fixes sessions
        // whose process/path metadata is incomplete during the first audio callback.
        if (rule.DiscoveryStatus == AppDiscoveryStatus.Unknown)
        {
            ResolveDiscoveryStatus(rule, effectiveExePath, nowUtc, decision);
        }
        else
        {
            decision.PresenceSignalUtc = rule.InstallSignalUtc;
            decision.PresenceSignalSource = rule.InstallSignalSource;
        }

        decision.DiscoveryStatus = rule.DiscoveryStatus;

        var hadAnyDeviceProfiles = rule.DeviceProfiles.Count > 0;
        var profileExisted = rule.DeviceProfiles.TryGetValue(deviceId, out var profile);
        decision.DeviceProfileExisted = profileExisted;

        if (profile == null)
        {
            profile = new DeviceVolumeProfile
            {
                DeviceId = deviceId,
                DeviceName = outputDeviceName ?? string.Empty,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
                GuardEvaluated = false,
                AutoApplied = false,
                AutoVolume = _autoVolume,
                ManualOverride = false,
                LastKnownVolume = currentVolume
            };

            // Lossless one-time migration from the pre-device-profile model. The old file
            // cannot tell which endpoint owned its volume, so the first endpoint observed
            // after upgrade adopts the legacy state. Later endpoints get independent profiles.
            if (!hadAnyDeviceProfiles && (rule.ManualOverride || rule.AutoApplied))
            {
                profile.ManualOverride = rule.ManualOverride;
                profile.AutoApplied = rule.AutoApplied;
                profile.GuardEvaluated = true;
                profile.AutoVolume = rule.AutoVolume > 0f ? rule.AutoVolume : _autoVolume;
                profile.LastKnownVolume = Math.Clamp(rule.LastKnownVolume, 0f, 1f);

                decision.ApplyVolume = true;
                decision.ProfileRestore = true;
                decision.TargetVolume = profile.LastKnownVolume;
                decision.Reason = $"Legacy-Geraeteprofil fuer {displayName} auf {profile.LastKnownVolume * 100:F0}% uebernommen";
            }

            rule.DeviceProfiles[deviceId] = profile;
        }
        else
        {
            profile.DeviceId = deviceId;
            if (!string.IsNullOrWhiteSpace(outputDeviceName))
            {
                profile.DeviceName = outputDeviceName;
            }
            profile.LastSeenUtc = nowUtc;
        }

        decision.RuleAutoApplied = rule.AutoApplied;
        decision.RuleManualOverride = rule.ManualOverride;
        decision.DeviceProfileAutoApplied = profile.AutoApplied;
        decision.DeviceProfileManualOverride = profile.ManualOverride;

        // Manual intent on this endpoint always wins.
        if (profile.ManualOverride)
        {
            profile.GuardEvaluated = true;
            if (!decision.ApplyVolume)
            {
                decision.Reason = $"Manueller Geraete-Override fuer {displayName} - keine Automatik";
            }
            PersistRule(identityKey, rule);
            return decision;
        }

        // Existing endpoint profiles are initialized once. Their remembered volume is restored
        // by MixerController when a fresh Windows audio session appears.
        if (profile.GuardEvaluated)
        {
            if (!decision.ApplyVolume)
            {
                decision.Reason = profile.AutoApplied
                    ? $"Geraeteprofil fuer {displayName} bereits initialisiert"
                    : $"Bekanntes Geraeteprofil fuer {displayName}";
            }
            PersistRule(identityKey, rule);
            return decision;
        }

        if (rule.DiscoveryStatus == AppDiscoveryStatus.Unknown)
        {
            // Important: do NOT finalize this profile. A later refresh may have a real exe path
            // or install signal and should get another chance to apply the new-app limit.
            decision.Reason = $"Praesenzstatus fuer {displayName} noch ungeklaert - Guard wird erneut pruefen";
            PersistRule(identityKey, rule);
            return decision;
        }

        if (rule.DiscoveryStatus is AppDiscoveryStatus.Baseline or AppDiscoveryStatus.PresentBeforeFirstRun)
        {
            profile.GuardEvaluated = true;
            profile.LastKnownVolume = currentVolume;
            decision.Reason = rule.DiscoveryStatus switch
            {
                AppDiscoveryStatus.Baseline => $"Baseline-Anwendung {displayName} - keine Automatik",
                _ => $"Bereits vor dem ersten MiniMixer-Start vorhanden - keine Automatik fuer {displayName}"
            };
            PersistRule(identityKey, rule);
            return decision;
        }

        if (!_autoApplyToAllNewApps && rule.Classification != AppClassification.Game)
        {
            profile.GuardEvaluated = true;
            profile.LastKnownVolume = currentVolume;
            decision.Reason = $"Neue Nicht-Spiel-Anwendung {displayName} - keine Automatik";
            PersistRule(identityKey, rule);
            return decision;
        }

        // NewEligible is global to the logical application, while AutoApplied is per device.
        // Every output endpoint gets its own one-time initialization and then keeps its own value.
        profile.GuardEvaluated = true;
        profile.AutoApplied = true;
        profile.AutoVolume = _autoVolume;
        profile.LastKnownVolume = _autoVolume;

        rule.AutoApplied = true; // legacy compatibility / status display only
        rule.AutoVolume = _autoVolume;
        rule.LastKnownVolume = _autoVolume;

        decision.AutoApplyAllowed = true;
        decision.ApplyVolume = true;
        decision.TargetVolume = _autoVolume;
        decision.DeviceProfileAutoApplied = true;
        decision.Reason = $"Neue App auf neuem Ausgabegeraet erkannt - Auto-Apply auf {_autoVolume * 100:F0}%";
        PersistRule(identityKey, rule);
        return decision;
    }

    private void ResolveDiscoveryStatus(AppRule rule, string effectiveExePath, DateTime nowUtc, GuardDecision decision)
    {
        if (rule.BaselineAtFirstRun)
        {
            rule.DiscoveryStatus = AppDiscoveryStatus.Baseline;
            return;
        }

        if (!TryGetPresenceSignalUtc(effectiveExePath, rule.DisplayName, out var presenceSignalUtc, out var presenceSignalSource))
        {
            // Keep Unknown. Persisting the rule is fine; Unknown is intentionally retryable.
            return;
        }

        var presenceAgeDays = (int)Math.Floor((nowUtc - presenceSignalUtc).TotalDays);
        if (presenceAgeDays < 0) presenceAgeDays = 0;

        // Legacy property names are intentionally retained in AppRule for rules.json compatibility.
        // From v11 onward they hold the earliest reliable local presence/arrival evidence.
        rule.InstallSignalUtc = presenceSignalUtc;
        rule.InstallSignalSource = presenceSignalSource;
        decision.PresenceAgeDays = presenceAgeDays;
        decision.PresenceSignalSource = presenceSignalSource;
        decision.PresenceSignalUtc = presenceSignalUtc;

        if (presenceSignalUtc <= _ruleStore.ToolFirstRunUtc.AddMinutes(2))
        {
            rule.DiscoveryStatus = AppDiscoveryStatus.PresentBeforeFirstRun;
            return;
        }

        // No arbitrary age window. "New" means the earliest reliable evidence says the
        // application appeared on this Windows installation after MiniMixer's first run.
        // This works for normal installers, Steam and portable/ZIP applications alike.
        rule.DiscoveryStatus = AppDiscoveryStatus.NewEligible;
    }

    private void PersistRule(string identityKey, AppRule rule)
    {
        rule.LastSeenUtc = DateTime.UtcNow;
        _ruleStore.UpsertRule(identityKey, rule);
    }

    /// <summary>
    /// Returns the earliest credible evidence that the application was present on this Windows
    /// installation. This deliberately models presence/arrival, not "installer execution".
    /// The earliest evidence wins so an updater cannot make an older application look new.
    /// </summary>
    private bool TryGetPresenceSignalUtc(string exePath, string displayNameHint, out DateTime presenceSignalUtc, out string presenceSignalSource)
    {
        presenceSignalUtc = DateTime.MinValue;
        presenceSignalSource = string.Empty;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var cacheKey = $"{exePath}\u001f{NormalizeTextKey(displayNameHint)}";
        if (_presenceSignalCache.TryGetValue(cacheKey, out var cached))
        {
            var age = nowUtc - cached.CheckedUtc;
            if (cached.PresenceSignalUtc != DateTime.MinValue && age <= PresenceSignalCacheTtl)
            {
                presenceSignalUtc = cached.PresenceSignalUtc;
                presenceSignalSource = cached.Source;
                return true;
            }

            if (cached.PresenceSignalUtc == DateTime.MinValue && age <= MissingPresenceSignalRetryTtl)
            {
                return false;
            }
        }

        var rootedPath = Path.IsPathRooted(exePath) && File.Exists(exePath)
            ? exePath
            : string.Empty;

        var candidates = new List<(DateTime Utc, string Source)>();

        static void AddCandidate(List<(DateTime Utc, string Source)> list, DateTime utc, string source, DateTime now)
        {
            // Ignore invalid/sentinel values and implausible future timestamps.
            if (utc == DateTime.MinValue || utc == DateTime.MaxValue || utc > now.AddDays(1))
            {
                return;
            }

            list.Add((DateTime.SpecifyKind(utc, DateTimeKind.Utc), source));
        }

        if (OperatingSystem.IsWindows())
        {
            var exeName = NormalizeExeName(exePath);

            // Installed applications: registry evidence is useful even when the executable was
            // replaced by an update after MiniMixer's first run.
            if (!string.IsNullOrWhiteSpace(exeName) &&
                TryGetInstallDateFromUninstallRegistry(exeName, rootedPath, displayNameHint, out var registryUtc))
            {
                AddCandidate(candidates, registryUtc, "windows_apps_registry", nowUtc);
            }

            // Embedded/shared audio runtimes can expose a helper executable while the session
            // display name belongs to the actual host application. Keep that source explicit in
            // diagnostics, but it participates in the same earliest-presence rule.
            if (IsDisplayNameDifferentFromExecutable(displayNameHint, rootedPath) &&
                TryGetInstallDateFromUninstallRegistry(exeName, rootedPath, displayNameHint, out var displayRegistryUtc))
            {
                AddCandidate(candidates, displayRegistryUtc, "windows_apps_registry_display", nowUtc);
            }

            // Steam's appmanifest CreationTime is a local-presence signal. LastUpdated is
            // intentionally not used because game updates must not turn an old game into a new app.
            if (!string.IsNullOrWhiteSpace(rootedPath) &&
                TryGetInstallSignalFromSteamManifest(rootedPath, out var steamUtc))
            {
                AddCandidate(candidates, steamUtc, "steam_appmanifest_creation", nowUtc);
            }
        }

        // Portable/ZIP fallback. The executable's NTFS CreationTime is the best generally
        // available signal for when that concrete file arrived on this Windows installation.
        // Do NOT use an arbitrary parent folder: D:\Portable may be years old while a new EXE
        // was copied there today. A dedicated app folder is used only when its name strongly
        // resembles the app; conflicting old/new weak evidence is then treated as unresolved.
        if (!string.IsNullOrWhiteSpace(rootedPath) &&
            TryGetPortablePresenceSignal(rootedPath, displayNameHint, nowUtc, out var portableUtc, out var portableSource))
        {
            AddCandidate(candidates, portableUtc, portableSource, nowUtc);
        }

        if (candidates.Count == 0)
        {
            _presenceSignalCache[cacheKey] = (nowUtc, DateTime.MinValue, string.Empty);
            return false;
        }

        // Earliest credible evidence is authoritative for "already present". This is a
        // conservative safety rule: later updater/file timestamps can never make an older app new.
        var selected = candidates
            .OrderBy(c => c.Utc)
            .ThenBy(c => c.Source, StringComparer.Ordinal)
            .First();

        presenceSignalUtc = selected.Utc;
        presenceSignalSource = selected.Source;
        _presenceSignalCache[cacheKey] = (nowUtc, presenceSignalUtc, presenceSignalSource);
        return true;
    }

    private bool TryGetPortablePresenceSignal(
        string rootedExePath,
        string displayNameHint,
        DateTime nowUtc,
        out DateTime presenceSignalUtc,
        out string presenceSignalSource)
    {
        presenceSignalUtc = DateTime.MinValue;
        presenceSignalSource = string.Empty;

        try
        {
            var exeCreatedUtc = File.GetCreationTimeUtc(rootedExePath);
            if (exeCreatedUtc == DateTime.MinValue || exeCreatedUtc > nowUtc.AddDays(1))
            {
                return false;
            }

            if (TryFindDedicatedAppDirectory(rootedExePath, displayNameHint, out var dedicatedDirectory))
            {
                var dirCreatedUtc = Directory.GetCreationTimeUtc(dedicatedDirectory);
                if (dirCreatedUtc != DateTime.MinValue && dirCreatedUtc <= nowUtc.AddDays(1))
                {
                    // Earliest local evidence wins. In particular, an old stable app root
                    // prevents a newly replaced executable in a version/bin subfolder from
                    // making the app look new. Generic collection folders are ignored.
                    presenceSignalUtc = exeCreatedUtc <= dirCreatedUtc ? exeCreatedUtc : dirCreatedUtc;
                    presenceSignalSource = "portable_exe_and_appdir_creation";
                    return true;
                }
            }

            // Generic collection folders (e.g. D:\Portable) are intentionally ignored.
            presenceSignalUtc = exeCreatedUtc;
            presenceSignalSource = "portable_exe_creation";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindDedicatedAppDirectory(string rootedExePath, string displayNameHint, out string directoryPath)
    {
        directoryPath = string.Empty;
        var parent = Path.GetDirectoryName(rootedExePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        DirectoryInfo? cursor;
        try
        {
            cursor = new DirectoryInfo(parent);
        }
        catch
        {
            return false;
        }

        // Look through a few local ancestors so version/bin layouts such as
        // App\app-1.2.3\App.exe still find the stable App directory.
        for (var depth = 0; cursor != null && depth < 4; depth++, cursor = cursor.Parent)
        {
            if (!cursor.Exists)
            {
                continue;
            }

            if (IsLikelyDedicatedAppDirectory(cursor.FullName, rootedExePath, displayNameHint))
            {
                directoryPath = cursor.FullName;
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyDedicatedAppDirectory(string directoryPath, string rootedExePath, string displayNameHint)
    {
        var directoryName = NormalizeTextKey(Path.GetFileName(directoryPath));
        var exeName = NormalizeTextKey(Path.GetFileNameWithoutExtension(rootedExePath));
        var displayName = NormalizeTextKey(displayNameHint);

        if (directoryName.Length < 3 || IsGenericAppSubdirectory(directoryName))
        {
            return false;
        }

        // Display name is usually more specific than helper executable names such as app.exe.
        if (displayName.Length >= 4 &&
            (directoryName.Contains(displayName, StringComparison.Ordinal) || displayName.Contains(directoryName, StringComparison.Ordinal)))
        {
            return true;
        }

        return exeName.Length >= 4 && !IsGenericAppSubdirectory(exeName) &&
               (directoryName.Contains(exeName, StringComparison.Ordinal) || exeName.Contains(directoryName, StringComparison.Ordinal));
    }

    private static bool IsGenericAppSubdirectory(string normalizedName)
    {
        if (normalizedName is "app" or "bin" or "current" or "release" or "releases" or
            "x64" or "x86" or "win64" or "win32" or "program" or "programs")
        {
            return true;
        }

        return Regex.IsMatch(normalizedName, @"^(?:app|version|ver|v)\d", RegexOptions.IgnoreCase);
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
        if (_steamPresenceSignalCache.TryGetValue(steamAppsDir, out var cached) &&
            (nowUtc - cached.CheckedUtc) <= SteamPresenceSignalCacheTtl)
        {
            return cached.PresenceSignalsUtcByDir;
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

                    if (!map.TryGetValue(installDirName, out var existing) || installSignalUtc < existing)
                    {
                        // Presence semantics use the earliest local manifest evidence.
                        map[installDirName] = installSignalUtc;
                    }
                }
            }
        }
        catch
        {
            // ignore IO/permissions and keep empty map
        }

        _steamPresenceSignalCache[steamAppsDir] = (nowUtc, map);
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

            // Steam's LastUpdated changes on every update and is therefore not an
            // installation timestamp. Prefer the manifest creation time, which is the
            // conservative signal for when this local installation first appeared.
            installSignalUtc = File.GetCreationTimeUtc(manifestPath);
            if (installSignalUtc == DateTime.MinValue)
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
    private static bool TryGetInstallDateFromUninstallRegistry(string exeName, string rootedExePath, string displayNameHint, out DateTime installUtc)
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
                        var displayHintMatches = StrongDisplayNameMatch(displayName, displayNameHint);

                        var match = string.Equals(iconName, normalizedExe, StringComparison.OrdinalIgnoreCase) ||
                                    locationContainsExe ||
                                    displayMentionsExe ||
                                    iconPathMatchesExe ||
                                    installLocationContainsExePath ||
                                    displayHintMatches;
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

    private static bool IsDisplayNameDifferentFromExecutable(string displayName, string rootedExePath)
    {
        var displayKey = NormalizeTextKey(displayName);
        var exeKey = NormalizeTextKey(Path.GetFileNameWithoutExtension(rootedExePath ?? string.Empty));
        if (displayKey.Length < 4 || exeKey.Length < 2)
        {
            return false;
        }

        return !displayKey.Contains(exeKey, StringComparison.Ordinal) &&
               !exeKey.Contains(displayKey, StringComparison.Ordinal);
    }

    private static bool StrongDisplayNameMatch(string registryDisplayName, string sessionDisplayName)
    {
        var registryKey = NormalizeTextKey(registryDisplayName);
        var sessionKey = NormalizeTextKey(sessionDisplayName);
        if (registryKey.Length < 4 || sessionKey.Length < 4)
        {
            return false;
        }

        if (string.Equals(registryKey, sessionKey, StringComparison.Ordinal))
        {
            return true;
        }

        // Permit a version/edition suffix only for sufficiently specific names.
        if (sessionKey.Length >= 8 && registryKey.StartsWith(sessionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (registryKey.Length >= 8 && sessionKey.StartsWith(registryKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeTextKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
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
