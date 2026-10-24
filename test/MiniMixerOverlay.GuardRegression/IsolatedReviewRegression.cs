using System;
using System.IO;
using System.Reflection;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

// Explicitly isolated, all-fake regression. No real AudioSessionManager or app startup.
internal static class IsolatedReviewRegression
{
    private sealed class AlwaysApplyGuard : IGuardEngine
    {
        public int Evaluations { get; private set; }
        public bool ShouldAutoApplyVolume(AppRule rule, DateTime firstRun) => true;
        public GuardDecision EvaluateNewSession(string exe, string display, float volume,
            bool isBaselineScan = false, System.Collections.Generic.IReadOnlyCollection<uint>? processIds = null)
        {
            Evaluations++;
            return new GuardDecision {
                AutoApplyAllowed = true, TargetVolume = 0.05f,
                IsBaselineScan = isBaselineScan,
                Classification = exe.Contains("new-game", StringComparison.OrdinalIgnoreCase)
                    ? AppClassification.Game : AppClassification.NonGame,
                DisplayName = display, InputExePath = exe, EffectiveExePath = exe,
                Reason = "forced fake game auto-limit"
            };
        }
    }

    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "mini-mixer-isolated-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var validate = owner.GetMethod("TryConfigureIsolatedReviewProfile", flags)
            ?? throw new MissingMethodException("Pre-window isolated review preflight");
        var field = owner.GetField("_isolatedReviewRoot", flags)
            ?? throw new MissingFieldException("Review path");
        var previous = field.GetValue(null);
        try
        {
            File.WriteAllText(Path.Combine(root, "review.sentinel"), "owned test profile");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
            var executable = "MiniMixerOverlay.App.exe";
            bool CheckArgs(params string[] args) =>
                (bool)(validate.Invoke(null, new object[] { new[] { executable }.Concat(args).ToArray() })
                    ?? throw new InvalidOperationException("Null preflight verdict"));

            Ensure(CheckArgs(), "normal launch rejected");
            Ensure(field.GetValue(null) == null, "normal launch retained test-profile state");
            Ensure(!CheckArgs("--isolated-review-profile=" + root, "--native-hook-preview"),
                "conflicting native preview accepted");
            Ensure(field.GetValue(null) == null, "failed preflight stored review path");
            Ensure(!CheckArgs("--isolated-review-profile=" + root, "--graphics-ipc-only"),
                "conflicting IPC preview accepted");
            Ensure(!CheckArgs("--isolated-review-profile=" + root, "--graphics-ipc-enable-sphere-input"),
                "conflicting input IPC accepted");
            Ensure(!CheckArgs("--isolated-review-profile=" + root, "--isolated-review-profile=" + root),
                "duplicate profile accepted");
            Ensure(!CheckArgs("--isolated-review-profile=relative-path"),
                "relative review path accepted");
            Ensure(!CheckArgs("--isolated-review-profile=" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MiniMixerOverlay")), "ordinary roaming profile accepted");
            Ensure(CheckArgs("--isolated-review-profile=" + root), "valid isolated path rejected");
            Ensure(string.Equals(field.GetValue(null) as string, root, StringComparison.OrdinalIgnoreCase),
                "resolved review path does not match test directory");
            var errorLogMethod = owner.GetMethod("LogUnhandledException", flags)
                ?? throw new MissingMethodException("WPF runtime error logger");
            errorLogMethod.Invoke(null, new object?[] {
                "controlled-isolated-review-error",
                new InvalidOperationException("expected test-only error"), null
            });
            Ensure(File.Exists(Path.Combine(root, "logs", "runtime-errors.log")),
                "isolated exception logger used the wrong profile");

            // Recreate the reviewer counterexample: 'games-only' still authorizes
            // a new game; the isolated controller must block the command itself.
            var gameRule = new AppRule {
                Classification = AppClassification.Game, FirstSeenUtc = DateTime.UtcNow
            };
            var gameOnly = new GuardEngine(new MemoryRules(DateTime.UtcNow.AddDays(-2)),
                new FakeClassifier(), autoApplyToAllNewApps: false);
            Ensure(gameOnly.ShouldAutoApplyVolume(gameRule, DateTime.UtcNow.AddDays(-2)),
                "Game-only guard reproduction no longer exercises the reviewer risk");

            var game = new AudioSessionInfo {
                SessionIdentifier = "owned|game", ExePath = Path.Combine(root, "new-game.exe"),
                ExeName = "new-game.exe", DisplayName = "New Game", ProcessId = 111u, Volume = 0.91f
            };
            var app = new AudioSessionInfo {
                SessionIdentifier = "owned|app", ExePath = Path.Combine(root, "new-app.exe"),
                ExeName = "new-app.exe", DisplayName = "New App", ProcessId = 112u, Volume = 0.88f
            };
            var fake = new FakeAudio(new[] { game });
            var guard = new AlwaysApplyGuard();
            var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
            var settings = new FakeSettings();
            var log = Path.Combine(root, "logs", "guard.log");
            var controller = new MixerController(fake, guard, new FakeClassifier(), rules, settings,
                guardLogFilePath: log, allowAudioWrites: false);
            controller.Initialize(); // baseline, even if a guard would demand writes
            fake.Sessions.Add(app);
            controller.RefreshSessions(); // new game/other app
            settings.Settings.Ui.AutoApplyToAllNewApps = false;
            controller.RefreshSessions();
            settings.Settings.Ui.AutoApplyToAllNewApps = true;
            controller.RefreshSessions(); // UI changes cannot override immutable write gate
            Ensure(!controller.TrySetVolume(game.ExePath, 0.30f),
                "manual volume accepted in protected review");
            Ensure(!controller.TrySetMute(app.ExePath, true),
                "manual mute accepted in protected review");
            controller.SetVolume(app.ExePath, 0.25f);
            controller.SetMute(game.ExePath, true);
            Ensure(fake.VolumeWrites.Count == 0 && fake.MuteWrites.Count == 0,
                "isolated review wrote to fake audio provider");
            Ensure(game.Volume == 0.91f && app.Volume == 0.88f,
                "isolated review changed observed volume");
            Ensure(guard.Evaluations == 0,
                "isolated review entered auto-guard decision path");
            Ensure(File.Exists(log), "audit did not use isolated log path");
            controller.Shutdown();

            // Positive control: the same forced-allow rule actually writes when enabled.
            var controlAudio = new FakeAudio(new[] { new AudioSessionInfo {
                SessionIdentifier = "control|game", ExePath = game.ExePath,
                ExeName = "new-game.exe", DisplayName = "New Game",
                ProcessId = 113u, Volume = 0.91f
            } });
            var controlGuard = new AlwaysApplyGuard();
            var positive = new MixerController(controlAudio, controlGuard, new FakeClassifier(),
                new MemoryRules(DateTime.UtcNow.AddDays(-20)), new FakeSettings(),
                guardLogFilePath: Path.Combine(root, "logs", "positive-control.log"),
                allowAudioWrites: true);
            positive.Initialize();
            Ensure(controlGuard.Evaluations > 0 && controlAudio.VolumeWrites.Count > 0,
                "positive control did not exercise the write-producing branch");
            positive.Shutdown();
        }
        finally
        {
            field.SetValue(null, previous);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
