using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;
using MiniMixerOverlay.Infrastructure.Persistence;

internal static class RuntimeAuditFixRegression
{
    internal static void Run()
    {
        var owned = Path.Combine(Path.GetTempPath(), "mm1-runtime-fixes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        try
        {
            SettingsNullSections(owned);
            UnchangedJsonSaveIsReadOnly(owned);
            FailedAutoWriteAndRecovery(owned);
        }
        finally
        {
            Directory.Delete(owned, recursive: true); // GUID-created, local, contains no links
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Runtime audit regression: " + message);
    }

    private static void SettingsNullSections(string root)
    {
        foreach (var section in new[] { "ui", "window", "gameHook", "system" })
        {
            var path = Path.Combine(root, "settings-" + section + ".json");
            var json = "{\"ui\":{\"visibleApps\":7},\"window\":{\"width\":410},\"gameHook\":{},\"system\":{},\"" + section + "\":null}";
            File.WriteAllText(path, json);
            var original = File.ReadAllBytes(path);
            var store = new JsonSettingsStore(path);
            store.Load();
            Check(store.Settings.Ui != null && store.Settings.Window != null &&
                  store.Settings.GameHook != null && store.Settings.System != null,
                  "Explicit null section published: " + section);
            if (section != "ui") Check(store.Settings.Ui.VisibleApps == 7, "Valid Ui sibling lost");
            if (section != "window") Check(Math.Abs(store.Settings.Window.Width - 410) < 0.1, "Valid Window sibling lost");
            Check(File.ReadAllBytes(path).SequenceEqual(original), "Damaged source overwritten on Load");
            var hash = Convert.ToHexString(SHA256.HashData(original))[..16];
            var backup = path + ".corrupt." + hash + ".bak";
            Check(File.Exists(backup) && File.ReadAllBytes(backup).SequenceEqual(original),
                  "Missing exact-byte backup of null section");
            store.Save();
            var reloaded = new JsonSettingsStore(path);
            reloaded.Load();
            Check(reloaded.Settings.Ui != null && reloaded.Settings.Window != null &&
                  reloaded.Settings.GameHook != null && reloaded.Settings.System != null,
                  "Saved repaired settings cannot round trip");
        }

        var missing = Path.Combine(root, "settings-missing.json");
        File.WriteAllText(missing, "{}");
        var absent = new JsonSettingsStore(missing);
        absent.Load();
        Check(absent.Settings.Ui != null && absent.Settings.Window != null &&
              !Directory.GetFiles(root, "settings-missing.json.corrupt.*.bak").Any(),
              "Omitted valid sections must retain constructor defaults without backup");

        // A backup obstacle must not authorize an overwrite of damaged originals.
        var blocked = Path.Combine(root, "settings-blocked.json");
        File.WriteAllText(blocked, "{\"ui\":null}");
        var originalBlocked = File.ReadAllBytes(blocked);
        var digest = Convert.ToHexString(SHA256.HashData(originalBlocked))[..16];
        Directory.CreateDirectory(blocked + ".corrupt." + digest + ".bak");
        var prevented = new JsonSettingsStore(blocked);
        prevented.Load();
        Check(prevented.Settings.Ui != null, "Failed backup leaked null into consumer");
        prevented.Save();
        Check(File.ReadAllBytes(blocked).SequenceEqual(originalBlocked), "Backup failure did not block destructive Save");
    }

    private static void UnchangedJsonSaveIsReadOnly(string root)
    {
        var oldTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var rulesPath = Path.Combine(root, "no-op-rules.json");
        var rules = new JsonRuleStore(rulesPath);
        rules.Load(); // initialize a missing file through the canonical store
        var rulesBytes = File.ReadAllBytes(rulesPath);
        File.SetLastWriteTimeUtc(rulesPath, oldTime);
        rules.Save();
        Check(File.GetLastWriteTimeUtc(rulesPath) == oldTime &&
              File.ReadAllBytes(rulesPath).SequenceEqual(rulesBytes) &&
              !File.Exists(rulesPath + ".tmp"),
              "Unchanged rules Save rewrote the file");

        File.WriteAllText(rulesPath, "{ \"apps\": {} }"); // valid external edit
        rules.Save();
        Check(File.ReadAllBytes(rulesPath).SequenceEqual(rulesBytes),
              "Changed external rules were ignored");
        File.Delete(rulesPath);
        rules.Save();
        Check(File.Exists(rulesPath) && File.ReadAllBytes(rulesPath).SequenceEqual(rulesBytes),
              "Deleted rules file was not recreated");

        var settingsPath = Path.Combine(root, "no-op-settings.json");
        var settings = new JsonSettingsStore(settingsPath);
        settings.Load();
        var settingsBytes = File.ReadAllBytes(settingsPath);
        File.SetLastWriteTimeUtc(settingsPath, oldTime);
        settings.Save();
        Check(File.GetLastWriteTimeUtc(settingsPath) == oldTime &&
              File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes) &&
              !File.Exists(settingsPath + ".tmp"),
              "Unchanged settings Save rewrote the file");

        File.WriteAllText(settingsPath, "{}"); // external update is not our own save
        settings.Save();
        Check(File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes),
              "Changed external settings were ignored");
        File.Delete(settingsPath);
        settings.Save();
        Check(File.Exists(settingsPath) && File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes),
              "Deleted settings file was not recreated");
    }

    private sealed class ControlledAudio : IAudioSessionManager
    {
        public List<AudioSessionInfo> Sessions { get; } = new();
        public HashSet<string> FailedIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissingIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Attempts { get; private set; }
        public int Successes { get; private set; }
        public List<AudioSessionInfo> EnumerateSessions() => Sessions.ToList();
        public bool TrySetVolume(string id, float volume)
        {
            Attempts++;
            if (FailedIds.Contains(id) || MissingIds.Contains(id)) return false;
            var s = Sessions.FirstOrDefault(x => x.SessionIdentifier == id);
            if (s == null) return false;
            s.Volume = volume;
            Successes++;
            return true;
        }
        public void SetVolume(string id, float volume)
        {
            if (!TrySetVolume(id, volume)) throw new IOException("Owned audio write fault");
        }
        public void SetMute(string id, bool mute) => throw new InvalidOperationException("No mute writes expected");
        public void OnSessionCreated(Action<AudioSessionInfo> callback) { }
        public void OnSessionDestroyed(Action<string> callback) { }
        public void StartMonitoring() { }
        public void StopMonitoring() { }
    }

    private sealed class AllGame : ISessionClassifier
    {
        public AppClassification Classify(string path, string displayName) => AppClassification.Game;
    }
    private sealed class LocalSettings : ISettingsStore
    {
        public AppSettings Settings { get; } = new();
        public void Load() { }
        public void Save() { }
    }
    private static AudioSessionInfo Session(string exe, string id, float volume) => new()
    {
        ExePath = exe, ExeName = Path.GetFileName(exe), DisplayName = "Owned audio",
        SessionIdentifier = id, Volume = volume
    };

    private static void FailedAutoWriteAndRecovery(string root)
    {
        var rulesPath = Path.Combine(root, "rules.json");
        // An old tool first-run date guarantees the new owned session is eligible.
        File.WriteAllText(rulesPath, "{\"toolFirstRunUtc\":\"2024-01-01T00:00:00Z\",\"apps\":{}}");
        var store = new JsonRuleStore(rulesPath);
        var audio = new ControlledAudio();
        var classifier = new AllGame();
        var path = Path.Combine(root, "owned-audio-game.exe");
        var session = Session(path, "audio-1", 0.90f);
        var controller = new MixerController(audio, new GuardEngine(store, classifier), classifier,
            store, new LocalSettings(), Path.Combine(root, "guard.log"));
        var messages = new List<string>();
        controller.OnGuardDecision += messages.Add;
        controller.Initialize(); // empty owned baseline
        audio.Sessions.Add(session);
        audio.FailedIds.Add("audio-1");
        controller.RefreshSessions();
        var rule = store.GetRule(path) ?? throw new Exception("Missing rule");
        Check(!rule.AutoApplied && Math.Abs(rule.LastKnownVolume - 0.90f) < 0.001f &&
              Math.Abs(controller.AppEntries.Single().CombinedVolume - 0.90f) < 0.001f &&
              !messages.Any(m => m.Contains("-> 5%")), "Failed auto write recorded success");
        Check(audio.Attempts == 1 && audio.Successes == 0, "Failed provider fault not exercised");
        controller.RefreshSessions();
        Check(audio.Attempts == 1, "Unbounded immediate auto write retry");
        controller.Shutdown();
        var reloaded = new JsonRuleStore(rulesPath);
        reloaded.Load();
        Check(!reloaded.GetRule(path)!.AutoApplied &&
              Math.Abs(reloaded.GetRule(path)!.LastKnownVolume - 0.9f) < 0.001f,
              "False AutoApplied persisted across controller restart");

        // Recover the provider in a separate controller with an empty baseline;
        // real session remains present and is reintroduced afterward.
        audio.FailedIds.Clear();
        audio.Sessions.Clear();
        var recovered = new MixerController(audio, new GuardEngine(reloaded, classifier),
            classifier, reloaded, new LocalSettings(), Path.Combine(root, "recover-guard.log"));
        var recoveredMessages = new List<string>();
        recovered.OnGuardDecision += recoveredMessages.Add;
        recovered.Initialize();
        session.Volume = 0.90f;
        audio.Sessions.Add(session);
        recovered.RefreshSessions();
        Check(audio.Successes == 1 && reloaded.GetRule(path)!.AutoApplied &&
              Math.Abs(session.Volume - 0.05f) < 0.001f &&
              recoveredMessages.Count(x => x.Contains("-> 5%")) == 1,
              "Recovery failed to apply once-only volume");
        recovered.RefreshSessions();
        Check(audio.Successes == 1, "Successful auto-write repeated");
        recovered.Shutdown();

        // Partial write must not claim success, even if one provider session was written.
        var partPath = Path.Combine(root, "two-session-game.exe");
        var parts = new ControlledAudio();
        var pStore = new JsonRuleStore(Path.Combine(root, "partial-rules.json"));
        var pCtrl = new MixerController(parts, new GuardEngine(pStore, classifier), classifier,
            pStore, new LocalSettings(), Path.Combine(root, "partial-guard.log"));
        pCtrl.Initialize();
        parts.Sessions.Add(Session(partPath, "ok", .90f));
        parts.Sessions.Add(Session(partPath, "bad", .90f));
        parts.FailedIds.Add("bad");
        pCtrl.RefreshSessions();
        var pRule = pStore.GetRule(partPath)!;
        Check(!pRule.AutoApplied && parts.Successes == 1 &&
              Math.Abs(pCtrl.AppEntries.Single().CombinedVolume - .9f) < .001f,
              "Partial success became full success");
        parts.FailedIds.Clear();
        parts.MissingIds.Add("bad"); // disappeared between snapshot and provider write
        pCtrl.SetVolume(partPath, .40f); // manual intention takes precedence
        Check(pRule.ManualOverride && !pRule.AutoApplied &&
              Math.Abs(pRule.LastKnownVolume - .90f) < .001f,
              "Failed manual write overwrote measured volume");
        pCtrl.SetMute(partPath, true); // Provider intentionally rejects mute.
        Check(!pCtrl.AppEntries.Single().IsMuted && pRule.ManualOverride,
              "Rejected mute command published a false muted UI state");
        parts.MissingIds.Clear();
        Thread.Sleep(1150);
        var attemptsBefore = parts.Attempts;
        pCtrl.RefreshSessions();
        Check(parts.Attempts == attemptsBefore, "Manual override did not stop deferred auto recovery");
        pCtrl.Shutdown();
    }
}
