using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;
using MiniMixerOverlay.Infrastructure.Persistence;
using MiniMixerOverlay.Infrastructure.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Application = System.Windows.Application;
using Orientation = System.Windows.Controls.Orientation;

if (args.Contains("--hotkey-os-smoke", StringComparer.OrdinalIgnoreCase))
    return HotkeyOsRegression.Run();

if (args.Contains("--topmost-only", StringComparer.OrdinalIgnoreCase))
{
    OptionalTopmostPreferenceOnSta();
    Console.WriteLine("TOPMOST_ONLY PASS");
    return 0;
}

if (args.Contains("--audio-owned-refresh-race", StringComparer.OrdinalIgnoreCase))
    return OwnAudioConcurrentRefreshSmoke.Run();

if (args.Contains("--audio-monitor-rebind-simulated", StringComparer.OrdinalIgnoreCase))
    return AudioMonitorRebindSimulated.Run();

if (args.Contains("--handle-attribution", StringComparer.OrdinalIgnoreCase))
{
    var argIndex=Array.FindIndex(args,a=>string.Equals(a,"--handle-attribution",StringComparison.OrdinalIgnoreCase));
    var mode=argIndex>=0 && argIndex+1<args.Length?args[argIndex+1]:"full";
    return AudioHandleAttributionProbe.Run(mode);
}

if (args.Contains("--audio-readonly-soak", StringComparer.OrdinalIgnoreCase))
    return AudioReadonlySoak.Run(
        args.Contains("--short", StringComparer.OrdinalIgnoreCase) ? 20 : 120,
        args.Contains("--short", StringComparer.OrdinalIgnoreCase) ? 50 : 175);

if (args.Contains("--audio-endpoints-readonly", StringComparer.OrdinalIgnoreCase))
    return AudioEndpointReadonlyProbe.Run();

if (args.Contains("--audio-control-own-session", StringComparer.OrdinalIgnoreCase))
    return OwnAudioControlReadbackSmoke.Run();

if (args.Contains("--native-owned-child", StringComparer.OrdinalIgnoreCase))
{
    NativeHookOwnedAttachSmoke.RunChild();
    return 0;
}
if (args.Contains("--native-owned-attach", StringComparer.OrdinalIgnoreCase))
    return NativeHookOwnedAttachSmoke.RunParent(args);
// Bounded owned WPF soak: the production layout and Refresh run with only
// fake sessions; process and UI resource samples are printed to stdout.
if (args.Contains("--wpf-soak-owned", StringComparer.OrdinalIgnoreCase))
{
    var raw = args.SkipWhile(a => !string.Equals(a, "--seconds", StringComparison.OrdinalIgnoreCase))
                  .Skip(1).FirstOrDefault();
    var seconds = int.TryParse(raw, out var parsed) ? Math.Clamp(parsed, 10, 180) : 65;
    VisibleWpfSmoke(false, true, TimeSpan.FromSeconds(seconds));
    return 0;
}

// Explicit interactive smoke fixture using the production WPF layout but fake audio/stores.
// Never invokes MiniMixerOverlay.App.Program.Main, StartAudio, GameHook or user persistence.
if (args.Contains("--wpf-visible-safe", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--wpf-visible-settings-safe", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--wpf-visible-glass-safe", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--wpf-visible-glass-advanced-safe", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--wpf-visible-hover-safe", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--wpf-visible-popup-safe", StringComparer.OrdinalIgnoreCase))
{
    VisibleWpfSmoke(
        args.Contains("--wpf-visible-hover-safe", StringComparer.OrdinalIgnoreCase),
        args.Contains("--wpf-visible-popup-safe", StringComparer.OrdinalIgnoreCase),
        showSettings: args.Contains("--wpf-visible-settings-safe", StringComparer.OrdinalIgnoreCase) ||
                      args.Contains("--wpf-visible-glass-safe", StringComparer.OrdinalIgnoreCase) ||
                      args.Contains("--wpf-visible-glass-advanced-safe", StringComparer.OrdinalIgnoreCase),
        showGlass: args.Contains("--wpf-visible-glass-safe", StringComparer.OrdinalIgnoreCase) ||
                   args.Contains("--wpf-visible-glass-advanced-safe", StringComparer.OrdinalIgnoreCase),
        showAdvancedGlass: args.Contains("--wpf-visible-glass-advanced-safe", StringComparer.OrdinalIgnoreCase));
    return 0;
}

// Offline desktop-render probe: invokes only WPF card composition with a fake audio provider.
// No windows are shown; Program.Main, guards on real audio and GameHook are never started.
if (args.Contains("--wpf-filter-offline", StringComparer.OrdinalIgnoreCase))
{
    try { OfflineWpfFilterOnSta(); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WPF FILTER FAIL: {ex}");
        return 1;
    }
    Console.WriteLine("WPF FILTER PASS: real WPF Refresh filters inactive app; mute exception remains; fake audio had zero writes");
    return 0;
}

// Owned signal in the real Windows WASAPI meter; never touches volume/mute or user settings.
if (args.Contains("--audio-peak-transition", StringComparer.OrdinalIgnoreCase))
    return RealAudioPeakTransitionProbe.Run();

// Optional read-only OS smoke probe. Never writes volume/mute or touches user settings.
if (args.Contains("--audio-readonly", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var audio = new NAudioSessionManager();
        var first = audio.EnumerateSessions();
        var second = audio.EnumerateSessions();
        foreach (var snapshot in new[] { first, second })
        {
            Check(snapshot.All(item => !string.IsNullOrWhiteSpace(item.SessionIdentifier)),
                "audio provider returned an empty control identity");
            Check(snapshot.Select(item => item.SessionIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() == snapshot.Count,
                "audio provider returned duplicate control identities");
        }
        Console.WriteLine($"READONLY PASS: first={first.Count} sessions, second={second.Count} sessions; no audio commands sent");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"READONLY INCONCLUSIVE: {ex.GetType().Name}: {ex.Message}");
        return 2;
    }
}

// Isolated child process: the OS tears down its audio sessions when it exits.
if (args.Contains("--audio-silent-child", StringComparer.OrdinalIgnoreCase))
{
    using var devices = new MMDeviceEnumerator();
    using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
    var silent = new BufferedWaveProvider(endpoint.AudioClient.MixFormat) { ReadFully = true };
    using var player = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 120);
    player.Init(silent);
    player.Play();
    Console.WriteLine("CHILD_AUDIO_READY");
    Thread.Sleep(2500);
    player.Stop();
    return 0;
}

// Exercise StopMonitoring against a real session-creation race, without changing volume.
if (args.Contains("--audio-monitor-stop", StringComparer.OrdinalIgnoreCase))
{
    var monitor = new NAudioSessionManager();
    var created = new System.Collections.Concurrent.ConcurrentQueue<AudioSessionInfo>();
    monitor.EnumerateSessions();
    monitor.OnSessionCreated(created.Enqueue);
    monitor.StartMonitoring();
    System.Diagnostics.Process? child = null;
    try
    {
        child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList = { System.Reflection.Assembly.GetEntryAssembly()?.Location ??
                throw new InvalidOperationException("Missing entry assembly"), "--audio-silent-child" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("Could not start audio child");

        var ready = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(6))
            .GetAwaiter().GetResult();
        Check(ready == "CHILD_AUDIO_READY", "child stream did not start");
        var childPid = (uint)child.Id;
        Thread.Sleep(30);
        monitor.StopMonitoring();
        var countAtStop = created.Count(s => s.ProcessId == childPid);
        Thread.Sleep(380);
        var countAfterStop = created.Count(s => s.ProcessId == childPid);
        Check(countAfterStop == countAtStop,
            "a new session callback fired after StopMonitoring");
        Check(child.WaitForExit(8000) && child.ExitCode == 0,
            "silent child failed");
        Console.WriteLine($"MONITOR_STOP PASS: child PID={childPid}, callbacks before stop={countAtStop}, after stop={countAfterStop}; no volume/mute commands");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"MONITOR_STOP INCONCLUSIVE: {ex.GetType().Name}: {ex.Message}");
        return 2;
    }
    finally
    {
        monitor.StopMonitoring();
        if (child != null)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            child.Dispose();
        }
    }
}

// Test real create and removal using the child PID; no user volume or mute writes.
if (args.Contains("--audio-lifecycle-process", StringComparer.OrdinalIgnoreCase))
{
    var monitor = new NAudioSessionManager();
    var baseline = monitor.EnumerateSessions();
    var created = new System.Collections.Concurrent.ConcurrentQueue<AudioSessionInfo>();
    var destroyed = new System.Collections.Concurrent.ConcurrentQueue<string>();
    monitor.OnSessionCreated(created.Enqueue);
    monitor.OnSessionDestroyed(destroyed.Enqueue);
    monitor.StartMonitoring();
    System.Diagnostics.Process? child = null;
    try
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location ??
            throw new InvalidOperationException("Missing entry assembly");
        child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList = { entryAssembly, "--audio-silent-child" },
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start audio child");
        var pid = (uint)child.Id;
        AudioSessionInfo? found = null;
        var createDeadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < createDeadline && !child.HasExited)
        {
            var snapshot = monitor.EnumerateSessions();
            found = snapshot.FirstOrDefault(s => s.ProcessId == pid) ??
                created.FirstOrDefault(s => s.ProcessId == pid);
            if (found != null) break;
            Thread.Sleep(120);
        }
        child.WaitForExit(8000);
        if (!child.HasExited) throw new InvalidOperationException("Silent child did not terminate");
        Check(child.ExitCode == 0, "Silent audio child failed");
        if (found == null)
        {
            Console.Error.WriteLine("LIFECYCLE INCONCLUSIVE: child audio session was not observed");
            return 2;
        }

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline && !destroyed.Contains(found.SessionIdentifier))
        {
            monitor.EnumerateSessions();
            Thread.Sleep(200);
        }
        var removedIds = destroyed.Where(s => s == found.SessionIdentifier).ToArray();
        Console.WriteLine($"LIFECYCLE_PROCESS: baseline={baseline.Count}, childPid={pid}, createdForChild={created.Count(s => s.ProcessId == pid)}, destroyedForChild={removedIds.Length}");
        if (removedIds.Length != 1)
        {
            Console.Error.WriteLine($"LIFECYCLE INCONCLUSIVE: expected one destroyed callback, observed {removedIds.Length}");
            return 2;
        }
        Check(removedIds[0] == found.SessionIdentifier, "wrong control was reported removed");
        var afterRemoval = monitor.EnumerateSessions();
        Check(!afterRemoval.Any(s => s.SessionIdentifier == found.SessionIdentifier),
            "terminated child's audio session remained published after destroy callback");
        Check(destroyed.Count(s => s == found.SessionIdentifier) == 1,
            "same session was reported destroyed twice");
        Console.WriteLine("LIFECYCLE_PROCESS PASS: real Windows session removed exactly once; no volume/mute commands");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"LIFECYCLE_PROCESS INCONCLUSIVE: {ex.GetType().Name}: {ex.Message}");
        return 2;
    }
    finally
    {
        if (child != null)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            child.Dispose();
        }
        monitor.StopMonitoring();
    }
}

// Opt-in Windows session lifecycle probe: writes only silent samples, never audio volume/mute.
if (args.Contains("--audio-lifecycle-silent", StringComparer.OrdinalIgnoreCase))
{
    NAudioSessionManager? monitor = null;
    try
    {
        monitor = new NAudioSessionManager();
        var baseline = monitor.EnumerateSessions();
        var created = new System.Collections.Concurrent.ConcurrentQueue<AudioSessionInfo>();
        var removed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        monitor.OnSessionCreated(created.Enqueue);
        monitor.OnSessionDestroyed(removed.Enqueue);
        monitor.StartMonitoring();

        using var devices = new MMDeviceEnumerator();
        using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        var silent = new BufferedWaveProvider(endpoint.AudioClient.MixFormat) { ReadFully = true };
        using (var player = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 120))
        {
            player.Init(silent);
            player.Play();
            Thread.Sleep(1600);
            var callbackHits = created.Where(s => s.ProcessId == (uint)Environment.ProcessId).ToArray();
            Console.WriteLine($"LIFECYCLE: baseline={baseline.Count}, created={created.Count}, this-process-create={callbackHits.Length}");
            if (callbackHits.Length == 0)
            {
                Console.Error.WriteLine("LIFECYCLE INCONCLUSIVE: no matching real Windows create callback");
                return 2;
            }

            Check(callbackHits.All(s => s.SessionIdentifier.Contains(endpoint.ID, StringComparison.OrdinalIgnoreCase)),
                "create callback was attributed to the wrong endpoint");
            Check(callbackHits.Select(s => s.SessionIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() == callbackHits.Length,
                "create callback reported the same session more than once");
            player.Stop();
        }

        Thread.Sleep(400);
        monitor.EnumerateSessions();
        Console.WriteLine($"LIFECYCLE PASS: observed real create notification for this process; destroyed-callback-count={removed.Count}; no volume/mute writes");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"LIFECYCLE INCONCLUSIVE: {ex.GetType().Name}: {ex.Message}");
        return 2;
    }
    finally
    {
        monitor?.StopMonitoring();
    }
}

var checks = new (string Name, Action Run)[]
{
    ("isolated review preflight, zero writes and local audit boundary", IsolatedReviewRegression.Run),
    ("baseline remains untouched on a second refresh", BaselineNeverReclassified),
    ("persisted pre-existing rule retains original firstSeen", ExistingRuleNeverReclassified),
    ("new post-baseline app is limited once and manual input wins", NewAppAndManualPriority),
    ("eligible baseline install is limited at most once", EligibleBaselineInstallIsOneShot),
    ("manual volume and mute survive later refresh", ManualOverrideSurvivesRefresh),
    ("system master and system sounds never receive auto-guard writes", MasterAndSystemSoundsSkipAutoGuard),

    ("mute preserves last audible volume in saved app rule", MutePreservesLastKnownVolume),
    ("guard log records changes not polls and keeps bounded rotation", GuardLogIsChangeOnlyAndBounded),
    ("guard log audits manual override transitions without slider spam", GuardManualOverrideAuditIsOneShot),
    ("persisted override survives a controller restart", PersistedManualOverrideSurvivesRestart),
    ("background creation dispatches collection refresh to captured context", BackgroundCreationUsesOwnerContext),
    ("background destroy dispatches collection refresh to captured context", BackgroundDestroyUsesOwnerContext),
    ("event bursts coalesce and shutdown cancels queued refresh", BurstAndShutdownAreSafe),
    ("persisted EXE keys remain case insensitive across restart", PersistedRulesRemainCaseInsensitive),
    ("legacy case-collision preserves manual priority and once-only guard", LegacyCaseCollisionIsSafe),
    ("malformed rules and settings retain recoverable originals", MalformedJsonPreservesOriginal),
    ("failed settings and rules writes preserve previous valid JSON", FailedWritesPreserveValidJson),
    ("JSON null roots preserve originals", NullRootsPreserveOriginal),
    ("backup failure blocks overwrite of malformed sources", FailedBackupBlocksWrites),
    ("runtime audit: failed auto writes and null settings recover without false success", RuntimeAuditFixRegression.Run),
    ("WPF normal quick popup controls reconcile rejected and partial audio writes", RejectedWpfCommandRegression.Run),
    ("audio session identity includes endpoint", SessionIdentityIncludesEndpoint),
    ("audio snapshot replacement reports exact removals", SessionSnapshotReportsRemovals),
    ("retired audio session controls exclude still-published instances", SessionSnapshotRetirementKeepsReusedObjects),
    ("audio snapshot remains coherent across concurrent readers", SessionSnapshotConcurrentReaders),
    ("created IDs report only newly published sessions", CreatedSessionDeltaExact),
    ("polling before notification suppresses duplicate created ID", CreatedSessionAlreadyPolled),
    ("offline WPF card and strip render honor active-audio setting", OfflineWpfFilterOnSta),
    ("mini strip pins master while full cards prioritize foreground and honor visible count", PinnedMasterUiRegression.Run),
    ("master card stays visible and unmute restores true endpoint volume", MasterCardUsesRealEndpointStateOnSta),
    ("offline WPF dock collapse and expand update geometry", OfflineDockGeometryOnSta),
    ("oversized WPF window remains placeable after screen shrink", OversizedWindowStaysWithinReachOnSta),
    ("dock top geometry tolerates synthetic smaller work areas", DockTopHandlesCrampedWorkArea),
    ("dock hover reveal obeys edge zone and hold expiry", DockHoverRevealOnSta),
    ("settings dock side and reveal sliders remain usable in both overlay modes", DockSettingsControlsOnSta),
    ("WPF normal and compact controls use manual audio owner", OfflineWpfSliderMuteOnSta),
    ("audio popup keeps icon anchor through mute and unmute refresh", OfflinePopupAnchorOnMuteRefresh),
    ("sphere only snaps near edge when docking enabled", SphereEdgeSnapIsOptional),
    ("manual sphere placement survives refresh and persisted settings", SphereManualStateRoundTripsOnSta),
    ("desktop sphere remains visible across main reveal dock settings minimize", DesktopSphereVisibilityOnSta),
    ("hover sphere stays independent while desktop mixer truly hides", IndependentSphereRevealAndHitZoneOnSta),
    ("hook only retires persisted desktop mode and resolves vendor-free assets", HookOnlyProfileAndAssetMigration),
    ("foreground audio permits titleless windows without weakening hook gate", ForegroundAudioTitlelessEligibility),
    ("game candidate rejects non-game and unrelated PID", AutoGameEligibilityFailsClosed),
    ("duplicate EXE game candidate is independent of entry order", AutoGameDuplicateExeOrderInvariant),
    ("qualified hook target identity rejects foreground switches", QualifiedHookTargetIdentity),
    ("hook framebuffer mutex contention prevents writes and notifications", HookFrameMutexContention),
    ("hook WM_COPYDATA timeout preserves responsive client and recovers", HookCopyDataTimeoutAndRecovery),
    ("late PID-only disconnect cannot evict newer client HWND", HookStalePidDisconnectOnSta),
    ("hook quarantined client recovers with bounded retry and cleanup", HookQuarantinedClientRecoveryOnSta),
    ("optional always-on-top survives mode apply, raise, UI and JSON restart", OptionalTopmostPreferenceOnSta),
    ("WPF-only default skips native IPC while manual/IPC opt-in remains usable", WpfOnlySkipsNativeIpcAutoStart),
};

var failures = 0;
foreach (var (name, run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name}: {ex.Message}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} regression checks passed");
var fixtureLogDir = Path.Combine(Path.GetTempPath(),
    "mini-mixer-regression-logs-" + Environment.ProcessId + "-" +
    System.Diagnostics.Process.GetCurrentProcess().StartTime.Ticks);
try
{
    if (Directory.Exists(fixtureLogDir))
        Directory.Delete(fixtureLogDir, recursive: true);
}
catch (IOException)
{
    // A leftover test-only log is not a product failure.
}
return failures == 0 ? 0 : 1;

static object CreateHookForeground(IntPtr hwnd, uint pid, string exeName, string title)
{
    var type = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
        "MiniMixerOverlay.App.GameHookForegroundWindowInfo") ??
        throw new InvalidOperationException("Hook foreground contract not found");
    var candidate = Activator.CreateInstance(type) ??
        throw new InvalidOperationException("Hook foreground construction failed");
    type.GetProperty("Hwnd")!.SetValue(candidate, hwnd);
    type.GetProperty("ProcessId")!.SetValue(candidate, pid);
    type.GetProperty("ProcessName")!.SetValue(candidate, exeName);
    type.GetProperty("WindowTitle")!.SetValue(candidate, title);
    return candidate;
}

static void AutoGameEligibilityFailsClosed()
{
    var (controller, _) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-2)));
    var owner = typeof(MiniMixerOverlay.App.Program);
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var ctrlField = owner.GetField("_ctrl", flags) ?? throw new MissingFieldException("_ctrl");
    var select = owner.GetMethod("IsLikelyGameCandidate", flags) ??
        throw new MissingMethodException("IsLikelyGameCandidate");
    const uint pid = 43210;
    var candidate = CreateHookForeground(new IntPtr(1234), pid, "hook-fixture-game", "Fixture Game");
    bool Eligible(object value) => (bool)(select.Invoke(null, new[] { value }) ?? false);
    var entry = new AppEntry
    {
        ExePath = @"C:\Games\hook-fixture-game.exe",
        Sessions = new List<AudioSessionInfo>
        {
            new() { ProcessId = pid, SessionIdentifier = "hook-fixture", Volume = 1f }
        },
        HasActiveAudio = true,
        Rule = new AppRule { Classification = AppClassification.NonGame }
    };
    try
    {
        ctrlField.SetValue(null, null);
        Check(!Eligible(candidate), "unknown controller authorized auto injection");
        ctrlField.SetValue(null, controller);
        controller.AppEntries.Add(entry);
        Check(!Eligible(candidate), "active non-game was authorized");
        entry.Rule!.Classification = AppClassification.Unknown;
        Check(!Eligible(candidate), "unknown classification was authorized");
        entry.Rule.Classification = AppClassification.Game;
        Check(Eligible(candidate), "classified foreground game was rejected");
        entry.HasActiveAudio = false;
        Check(Eligible(candidate), "game eligibility incorrectly depends on audible volume");
        entry.Sessions[0].ProcessId = pid + 1;
        Check(!Eligible(candidate), "unrelated PID sharing game executable was authorized");
        entry.Sessions[0].ProcessId = pid;
        Check(!Eligible(CreateHookForeground(new IntPtr(1234), pid, "other-fixture", "Other")),
            "unrelated executable was authorized");
    }
    finally
    {
        ctrlField.SetValue(null, null);
        controller.Shutdown();
    }
}

static void OptionalTopmostPreferenceOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        T Read<T>(string name) =>
            (T)(owner.GetField(name, flags) ?? throw new MissingFieldException(name)).GetValue(null)!;
        void Invoke(string name) =>
            (owner.GetMethod(name, flags) ??
                throw new MissingMethodException(name)).Invoke(null,
                    name == "BringMainWindowToFront" ? new object[] { false } : null);
        System.Windows.Controls.CheckBox FindTopmost(DependencyObject root)
        {
            if (root is System.Windows.Controls.CheckBox cb && cb.Content is string label &&
                (label.Contains("Immer im Vordergrund", StringComparison.OrdinalIgnoreCase) ||
                 label.Contains("Always on top", StringComparison.OrdinalIgnoreCase)))
                return cb;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                try { return FindTopmost(System.Windows.Media.VisualTreeHelper.GetChild(root, i)); }
                catch (InvalidOperationException) { }
            }
            throw new InvalidOperationException("Real always-on-top checkbox not found");
        }

        var (controller, audio) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-5)));
        var window = new Window
        {
            Width = 350, Height = 175, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Opacity = 0, Topmost = false
        };
        var settingsPath = Path.Combine(Path.GetTempPath(),
            "mm-topmost-preference-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            controller.Settings.Window.AlwaysOnTop = false;
            Set("_ctrl", controller);
            Set("_mainWin", window);
            Set("_cornerHintShowDot", false);
            Set("_cornerHintShowValue", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockEnabled", false);
            Set("_gameHookAutoStartRuntime", false);
            var normalized = (string)(owner.GetMethod("NormalizeOverlayMode", flags)!
                .Invoke(null, new object[] { "desktop" })!);
            Check(normalized == "gamehook", "profile no longer normalized to expected gamehook mode");
            Set("_overlayMode", normalized);

            Invoke("ApplyOverlayModeRuntimeState");
            Check(!window.Topmost && !controller.Settings.Window.AlwaysOnTop,
                "mode apply forced previously disabled topmost");
            window.Show();
            Invoke("BringMainWindowToFront");
            Check(!window.Topmost,
                "actual WPF raise promoted a non-topmost mixer");
            Invoke("WriteCurrentStateToSettings");
            Check(!controller.Settings.Window.AlwaysOnTop,
                "window writeback overwrote user topmost=false");

            Invoke("ShowSettings");
            var settings = Read<Window>("_settingsWin");
            settings.UpdateLayout();
            var toggle = FindTopmost(settings);
            Check(toggle.IsChecked == false && !settings.Topmost,
                "settings reopened at wrong preference or elevated unexpectedly");

            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(controller.Settings.Window.AlwaysOnTop && window.Topmost && settings.Topmost,
                "enabling topmost failed to apply to main and settings windows");
            Invoke("ApplyOverlayModeRuntimeState");
            Invoke("BringMainWindowToFront");
            Check(window.Topmost && settings.Topmost,
                "mode reapply/raise lost enabled preference");

            toggle.IsChecked = false;
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!controller.Settings.Window.AlwaysOnTop && !window.Topmost && !settings.Topmost,
                "disabling topmost failed to demote both managed windows");
            Invoke("BringMainWindowToFront");
            Invoke("ApplyOverlayModeRuntimeState");
            Invoke("WriteCurrentStateToSettings");
            Check(!controller.Settings.Window.AlwaysOnTop && !window.Topmost && !settings.Topmost,
                "raise/mode/writeback restored topmost behind user's back");

            settings.Close();
            Set("_settingsWin", null);
            Invoke("ShowSettings");
            settings = Read<Window>("_settingsWin");
            settings.UpdateLayout();
            Check(FindTopmost(settings).IsChecked == false && !settings.Topmost,
                "closing/reopening settings changed topmost");
            settings.Close();
            Set("_settingsWin", null);

            var disk = new JsonSettingsStore(settingsPath);
            disk.Settings.Window.AlwaysOnTop = controller.Settings.Window.AlwaysOnTop;
            disk.Save();
            var reloaded = new JsonSettingsStore(settingsPath);
            reloaded.Load();
            Check(!reloaded.Settings.Window.AlwaysOnTop,
                "isolated JSON save/restart did not preserve false");
            Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
                "topmost UI generated audio writes");
        }
        catch (Exception ex)
        {
            failure = ex;
            Console.Error.WriteLine("TOPMOST_CHECK_FAILED: " + ex);
        }
        finally
        {
            if (Read<Window?>("_settingsWin") is Window settings) settings.Close();
            Set("_settingsWin", null);
            window.Close();
            Set("_mainWin", null);
            Set("_ctrl", null);
            Set("_overlayMode", "gamehook");
            Set("_cornerHintShowDot", true);
            Set("_cornerHintShowValue", true);
            controller.Shutdown();
            if (Directory.Exists(Path.GetDirectoryName(settingsPath)!))
                Directory.Delete(Path.GetDirectoryName(settingsPath)!, true);
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start(); worker.Join();
    if (failure != null)
        throw new InvalidOperationException("Optional topmost real WPF test failed", failure);
}

static void DockSettingsControlsOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Field(string name, object? value) =>
            (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        T Read<T>(string name) => (T)(owner.GetField(name, flags) ??
            throw new MissingFieldException(name)).GetValue(null)!;
        void Show() => (owner.GetMethod("ShowSettings", flags) ??
            throw new MissingMethodException("ShowSettings")).Invoke(null, null);

        // Traverse the actual instantiated WPF visual tree. A constructed but
        // unmounted ComboBox/Slider must not satisfy this regression.
        T Find<T>(DependencyObject node, string name) where T : FrameworkElement
        {
            if (node is T own && own.Name == name) return own;
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                try { return Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(node, i), name); }
                catch (InvalidOperationException) { }
            }
            throw new InvalidOperationException($"Dock settings control missing from visual tree: {name}");
        }

        var (controller, fake) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-4)));
        try
        {
            Field("_ctrl", controller);
            Field("_mainWin", null);
            Field("_edgeDockActive", false); // Never move an actual desktop overlay.
            Field("_edgeDockEnabled", true);
            Field("_edgeDockSide", "right");
            Field("_edgeDockVisibleWidth", 72);
            Field("_edgeDockRevealZoneWidth", 72);

            foreach (var mode in new[] { "desktop", "gamehook" })
            {
                Field("_overlayMode", mode);
                Show(); // Real WPF Settings construction; fake provider and in-memory persistence.
                var settings = Read<Window>("_settingsWin");
                settings.UpdateLayout();
                try
                {
                    // The mode selection must be absent from the instantiated
                    // settings tree, not merely disabled or Visibility.Collapsed.
                    bool HasRetiredModeSelector(DependencyObject node)
                    {
                        if (node is ComboBox combo)
                        {
                            foreach (var item in combo.Items)
                            {
                                var value = item?.ToString() ?? string.Empty;
                                if (value is "Desktop Overlay" or
                                    "Game-Hook (Experimental)" or "Game-Hook")
                                    return true;
                            }
                        }
                        if (node is TextBlock heading &&
                            (heading.Text == "Overlay-Modus:" ||
                             heading.Text == "Overlay mode:" ||
                             heading.Text == "Game-Hook (einziger Modus)" ||
                             heading.Text == "Game-Hook (only mode)"))
                            return true;
                        for (var child = 0;
                             child < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                             child++)
                        {
                            if (HasRetiredModeSelector(
                                System.Windows.Media.VisualTreeHelper.GetChild(node, child)))
                                return true;
                        }
                        return false;
                    }

                    Check(!HasRetiredModeSelector(settings),
                        "retired desktop/hook mode selector still exists in Settings WPF tree");
                    var side = Find<ComboBox>(settings, "DockSideCombo");
                    var peek = Find<Slider>(settings, "DockVisibleWidthSlider");
                    var reveal = Find<Slider>(settings, "DockRevealZoneSlider");
                    Check(settings.IsVisible && side.IsVisible &&
                          peek.IsVisible && reveal.IsVisible,
                        "dock controls were not actually visible in rendered settings: " + mode);
                    Check(side.Visibility == Visibility.Visible &&
                          peek.Visibility == Visibility.Visible &&
                          reveal.Visibility == Visibility.Visible,
                        "one or more dock settings were hidden in " + mode);
                    Check(side.IsEnabled && peek.IsEnabled && reveal.IsEnabled,
                        "dock settings were disabled with docking enabled in " + mode);
                    Check(side.Items.Count == 2 &&
                          Math.Abs(peek.Value - Read<int>("_edgeDockVisibleWidth")) < 0.01 &&
                          Math.Abs(reveal.Value - Read<int>("_edgeDockRevealZoneWidth")) < 0.01,
                        "dock settings did not load current persisted values in " + mode);
                    var sideIndex = mode == "desktop" ? 0 : 1;
                    var peekValue = mode == "desktop" ? 84 : 88;
                    var revealValue = mode == "desktop" ? 96 : 100;
                    side.SelectedIndex = sideIndex;
                    peek.Value = peekValue;
                    reveal.Value = revealValue;
                    Check(controller.Settings.Window.DockSide == (sideIndex == 0 ? "left" : "right") &&
                          controller.Settings.Window.DockVisibleWidth == peekValue &&
                          controller.Settings.Window.DockRevealZoneWidth == revealValue,
                        "editing dock settings did not write existing settings owner in " + mode);
                    Check(Read<bool>("_edgeDockEnabled") && !Read<bool>("_edgeDockActive") &&
                          Read<string>("_overlayMode") == mode,
                        "dock settings edit changed mode or activated docking in " + mode);
                }
                finally
                {
                    settings.Close();
                    Field("_settingsWin", null);
                }
            }
            Check(fake.VolumeWrites.Count == 0 && fake.MuteWrites.Count == 0,
                "editing dock controls caused real or fake audio writes");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (Read<Window?>("_settingsWin") is Window remaining) remaining.Close();
            Field("_settingsWin", null);
            Field("_ctrl", null);
            Field("_mainWin", null);
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_overlayMode", "desktop");
            controller.Shutdown();
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    worker.Join();
    if (failure != null) throw new InvalidOperationException($"Dock settings regression: {failure}", failure);
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern IntPtr WindowFromPoint(System.Drawing.Point point);

static void HookOnlyProfileAndAssetMigration()
{
    var owner = typeof(MiniMixerOverlay.App.Program);
    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
    var normalizeMode = owner.GetMethod("NormalizeOverlayMode", flags)!;
    foreach (var previous in new string?[] { null, "", "desktop", "DESKTOP", "gamehook", "unknown" })
    {
        Check((string)normalizeMode.Invoke(null, new object?[] { previous })! == "gamehook",
            "legacy or unknown profile can still activate retired desktop mode");
    }
    Check(new MiniMixerOverlay.Core.Interfaces.UiSettings().OverlayMode == "gamehook",
        "new UI profile still defaults to desktop");
    Check(new MiniMixerOverlay.Core.Interfaces.GameHookSettings().AssetsPath ==
          @"native\GameHook\candidate", "default assets still point to archived GOverlay binaries");

    var migrate = owner.GetMethod("NormalizeHookAssetsPath", flags)!;
    const string newPath = @"native\GameHook\candidate";
    const string previousPath = @"native\GameHook\prebuilt";
    const string oldPath = @"goverlay-master\goverlay-master\game-overlay\prebuilt";
    foreach (var previous in new string?[] { null, "", oldPath, oldPath.ToUpperInvariant(), previousPath, previousPath.ToUpperInvariant(), newPath })
    {
        Check((string)migrate.Invoke(null, new object?[] { previous })! == newPath,
            "legacy default assets path was not migrated");
    }
    var custom = Path.Combine(Path.GetTempPath(), "custom-hook-assets");
    Check((string)migrate.Invoke(null, new object?[] { custom })! == custom,
        "user-customized absolute hook path must be retained");
    var resolve = owner.GetMethod("ResolveGameHookAssetsPath", flags)!;
    var resolved = (string)resolve.Invoke(null, new object?[] { oldPath })!;
    Check(Path.IsPathRooted(resolved) &&
          resolved.EndsWith(Path.Combine("native", "GameHook", "candidate"),
              StringComparison.OrdinalIgnoreCase),
        "old GOverlay source path did not resolve to new bundled native assets");
    Check(!File.Exists(Path.Combine(resolved, "n_overlay.x64.dll")) &&
          !File.Exists(Path.Combine(resolved, "injector_helper.x64.exe")),
        "active native candidate directory still contains archived vendor injection assets");
    var bridge = owner.Assembly.GetType("MiniMixerOverlay.App.GameHookBridge")!;
    var disabled = bridge.GetProperty("LegacyVendorInjectionDisabled", flags)!;
    Check((bool)disabled.GetValue(null)!,
          "GOverlay provider must fail closed even for custom or legacy paths");
}

static void IndependentSphereRevealAndHitZoneOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        void Set(string name, object? value) =>
            owner.GetField(name, flags)!.SetValue(null, value);
        T Read<T>(string name) => (T)owner.GetField(name, flags)!.GetValue(null)!;
        object? Invoke(string name, params object[] args) =>
            owner.GetMethod(name, flags)!.Invoke(null, args);
        Window? main = null;
        try
        {
            main = new Window
            {
                Width = 390, Height = 425, Left = 140, Top = 155,
                Topmost = true, ShowActivated = false, ShowInTaskbar = false
            };
            Set("_mainWin", main);
            Set("_settingsWin", null);
            Set("_overlayMode", "desktop");
            Set("_cornerRevealEnabled", true);
            Set("_cornerRevealVisible", true);
            Set("_cornerHintShowDot", true);
            Set("_cornerHintShowValue", true);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockRevealZoneWidth", 72);
            Set("_cornerHintManualPosition", true);
            var area = (System.Windows.Rect)Invoke("GetMainWorkAreaOrVirtual")!;
            // Deliberate HWND overlap with the main mixer: verify actual
            // user32 hit-test ownership instead of relying on WPF IsVisible.
            Set("_cornerHintManualX", main.Left + 62);
            Set("_cornerHintManualY", main.Top + 62);
            main.Show();
            Invoke("UpdateCornerHintWindow");
            var sphere = Read<Window>("_cornerHintWin");
            Check(sphere.IsVisible && sphere.IsHitTestVisible,
                "configured sphere is not usable before main reveal");

            Invoke("ApplyCornerRevealVisualState", false);
            Check(!main.IsVisible && sphere.IsVisible && sphere.IsHitTestVisible,
                "desktop main is still a visible HWND when collapsed or sphere disappeared");
            Invoke("ApplyCornerRevealVisualState", true);
            Check(main.IsVisible && sphere.IsVisible && sphere.IsHitTestVisible,
                "revealing main hides or disables the independent sphere");

            var hot = new System.Windows.Point(sphere.Left + 12, sphere.Top + 12);
            Invoke("BringMainWindowToFront", false);
            var sphWnd = new System.Windows.Interop.WindowInteropHelper(sphere).Handle;
            var hitWnd = WindowFromPoint(new System.Drawing.Point(
                (int)Math.Round(hot.X), (int)Math.Round(hot.Y)));
            Check(hitWnd == sphWnd,
                "main mixer is above sphere in Win32 hit-test order after foreground raise");

            // Exercise the real sphere's routed mouse handlers while the main
            // HWND is raised. This must take priority over hover reveal.
            var root = (Grid)sphere.Content;
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount,
                MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            root.RaiseEvent(down);
            Check(Read<bool>("_cornerHintDragging") && sphere.IsVisible,
                "mouse down on raised sphere cannot begin dragging");
            Invoke("UpdateCornerRevealState");
            Check(sphere.IsVisible && !main.IsVisible,
                "dragging sphere did not suppress the large mixer");
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount,
                MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
            root.RaiseEvent(up);
            Check(!Read<bool>("_cornerHintDragging") && sphere.IsVisible,
                "mouse up failed to release sphere dragging");
            Invoke("ApplyCornerRevealVisualState", true);
            Check(Invoke("IsCursorInRevealHotZone", hot) is true,
                "center of sphere must activate reveal");
            var miss = new System.Windows.Point(hot.X + 60, hot.Y);
            Check(Invoke("IsCursorInRevealHotZone", miss) is false,
                "dock reveal width incorrectly extends the sphere hover hit zone");

            Set("_overlayMode", "gamehook");
            Set("_cornerRevealEnabled", false);
            Invoke("UpdateCornerHintWindow");
            Check(sphere.IsVisible && sphere.IsHitTestVisible,
                "gamehook reveal settings should not hide separately enabled sphere");

            // An explicit minus is a normal collapse, not a second state
            // machine which can keep the main visible or lock the sphere out.
            Set("_overlayMode", "desktop");
            Set("_cornerRevealEnabled", true);
            Invoke("MinimizeMainOverlay");
            Check(!main.IsVisible && sphere.IsVisible,
                "minimize must fully collapse the main without hiding the sphere");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (Read<Window?>("_cornerHintWin") != null)
                Invoke("CloseCornerHintWindow");
            main?.Close();
            Set("_mainWin", null);
            Set("_settingsWin", null);
            Set("_overlayMode", "desktop");
            Set("_cornerRevealEnabled", true);
            Set("_cornerRevealVisible", true);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_cornerHintManualPosition", false);
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Check(worker.Join(9000), "independent sphere WPF test stalled");
    if (failure != null)
        throw new InvalidOperationException("Independent sphere: " + failure, failure);
}

static void DesktopSphereVisibilityOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        void Set(string name, object? value) => owner.GetField(name, flags)!.SetValue(null, value);
        T Get<T>(string name) => (T)owner.GetField(name, flags)!.GetValue(null)!;
        object? Call(string name, params object[] args) => owner.GetMethod(name, flags)!.Invoke(null, args);
        Window? main = null, settings = null;
        try
        {
            main = new Window
            {
                Width = 390, Height = 425, Left = 140, Top = 155,
                Topmost = true, ShowActivated = false, ShowInTaskbar = false
            };
            Set("_mainWin", main);
            Set("_settingsWin", null);
            Set("_overlayMode", "desktop");
            Set("_cornerRevealEnabled", true);
            Set("_cornerRevealVisible", true);
            Set("_cornerHintShowDot", true);
            Set("_cornerHintShowValue", true);
            Set("_cornerHintDragging", false);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_isUserInteracting", false);
            var area = (System.Windows.Rect)Call("GetMainWorkAreaOrVirtual")!;
            var x = area.Left + 160;
            var y = area.Top + 120;
            Set("_cornerHintManualPosition", true);
            Set("_cornerHintManualX", x);
            Set("_cornerHintManualY", y);
            main.Show();
            void Sphere(string where)
            {
                Call("UpdateCornerHintWindow");
                var s = Get<Window>("_cornerHintWin");
                Check(s.IsVisible && s.Topmost && s.IsHitTestVisible &&
                    s.Content is Grid root && root.IsHitTestVisible,
                    "sphere missing or not draggable: " + where);
                Check(Math.Abs(s.Left - x) < 1 && Math.Abs(s.Top - y) < 1,
                    "manual position changed: " + where);
            }
            Sphere("mixer initially open");

            Call("ApplyCornerRevealVisualState", false);
            Check(!main.IsVisible, "collapsed desktop overlay remains visible");
            Sphere("mixer collapsed");
            Call("ApplyCornerRevealVisualState", true);
            Check(main.IsVisible && main.Opacity == 1, "main cannot reopen after collapse");
            Sphere("mixer re-opened");

            // Minus and tray are just alternate callers of the common reveal state.
            Call("MinimizeMainOverlay");
            Check(!main.IsVisible, "minus did not fully hide mixer");
            Sphere("minus collapse");
            Call("ToggleMainWindowVisibility");
            Check(main.IsVisible, "tray could not restore the mixer");
            Call("ToggleMainWindowVisibility");
            Check(!main.IsVisible, "tray could not collapse the mixer");
            Sphere("tray collapsed");

            // Normal reveal timeouts must never hide the little independent window.
            Set("_cornerRevealVisibleUntilUtc", DateTime.MinValue);
            Call("UpdateCornerRevealState");
            Sphere("timer after hover-out");
            Set("_edgeDockEnabled", true);
            Set("_edgeDockActive", true);
            Call("UpdateCornerRevealState");
            Sphere("docked");
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_cornerRevealEnabled", false);
            Call("UpdateCornerRevealState");
            Sphere("corner reveal disabled");
            settings = new Window { Width = 200, Height = 175,
                Left = 600, Top = 200, ShowInTaskbar = false, ShowActivated = false };
            Set("_settingsWin", settings);
            settings.Show();
            Call("UpdateCornerRevealState");
            Sphere("settings open");
            settings.Close();
            Set("_settingsWin", null);

            Set("_overlayMode", "gamehook");
            Set("_cornerRevealEnabled", false);
            Call("UpdateCornerHintWindow");
            Sphere("gamehook disabled-reveal independently configured dot");
            Set("_overlayMode", "desktop");
            Set("_cornerHintShowDot", false);
            Set("_cornerHintShowValue", false);
            Call("UpdateCornerHintWindow");
            Check(!Get<Window>("_cornerHintWin").IsVisible,
                "explicitly disabled sphere should disappear");
            Call("ApplyCornerRevealVisualState", false);
            Check(!Get<Window>("_cornerHintWin").IsVisible,
                "raising/lowering mixer resurrected an explicitly disabled sphere");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (settings != null && settings.IsVisible) settings.Close();
            Call("CloseCornerHintWindow");
            main?.Close();
            Set("_mainWin", null);
            Set("_settingsWin", null);
            Set("_overlayMode", "desktop");
            Set("_cornerRevealEnabled", true);
            Set("_cornerRevealVisible", true);
            Set("_cornerHintShowDot", true);
            Set("_cornerHintShowValue", true);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_cornerHintManualPosition", false);
            Set("_isUserInteracting", false);
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Check(worker.Join(10000), "desktop sphere UI regression timed out");
    if (failure != null) throw new InvalidOperationException("Desktop sphere regression: " + failure, failure);
}
static void SphereManualStateRoundTripsOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Field(string name, object? value) =>
            (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        T Read<T>(string name) => (T)(owner.GetField(name, flags) ??
            throw new MissingFieldException(name)).GetValue(null)!;
        object? Call(string name, params object[] args) =>
            (owner.GetMethod(name, flags) ?? throw new MissingMethodException(name)).Invoke(null, args);
        var (controller, audio) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-3)));
        try
        {
            Field("_ctrl", controller);
            Field("_mainWin", new Window { Left = 150, Top = 140, Width = 380, Height = 426 });
            var area = (System.Windows.Rect)(Call("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("Missing work area"));
            var x = area.Left + 120d;
            var y = area.Top + 150d;
            Field("_cornerHintManualPosition", true);
            Field("_cornerHintManualX", x);
            Field("_cornerHintManualY", y);
            Field("_edgeDockEnabled", false);
            System.Windows.Point Position() => (System.Windows.Point)(Call(
                "GetValidatedManualCornerHintPosition", 24d, 24d) ??
                    throw new InvalidOperationException("Missing sphere position"));
            Check(Position() == new System.Windows.Point(x, y),
                "manual sphere position was overridden with docking disabled");
            Field("_edgeDockEnabled", true);
            Check(Position() == new System.Windows.Point(x, y),
                "docking option forced manually positioned sphere into a corner");

            Field("_overlayMode", "gamehook");
            Field("_gameHookInputInterceptEnabled", false);
            Call("EnsureCornerHintWindow");
            var sphere = Read<Window>("_cornerHintWin");
            var root = sphere.Content as Grid ??
                throw new InvalidOperationException("Missing sphere root grid");
            Check(sphere.IsHitTestVisible && root.IsHitTestVisible && root.Cursor == Cursors.SizeAll,
                "sphere does not expose the original draggable WPF hit target");

            // The user verified the ZIP sphere visually. Preserve that exact
            // dark glass + accent rim design instead of a saturated accent disc.
            var sphereDot = Read<Border>("_cornerHintDot");
            var accent = Read<System.Windows.Media.Color>("_activeGlassAccent");
            var darkBase = (System.Windows.Media.Color)Call("BlendColor",
                System.Windows.Media.Color.FromRgb(14, 20, 32), accent, 0.22d)!;
            var accentRim = (System.Windows.Media.Color)Call("BlendColor",
                accent, System.Windows.Media.Colors.White, 0.18d)!;
            Check(sphere.Width == 24 && sphere.Height == 24 &&
                  root.Width == 24 && root.Height == 24 &&
                  sphereDot.Width == 24 && sphereDot.Height == 24 &&
                  sphereDot.CornerRadius.TopLeft == 12,
                "sphere geometry no longer matches user-verified 24px ZIP design");
            Check(sphereDot.Background is System.Windows.Media.SolidColorBrush background &&
                  background.Color == System.Windows.Media.Color.FromArgb(
                      224, darkBase.R, darkBase.G, darkBase.B),
                "sphere fill became saturated instead of dark ZIP glass");
            Check(sphereDot.BorderBrush is System.Windows.Media.SolidColorBrush rim &&
                  rim.Color == System.Windows.Media.Color.FromArgb(
                      238, accentRim.R, accentRim.G, accentRim.B),
                "sphere rim no longer follows the glass accent");
            var sphereNumber = Read<TextBlock>("_cornerHintVolumeText");
            Check(sphereNumber.HorizontalAlignment == HorizontalAlignment.Center &&
                  sphereNumber.VerticalAlignment == VerticalAlignment.Center &&
                  !sphereNumber.IsHitTestVisible,
                "volume number moved or intercepted sphere dragging");
            // The sphere is a separate Windows desktop HWND even in game-hook mode.
            // Its drag must not depend on the native game's input-interception switch.
            Check(Call("CanInteractWithCornerHint") is true,
                "game-hook mode wrongly disabled sphere mouse interaction");
            var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            root!.RaiseEvent(mouseDown);
            Check(Read<bool>("_cornerHintDragging"),
                "game-hook sphere mouse-down event did not begin dragging");
            var mouseUp = new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            };
            root.RaiseEvent(mouseUp);
            Check(!Read<bool>("_cornerHintDragging"),
                "sphere mouse-up event did not finish dragging");
            Field("_overlayMode", "desktop");
            Check(Call("CanInteractWithCornerHint") is true,
                "desktop sphere drag did not return after mode switch");

            Field("_cornerHintTransparencyPercent", 0);
            Call("ApplyCornerHintTheme");
            Check(Math.Abs(sphere.Opacity - 1d) < 0.001,
                "default sphere transparency changed previous appearance");
            Field("_cornerHintTransparencyPercent", 60);
            Call("ApplyCornerHintTheme");
            Check(Math.Abs(sphere.Opacity - 0.4d) < 0.001,
                "sphere transparency does not apply to the actual WPF window");
            Field("_cornerHintTransparencyPercent", 100);
            Call("ApplyCornerHintTheme");
            Check(Math.Abs(sphere.Opacity - 0.2d) < 0.001,
                "sphere transparency should be clamped to preserve hit testing");
            Field("_cornerHintTransparencyPercent", 35);
            Call("ApplyCornerHintTheme");

            Call("WriteCurrentStateToSettings");
            var settings = controller.Settings.Window;
            Check(settings.CornerHintManualPosition && settings.CornerHintX == x &&
                  settings.CornerHintY == y,
                "manual sphere coordinates were not persisted to the settings owner");
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            var restored = System.Text.Json.JsonSerializer.Deserialize<WindowSettings>(json)!;
            Check(restored.CornerHintManualPosition && restored.CornerHintX == x &&
                  restored.CornerHintY == y, "restarted settings would lose sphere coordinates");
            var uiSettings = controller.Settings.Ui;
            Check(uiSettings.CornerHintTransparencyPercent == 35,
                "sphere transparency was not written to settings");
            var uiJson = System.Text.Json.JsonSerializer.Serialize(uiSettings);
            var restoredUi = System.Text.Json.JsonSerializer.Deserialize<UiSettings>(uiJson)!;
            Check(restoredUi.CornerHintTransparencyPercent == 35,
                "sphere transparency lost across JSON settings roundtrip");
            Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
                "sphere configuration unexpectedly wrote audio");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Call("CloseCornerHintWindow");
            Field("_cornerHintManualPosition", false);
            Field("_cornerHintManualX", double.NaN);
            Field("_cornerHintManualY", double.NaN);
            Field("_cornerHintTransparencyPercent", 0);
            Field("_edgeDockEnabled", false);
            Field("_overlayMode", "desktop");
            Field("_mainWin", null);
            Field("_ctrl", null);
            controller.Shutdown();
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    worker.Join();
    if (failure != null) throw new InvalidOperationException("Sphere state restoration failed", failure);
}

static void SphereEdgeSnapIsOptional()
{
    var method = typeof(MiniMixerOverlay.App.Program).GetMethod(
        "SnapSphereToEdges", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingMethodException("SnapSphereToEdges");
    var area = new System.Windows.Rect(100, 200, 800, 500);
    System.Windows.Point Snap(double x, double y, bool enabled) =>
        (System.Windows.Point)(method.Invoke(null, new object[]
        { new System.Windows.Point(x, y), area, 24d, 24d, enabled }) ??
        throw new InvalidOperationException("missing snapped position"));
    Check(Snap(432, 391, false) == new System.Windows.Point(432, 391),
        "disabled docking must allow freely positioned sphere");
    Check(Snap(432, 391, true) == new System.Windows.Point(432, 391),
        "enabled docking must not force sphere to corner");
    Check(Snap(110, 391, true) == new System.Windows.Point(100, 391),
        "sphere near left edge did not snap");
    Check(Snap(871, 670, true) == new System.Windows.Point(876, 676),
        "sphere near bottom/right edge did not snap");
    Check(Snap(871, 670, false) == new System.Windows.Point(871, 670),
        "disabled docking unexpectedly snapped sphere");
}

static void ForegroundAudioTitlelessEligibility()
{
    var bridge = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
        "MiniMixerOverlay.App.GameHookBridge") ??
        throw new InvalidOperationException("GameHookBridge not found");
    var eligibility = bridge.GetMethod("IsUsableForegroundWindow",
        BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingMethodException("IsUsableForegroundWindow");
    bool Valid(uint pid, string title, bool needsTitle) =>
        (bool)(eligibility.Invoke(null, new object[] { pid, title, needsTitle }) ?? false);
    Check(Valid(42, "", false), "audio foreground must accept valid PID with blank title");
    Check(!Valid(42, "", true), "injector title requirement must remain intact");
    Check(!Valid(0, "Title", false), "audio must reject PID zero");
    Check(Valid(42, "Title", true), "valid hook window was rejected");

    // Matching the foreground volume uses its session PID, not a window title.
    var session = Session(MissingExe(), "sphere-volume", 0.24f);
    session.ProcessId = 42421;
    var (controller, audio) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-2)), session);
    var field = typeof(MiniMixerOverlay.App.Program).GetField(
        "_ctrl", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingFieldException("_ctrl");
    var match = typeof(MiniMixerOverlay.App.Program).GetMethod(
        "FindForegroundAudioEntry", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingMethodException("FindForegroundAudioEntry");
    try
    {
        field.SetValue(null, controller);
        var titleless = CreateHookForeground(new IntPtr(555), 42421, "unrelated", "");
        Check(ReferenceEquals(match.Invoke(null, new[] { titleless }), controller.AppEntries[0]),
            "foreground sphere failed to identify audio by PID without a title");
        var unrelated = CreateHookForeground(new IntPtr(666), 42422, "unrelated", "");
        Check(match.Invoke(null, new[] { unrelated }) == null,
            "foreground sphere showed volume belonging to another PID");
        Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
            "titleless foreground matching changed audio");
    }
    finally
    {
        field.SetValue(null, null);
        controller.Shutdown();
    }
}

static void AutoGameDuplicateExeOrderInvariant()
{
    var (controller, _) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-2)));
    var owner = typeof(MiniMixerOverlay.App.Program);
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var ctrlField = owner.GetField("_ctrl", flags) ?? throw new MissingFieldException("_ctrl");
    var select = owner.GetMethod("IsLikelyGameCandidate", flags) ??
        throw new MissingMethodException("IsLikelyGameCandidate");
    const uint targetPid = 43215;
    var candidate = CreateHookForeground(new IntPtr(5678), targetPid, "shared-name", "Fixture Game");
    bool Eligible() => (bool)(select.Invoke(null, new[] { candidate }) ?? false);
    AppEntry Entry(string path, AppClassification classification, uint pid) => new()
    {
        ExePath = path,
        Rule = new AppRule { Classification = classification },
        HasActiveAudio = true,
        Sessions = new List<AudioSessionInfo>
        {
            new() { ProcessId = pid, SessionIdentifier = path, Volume = 1f }
        }
    };
    var nonGame = Entry(@"C:\Utilities\shared-name.exe", AppClassification.NonGame, targetPid);
    var unrelatedGame = Entry(@"C:\OtherGames\shared-name.exe", AppClassification.Game, targetPid + 1);
    var unknown = Entry(@"C:\Unknown\shared-name.exe", AppClassification.Unknown, targetPid);
    var qualifiedGame = Entry(@"C:\Games\shared-name.exe", AppClassification.Game, targetPid);

    try
    {
        ctrlField.SetValue(null, controller);
        void Arrange(params AppEntry[] entries)
        {
            controller.AppEntries.Clear();
            foreach (var entry in entries) controller.AppEntries.Add(entry);
        }

        Arrange(nonGame, qualifiedGame);
        Check(Eligible(), "non-game with same basename suppressed subsequent qualified game");
        Arrange(unrelatedGame, qualifiedGame);
        Check(Eligible(), "game with unrelated PID suppressed subsequent qualified game");
        Arrange(unknown, qualifiedGame);
        Check(Eligible(), "unknown classification suppressed subsequent qualified game");
        Arrange(qualifiedGame, nonGame, unrelatedGame);
        Check(Eligible(), "reversing duplicate basename order changed an eligible decision");
        Arrange(nonGame, unknown, unrelatedGame);
        Check(!Eligible(), "duplicate basename without qualified entry was authorized");
        Arrange(unrelatedGame, nonGame);
        Check(!Eligible(), "matching basename with wrong PID was authorized");
    }
    finally
    {
        ctrlField.SetValue(null, null);
        controller.Shutdown();
    }
}

static void QualifiedHookTargetIdentity()
{
    var bridge = typeof(MiniMixerOverlay.App.Program).Assembly.GetType("MiniMixerOverlay.App.GameHookBridge") ??
        throw new InvalidOperationException("Hook bridge not found");
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    var match = bridge.GetMethod("MatchesQualifiedWindow", flags) ??
        throw new MissingMethodException("MatchesQualifiedWindow");
    var target = CreateHookForeground(new IntPtr(111), 321u, "fixture", "Game Window");
    bool Matches(IntPtr foregroundHwnd, uint currentPid, string currentTitle) =>
        (bool)(match.Invoke(null, new object[] { target, foregroundHwnd, currentPid, currentTitle }) ?? false);
    Check(Matches(new IntPtr(111), 321u, "Game Window"), "correct qualified target was rejected");
    Check(!Matches(new IntPtr(222), 333u, "Browser"), "foreground switched to browser");
    Check(!Matches(new IntPtr(222), 321u, "Game Window"), "new HWND was incorrectly accepted");
    Check(!Matches(new IntPtr(111), 333u, "Game Window"), "reused HWND with different PID was accepted");
    Check(!Matches(new IntPtr(111), 321u, "Different Window"), "changed title was not rejected");
    var inject = bridge.GetMethod("InjectQualifiedForegroundWindow", flags) ??
        throw new MissingMethodException("InjectQualifiedForegroundWindow");
    var result = inject.Invoke(null, new object[] { target, Path.GetTempPath() }) ??
        throw new InvalidOperationException("Missing injection result");
    var success = result.GetType().GetProperty("Success")?.GetValue(result);
    Check(success is false, "invalid foreground identity reached a successful injector path");
}

static void DockHoverRevealOnSta()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        T Get<T>(string name) => (T)(uiType.GetField(name, flags) ??
            throw new MissingFieldException(name)).GetValue(null)!;
        object? Invoke(string method, params object[] args) =>
            (uiType.GetMethod(method, flags) ?? throw new MissingMethodException(method)).Invoke(null, args);

        try
        {
            var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
            var work = (System.Windows.Rect)(Invoke("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("No work area"));
            var win = new Window
            {
                Width = 380, Height = 426,
                Left = work.Right - 380, Top = work.Top + 50
            };
            Set("_mainWin", win);
            Set("_edgeDockEnabled", true);
            Set("_edgeDockActive", true);
            Set("_edgeDockSide", "right");
            Set("_edgeDockVisibleWidth", 72);
            Set("_edgeDockRevealZoneWidth", 72);
            Set("_visibleApps", 5);
            Set("_bringToFrontOnHover", false);
            Set("_settingsWin", null);
            Set("_isUserInteracting", false);
            Set("_edgeDockVisibleUntilUtc", DateTime.MinValue);
            Invoke("ApplyEdgeDockPlacement", false, true);
            Check(!Get<bool>("_edgeDockExpanded"), "dock must initially be collapsed");

            void Tick(System.Windows.Point cursor, DateTime time) =>
                Invoke("UpdateEdgeDockStateAtCursor", cursor, time);
            var far = new System.Windows.Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
            Tick(far, now);
            Check(!Get<bool>("_edgeDockExpanded"), "distant pointer reopened dock");

            var atStripRight = new System.Windows.Point(work.Right - 1, win.Top + 25);
            Tick(atStripRight, now.AddMilliseconds(10));
            Check(Get<bool>("_edgeDockExpanded"), "right-edge hover did not reveal dock");
            Check(win.Height > 154, "right dock did not expand to full height");

            Tick(far, now.AddMilliseconds(600));
            Check(Get<bool>("_edgeDockExpanded"), "dock collapsed before hold interval");

            Tick(far, now.AddMilliseconds(1600));
            Check(!Get<bool>("_edgeDockExpanded"), "dock did not collapse after hold expiry");
            Check(Math.Abs(win.Height - 154) < 0.5, "collapsed dock has wrong geometry");

            // The adjacent reveal strip is useful even when cursor is outside the visible handle.
            Tick(new System.Windows.Point(work.Right - 72 - 15, win.Top + 25),
                now.AddMilliseconds(1700));
            Check(Get<bool>("_edgeDockExpanded"), "right adjacent reveal zone ignored");

            Set("_edgeDockSide", "left");
            Set("_edgeDockVisibleUntilUtc", DateTime.MinValue);
            Invoke("ApplyEdgeDockPlacement", false, true);
            var leftWork = (System.Windows.Rect)(Invoke("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("Missing left dock work area"));
            Tick(new System.Windows.Point(leftWork.Left + 72 + 15, win.Top + 25),
                now.AddMilliseconds(3000));
            Check(Get<bool>("_edgeDockExpanded"), "left adjacent reveal zone ignored");
            Tick(far, now.AddMilliseconds(4600));
            Check(!Get<bool>("_edgeDockExpanded"), "left dock failed hold expiry");

            Set("_isUserInteracting", true);
            Tick(far, now.AddMilliseconds(4800));
            Check(Get<bool>("_edgeDockExpanded"), "user interaction failed to hold dock open");
            Set("_isUserInteracting", false);
            Tick(far, now.AddMilliseconds(6400));
            Check(!Get<bool>("_edgeDockExpanded"), "dock stayed expanded after user interaction ended");

            Set("_settingsWin", new Window()); // not shown; no user desktop interaction
            Tick(far, now.AddMilliseconds(6500));
            Check(Get<bool>("_edgeDockExpanded"), "open settings did not keep dock expanded");
            Set("_settingsWin", null);
            Tick(far, now.AddMilliseconds(8000));
            Check(!Get<bool>("_edgeDockExpanded"), "dock failed to collapse after settings closed");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Set("_mainWin", null);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);
            Set("_edgeDockSide", "right");
            Set("_edgeDockVisibleWidth", 72);
            Set("_edgeDockRevealZoneWidth", 72);
            Set("_bringToFrontOnHover", true);
            Set("_settingsWin", null);
            Set("_isUserInteracting", false);
            Set("_edgeDockVisibleUntilUtc", DateTime.MinValue);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null)
        throw new InvalidOperationException($"Dock hover state test failed: {failure}", failure);
}

static void DockTopHandlesCrampedWorkArea()
{
    var method = typeof(MiniMixerOverlay.App.Program).GetMethod(
        "ClampWindowTop", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingMethodException("ClampWindowTop");
    double Position(double wanted, System.Windows.Rect area, double height) =>
        (double)(method.Invoke(null, new object[] { wanted, area, height }) ??
            throw new InvalidOperationException("No dock top"));

    var smallDisplay = new System.Windows.Rect(100, 200, 800, 500);
    Check(Position(450, smallDisplay, 900) == smallDisplay.Top,
        "window taller than the work area must remain placeable");
    Check(Position(500, smallDisplay, 400) == 300,
        "window with room should clamp at the lower available edge");
    Check(Position(0, smallDisplay, 400) == smallDisplay.Top,
        "window above work area must clamp at its upper edge");
}

static void OversizedWindowStaysWithinReachOnSta()
{
    Exception? failure = null;
    var sta = new Thread(() =>
    {
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        object? Call(string name, params object[] args) =>
            (uiType.GetMethod(name, flags) ?? throw new MissingMethodException(name)).Invoke(null, args);

        try
        {
            var area = (System.Windows.Rect)(Call("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("No work area"));
            Check(area.Width > 0 && area.Height > 0, "No display geometry");
            var win = new Window
            {
                Width = area.Width + 300,
                Height = area.Height + 200,
                Left = area.Left + 50,
                Top = area.Top + 30
            };
            Set("_mainWin", win);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Call("EnsureMainWindowInBounds", false);
            Check(Math.Abs(win.Left - area.Left) < 0.5 &&
                  Math.Abs(win.Top - area.Top) < 0.5,
                  "oversized undocked window should be anchored to work area origin");

            Set("_edgeDockEnabled", true);
            Set("_edgeDockActive", true);
            Set("_edgeDockSide", "right");
            Set("_edgeDockVisibleWidth", 72);
            win.Left = area.Right + 50;
            win.Top = area.Bottom + 50;
            Call("EnsureMainWindowInBounds", true);
            Check(Math.Abs(win.Left - (area.Right - 72)) < 0.5 &&
                  Math.Abs(win.Top - area.Top) < 0.5,
                  "oversized dock must keep its visible edge accessible");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Set("_mainWin", null);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockSide", "right");
            Set("_edgeDockVisibleWidth", 72);
        }
    });
    sta.SetApartmentState(ApartmentState.STA);
    sta.Start();
    sta.Join();
    if (failure != null) throw new InvalidOperationException($"Window bounds test failed: {failure}", failure);
}

static void OfflineDockGeometryOnSta()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Field(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        object? Call(string name, params object[] args) =>
            (uiType.GetMethod(name, flags) ?? throw new MissingMethodException(name)).Invoke(null, args);

        try
        {
            var window = new Window { Width = 380, Height = 426, Left = 100, Top = 100 };
            Field("_mainWin", window);
            var glass = new Border();
            var rows = new Grid();
            rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            glass.Child = rows;
            Field("_mainGlassBorder", glass);
            Field("_edgeDockEnabled", true);
            Field("_edgeDockActive", true);
            Field("_edgeDockExpanded", true);
            Field("_edgeDockSide", "right");
            Field("_edgeDockVisibleWidth", 72);
            Field("_visibleApps", 5);
            var workArea = (System.Windows.Rect)(Call("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("Missing work area"));
            var compactHeight = (double)(Call("ComputeHeight", 1) ?? 0d);
            var expandedHeight = (double)(Call("ComputeHeight", 5) ?? 0d);

            Call("ApplyEdgeDockPlacement", false, true);
            Check(Math.Abs(window.Height - compactHeight) < 0.5,
                $"collapsed dock height {window.Height} differs from expected compact {compactHeight}");
            Check(Math.Abs(window.Left - (workArea.Right - 72)) < 1.5,
                "collapsed dock did not leave the expected visible width");
            Check(glass.Clip is System.Windows.Media.RectangleGeometry rightClip &&
                Math.Abs(rightClip.Rect.X) < 0.5 && Math.Abs(rightClip.Rect.Width - 72) < 0.5,
                "right dock renders into adjacent monitor instead of visible strip");
            Check(rows.RowDefinitions[0].Height.Value == 0 &&
                  rows.RowDefinitions[2].Height.Value == 0,
                "collapsed dock header/footer leave quick slider clipped");

            Call("ApplyEdgeDockPlacement", true, true);
            Check(Math.Abs(window.Height - expandedHeight) < 0.5,
                "expanded dock retained compact height");
            Check(Math.Abs(window.Left - (workArea.Right - window.Width)) < 1.5,
                "expanded dock was not flush with work-area edge");
            Check(glass.Clip == null, "expanding dock left its content clipped");
            Check(rows.RowDefinitions[0].Height.Value == 42 &&
                  rows.RowDefinitions[2].Height.Value == 28,
                "expanded dock failed to restore header/footer");

            Field("_edgeDockSide", "left");
            window.Left = workArea.Left;
            var leftArea = (System.Windows.Rect)(Call("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("Missing left work area"));
            Call("ApplyEdgeDockPlacement", false, true);
            Check(Math.Abs(window.Height - compactHeight) < 0.5,
                "left-collapsed dock has incorrect height");
            Check(Math.Abs(window.Left - (leftArea.Left - (window.Width - 72))) < 1.5,
                "left-collapsed dock did not leave the expected visible width");
            Check(glass.Clip is System.Windows.Media.RectangleGeometry leftClip &&
                Math.Abs(leftClip.Rect.X - (window.Width - 72)) < 0.5 &&
                Math.Abs(leftClip.Rect.Width - 72) < 0.5,
                "left dock did not clip the off-monitor portion");
            Call("ApplyEdgeDockPlacement", true, true);
            Check(Math.Abs(window.Height - expandedHeight) < 0.5 &&
                  Math.Abs(window.Left - leftArea.Left) < 1.5,
                "left-expanded dock is not full-height or flush to the edge");

            // Restoring a bottom-corner compact dock must anchor after its height changes.
            Field("_edgeDockSide", "right");
            window.Left = workArea.Right - window.Width;
            var cornerField = uiType.GetField("_rememberedSnapCorner", flags) ??
                throw new MissingFieldException("_rememberedSnapCorner");
            cornerField.SetValue(null, Enum.Parse(cornerField.FieldType, "BottomRight"));
            var bottomArea = (System.Windows.Rect)(Call("GetMainWorkAreaOrVirtual") ??
                throw new InvalidOperationException("Missing bottom-corner work area"));
            Call("RestoreDockPositionIfEnabled", false);
            Check(Math.Abs(window.Height - compactHeight) < 0.5 &&
                  Math.Abs(window.Top - (bottomArea.Bottom - window.Height)) < 1.5,
                "restored bottom compact dock is not anchored at the bottom");
            Call("RestoreDockPositionIfEnabled", true);
            Check(Math.Abs(window.Height - expandedHeight) < 0.5 &&
                  Math.Abs(window.Top - (bottomArea.Bottom - window.Height)) < 1.5,
                "restored bottom expanded dock is not anchored at the bottom");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Field("_mainWin", null);
            Field("_mainGlassBorder", null);
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_edgeDockExpanded", true);
            Field("_edgeDockSide", "right");
            Field("_visibleApps", 5);
            var corner = uiType.GetField("_rememberedSnapCorner", flags);
            if (corner != null) corner.SetValue(null, Enum.Parse(corner.FieldType, "TopRight"));
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new InvalidOperationException($"Offline dock geometry failed: {failure}", failure);
}

static void VisibleWpfSmoke(bool exerciseRealHover, bool exercisePopupRefresh = false,
    TimeSpan? boundedSoakDuration = null, bool showSettings = false, bool showGlass = false,
    bool showAdvancedGlass = false)
{
    Exception? soakFailure = null;
    var thread = new Thread(() =>
    {
        var exeA = MissingExe();
        var exeB = MissingExe();
        var first = Session(exeA, "visible-a", 0.8f);
        first.DisplayName = "SAFE TEST A (Fake Audio)";
        var second = Session(exeB, "visible-b", 0.3f);
        second.DisplayName = "SAFE TEST B (Fake Audio)";
        var (controller, fake) = CreateController(
            new MemoryRules(DateTime.UtcNow.AddDays(-30)), first, second);
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        object? Call(string method, params object[] arguments) =>
            (uiType.GetMethod(method, flags) ?? throw new MissingMethodException(method)).Invoke(null, arguments);

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var win = new Window
        {
            Title = "Mini Mixer - SAFE WPF Smoke (Fake Audio)",
            Width = 380,
            Height = 426,
            Left = 150,
            Top = 120,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = boundedSoakDuration == null,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        Set("_ctrl", controller);
        Set("_mainWin", win);
        // A fake provider guarantees no real audio access even during RefreshSessions().
        // Real hover needs _settingsWin == null or the production state machine holds open.
        Set("_settingsWin", (exerciseRealHover || showSettings) ? null : new Window());
        Set("_edgeDockEnabled", false);
        Set("_edgeDockActive", false);
        Set("_edgeDockExpanded", true);
        controller.Settings.Ui.ShowOnlyActiveAudio = true;

        var outer = (Border)(Call("BuildGlass") ??
            throw new InvalidOperationException("Production glass layout not available"));
        outer.Child = (UIElement)(Call("BuildMainLayout", app) ??
            throw new InvalidOperationException("Production layout not available"));
        Set("_mainGlassBorder", outer);
        win.Content = outer;
        // Smoke-only keyboard shortcuts. The regular production UI remains unchanged.
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F6 || e.Key == Key.F7)
            {
                Set("_edgeDockEnabled", true);
                Set("_edgeDockActive", true);
                Call("ApplyEdgeDockPlacement", e.Key == Key.F7, true);
                Call("Refresh");
                e.Handled = true;
            }
            if (e.Key == Key.Escape) win.Close();
        };

        var logFile = Path.Combine(Path.GetTempPath(), "mini-mixer-safe-smoke-" + Environment.ProcessId + ".json");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var ticks = 0;
        var samples = new List<(double Seconds, double PrivateMiB, int Handles, double CpuSeconds, long ManagedBytes)>();
        double firstPrivate = -1;
        int firstHandles = -1;
        timer.Tick += (_, _) =>
        {
            try
            {
            if (exerciseRealHover && (bool)(uiType.GetField("_edgeDockActive", flags)?.GetValue(null) ?? false))
                Call("UpdateEdgeDockState");
            if (exercisePopupRefresh)
                Call("Refresh"); // safe fake audio only; exercises the real popup anchor during UI polling

            var activePopup = uiType.GetField("_activeAppPopup", flags)?.GetValue(null)
                as System.Windows.Controls.Primitives.Popup;
            var obj = new {
                pid = Environment.ProcessId,
                popupOpen = activePopup?.IsOpen ?? false,
                popupAnchorAttached = activePopup?.PlacementTarget is FrameworkElement anchor &&
                    anchor.Parent is not null,
                windowVisible = win.IsVisible,
                volumeWrites = fake.VolumeWrites.Select(x => new { x.Id, x.Volume }).ToArray(),
                muteWrites = fake.MuteWrites.Select(x => new { x.Id, x.Mute }).ToArray(),
                dockExpanded = (bool)(uiType.GetField("_edgeDockExpanded", flags)?.GetValue(null) ?? true)
            };
            if (boundedSoakDuration == null || ticks % 20 == 0)
                File.WriteAllText(logFile, System.Text.Json.JsonSerializer.Serialize(obj));

            if (boundedSoakDuration != null)
            {
                ++ticks;
                if (!win.IsVisible || !ReferenceEquals(win.Content, outer))
                    throw new InvalidOperationException("WPF main content was lost during soak");
                if (fake.VolumeWrites.Count != 0 || fake.MuteWrites.Count != 0)
                    throw new InvalidOperationException("WPF render-only soak emitted audio commands");
                if (ticks % 20 == 0 || ticks == 1)
                {
                    using var proc = System.Diagnostics.Process.GetCurrentProcess();
                    proc.Refresh();
                    var priv = proc.PrivateMemorySize64 / 1048576d;
                    var handles = proc.HandleCount;
                    firstPrivate = firstPrivate < 0 ? priv : firstPrivate;
                    firstHandles = firstHandles < 0 ? handles : firstHandles;
                    samples.Add((elapsed.Elapsed.TotalSeconds, priv, handles,
                        proc.TotalProcessorTime.TotalSeconds, GC.GetTotalMemory(false)));
                }
                if (elapsed.Elapsed >= boundedSoakDuration)
                    win.Close();
            }
            }
            catch (Exception ex)
            {
                soakFailure = ex;
                win.Close();
            }
        };
        win.Closed += (_, _) =>
        {
            timer.Stop();
            controller.Shutdown();
            if (File.Exists(logFile)) File.Delete(logFile);
        };
        app.MainWindow = win;
        Call("Refresh");
        if (showSettings)
        {
            // The real production settings window with only fake in-memory audio and stores.
            // Do not call Program.Main, StartAudio, injector, or user's settings store.
            win.ContentRendered += (_, _) => win.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    Call("ShowSettings");
                    if (!showGlass) return;
                    // Navigate the real displayed WPF ScrollViewer; no alternate Settings UI.
                    win.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var settings = uiType.GetField("_settingsWin", flags)?.GetValue(null) as Window;
                        if (settings?.Content is not Border panel || panel.Child is not ScrollViewer scroller)
                            throw new InvalidOperationException("Visible settings scroll owner unavailable");
                        settings.UpdateLayout();
                        if (showAdvancedGlass && scroller.Content is StackPanel options)
                        {
                            var details = options.Children.OfType<Expander>().Single(e => e.Name == "AdvancedGlassOptions");
                            var looks = options.Children.OfType<ComboBox>().Single(c => c.Name == "GlassLookPreset");
                            details.IsExpanded = true;
                            looks.SelectedIndex = 0; // Act through the real ComboBox handler with fake settings.
                            settings.UpdateLayout();
                            var label = looks.Template?.FindName("SelectedText", looks) as TextBlock;
                            Console.WriteLine($"VISIBLE_GLASS_CONTROL selected={looks.SelectedItem} display={looks.SelectionBoxItem} visual={label?.Text} " +
                                $"strength={uiType.GetField("_glassStrength", flags)?.GetValue(null)} " +
                                $"transparent={uiType.GetField("_glassTransparency", flags)?.GetValue(null)}");
                            var detailsY = details.TranslatePoint(new System.Windows.Point(0, 0), options).Y;
                            scroller.ScrollToVerticalOffset(Math.Max(0, detailsY - 110));
                        }
                        else
                        {
                            // Glass appearance is in the latter part of the existing Settings content.
                            scroller.ScrollToVerticalOffset(scroller.ScrollableHeight * 0.72);
                        }
                        // Capture the actual rendered WPF visual; a screen-coordinate GDI copy
                        // can accidentally contain another user's overlapping desktop window.
                        win.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                settings.UpdateLayout();
                                if (settings.Content is not FrameworkElement visual)
                                    throw new InvalidOperationException("WPF Settings visual unavailable");
                                var width = (int)Math.Ceiling(visual.ActualWidth);
                                var height = (int)Math.Ceiling(visual.ActualHeight);
                                if (width < 250 || height < 300)
                                    throw new InvalidOperationException("WPF settings layout not settled");
                                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                bmp.Render(visual);
                                var dir = @"F:\mini_mixer_overlay\Docs\reviews\MM1_SETTINGS_FINAL_2026-10-23_EVIDENCE";
                                Directory.CreateDirectory(dir);
                                var name = showAdvancedGlass ? "settings-wpf-rendered-advanced.png" : "settings-wpf-rendered-collapsed.png";
                                using var file = File.Create(Path.Combine(dir, name));
                                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                                encoder.Save(file);
                                Console.WriteLine("WPF_RENDER_EVIDENCE " + Path.Combine(dir, name) +
                                    " fake_audio_writes=" + (fake.VolumeWrites.Count + fake.MuteWrites.Count));
                            }
                            catch (Exception ex) { soakFailure = ex; win.Close(); }
                        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        timer.Start();
        app.Run(win);
        if (boundedSoakDuration != null)
        {
            if (soakFailure == null && samples.Count < 3)
                soakFailure = new InvalidOperationException("Insufficient WPF lifetime samples");
            if (soakFailure == null)
            {
                // Deliberately generous catastrophic-leak and stalled-dispatcher bounds.
                // These are development smoke guards, NOT Stable performance targets.
                var privateGrowth = samples.Max(x => x.PrivateMiB) - firstPrivate;
                var handleGrowth = samples.Max(x => x.Handles) - firstHandles;
                var minimumTicks = boundedSoakDuration.Value.TotalSeconds * 1.5;
                if (privateGrowth > 96 || handleGrowth > 128 || ticks < minimumTicks)
                    soakFailure = new InvalidOperationException(
                        $"WPF smoke exceeded resource/heartbeat guard: peakMemoryDeltaMiB={privateGrowth:F1}, " +
                        $"peakHandleDelta={handleGrowth}, ticks={ticks}, requiredTicks={minimumTicks:F0}");
            }
            Console.WriteLine($"OWN_WPF_SOAK actual_seconds={elapsed.Elapsed.TotalSeconds:F1} " +
                $"ticks={ticks} samples={samples.Count} initial_private_mib={firstPrivate:F1} " +
                $"final_private_mib={(samples.Count>0?samples[^1].PrivateMiB:0):F1} " +
                $"max_private_mib={(samples.Count>0?samples.Max(x=>x.PrivateMiB):0):F1} " +
                $"initial_handles={firstHandles} " +
                $"final_handles={(samples.Count>0?samples[^1].Handles:0)} " +
                $"max_handles={(samples.Count>0?samples.Max(x=>x.Handles):0)} " +
                $"fake_audio_writes={fake.VolumeWrites.Count+fake.MuteWrites.Count}");
            if (soakFailure == null)
                Console.WriteLine("PASS OWN_WPF_RENDER_REFRESH_SOAK bounded_run=1 input_audio_fake=1");
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (soakFailure != null)
        throw new InvalidOperationException($"Owned WPF soak: {soakFailure.Message}", soakFailure);
}

static void OfflinePopupAnchorOnMuteRefresh()
{
    Exception? failure = null;
    var sta = new Thread(() =>
    {
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        void RefreshUi() =>
            (uiType.GetMethod("Refresh", flags) ?? throw new MissingMethodException("Refresh")).Invoke(null, null);

        var exe = MissingExe();
        var (controller, audio) = CreateController(
            new MemoryRules(DateTime.UtcNow.AddDays(-40)),
            Session(exe, "popup-anchor", 0.06f));
        var strip = new StackPanel();
        try
        {
            Set("_ctrl", controller);
            Set("_appList", new StackPanel());
            Set("_activeAppsStrip", strip);
            Set("_settingsWin", new Window()); // no real audio polling
            Set("_mainWin", null);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);
            controller.Settings.Ui.ShowOnlyActiveAudio = true;
            RefreshUi();
            Check(strip.Children.Count == 1 && strip.Children[0] is Border,
                "missing initial audio popup icon");
            var anchor = (Border)strip.Children[0];
            // The real popup uses PlacementTarget=anchor. Keep it closed in an offline
            // test to avoid displaying an uncontrolled native popup window.
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top
            };
            Set("_activeAppPopup", popup);

            for (var i = 0; i < 3; ++i)
            {
                controller.SetMute(exe, true);
                RefreshUi();
                Check(ReferenceEquals(strip.Children[0], anchor) &&
                      ReferenceEquals(popup.PlacementTarget, anchor) &&
                      ReferenceEquals(anchor.Parent, strip),
                    "muting detached the popup placement target");
                Check(anchor.ToolTip?.ToString()?.Contains("Mute") == true,
                    "strip icon did not reflect mute");

                controller.SetMute(exe, false);
                RefreshUi();
                Check(ReferenceEquals(strip.Children[0], anchor) &&
                      ReferenceEquals(popup.PlacementTarget, anchor) &&
                      ReferenceEquals(anchor.Parent, strip),
                    "unmuting detached the popup placement target");
                Check(anchor.ToolTip?.ToString()?.Contains("6%") == true,
                    "strip icon did not restore the volume tooltip");
            }

            // The controller may publish a new AppEntry instance after enumeration.
            // The old icon still anchors the popup, but its click must use new data.
            var updated = new AppEntry
            {
                ExePath = exe,
                DisplayName = "Updated audio app",
                CombinedVolume = 0.48f,
                HasActiveAudio = true,
                Sessions = new List<AudioSessionInfo>
                {
                    new() { SessionIdentifier = "popup-anchor", Volume = 0.48f }
                }
            };
            controller.AppEntries.Clear();
            controller.AppEntries.Add(updated);
            RefreshUi();
            Check(ReferenceEquals(strip.Children[0], anchor) &&
                  ReferenceEquals(anchor.Tag, updated) &&
                  ReferenceEquals(popup.PlacementTarget, anchor),
                "new app snapshot replaced anchor or left a stale click target");
            Check(anchor.ToolTip?.ToString()?.Contains("48%") == true,
                "new audio snapshot did not update strip tooltip");

            Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 6,
                "popup anchor refresh issued extra audio writes");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Set("_activeAppPopup", null);
            Set("_ctrl", null);
            Set("_appList", null);
            Set("_activeAppsStrip", null);
            Set("_settingsWin", null);
            Set("_mainWin", null);
            controller.Shutdown();
        }
    });
    sta.SetApartmentState(ApartmentState.STA);
    sta.Start();
    sta.Join();
    if (failure != null) throw new InvalidOperationException($"Popup anchor test failed: {failure}", failure);
}

static void OfflineWpfSliderMuteOnSta()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var uiType = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Field(string name, object? value) =>
            (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        void RefreshUi() =>
            (uiType.GetMethod("Refresh", flags) ??
                throw new MissingMethodException("Refresh")).Invoke(null, null);
        var exe = MissingExe();
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-40));
        var (controller, audio) = CreateController(rules, Session(exe, "wpf-controls", 0.8f));
        var list = new StackPanel();
        try
        {
            Field("_ctrl", controller);
            Field("_appList", list);
            Field("_activeAppsStrip", new StackPanel());
            Field("_settingsWin", new Window()); // blocks polling
            Field("_mainWin", null);
            controller.Settings.Ui.ShowOnlyActiveAudio = false;
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_edgeDockExpanded", true);
            RefreshUi();

            Check(list.Children.Count == 1 && list.Children[0] is Border,
                "normal app card was not built");
            var normalCard = (Border)list.Children[0];
            var normalGrid = normalCard.Child as Grid ??
                throw new InvalidOperationException("Normal card has no grid");
            var normalSlider = normalGrid.Children.OfType<Slider>().Single();
            var normalIcon = normalGrid.Children.OfType<Border>().Single();
            Check(normalSlider.Orientation == Orientation.Horizontal &&
                  Math.Abs(normalSlider.Value - 80) < 0.1,
                  "normal volume slider starts at the wrong value");
            Check(audio.VolumeWrites.Count == 0, "initial WPF card render wrote audio");
            normalSlider.Value = 63;
            Check(audio.VolumeWrites.Count == 1 &&
                  audio.VolumeWrites[0].Id == "wpf-controls" &&
                  Math.Abs(audio.VolumeWrites[0].Volume - 0.63f) < 0.001f,
                  "normal slider did not invoke the canonical manual volume owner");
            Check(rules.GetRule(exe)?.ManualOverride == true,
                "normal slider did not persist manual override");

            void Click(Border border)
            {
                var ev = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent
                };
                border.RaiseEvent(ev);
            }

            Click(normalIcon);
            Check(audio.MuteWrites.Count == 1 && audio.MuteWrites[0] == ("wpf-controls", true),
                "normal icon did not issue mute");
            Check(!normalSlider.IsEnabled, "normal slider remained enabled when muted");
            Click(normalIcon);
            Check(audio.MuteWrites.Count == 2 && !audio.MuteWrites[^1].Mute && normalSlider.IsEnabled,
                "normal icon did not restore unmuted slider");

            Field("_edgeDockEnabled", true);
            Field("_edgeDockActive", true);
            Field("_edgeDockExpanded", false);
            RefreshUi();
            Check(list.Children.Count == 1 && list.Children[0] is Border,
                "compact dock card was not built");
            var compactCard = (Border)list.Children[0];
            var compactPanel = compactCard.Child as StackPanel ??
                throw new InvalidOperationException("Compact card has no stack");
            var compactSlider = compactPanel.Children.OfType<Slider>().Single();
            var compactIcon = compactPanel.Children.OfType<Border>().Single();
            Check(compactSlider.Orientation == Orientation.Vertical &&
                  Math.Abs(compactSlider.Value - 63) < 0.1,
                  "compact slider did not inherit latest manually selected volume");
            var originalWrites = audio.VolumeWrites.Count;
            compactSlider.Value = 41;
            Check(audio.VolumeWrites.Count == originalWrites + 1 &&
                  Math.Abs(audio.VolumeWrites[^1].Volume - 0.41f) < 0.001f,
                  "compact slider did not invoke canonical volume owner");
            Click(compactIcon);
            Check(audio.MuteWrites[^1].Mute && !compactSlider.IsEnabled,
                  "compact icon did not mute the session");
            Click(compactIcon);
            Check(!audio.MuteWrites[^1].Mute && compactSlider.IsEnabled,
                  "compact icon failed to unmute");
            Check(rules.GetRule(exe)?.ManualOverride == true,
                  "compact actions lost the manual override");
            var writesBeforeRerender = audio.VolumeWrites.Count;
            var mutesBeforeRerender = audio.MuteWrites.Count;
            RefreshUi();
            Check(list.Children.Count == 1 && list.Children[0] is Border refreshedCard &&
                  refreshedCard.Child is StackPanel refreshedPanel &&
                  refreshedPanel.Children.OfType<Slider>().Single() is Slider refreshedSlider &&
                  Math.Abs(refreshedSlider.Value - 41) < 0.1 && refreshedSlider.IsEnabled,
                  "compact slider reverted to stale value after rerender");
            Check(audio.VolumeWrites.Count == writesBeforeRerender &&
                  audio.MuteWrites.Count == mutesBeforeRerender,
                  "rerender emitted unintended audio commands");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_edgeDockExpanded", true);
            Field("_ctrl", null);
            Field("_appList", null);
            Field("_activeAppsStrip", null);
            Field("_settingsWin", null);
            Field("_mainWin", null);
            controller.Shutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null)
        throw new InvalidOperationException($"Offline WPF card control test failed: {failure}", failure);
}

static void MasterCardUsesRealEndpointStateOnSta()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var master = Session("__system_master__", "__system_master__|real-endpoint-fixture", 0.38f);
        master.DisplayName = "Systemlautstärke";
        master.ExeName = "SystemMaster";
        master.IsMuted = true;
        var sounds = Session("__system_sound__", "notification-fixture", 0.09f);
        sounds.DisplayName = "Systemklänge";
        sounds.IsSystemSound = true;
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-40));
        var audio = new FakeAudio(new[] { master, sounds });
        var classifier = new FakeClassifier();
        var controller = new MixerController(audio, new GuardEngine(rules, classifier),
            classifier, rules, new FakeSettings(), guardLogFilePath: Path.Combine(
                Path.GetTempPath(), "mini-mixer-regression-logs-" + Environment.ProcessId + "-" +
                System.Diagnostics.Process.GetCurrentProcess().StartTime.Ticks, "wpf-audit.log"));
        var type = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Field(string name, object? value) =>
            (type.GetField(name, flags) ?? throw new MissingFieldException(name))
                .SetValue(null, value);
        void RefreshUi() =>
            (type.GetMethod("Refresh", flags) ??
                throw new MissingMethodException("Refresh")).Invoke(null, null);
        var cards = new StackPanel();
        try
        {
            controller.Initialize();
            Field("_ctrl", controller);
            Field("_appList", cards);
            Field("_activeAppsStrip", new StackPanel());
            Field("_settingsWin", new Window()); // no actual audio polling
            Field("_mainWin", null);
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_edgeDockExpanded", true);
            controller.Settings.Ui.ShowOnlyActiveAudio = true;
            RefreshUi();
            Check(cards.Children.Count == 2,
                "muted master and active System Sounds must remain separate visible cards");
            var grid = ((Border)cards.Children[0]).Child as Grid ??
                throw new InvalidOperationException("Master card did not render as normal WPF control");
            var slider = grid.Children.OfType<Slider>().Single();
            var value = grid.Children.OfType<TextBlock>().FirstOrDefault(t=>t.Text=="0%");
            var icon = grid.Children.OfType<Border>().Single();
            Check(value != null && !slider.IsEnabled &&
                  Math.Abs(slider.Value-38)<0.01,
                "muted master card lost the true endpoint volume (38%)");
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice,
                Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            };
            icon.RaiseEvent(click);
            Check(audio.MuteWrites.Count==1 &&
                  audio.MuteWrites[0] == ("__system_master__|real-endpoint-fixture", false) &&
                  slider.IsEnabled && value!.Text=="38%",
                "unmuted master UI displayed 0% or issued mute to System Sounds");
            Check(audio.VolumeWrites.Count==0 &&
                  rules.GetRule("__system_master__")==null,
                "unmute master accidentally changed the volume or created an automatic install rule");
            master.Volume=0f;master.IsMuted=false;
            controller.RefreshSessions(); // snapshot owner before UI-only rerender
            RefreshUi();
            Check(cards.Children.Count==2 &&
                  ((Border)cards.Children[0]).Child is Grid firstGrid &&
                  firstGrid.Children.OfType<TextBlock>().Any(t=>t.Text=="0%"),
                "0%-but-not-muted master was hidden by active-only filter");
        }
        catch(Exception ex){failure=ex;}
        finally
        {
            Field("_ctrl", null);
            Field("_appList", null);
            Field("_activeAppsStrip", null);
            Field("_settingsWin", null);
            Field("_mainWin", null);
            Field("_edgeDockEnabled", false);
            Field("_edgeDockActive", false);
            Field("_edgeDockExpanded", true);
            controller.Shutdown();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start(); thread.Join();
    if(failure!=null)throw new InvalidOperationException(
        $"Master WPF card regression: {failure}",failure);
}

static void OfflineWpfFilterOnSta()
{
    Exception? failure = null;
    var sta = new Thread(() =>
    {
        try { OfflineWpfFilter(); }
        catch (Exception ex) { failure = ex; }
    });
    sta.SetApartmentState(ApartmentState.STA);
    sta.Start();
    sta.Join();
    if (failure != null) throw new InvalidOperationException("Offline WPF renderer failed", failure);
}

static void OfflineWpfFilter()
{
    var active = Session(MissingExe(), "ui-active", 0.8f);
    var inactive = Session(MissingExe(), "ui-inactive", 0f);
    var muted = Session(MissingExe(), "ui-muted", 0.5f);
    muted.IsMuted = true;
    var system = Session("__system_sound__", "ui-system", 0f);
    system.IsSystemSound = true;
    var (controller, audio) = CreateController(new MemoryRules(DateTime.UtcNow.AddDays(-40)),
        active, inactive, muted, system);
    var uiType = typeof(MiniMixerOverlay.App.Program);
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    void Field(string name, object? value) =>
        (uiType.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
    void RefreshUi() =>
        (uiType.GetMethod("Refresh", flags) ?? throw new MissingMethodException("Refresh")).Invoke(null, null);

    var appList = new StackPanel();
    var strip = new StackPanel();
    try
    {
        Field("_ctrl", controller);
        Field("_appList", appList);
        Field("_activeAppsStrip", strip);
        Field("_settingsWin", new Window()); // disables real audio poll; fixture is never shown
        Field("_mainWin", null);

        Check(controller.AppEntries.Count == 4, "fixture aggregation unexpectedly lost a session");
        controller.Settings.Ui.ShowOnlyActiveAudio = true;
        RefreshUi();
        Check(appList.Children.Count == 2, "active filter did not hide the unmuted silent entry");
        Check(strip.Children.Count == 2, "active-app strip did not apply the same filter");

        controller.Settings.Ui.ShowOnlyActiveAudio = false;
        RefreshUi();
        Check(appList.Children.Count == 4, "render path ignores explicit configuration");
        Check(strip.Children.Count == 4, "active-app strip diverges from app cards");

        // In collapsed dock mode the preferred system card is present but filtered out.
        // A live active app must still have an interactive compact card rather than a placeholder.
        controller.Settings.Ui.ShowOnlyActiveAudio = true;
        Field("_edgeDockEnabled", true);
        Field("_edgeDockActive", true);
        Field("_edgeDockExpanded", false);
        RefreshUi();
        Check(appList.Children.Count == 1 && appList.Children[0] is Border,
            "collapsed dock fell back to an inactive preferred card, hiding an active app");
        Check(strip.Children.Count == 2, "collapsed dock strip lost eligible app choices");
        var compact = (StackPanel)((Border)appList.Children[0]).Child;
        Check(compact.HorizontalAlignment == HorizontalAlignment.Left,
            "right dock quick controls were drawn on the neighboring monitor");
        Field("_edgeDockSide", "left");
        RefreshUi();
        compact = (StackPanel)((Border)appList.Children[0]).Child;
        Check(compact.HorizontalAlignment == HorizontalAlignment.Right,
            "left dock quick controls were drawn off its visible strip");
        Field("_edgeDockSide", "right");
        Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
            "offline rendering changed audio");
    }
    finally
    {
        Field("_edgeDockEnabled", false);
        Field("_edgeDockActive", false);
        Field("_edgeDockExpanded", true);
        Field("_ctrl", null);
        Field("_appList", null);
        Field("_activeAppsStrip", null);
        Field("_settingsWin", null);
        Field("_mainWin", null);
        controller.Shutdown();
    }
}

static string MissingExe() => Path.Combine(Path.GetTempPath(), "mini-mixer-guard-test-" + Guid.NewGuid().ToString("N"), "app.exe");

static (MixerController Controller, FakeAudio Audio) CreateController(IRuleStore rules, params AudioSessionInfo[] sessions)
{
    var audio = new FakeAudio(sessions);
    var classifier = new FakeClassifier();
    var guard = new GuardEngine(rules, classifier);
    // All fixture controllers must use an isolated log, never the user's
    // AppData guard.log. This directory is removed after the regression suite.
    var logPath = Path.Combine(Path.GetTempPath(),
        "mini-mixer-regression-logs-" + Environment.ProcessId + "-" +
        System.Diagnostics.Process.GetCurrentProcess().StartTime.Ticks,
        "guard.log");
    var controller = new MixerController(audio, guard, classifier, rules,
        new FakeSettings(), guardLogFilePath: logPath);
    controller.Initialize();
    return (controller, audio);
}

static AudioSessionInfo Session(string path, string id, float volume = 0.8f) => new()
{
    ExePath = path,
    ExeName = Path.GetFileName(path),
    DisplayName = "Regression Test App",
    SessionIdentifier = id,
    Volume = volume,
    ProcessId = 0
};

static void HookQuarantinedClientRecoveryOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var type = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
            "MiniMixerOverlay.App.GameHookOverlayRuntime") ??
            throw new InvalidOperationException("GameHook runtime type not found");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var processId = (uint)Environment.ProcessId;
        var first = new Window { Width = 36, Height = 36, ShowInTaskbar = false };
        Window? receiver = null;
        System.Windows.Threading.Dispatcher? receiverDispatcher = null;
        IntPtr clientHwnd = IntPtr.Zero;
        using var ready = new ManualResetEventSlim(false);
        int received = 0;
        Exception? receiverError = null;
        var client = new Thread(() =>
        {
            try
            {
                receiver = new Window { Width = 36, Height = 36, ShowInTaskbar = false };
                clientHwnd = new System.Windows.Interop.WindowInteropHelper(receiver).EnsureHandle();
                System.Windows.Interop.HwndSource.FromHwnd(clientHwnd)?.AddHook(
                    (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        if (msg == 0x4A)
                        {
                            Interlocked.Increment(ref received);
                            handled = true;
                            return new IntPtr(1);
                        }
                        return IntPtr.Zero;
                    });
                receiverDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex) { receiverError = ex; ready.Set(); }
            finally { receiver?.Close(); }
        });
        client.SetApartmentState(ApartmentState.STA);
        client.IsBackground = true;
        client.Start();
        object? runtime = null;
        try
        {
            Check(ready.Wait(3000) && clientHwnd != IntPtr.Zero,
                "recovery receiver HWND unavailable");
            _ = new System.Windows.Interop.WindowInteropHelper(first).EnsureHandle();
            runtime = Activator.CreateInstance(type, new object?[]
            {
                first, null, "recovery-bounded-" + Guid.NewGuid().ToString("N")
            })!;
            Check((bool)type.GetMethod("Start")!.Invoke(runtime, null)!,
                "isolated recovery host did not start");
            var clients = (System.Collections.IDictionary)type.GetField("_clients", flags)!.GetValue(runtime)!;
            var candidates = (System.Collections.IDictionary)type.GetField("_recoveryCandidates", flags)!.GetValue(runtime)!;
            var pending = (System.Collections.IDictionary)type.GetField("_pendingCloseAfterEviction", flags)!.GetValue(runtime)!;
            type.GetMethod("HandleClientConnect", flags)!.Invoke(runtime, new object[] { clientHwnd });
            Check(clients.Contains(processId), "client not admitted before timeout");
            var staleClient = clients[processId]!;
            type.GetMethod("DropUnresponsiveClient", flags)!.Invoke(runtime, new[] { staleClient });
            Check(!clients.Contains(processId) && candidates.Contains(processId) && pending.Contains(processId),
                "timeout did not retain a bounded reconnect candidate");

            var recoveryType = candidates[processId]!.GetType();
            var nextTry = recoveryType.GetField("NextAttemptAtMs")!;
            var expires = recoveryType.GetField("ExpiresAtMs")!;
            var probe = type.GetMethod("RecoverResponsiveClients", flags)!;
            nextTry.SetValue(candidates[processId], Environment.TickCount64 + 3000L);
            probe.Invoke(runtime, null);
            Check(received == 0 && !clients.Contains(processId),
                "early retry bypassed the recovery backoff");

            nextTry.SetValue(candidates[processId], Environment.TickCount64 - 1L);
            probe.Invoke(runtime, null);
            Check(received == 1 && clients.Contains(processId) && candidates.Count == 0 &&
                  !pending.Contains(processId),
                "recovered native HWND did not receive re-init or clear stale bookkeeping");

            // A normal PID Close must still terminate the restored connection.
            type.GetMethod("HandleClientClose", flags)!
                .Invoke(runtime, new object[] { new IntPtr((int)processId) });
            Check(!clients.Contains(processId), "recovered client did not close cleanly");

            type.GetMethod("HandleClientConnect", flags)!.Invoke(runtime, new object[] { clientHwnd });
            staleClient = clients[processId]!;
            type.GetMethod("DropUnresponsiveClient", flags)!.Invoke(runtime, new[] { staleClient });
            expires.SetValue(candidates[processId], Environment.TickCount64 - 1L);
            nextTry.SetValue(candidates[processId], Environment.TickCount64 - 1L);
            probe.Invoke(runtime, null);
            Check(!clients.Contains(processId) && candidates.Count == 0 &&
                  pending.Count == 0 && received == 1,
                "expired recovery window leaked client state or sent a late probe");
            type.GetMethod("Stop")!.Invoke(runtime, null);
            Check(candidates.Count == 0 && pending.Count == 0,
                "Stop failed to clear retry candidates and tombstones");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (runtime != null) type.GetMethod("Stop")!.Invoke(runtime, null);
            first.Close();
            receiverDispatcher?.BeginInvokeShutdown(
                System.Windows.Threading.DispatcherPriority.Normal);
            if (!client.Join(3000) && failure == null)
                failure = new InvalidOperationException("receiver thread did not exit");
            failure ??= receiverError;
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Check(worker.Join(8000), "isolated host recovery STA timed out");
    if (failure != null)
        throw new InvalidOperationException("Host recovery test failure: " + failure, failure);
}

static void HookStalePidDisconnectOnSta()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var type = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
            "MiniMixerOverlay.App.GameHookOverlayRuntime") ??
            throw new InvalidOperationException("Hook runtime missing");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Window? hostWindow = null;
        Window? first = null;
        Window? second = null;
        Window? third = null;
        object? runtime = null;
        try
        {
            // Two distinct HWNDs in the same actual Windows PID.
            hostWindow = new Window { Width = 35, Height = 35, ShowInTaskbar = false };
            first = new Window { Width = 35, Height = 35, ShowInTaskbar = false };
            second = new Window { Width = 35, Height = 35, ShowInTaskbar = false };
            third = new Window { Width = 35, Height = 35, ShowInTaskbar = false };
            _ = new System.Windows.Interop.WindowInteropHelper(hostWindow).EnsureHandle();
            var h1 = new System.Windows.Interop.WindowInteropHelper(first).EnsureHandle();
            var h2 = new System.Windows.Interop.WindowInteropHelper(second).EnsureHandle();
            var h3 = new System.Windows.Interop.WindowInteropHelper(third).EnsureHandle();
            runtime = Activator.CreateInstance(type,
                new object?[] { hostWindow, null, "stale-close-" + Guid.NewGuid().ToString("N") })!;
            var registry = (System.Collections.IDictionary)(type.GetField("_clients", flags)!
                .GetValue(runtime)!);
            var windowField = type.GetField("_ipcWindow", flags)!;
            var remoteWindowField = type.GetNestedType("RemoteClient", BindingFlags.NonPublic)!
                .GetField("RemoteWindow")!;
            var post = type.GetMethod("PostMessage", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Post(IntPtr destination, int message, IntPtr param) =>
                (bool)post.Invoke(null, new object[] { destination, message, param, IntPtr.Zero })!;
            void Pump()
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(65) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            type.GetMethod("Start")!.Invoke(runtime, null);
            var ipc = (IntPtr)windowField.GetValue(runtime)!;
            Check(ipc != IntPtr.Zero, "hook IPC host not initialized");
            var pid = (uint)Environment.ProcessId;

            Check(Post(ipc, 0x8201, h1), "first native connect failed");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h1,
                "initial client A not registered");

            Check(Post(ipc, 0x8201, h2), "second native connect failed");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h2,
                "newer connection B did not replace A");

            Check(Post(ipc, 0x8201, h2), "duplicate connect B not posted");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h2,
                "duplicate connect B unexpectedly discarded current client");

            Check(Post(ipc, 0x8203, new IntPtr((int)pid)), "stale close A not posted");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h2,
                "stale PID-only close A removed newer connection B");

            Check(Post(ipc, 0x8203, new IntPtr((int)pid)), "current close B not posted");
            Pump();
            Check(!registry.Contains(pid), "current close B did not remove the connection");

            Check(Post(ipc, 0x8201, h1), "second generation A reconnect failed");
            Pump();
            Check(Post(ipc, 0x8201, h2), "second generation B reconnect failed");
            Pump();
            Check(Post(ipc, 0x8201, h3), "third HWND C connect failed");
            Pump();
            for (var superseded = 0; superseded < 2; superseded++)
            {
                Check(Post(ipc, 0x8203, new IntPtr((int)pid)),
                    "old generation close not posted");
                Pump();
                Check(registry.Contains(pid) &&
                      (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h3,
                    "an older generation close evicted newest HWND C");
            }
            Check(Post(ipc, 0x8203, new IntPtr((int)pid)),
                "current generation C close not posted");
            Pump();
            Check(!registry.Contains(pid), "last generation close did not remove C");
            Check(Post(ipc, 0x8201, h3), "fresh reconnect after closes failed");
            Pump();
            Check(registry.Contains(pid), "fresh reconnect after closes was not restored");

            // A prior generation need not send its Close at all. When the
            // current HWND is destroyed, its own PID-close must still clean up.
            Check(Post(ipc, 0x8201, h1), "replacement A not posted");
            Pump();
            Check(Post(ipc, 0x8201, h3), "replacement C not posted");
            Pump();
            third.Close();
            Check(Post(ipc, 0x8203, new IntPtr((int)pid)),
                "destroyed current HWND close not posted");
            Pump();
            Check(!registry.Contains(pid),
                "dead current HWND was retained behind missing stale closes");

            type.GetMethod("Stop")!.Invoke(runtime, null);
            Check(registry.Count == 0 &&
                  !(bool)type.GetProperty("IsRunning")!.GetValue(runtime)!,
                "Stop retained stale clients or runtime state");
            Check((bool)type.GetMethod("Start")!.Invoke(runtime, null)!,
                "runtime did not restart after disconnect");
            var restartedIpc = (IntPtr)windowField.GetValue(runtime)!;
            Check(restartedIpc != IntPtr.Zero &&
                  Post(restartedIpc, 0x8201, h1), "restarted host did not accept connect");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h1,
                "restart did not clear prior connection generations");
            Check(Post(restartedIpc, 0x8203, new IntPtr((int)pid)),
                "close after restart not posted");
            Pump();
            Check(!registry.Contains(pid),
                "fresh first-generation close was incorrectly guarded after restart");

            // Also model the existing SendMessageTimeout eviction path. An
            // eventual Close(A) may arrive only after a new explicit Connect(B).
            Check(Post(restartedIpc, 0x8201, h1), "timeout fixture A connect failed");
            Pump();
            var failedClient = registry[pid]!;
            type.GetMethod("DropUnresponsiveClient", flags)!
                .Invoke(runtime, new[] { failedClient });
            Check(!registry.Contains(pid), "timeout fixture did not evict A");
            Check(Post(restartedIpc, 0x8201, h2), "post-timeout reconnect B failed");
            Pump();
            Check(Post(restartedIpc, 0x8203, new IntPtr((int)pid)),
                "post-timeout stale close A not posted");
            Pump();
            Check(registry.Contains(pid) &&
                  (IntPtr)remoteWindowField.GetValue(registry[pid]!)! == h2,
                "post-timeout stale close A evicted reconnected B");
            Check(Post(restartedIpc, 0x8203, new IntPtr((int)pid)),
                "post-timeout actual close B not posted");
            Pump();
            Check(!registry.Contains(pid), "post-timeout actual close B was not removed");

            // The late close may also arrive before any replacement exists.
            Check(Post(restartedIpc, 0x8201, h1), "pre-reconnect timeout fixture connect failed");
            Pump();
            failedClient = registry[pid]!;
            type.GetMethod("DropUnresponsiveClient", flags)!
                .Invoke(runtime, new[] { failedClient });
            Check(Post(restartedIpc, 0x8203, new IntPtr((int)pid)),
                "close for evicted client without replacement not posted");
            Pump();
            Check(Post(restartedIpc, 0x8201, h2), "connect after old close failed");
            Pump();
            Check(Post(restartedIpc, 0x8203, new IntPtr((int)pid)),
                "new active client close after consumed tombstone not posted");
            Pump();
            Check(!registry.Contains(pid),
                "consumed timeout tombstone guarded an unrelated new close");

            type.GetMethod("Stop")!.Invoke(runtime, null);
            var pendingClose = (System.Collections.IDictionary)type.GetField(
                "_pendingCloseAfterEviction", flags)!.GetValue(runtime)!;
            Check(pendingClose.Count == 0, "Stop leaked pending PID-close tombstones");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            if (runtime != null)
            {
                try { type.GetMethod("Stop")!.Invoke(runtime, null); }
                catch (Exception ex) { failure ??= ex; }
            }
            third?.Close();
            second?.Close();
            first?.Close();
            hostWindow?.Close();
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Check(worker.Join(7000), "stale close STA test timed out");
    if (failure != null)
        throw new InvalidOperationException("Stale close failure: " + failure, failure);
}

static void HookCopyDataTimeoutAndRecovery()
{
    // Real Win32 WM_COPYDATA HWNDs on independent STA threads. The slow window
    // is deliberately not pumping messages; no game, injector or audio involved.
    var runtimeType = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
        "MiniMixerOverlay.App.GameHookOverlayRuntime") ??
        throw new InvalidOperationException("GameHookOverlayRuntime missing");
    var sendMethod = runtimeType.GetMethod("SendCopyData", BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException("SendCopyData");
    var broadcast = runtimeType.GetMethod("BroadcastOverlayMessage",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException("BroadcastOverlayMessage");
    var clientsField = runtimeType.GetField("_clients", BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingFieldException("_clients");
    var clientType = runtimeType.GetNestedType("RemoteClient", BindingFlags.NonPublic) ??
        throw new MissingMemberException("RemoteClient");

    using var slowReady = new ManualResetEventSlim(false);
    using var fastReady = new ManualResetEventSlim(false);
    IntPtr slowHwnd = IntPtr.Zero, fastHwnd = IntPtr.Zero;
    System.Windows.Threading.Dispatcher? fastDispatcher = null;
    System.Windows.Threading.Dispatcher? slowDispatcher = null;
    Exception? receiverError = null;
    var received = 0;
    Thread CreateReceiver(bool slow, ManualResetEventSlim ready)
    {
        var worker = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window { Width = 30, Height = 30, ShowInTaskbar = false };
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                if (slow) { slowHwnd = hwnd; slowDispatcher = dispatcher; }
                else
                {
                    fastHwnd = hwnd;
                    fastDispatcher = dispatcher;
                    System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(
                        (IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
                        {
                            if (msg == 0x004A)
                            {
                                Interlocked.Increment(ref received);
                                handled = true;
                                return new IntPtr(1);
                            }
                            return IntPtr.Zero;
                        });
                }
                ready.Set();
                if (slow) Thread.Sleep(1150);
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                receiverError = ex;
                ready.Set();
            }
            finally { window?.Close(); }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.IsBackground = true;
        worker.Start();
        return worker;
    }

    var slowThread = CreateReceiver(true, slowReady);
    var fastThread = CreateReceiver(false, fastReady);
    try
    {
        Check(slowReady.Wait(3000) && fastReady.Wait(3000) &&
              slowHwnd != IntPtr.Zero && fastHwnd != IntPtr.Zero,
            "offline receiving HWNDs did not initialize");

        Exception? testFailure = null;
        var testThread = new Thread(() =>
        {
            try
            {
                var runtime = Activator.CreateInstance(runtimeType,
                    new object?[] { new Window(), null, "isolated-wm-copydata-test" }) ??
                    throw new InvalidOperationException("Runtime fixture unavailable");
                var data = new byte[] { 1, 2, 3, 4 };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = sendMethod.Invoke(runtime, new object[] { slowHwnd, data });
                sw.Stop();
                Check(result is bool ok && !ok && sw.ElapsedMilliseconds < 400,
                    "unresponsive receiver blocked synchronous WM_COPYDATA or returned success: " +
                    sw.ElapsedMilliseconds + "ms");

                // A second healthy HWND must receive frames even after the first fails.
                var clients = clientsField.GetValue(runtime) as System.Collections.IDictionary ??
                    throw new InvalidOperationException("Client registry unavailable");
                void Register(uint id, IntPtr hwnd)
                {
                    var entry = Activator.CreateInstance(clientType)!;
                    clientType.GetField("ProcessId")!.SetValue(entry, id);
                    clientType.GetField("RemoteWindow")!.SetValue(entry, hwnd);
                    clients[id] = entry;
                }
                Register(101, slowHwnd);
                Register(202, fastHwnd);
                sw.Restart();
                broadcast.Invoke(runtime, new object[] { "window.framebuffer",
                    "{\"type\":\"window.framebuffer\",\"windowId\":1}" });
                sw.Stop();
                Check(sw.ElapsedMilliseconds < 400 && Volatile.Read(ref received) == 1,
                    "a hung client prevented another client receiving its frame");
                Check(clients.Count == 1 && clients.Contains((uint)202),
                    "timed-out client was not removed from active broadcast registry");

                sw.Restart();
                broadcast.Invoke(runtime, new object[] { "window.framebuffer", "{}" });
                sw.Stop();
                Check(sw.ElapsedMilliseconds < 400 && Volatile.Read(ref received) == 2,
                    "responsive client did not recover after timeout");

                // A real connect handler (not an injected registry entry) must
                // readmit a client after an earlier timeout.
                runtimeType.GetMethod("HandleClientConnect", BindingFlags.Instance |
                    BindingFlags.NonPublic)!.Invoke(runtime, new object[] { fastHwnd });
                Check(clients.Count == 2,
                    "reconnected HWND was not admitted through its connect handler");

                for (var i = 0; i < 30; i++)
                {
                    broadcast.Invoke(runtime, new object[] { "window.framebuffer", "{}" });
                }
                Check(Volatile.Read(ref received) == 62,
                    "reconnected/healthy clients did not receive all 30 frames");

                // An explicit close removes only its connection. The other
                // receiver must remain usable, and a subsequent close is inert.
                var actualPid = (uint)Environment.ProcessId;
                var close = runtimeType.GetMethod("HandleClientClose",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                close.Invoke(runtime, new object[] { new IntPtr((int)actualPid) });
                close.Invoke(runtime, new object[] { new IntPtr((int)actualPid) });
                Check(clients.Count == 1 && clients.Contains((uint)202),
                    "client disconnect removed an unrelated healthy connection");
                broadcast.Invoke(runtime, new object[] { "window.framebuffer", "{}" });
                Check(Volatile.Read(ref received) == 63,
                    "healthy receiver failed after peer disconnected");

                sw.Restart();
                runtimeType.GetMethod("Stop")!.Invoke(runtime, null);
                sw.Stop();
                Check(sw.ElapsedMilliseconds < 400 && clients.Count == 0,
                    "stopping the runtime blocked or retained remote clients");
            }
            catch (Exception ex) { testFailure = ex; }
        });
        testThread.SetApartmentState(ApartmentState.STA);
        testThread.IsBackground = true;
        testThread.Start();
        Check(testThread.Join(4000), "WPF IPC sender thread stalled");
        if (testFailure != null) throw new InvalidOperationException("IPC regression", testFailure);
        Check(receiverError == null, "offline receiver failed: " + receiverError);
    }
    finally
    {
        fastDispatcher?.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
        slowDispatcher?.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
        Check(fastThread.Join(3000) && slowThread.Join(3000),
            "test receiver threads did not exit");
    }
}

static void HookFrameMutexContention()
{
    // A real mutex, a real isolated memory map and a second owning thread.
    // No native game client, injector, UI runtime, or real audio is involved.
    var hookType = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
        "MiniMixerOverlay.App.GameHookOverlayRuntime") ??
        throw new InvalidOperationException("Hook runtime not found");
    var publish = hookType.GetMethod("TryWriteAndNotifyFrame",
        BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new MissingMethodException("GameHookOverlayRuntime.TryWriteAndNotifyFrame");
    using var mutex = new Mutex(false);
    using var buffer = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(null, 128);
    using var view = buffer.CreateViewAccessor(0, 128);
    var pixels = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    view.Write(0, -1);
    view.Write(4, -1);
    var oldPixels = Enumerable.Repeat((byte)0xEE, 16).ToArray();
    view.WriteArray(8, oldPixels, 0, oldPixels.Length);
    var sent = 0;
    var notification = (Action)(() => sent++);
    bool TryFrame() => (bool)(publish.Invoke(null,
        new object[] { mutex, view, 2, 2, pixels, notification }) ??
        throw new InvalidOperationException("No frame publication result"));

    using var owned = new ManualResetEventSlim(false);
    using var release = new ManualResetEventSlim(false);
    var contender = Task.Run(() =>
    {
        mutex.WaitOne();
        owned.Set();
        try { release.Wait(); }
        finally { mutex.ReleaseMutex(); }
    });
    try
    {
        Check(owned.Wait(2000), "contention thread did not acquire framebuffer mutex");
        Check(!TryFrame(), "frame was written despite failed mutex acquisition");
        var currentPixels = new byte[16];
        view.ReadArray(8, currentPixels, 0, 16);
        Check(view.ReadInt32(0) == -1 && view.ReadInt32(4) == -1 &&
              currentPixels.SequenceEqual(oldPixels),
            "mutex timeout modified shared header or frame pixels");
        Check(sent == 0, "mutex timeout falsely published a framebuffer notification");
    }
    finally
    {
        release.Set();
        Check(contender.Wait(2000), "contention thread did not release its mutex");
    }

    Check(TryFrame(), "a free framebuffer mutex should permit the frame");
    var copied = new byte[16];
    view.ReadArray(8, copied, 0, 16);
    Check(view.ReadInt32(0) == 2 && view.ReadInt32(4) == 2 &&
          copied.SequenceEqual(pixels), "successful write produced an incoherent frame");
    Check(sent == 1, "exactly one complete frame should be published");
    Check(mutex.WaitOne(0), "writer failed to release the acquired mutex");
    mutex.ReleaseMutex();

    using var abandoned = new Mutex(false);
    var exitedOwner = new Thread(() =>
    {
        abandoned.WaitOne();
        // Simulate client termination without ReleaseMutex.
    });
    exitedOwner.Start();
    Check(exitedOwner.Join(2000), "abandonment fixture did not exit");
    Check(!(bool)publish.Invoke(null,
        new object[] { abandoned, view, 2, 2, pixels, notification })!,
        "abandoned mutex published an untrusted framebuffer");
    Check(sent == 1, "abandoned mutex emitted a framebuffer message");
    Check(abandoned.WaitOne(0), "abandoned mutex ownership was not released");
    abandoned.ReleaseMutex();

    using var closedView = buffer.CreateViewAccessor(0, 128);
    closedView.Dispose();
    Check(!(bool)publish.Invoke(null,
        new object[] { mutex, closedView, 2, 2, pixels, notification })!,
        "failed framebuffer write was reported as successful");
    Check(sent == 1, "failed framebuffer write broadcast a false frame");
    Check(mutex.WaitOne(0), "write error leaked the framebuffer mutex");
    mutex.ReleaseMutex();
}

static void GuardManualOverrideAuditIsOneShot()
{
    var dir = Path.Combine(Path.GetTempPath(),
        "mini-mixer-guard-manual-audit-" + Guid.NewGuid().ToString("N"));
    var file = Path.Combine(dir, "guard.log");
    var volumePath = MissingExe();
    var mutePath = MissingExe();
    var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
    var audio = new FakeAudio(new[]
    {
        Session(volumePath, "manual-volume", 0.8f),
        Session(mutePath, "manual-mute", 0.6f)
    });
    var classifier = new FakeClassifier();
    var controller = new MixerController(audio, new GuardEngine(rules, classifier),
        classifier, rules, new FakeSettings(), guardLogFilePath: file);
    try
    {
        controller.Initialize();
        controller.SetVolume(volumePath, 0.65f);
        controller.SetMute(mutePath, true);
        var afterFirstActions = File.ReadAllText(file);
        Check(afterFirstActions.Contains("manual_override action=volume") &&
              afterFirstActions.Contains("manual_override action=mute"),
            "manual override transitions were not audited");
        controller.SetVolume(volumePath, 0.45f);
        controller.SetMute(mutePath, false);
        Check(File.ReadAllText(file) == afterFirstActions,
            "repeated manual control changes generated unbounded audit spam");
        Check(audio.VolumeWrites.Count == 2 && audio.MuteWrites.Count == 2,
            "logging changes interfered with the audio control owner");
    }
    finally
    {
        controller.Shutdown();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

static void GuardLogIsChangeOnlyAndBounded()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-guard-log-regression-" +
        Guid.NewGuid().ToString("N"));
    var file = Path.Combine(dir, "guard.log");
    var oldApp = MissingExe();
    var newApp = MissingExe();
    var oldSession = Session(oldApp, "stable-old", 0.40f);
    var audio = new FakeAudio(new[] { oldSession });
    var rules = new MemoryRules(DateTime.UtcNow.AddHours(-2));
    var classifier = new FakeClassifier();
    var guard = new GuardEngine(rules, classifier);
    var controller = new MixerController(audio, guard, classifier, rules,
        new FakeSettings(), guardLogFilePath: file, guardLogMaxBytes: 2048);
    try
    {
        controller.Initialize();
        Check(File.Exists(file), "guard baseline audit was not written");
        var initial = File.ReadAllText(file);
        Check(initial.Contains("auto=0") && initial.Contains("baseline="),
            "initial guard decision was not recorded");
        // First follow-up may legitimately change the baseline decision.
        controller.RefreshSessions();
        var before = File.ReadAllText(file);

        for (var i = 0; i < 8; i++) controller.RefreshSessions();
        Check(File.ReadAllText(file) == before,
            "unchanged periodic refresh created redundant guard records");

        oldSession.Volume = 0.63f;
        controller.RefreshSessions();
        var changed = File.ReadAllText(file);
        Check(changed.Length > before.Length && changed.Contains("current=63%"),
            "changed session volume was not logged");
        controller.RefreshSessions();
        Check(File.ReadAllText(file) == changed,
            "identical state after a change was redundantly logged");

        audio.Sessions.Add(Session(newApp, "new-session", 0.75f));
        controller.RefreshSessions();
        Check(audio.VolumeWrites.Any(v => Math.Abs(v.Volume - 0.05f) < 0.001f),
            "fixture did not trigger the real one-time Guard action");
        Check(File.ReadAllText(file).Contains("auto=1"),
            "actual automatic Guard action was not audited");
        var writes = audio.VolumeWrites.Count;
        controller.RefreshSessions();
        Check(audio.VolumeWrites.Count == writes,
            "logging changes broke one-time Guard application");

        var beforeDuplicateCallback = File.ReadAllText(file);
        audio.RaiseCreated(Session(newApp, "new-session", 0.75f));
        Check(File.ReadAllText(file) == beforeDuplicateCallback,
            "duplicate session-created callback wrote a false audit event");
        audio.RaiseDestroyed("not-actually-removed");
        Check(File.ReadAllText(file) == beforeDuplicateCallback,
            "duplicate session-removed callback wrote a false audit event");
        audio.Sessions.RemoveAll(s => s.ExePath == newApp);
        controller.RefreshSessions();
        Check(File.ReadAllText(file).Contains("session_removed"),
            "actual session removal was not audited");
        audio.Sessions.Add(Session(newApp, "new-session", 0.75f));
        controller.RefreshSessions();
        var retainedAudit = File.ReadAllText(file) +
            (File.Exists(file + ".1") ? File.ReadAllText(file + ".1") : "");
        Check(retainedAudit.Contains("session_created"),
            "actual session reappearance was not audited");
        Check(audio.VolumeWrites.Count == writes,
            "session reappearance retriggered one-time auto-limit");

        for (var i = 0; i < 50; i++)
        {
            oldSession.Volume = i % 2 == 0 ? 0.42f : 0.68f;
            controller.RefreshSessions();
        }

        var backup = file + ".1";
        Check(File.Exists(backup), "guard log did not rotate");
        Check(new FileInfo(file).Length <= 2048 &&
              new FileInfo(backup).Length <= 2048,
            "guard log or rotated backup exceeds retention cap");
        Check(File.ReadAllText(file).Contains("current=68%"),
            "latest meaningful guard decision missing after rotation");

        // A legacy build can leave a giant guard.log. Migrating it must not
        // preserve a multi-megabyte .1 backup and defeat the retention cap.
        var legacyPath = Path.Combine(dir, "legacy.log");
        File.WriteAllText(legacyPath, new string('L', 8192));
        var legacyController = new MixerController(new FakeAudio(new[]
        {
            Session(MissingExe(), "legacy-baseline", 0.5f)
        }), new GuardEngine(new MemoryRules(DateTime.UtcNow.AddDays(-20)),
            new FakeClassifier()), new FakeClassifier(),
            new MemoryRules(DateTime.UtcNow.AddDays(-20)), new FakeSettings(),
            guardLogFilePath: legacyPath, guardLogMaxBytes: 2048);
        try
        {
            legacyController.Initialize();
            Check(new FileInfo(legacyPath).Length <= 2048 &&
                  new FileInfo(legacyPath + ".1").Length <= 2048,
                "rotating pre-existing oversized legacy log exceeded retention");
        }
        finally { legacyController.Shutdown(); }
        controller.Shutdown();
    }
    finally
    {
        controller.Shutdown();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

static void BaselineNeverReclassified()
{
    var firstRun = DateTime.UtcNow.AddDays(-20);
    var path = MissingExe();
    var rules = new MemoryRules(firstRun);
    var (controller, audio) = CreateController(rules, Session(path, "existing"));
    Check(audio.VolumeWrites.Count == 0, "initial baseline wrote volume");
    Check(rules.GetRule(path)!.FirstSeenUtc == firstRun, "initial baseline firstSeen mismatch");
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 0, "existing baseline app was auto-limited on refresh");
    Check(rules.GetRule(path)!.FirstSeenUtc == firstRun, "baseline firstSeen was rewritten as new");
    controller.Shutdown();
}

static void ExistingRuleNeverReclassified()
{
    var firstRun = DateTime.UtcNow.AddDays(-20);
    var path = MissingExe();
    var rules = new MemoryRules(firstRun);
    var original = firstRun.AddDays(-40);
    rules.UpsertRule(path, new AppRule { DisplayName = "Known App", FirstSeenUtc = original, LastSeenUtc = original });
    var (controller, audio) = CreateController(rules, Session(path, "persisted"));
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 0, "persisted known app was auto-limited");
    Check(rules.GetRule(path)!.FirstSeenUtc == original, "persisted firstSeen was mutated");
    controller.Shutdown();
}

static void NewAppAndManualPriority()
{
    var rules = new MemoryRules(DateTime.UtcNow.AddHours(-2));
    var (controller, audio) = CreateController(rules);
    var path = MissingExe();
    audio.Sessions.Add(Session(path, "new-a"));
    audio.Sessions.Add(Session(path, "new-b", 0.6f));
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 2, "new app should limit all sessions once");
    Check(audio.VolumeWrites.All(w => Math.Abs(w.Volume - 0.05f) < 0.0001f), "incorrect auto-limit");
    Check(rules.GetRule(path)?.AutoApplied == true, "missing one-time marker");
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 2, "new app auto-limited more than once");
    controller.SetVolume(path, 0.73f);
    Check(rules.GetRule(path)?.ManualOverride == true, "manual change not recorded");
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 4, "manual override was overwritten by refresh");
    controller.Shutdown();
}

static void EligibleBaselineInstallIsOneShot()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-guard-install-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var exe = Path.Combine(dir, "newly-installed.exe");
    try
    {
        File.WriteAllText(exe, "fixture-only, never executed");
        var rules = new MemoryRules(DateTime.UtcNow.AddHours(-3));
        var (controller, audio) = CreateController(rules, Session(exe, "baseline-new"));
        Check(audio.VolumeWrites.Count == 1, "eligible new install should be limited in baseline exactly once");
        Check(rules.GetRule(exe)?.AutoApplied == true, "auto action missing one-time marker");
        controller.RefreshSessions();
        Check(audio.VolumeWrites.Count == 1, "eligible baseline install was limited twice");
        controller.Shutdown();
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}

static void ManualOverrideSurvivesRefresh()
{
    var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
    var path = MissingExe();
    var (controller, audio) = CreateController(rules, Session(path, "manual"));
    controller.SetVolume(path, 0.63f);
    controller.SetMute(path, true);
    var saved = rules.GetRule(path)!;
    Check(saved.ManualOverride, "manual override not marked");
    controller.RefreshSessions();
    Check(audio.VolumeWrites.Count == 1, "guard overwrote manual volume");
    Check(audio.MuteWrites.Count == 1 && audio.MuteWrites[0].Mute, "manual mute missing");
    Check(rules.GetRule(path)!.ManualOverride, "manual flag lost");
    controller.Shutdown();
}

static void MasterAndSystemSoundsSkipAutoGuard()
{
    var master = Session("__system_master__", "__system_master__|device-A", 0.38f);
    master.ExeName = "SystemMaster";
    master.DisplayName = "Systemlautstärke";
    var sounds = Session("__system_sound__", "system-sounds-fixture", 0.09f);
    sounds.ExeName = "SystemSound";
    sounds.DisplayName = "Systemklänge";
    sounds.IsSystemSound = true;
    var rules = new MemoryRules(DateTime.UtcNow.AddDays(-50));
    var audio = new FakeAudio(new[] { master, sounds });
    var classifier = new FakeClassifier();
    var guard = new GuardEngine(rules, classifier);
    var controller = new MixerController(audio, guard, classifier, rules,
        new FakeSettings(), guardLogFilePath: Path.Combine(Path.GetTempPath(),
            "mini-mixer-regression-logs-" + Environment.ProcessId + "-" +
            System.Diagnostics.Process.GetCurrentProcess().StartTime.Ticks,
            "master-and-sounds.log"));
    try
    {
        controller.Initialize();
        controller.RefreshSessions();
        Check(controller.AppEntries.Count == 2,
            "master and system sounds must remain independent entries");
        Check(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
            "guard wrote to system master or Windows notification sounds");
        Check(rules.GetRule("__system_master__") == null &&
              rules.GetRule("__system_sound__") == null,
            "guard created install/auto-volume rules for system audio");
        Check(Math.Abs(controller.AppEntries.Single(s=>s.ExePath=="__system_master__")
                   .CombinedVolume - 0.38f)<0.0001f,
            "system master was substituted with system sounds volume");
        controller.SetMute("__system_master__", true);
        Check(audio.MuteWrites.Count==1 &&
              audio.MuteWrites[0].Id=="__system_master__|device-A" &&
              audio.Sessions.Single(s=>s.ExePath=="__system_sound__").IsMuted==false,
            "master mute dispatched to system sounds instead");
        controller.SetMute("__system_master__", false);
        controller.SetVolume("__system_master__", 0.42f);
        Check(audio.VolumeWrites.Count==1 &&
              audio.VolumeWrites[0].Id=="__system_master__|device-A",
            "master volume dispatched to unrelated session");
        Check(rules.GetRule("__system_master__")==null,
            "manual master controls created an app install rule");
    }
    finally { controller.Shutdown(); }
}

static void MutePreservesLastKnownVolume()
{
    var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
    var path = MissingExe();
    var (controller, audio) = CreateController(rules, Session(path, "own-audio-test", 0.76f));
    try
    {
        controller.SetVolume(path, 0.63f);
        Check(Math.Abs(rules.GetRule(path)!.LastKnownVolume - 0.63f) < 0.0001f,
            "manual audible level was not saved");
        controller.SetMute(path, true);
        Check(rules.GetRule(path)!.ManualOverride, "manual mute must set override");
        Check(Math.Abs(rules.GetRule(path)!.LastKnownVolume - 0.63f) < 0.0001f,
            "mute overwrote the last audible level with zero");
        controller.RefreshSessions();
        controller.SetMute(path, false);
        Check(Math.Abs(rules.GetRule(path)!.LastKnownVolume - 0.63f) < 0.0001f,
            "unmute lost the remembered level");
        Check(audio.Sessions.Single().Volume == 0.63f,
            "muting was implemented by modifying session volume");
        Check(audio.VolumeWrites.Count == 1 && audio.MuteWrites.Count == 2,
            "mute/unmute issued unexpected volume writes");
    }
    finally
    {
        controller.Shutdown();
    }

    // The rule must also survive a real JSON round-trip, not just the
    // in-memory fake store used above. No audio hardware is modified.
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-mute-retain-" +
        Guid.NewGuid().ToString("N"));
    try
    {
        var file = Path.Combine(dir, "rules.json");
        var store = new JsonRuleStore(file);
        var (persistController, _) = CreateController(store,
            Session(path, "persist-own-mute", 0.76f));
        try
        {
            persistController.SetVolume(path, 0.63f);
            persistController.SetMute(path, true);
            persistController.SaveState();
        }
        finally { persistController.Shutdown(); }
        var reload = new JsonRuleStore(file);
        reload.Load();
        var saved = reload.GetRule(path);
        Check(saved != null && saved.ManualOverride &&
              Math.Abs(saved.LastKnownVolume - 0.63f) < 0.0001f,
            "mute persisted a zero volume after an actual JSON restart");
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

static void PersistedManualOverrideSurvivesRestart()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-guard-persist-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, "rules.json");
    var exe = MissingExe();
    try
    {
        var original = new JsonRuleStore(path);
        original.Load();
        original.UpsertRule(exe, new AppRule
        {
            DisplayName = "Persisted",
            FirstSeenUtc = original.ToolFirstRunUtc.AddDays(-10),
            LastSeenUtc = DateTime.UtcNow,
            ManualOverride = true,
            LastKnownVolume = 0.48f
        });
        var reopened = new JsonRuleStore(path);
        var (controller, audio) = CreateController(reopened, Session(exe, "restarted", 0.48f));
        controller.RefreshSessions();
        var rule = reopened.GetRule(exe);
        Check(rule != null && rule.ManualOverride && Math.Abs(rule.LastKnownVolume - 0.48f) < 0.0001f, "persisted override lost");
        Check(audio.VolumeWrites.Count == 0, "restart overwrote user volume");
        controller.Shutdown();
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

static void PersistedRulesRemainCaseInsensitive()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-case-roundtrip-" + Guid.NewGuid().ToString("N"));
    var file = Path.Combine(dir, "rules.json");
    var path = @"C:\Apps\Mixer-Case-Test\EXAMPLE.EXE";
    try
    {
        var rules = new JsonRuleStore(file);
        rules.Load();
        rules.UpsertRule(path, new AppRule
        {
            DisplayName = "Case Test",
            FirstSeenUtc = DateTime.UtcNow.AddDays(-8),
            ManualOverride = true,
            LastKnownVolume = 0.71f
        });
        var reopened = new JsonRuleStore(file);
        reopened.Load();
        Check(reopened.GetRule(path.ToLowerInvariant())?.ManualOverride == true,
            "persisted rule lookup lost Windows path case-insensitivity");
        reopened.UpsertRule(path.ToLowerInvariant(), reopened.GetRule(path.ToLowerInvariant())!);
        Check(reopened.Apps.Count == 1, "case variant created a second authority entry");
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}

static void LegacyCaseCollisionIsSafe()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-case-collision-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var file = Path.Combine(dir, "rules.json");
    var upper = @"C:\Apps\Mixer-Legacy\APP.EXE";
    var lower = upper.ToLowerInvariant();
    try
    {
        var firstSeen = DateTime.UtcNow.AddDays(-30);
        var manual = new AppRule { DisplayName = "Original", FirstSeenUtc = firstSeen, LastSeenUtc = DateTime.UtcNow.AddMinutes(-5), ManualOverride = true, LastKnownVolume = 0.74f, Favorite = true };
        var automatic = new AppRule { DisplayName = "Later Case Variant", FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow, AutoApplied = true, LastKnownVolume = 0.05f };
        var payload = new
        {
            toolFirstRunUtc = DateTime.UtcNow.AddDays(-20),
            apps = new Dictionary<string, AppRule> { [upper] = manual, [lower] = automatic }
        };
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        var reopened = new JsonRuleStore(file);
        reopened.Load();
        Check(reopened.Apps.Count == 1, "same EXE persisted as two different owners");
        Check(Directory.GetFiles(dir, "rules.json.case-conflicts.*.bak").Length == 1, "original conflicting user data was not backed up");
        var rule = reopened.GetRule(lower);
        Check(rule is { ManualOverride: true, AutoApplied: true, Favorite: true }, "safe rule flags lost when reconciling case variants");
        Check(Math.Abs(rule!.LastKnownVolume - 0.74f) < 0.0001f, "last manual volume overwritten by newer auto value");
        Check(rule.FirstSeenUtc == firstSeen, "initial firstSeen lost after case-fold collision");
        reopened.Save();
        var secondLoad = new JsonRuleStore(file);
        secondLoad.Load();
        Check(secondLoad.Apps.Count == 1, "normalized rules recreated duplicate after second restart");
        Check(secondLoad.GetRule(upper)?.ManualOverride == true, "manual override lost on second restart");
        Check(Directory.GetFiles(dir, "rules.json.case-conflicts.*.bak").Length == 1, "backup lost during normalized save");
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

static void MalformedJsonPreservesOriginal()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-malformed-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    const string invalid = "{ this is not JSON";
    try
    {
        var ruleFile = Path.Combine(dir, "rules.json");
        var settingsFile = Path.Combine(dir, "settings.json");
        File.WriteAllText(ruleFile, invalid);
        File.WriteAllText(settingsFile, invalid);

        var rules = new JsonRuleStore(ruleFile);
        rules.Load();
        rules.Save();
        var settings = new JsonSettingsStore(settingsFile);
        settings.Load();
        settings.Save();

        var ruleBackups = Directory.GetFiles(dir, "rules.json.corrupt.*.bak");
        var settingsBackups = Directory.GetFiles(dir, "settings.json.corrupt.*.bak");
        Check(ruleBackups.Length == 1 && settingsBackups.Length == 1,
            "missing malformed source backups");
        Check(File.ReadAllText(ruleBackups[0]) == invalid && File.ReadAllText(settingsBackups[0]) == invalid,
            "original malformed contents not preserved exactly");
        using (System.Text.Json.JsonDocument.Parse(File.ReadAllText(ruleFile))) { }
        using (System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsFile))) { }
        new JsonRuleStore(ruleFile).Load();
        new JsonSettingsStore(settingsFile).Load();
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

static void FailedWritesPreserveValidJson()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-failed-write-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var ruleFile = Path.Combine(dir, "rules.json");
        var settingsFile = Path.Combine(dir, "settings.json");
        var rules = new JsonRuleStore(ruleFile);
        rules.Load();
        var settings = new JsonSettingsStore(settingsFile);
        settings.Load();
        var oldRules = File.ReadAllText(ruleFile);
        var oldSettings = File.ReadAllText(settingsFile);
        Directory.CreateDirectory(ruleFile + ".tmp");
        Directory.CreateDirectory(settingsFile + ".tmp");
        rules.UpsertRule(@"C:\Apps\new.exe", new AppRule { DisplayName = "Write recovery" });
        settings.Settings.Ui.VisibleApps = 7;
        settings.Save();
        Check(File.ReadAllText(ruleFile) == oldRules, "failed rules write changed valid file");
        Check(File.ReadAllText(settingsFile) == oldSettings, "failed settings write changed valid file");
        Directory.Delete(ruleFile + ".tmp");
        Directory.Delete(settingsFile + ".tmp");
        rules.Save();
        settings.Save();
        var recovered = new JsonRuleStore(ruleFile);
        recovered.Load();
        var recoveredSettings = new JsonSettingsStore(settingsFile);
        recoveredSettings.Load();
        Check(recovered.GetRule(@"C:\Apps\new.exe") != null, "valid rules not saved after obstacle removed");
        Check(recoveredSettings.Settings.Ui.VisibleApps == 7, "valid settings not saved after obstacle removed");
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

static void NullRootsPreserveOriginal()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-null-root-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var ruleFile = Path.Combine(dir, "rules.json");
        var settingsFile = Path.Combine(dir, "settings.json");
        File.WriteAllText(ruleFile, "null");
        File.WriteAllText(settingsFile, "null");
        var rules = new JsonRuleStore(ruleFile);
        rules.Load();
        rules.Save();
        var settings = new JsonSettingsStore(settingsFile);
        settings.Load();
        settings.Save();
        Check(Directory.GetFiles(dir, "rules.json.corrupt.*.bak").Length == 1,
            "null rule document was overwritten without backup");
        Check(Directory.GetFiles(dir, "settings.json.corrupt.*.bak").Length == 1,
            "null settings document was overwritten without backup");
        Check(File.ReadAllText(ruleFile).Contains("toolFirstRunUtc"), "null rule document did not recover");
        Check(File.ReadAllText(settingsFile).Contains("window"), "null settings document did not recover");
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

static void FailedBackupBlocksWrites()
{
    var dir = Path.Combine(Path.GetTempPath(), "mini-mixer-block-backup-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    const string invalid = "{ broken JSON";
    try
    {
        foreach (var filename in new[] { "rules.json", "settings.json" })
        {
            var path = Path.Combine(dir, filename);
            File.WriteAllText(path, invalid);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(invalid)))[..16];
            Directory.CreateDirectory(path + ".corrupt." + hash + ".bak");
            if (filename == "rules.json")
            {
                var rules = new JsonRuleStore(path);
                rules.Load();
                rules.Save();
            }
            else
            {
                var settings = new JsonSettingsStore(path);
                settings.Load();
                settings.Save();
            }
            Check(File.ReadAllText(path) == invalid, filename + " was overwritten after backup failure");
        }
    }
    finally
    {
        Directory.Delete(dir, true);
    }
}

static void SessionIdentityIncludesEndpoint()
{
    var deviceA = NAudioSessionManager.BuildControlKey("endpoint-A", "same-sid", 42, 0);
    var deviceB = NAudioSessionManager.BuildControlKey("endpoint-B", "same-sid", 42, 0);
    Check(!string.Equals(deviceA, deviceB, StringComparison.OrdinalIgnoreCase),
        "distinct devices share a control identity");
    Check(deviceA == NAudioSessionManager.BuildControlKey("endpoint-A", "same-sid", 99, 3),
        "stable session ID changed when process/index changed");
    var fallbackA = NAudioSessionManager.BuildControlKey("endpoint-A", "", 42, 0);
    var fallbackB = NAudioSessionManager.BuildControlKey("endpoint-A", "", 42, 1);
    Check(fallbackA != fallbackB, "unidentified sessions on same device were merged");
}

static void SessionSnapshotRetirementKeepsReusedObjects()
{
    var index = new SessionSnapshotIndex<object>();
    var a = new object();
    var b = new object();
    var c = new object();
    var initial = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
    {
        ["ep1|sid-a"] = a,
        ["ep2|sid-b"] = b
    };
    var initialRemoved = index.Replace(initial, out var initialAdded, out var initialRetired);
    Check(initialRemoved.Count == 0 && initialAdded.Count == 2 &&
        initialRetired.Count == 0, "initial publication returned retired handles");

    var refresh = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
    {
        ["EP1|SID-A"] = a, // same handle under same case-insensitive identity
        ["ep2|sid-b"] = c // same session key but new per-scan COM wrapper
    };
    var removed = index.Replace(refresh, out var added, out var retired);
    Check(removed.Count==0 && added.Count==0 && retired.Count==1 &&
        ReferenceEquals(retired[0],b), "superseded COM wrapper must be retired, reused must survive");
    Check(index.TryGet("ep1|sid-a", out var currentA) && ReferenceEquals(currentA,a) &&
        index.TryGet("ep2|sid-b",out var currentC) && ReferenceEquals(currentC,c),
        "latest wrapper identities were lost");

    removed=index.Replace(new Dictionary<string, object>(),out added,out retired);
    Check(removed.Count==2 && added.Count==0 && retired.Count==2 &&
        retired.Any(x=>ReferenceEquals(x,a)) && retired.Any(x=>ReferenceEquals(x,c)),
        "withdrawal did not retire all remaining session wrappers");
}

static void SessionSnapshotReportsRemovals()
{
    var index = new SessionSnapshotIndex<string>();
    var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["endpoint-A|sid-1"] = "first",
        ["endpoint-B|sid-1"] = "second"
    };
    Check(index.Replace(before).Count == 0, "initial snapshot incorrectly reports destroyed sessions");
    before.Clear();
    Check(index.TryGet("endpoint-a|SID-1", out var old) && old == "first",
        "published snapshot was mutable or case-sensitive");

    var after = new Dictionary<string, string> { ["endpoint-B|sid-1"] = "updated" };
    var removed = index.Replace(after);
    Check(removed.Count == 1 && removed[0] == "endpoint-A|sid-1",
        "wrong destroyed session identity");
    Check(!index.TryGet("endpoint-A|sid-1", out _), "removed control still addressable");
    Check(index.TryGet("endpoint-b|SID-1", out var kept) && kept == "updated",
        "surviving endpoint was not updated");
    Check(index.Replace(after).Count == 0, "unchanged refresh repeated destruction event");
    Check(index.Replace(new Dictionary<string, string>()).SequenceEqual(new[] { "endpoint-B|sid-1" }),
        "final removal was not detected exactly once");
}

static void SessionSnapshotConcurrentReaders()
{
    var index = new SessionSnapshotIndex<string>();
    index.Replace(new Dictionary<string, string> { ["audio"] = "valid" });
    var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var writer = Task.Run(() =>
    {
        for (var i = 0; i < 2500; i++)
        {
            index.Replace(new Dictionary<string, string>
            {
                ["audio"] = "valid",
                [$"changing-{i}"] = "transient"
            });
        }
    });
    var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
    {
        for (var i = 0; i < 10000; i++)
        {
            if (!index.TryGet("audio", out var value) || value != "valid")
                errors.Enqueue("core control disappeared in concurrent snapshot");
        }
    })).ToArray();
    Task.WaitAll(readers.Append(writer).ToArray());
    Check(errors.IsEmpty, errors.TryPeek(out var error) ? error : "inconsistent published snapshot");
}

static void CreatedSessionDeltaExact()
{
    var index = new SessionSnapshotIndex<string>();
    var established = new Dictionary<string, string>
    {
        ["device-A|sid-old"] = "old",
        ["device-B|sid-old"] = "other device"
    };
    index.Replace(established, out var baselineAdded);
    Check(baselineAdded.Count == 2, "initial scan missed baseline identities");

    var updated = new Dictionary<string, string>
    {
        ["device-A|sid-old"] = "old updated",
        ["device-B|sid-old"] = "other device",
        ["device-A|sid-new"] = "new first",
        ["device-B|sid-new"] = "new second"
    };
    var removed = index.Replace(updated, out var added);
    Check(removed.Count == 0, "created sessions fabricated removals");
    Check(added.Count == 2 &&
          added.Contains("device-A|sid-new") &&
          added.Contains("device-B|sid-new"),
        "create notification included an old or wrong session");
    Check(!added.Contains("device-B|sid-old"), "unrelated last session was reported as new");
    var repeatedRemoved = index.Replace(updated, out var repeatedAdded);
    Check(repeatedRemoved.Count == 0 && repeatedAdded.Count == 0,
        "repeated notification fabricated a second create event");
}

static void CreatedSessionAlreadyPolled()
{
    var index = new SessionSnapshotIndex<string>();
    index.Replace(new Dictionary<string, string> { ["device-A|sid-existing"] = "old" });
    var withNew = new Dictionary<string, string>
    {
        ["device-A|sid-existing"] = "old",
        ["device-A|sid-new"] = "new"
    };
    index.Replace(withNew, out var polledAdded);
    Check(polledAdded.SequenceEqual(new[] { "device-A|sid-new" }),
        "UI polling missed the real addition");
    var removed = index.Replace(withNew, out var eventAdded);
    Check(removed.Count == 0 && eventAdded.Count == 0,
        "late WASAPI callback fabricated a duplicate create event");
}

static void BackgroundCreationUsesOwnerContext() => BackgroundEventUsesOwnerContext(created: true);

static void BackgroundDestroyUsesOwnerContext() => BackgroundEventUsesOwnerContext(created: false);

static void BackgroundEventUsesOwnerContext(bool created)
{
    var previous = SynchronizationContext.Current;
    var ownerThread = Environment.CurrentManagedThreadId;
    var context = new QueuedContext();
    SynchronizationContext.SetSynchronizationContext(context);
    try
    {
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
        var path = MissingExe();
        var (controller, audio) = CreateController(rules, Session(path, "original"));
        var enumerationCount = audio.EnumerationThreads.Count;
        var mutationThreads = new List<int>();
        controller.AppEntries.CollectionChanged += (_, _) => mutationThreads.Add(Environment.CurrentManagedThreadId);
        if (created) audio.Sessions.Add(Session(MissingExe(), "added"));
        else audio.Sessions.Clear();
        Task.Run(() =>
        {
            if (created) audio.RaiseCreated(Session(path, "new-signal"));
            else audio.RaiseDestroyed("original");
        }).GetAwaiter().GetResult();
        Check(audio.EnumerationThreads.Count == enumerationCount, "audio enumerated on worker thread before owner dispatch");
        Check(mutationThreads.Count == 0, "collection mutated before owner dispatch");
        Check(context.PendingCount > 0, "background event was not posted to the owner context");
        context.Pump();
        Check(audio.EnumerationThreads.Count == enumerationCount + 1, "expected exactly one owner-context enumeration");
        Check(audio.EnumerationThreads[^1] == ownerThread, "enumeration executed off owner thread");
        Check(mutationThreads.All(thread => thread == ownerThread), "collection changed off owner thread");
        Check(controller.AppEntries.Count == (created ? 2 : 0), "collection not updated after dispatch");
        controller.Shutdown();
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
    }
}

static void BurstAndShutdownAreSafe()
{
    var previous = SynchronizationContext.Current;
    var context = new QueuedContext();
    SynchronizationContext.SetSynchronizationContext(context);
    try
    {
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-20));
        var path = MissingExe();
        var (controller, audio) = CreateController(rules, Session(path, "one"));
        var count = audio.EnumerationThreads.Count;
        Task.Run(() =>
        {
            for (var i = 0; i < 20; i++) audio.RaiseCreated(Session(path, "signal"));
        }).GetAwaiter().GetResult();
        Check(context.PendingCount == 1, "many audio events scheduled duplicate UI refreshes");
        context.Pump();
        Check(audio.EnumerationThreads.Count == count + 1, "burst must trigger exactly one refresh");
        Task.Run(() => audio.RaiseDestroyed("one")).GetAwaiter().GetResult();
        Check(context.PendingCount == 1, "destroy not queued");
        controller.Shutdown();
        context.Pump();
        Check(audio.EnumerationThreads.Count == count + 1, "queued refresh continued after shutdown");
        Task.Run(() => audio.RaiseCreated(Session(path, "late"))).GetAwaiter().GetResult();
        Check(context.PendingCount == 0, "late event scheduled refresh after shutdown");
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void WpfOnlySkipsNativeIpcAutoStart()
{
    Exception? failure = null;
    var worker = new Thread(() =>
    {
        var owner = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var names = new[]
        {
            "_mainWin", "_gameHookRuntime", "_overlayMode", "_gameHookAutoStartRuntime",
            "_gameHookIpcOnlyTestRun", "_gameHookIpcSphereInputTest", "_gameHookForwardInput",
            "_gameHookInputInterceptEnabled", "_statusTxt", "_settingsGameHookStatusTxt",
            "_gameHookAutoDetectTimer"
        };
        var original = names.ToDictionary(
            name => name,
            name => (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).GetValue(null));
        object? Read(string name) =>
            (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).GetValue(null);
        void Set(string name, object? value) =>
            (owner.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        object? Invoke(string name) =>
            (owner.GetMethod(name, flags) ?? throw new MissingMethodException(name)).Invoke(null, null);
        Window? window = null;
        try
        {
            window = new Window
            {
                Width = 32, Height = 32, Left = -32000, Top = -32000,
                ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None
            };
            _ = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            Set("_mainWin", window);
            Set("_gameHookRuntime", null);
            Set("_overlayMode", "gamehook");
            Set("_gameHookAutoStartRuntime", true); // historical default / user preference
            Set("_gameHookIpcOnlyTestRun", false);
            Set("_gameHookIpcSphereInputTest", false);
            Set("_gameHookForwardInput", false);
            Set("_gameHookInputInterceptEnabled", false);
            Set("_statusTxt", null);
            Set("_settingsGameHookStatusTxt", null);

            Check(Invoke("ShouldAutoStartGameHookRuntime") is false,
                "WPF-only launch treated stored AutoStart as explicit native consent");
            Invoke("SyncGameHookRuntimeState");
            Invoke("SyncGameHookRuntimeState"); // SourceInitialized + ContentRendered
            Check(Read("_gameHookRuntime") == null,
                "normal MM1 launch created IPC host or frame buffers");
            Invoke("StartGameHookAutoDetectLoop");
            Check(Read("_gameHookAutoDetectTimer") is not System.Windows.Threading.DispatcherTimer { IsEnabled: true },
                "WPF-only launch started the unnecessary 1.25-second native target scanner");

            Check(Invoke("StartGameHookRuntime") is true,
                "explicit manual IPC runtime start no longer works");
            var runtime = Read("_gameHookRuntime")!;
            var runtimeType = runtime.GetType();
            Check(runtimeType.GetProperty("IsRunning")!.GetValue(runtime) is true,
                "manual IPC runtime did not start");
            Invoke("SyncGameHookRuntimeState");
            Check(runtimeType.GetProperty("IsRunning")!.GetValue(runtime) is true,
                "normal mode resync stopped manually started runtime");
            Invoke("StopGameHookRuntime");
            Check(runtimeType.GetProperty("IsRunning")!.GetValue(runtime) is false,
                "manual IPC runtime did not stop cleanly");

            Set("_gameHookIpcOnlyTestRun", true);
            Check(Invoke("ShouldAutoStartGameHookRuntime") is true,
                "explicit IPC-only test opt-in was blocked");
            Invoke("SyncGameHookRuntimeState");
            Check(runtimeType.GetProperty("IsRunning")!.GetValue(Read("_gameHookRuntime")!) is true,
                "explicit IPC-only auto start did not start runtime");
            Invoke("StopGameHookRuntime");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            try { Invoke("StopGameHookRuntime"); } catch (Exception ex) { failure ??= ex; }
            foreach (var pair in original)
                Set(pair.Key, pair.Value);
            window?.Close();
        }
    });
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    Check(worker.Join(TimeSpan.FromSeconds(15)), "WPF IPC lifecycle probe did not finish");
    if (failure != null)
        throw new InvalidOperationException("WPF-only startup/IPC lifecycle failed", failure);
}

sealed class MemoryRules(DateTime firstRunUtc) : IRuleStore
{
    public DateTime ToolFirstRunUtc { get; } = firstRunUtc;
    public Dictionary<string, AppRule> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public void Load() { }
    public void Save() { }
    public AppRule? GetRule(string exePath) => Apps.GetValueOrDefault(exePath);
    public void UpsertRule(string exePath, AppRule rule) => Apps[exePath] = rule;
}

sealed class FakeSettings : ISettingsStore
{
    public AppSettings Settings { get; } = new();
    public void Load() { }
    public void Save() { }
}

sealed class FakeClassifier : ISessionClassifier
{
    public AppClassification Classify(string exePath, string displayName) => AppClassification.NonGame;
}

sealed class FakeAudio(IEnumerable<AudioSessionInfo> sessions) : IAudioSessionManager
{
    public List<AudioSessionInfo> Sessions { get; } = sessions.ToList();
    public List<(string Id, float Volume)> VolumeWrites { get; } = new();
    public List<(string Id, bool Mute)> MuteWrites { get; } = new();
    public List<int> EnumerationThreads { get; } = new();
    private Action<AudioSessionInfo>? _onCreated;
    private Action<string>? _onDestroyed;
    public List<AudioSessionInfo> EnumerateSessions()
    {
        EnumerationThreads.Add(Environment.CurrentManagedThreadId);
        return Sessions.ToList();
    }
    public void RaiseCreated(AudioSessionInfo session) => _onCreated?.Invoke(session);
    public void RaiseDestroyed(string sessionId) => _onDestroyed?.Invoke(sessionId);
    public void SetVolume(string sessionIdentifier, float volume)
    {
        VolumeWrites.Add((sessionIdentifier, volume));
        foreach (var s in Sessions.Where(s => s.SessionIdentifier == sessionIdentifier)) s.Volume = volume;
    }
    public void SetMute(string sessionIdentifier, bool mute)
    {
        MuteWrites.Add((sessionIdentifier, mute));
        foreach (var s in Sessions.Where(s => s.SessionIdentifier == sessionIdentifier)) s.IsMuted = mute;
    }
    public void OnSessionCreated(Action<AudioSessionInfo> callback) => _onCreated = callback;
    public void OnSessionDestroyed(Action<string> callback) => _onDestroyed = callback;
    public void StartMonitoring() { }
    public void StopMonitoring() { }
}

sealed class QueuedContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
    public int PendingCount => _pending.Count;
    public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));
    public void Pump()
    {
        while (_pending.Count > 0)
        {
            var (callback, state) = _pending.Dequeue();
            callback(state);
        }
    }
}
