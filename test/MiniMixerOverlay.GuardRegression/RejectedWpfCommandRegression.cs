using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;

internal static class RejectedWpfCommandRegression
{
    private enum Fault { False, Exception, Partial, Missing, Accepted }

    internal static void Run()
    {
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            try
            {
                foreach (var mode in new[] { "normal", "quick", "popup" })
                foreach (var fault in new[] { Fault.False, Fault.Exception, Fault.Partial, Fault.Missing, Fault.Accepted })
                    CheckUi(mode, fault);
            }
            catch (Exception ex) { failure = ex; }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        if (failure != null)
            throw new InvalidOperationException("R-01 UI command-outcome regression failed: " + failure, failure);
    }

    private static void Require(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException(reason);
    }

    private sealed class TestAudio : IAudioSessionManager
    {
        public List<AudioSessionInfo> Sessions = new();
        public bool Reject, Throw;
        public HashSet<string> RejectIds = new(StringComparer.OrdinalIgnoreCase);
        public int VolumeAttempts, MuteAttempts, VolumeAccepted, MuteAccepted;
        public List<AudioSessionInfo> EnumerateSessions() => Sessions.ToList();
        public bool TrySetVolume(string id, float volume)
        {
            VolumeAttempts++;
            if (Throw) throw new InvalidOperationException("Fake provider rejected volume");
            if (Reject || RejectIds.Contains(id)) return false;
            var target = Sessions.FirstOrDefault(x => x.SessionIdentifier == id);
            if (target == null) return false;
            target.Volume = volume;
            VolumeAccepted++;
            return true;
        }
        public bool TrySetMute(string id, bool muted)
        {
            MuteAttempts++;
            if (Throw) throw new InvalidOperationException("Fake provider rejected mute");
            if (Reject || RejectIds.Contains(id)) return false;
            var target = Sessions.FirstOrDefault(x => x.SessionIdentifier == id);
            if (target == null) return false;
            target.IsMuted = muted;
            MuteAccepted++;
            return true;
        }
        public void SetVolume(string id, float volume) => throw new Exception("TrySetVolume should be used");
        public void SetMute(string id, bool muted) => throw new Exception("TrySetMute should be used");
        public void OnSessionCreated(Action<AudioSessionInfo> cb) { }
        public void OnSessionDestroyed(Action<string> cb) { }
        public void StartMonitoring() { }
        public void StopMonitoring() { }
    }
    private static void Click(Border target)
    {
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });
    }

    private static void CheckUi(string mode, Fault fault)
    {
        var t = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        void Set(string name, object? value) =>
            (t.GetField(name, flags) ?? throw new MissingFieldException(name)).SetValue(null, value);
        object? Get(string name) =>
            (t.GetField(name, flags) ?? throw new MissingFieldException(name)).GetValue(null);

        var path = @"C:\owned\ui-command-" + mode + "-" + fault + ".exe";
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-90));
        var audio = new TestAudio();
        audio.Sessions.Add(new AudioSessionInfo
        {
            ExePath = path, ExeName = "owned-ui.exe", DisplayName = "Owned UI",
            SessionIdentifier = "good", Volume = .90f
        });
        if (fault == Fault.Partial)
        {
            audio.Sessions.Add(new AudioSessionInfo
            {
                ExePath = path, ExeName = "owned-ui.exe", DisplayName = "Owned UI",
                SessionIdentifier = "bad", Volume = .90f
            });
            audio.RejectIds.Add("bad");
        }
        if (fault == Fault.Missing) audio.RejectIds.Add("good");
        if (fault == Fault.False) audio.Reject = true;
        if (fault == Fault.Exception) audio.Throw = true;

        var classifier = new FakeClassifier();
        var controller = new MixerController(audio, new GuardEngine(rules, classifier),
            classifier, rules, new FakeSettings(),
            guardLogFilePath: System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "mini-mixer-ui-fault-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"), "guard.log"));
        Popup? popup = null;
        Window? main = null;
        var settings = new Window();
        try
        {
            controller.Initialize(); // Baseline, no automatic volume write
            var entry = controller.AppEntries.Single();
            Set("_ctrl", controller);
            Set("_settingsWin", settings); // settings open: periodic Refresh is paused
            Set("_mainWin", null);
            Set("_isRefreshing", false);
            Set("_edgeDockEnabled", mode == "quick");
            Set("_edgeDockActive", mode == "quick");
            Set("_edgeDockExpanded", mode != "quick");

            Border icon;
            Slider slider;
            TextBlock percent;
            if (mode != "popup")
            {
                var card = (Border)(t.GetMethod("BuildCard", flags) ??
                    throw new MissingMethodException("BuildCard")).Invoke(null, new object[] { entry })!;
                var elements = mode == "quick"
                    ? ((StackPanel)card.Child).Children.Cast<UIElement>()
                    : ((Grid)card.Child).Children.Cast<UIElement>();
                slider = elements.OfType<Slider>().Single();
                icon = elements.OfType<Border>().Single();
                percent = elements.OfType<TextBlock>().Single(x => x.Text == "90%");
            }
            else
            {
                main = new Window { Width = 1, Height = 1, ShowInTaskbar = false };
                Set("_mainWin", main);
                var open = t.GetMethod("ShowActiveAppPopup", flags) ??
                           throw new MissingMethodException("ShowActiveAppPopup");
                open.Invoke(null, new object[] { entry, new Border { Width = 22, Height = 22 } });
                popup = Get("_activeAppPopup") as Popup ??
                        throw new InvalidOperationException("Popup not created");
                var panel = (StackPanel)((Border)popup.Child).Child;
                var row = panel.Children.OfType<Grid>().Single();
                slider = row.Children.OfType<Slider>().Single();
                icon = row.Children.OfType<Border>().Single();
                percent = row.Children.OfType<TextBlock>().Single();
                Require(popup.IsOpen, "Popup not open for owned UI test");
            }

            Require(slider.IsEnabled && Math.Abs(slider.Value - 90) < .01 && percent.Text == "90%",
                "Baseline UI mismatch " + mode + " / " + fault);
            Click(icon);
            var good = fault == Fault.Accepted;
            Require(audio.MuteAttempts == audio.Sessions.Count, "Mute command attempts mismatch");
            Require(controller.AppEntries.Single().IsMuted == good,
                "Controller mute outcome mismatch");
            Require(slider.IsEnabled == !good &&
                (good ? percent.Text == "0%" : percent.Text.Contains("90%") && percent.Text.Contains("!")),
                "Mute reject/accept UI state mismatch " + mode + " / " + fault + ": " + percent.Text);
            if (!good)
            {
                Require(Math.Abs(slider.Value - 90) < .01, "Rejected mute moved slider");
                Require(icon.BorderThickness.Left > 0, "Rejected mute has no visual warning");
            }
            else
            {
                Require(audio.Sessions.All(s => s.IsMuted), "Accepted mute did not change provider");
                // Unmute to permit a successful volume control write.
                Click(icon);
                Require(slider.IsEnabled && controller.AppEntries.Single().IsMuted == false,
                    "Successful unmute did not restore UI");
            }

            if (mode == "popup" && (fault == Fault.False || fault == Fault.Accepted))
            {
                var staleEntry = entry;
                controller.RefreshSessions(); // Popup remains anchored to old UI data.
                Require(!ReferenceEquals(staleEntry, controller.AppEntries.Single()),
                    "Popup test did not replace the underlying controller snapshot");
                Require(popup!.IsOpen, "Popup unexpectedly closed during session refresh");
            }

            // In success case clear mute, in failure case already unmuted.
            var beforeWrites = audio.VolumeAttempts;
            slider.Value = 25;
            Require(audio.VolumeAttempts == beforeWrites + audio.Sessions.Count,
                "ValueChanged restoration issued a second write " + mode + " / " + fault);
            Require(rules.GetRule(path)!.ManualOverride, "Manual intent lost after provider failure");
            var current = controller.AppEntries.Single();
            if (good)
            {
                Require(Math.Abs(current.CombinedVolume - .25f) < .001f &&
                        Math.Abs(slider.Value - 25) < .1 &&
                        percent.Text == "25%" && audio.Sessions.All(s => Math.Abs(s.Volume - .25f) < .001f),
                        "Accepted volume did not update all controls " + mode);
            }
            else
            {
                Require(Math.Abs(current.CombinedVolume - .90f) < .001f &&
                        Math.Abs(slider.Value - 90) < .1 && percent.Text.Contains("90%") &&
                        percent.Text.Contains("!") && slider.IsEnabled,
                        "Rejected volume left optimistic WPF state " + mode + " / " + fault);
                if (fault != Fault.Partial)
                    Require(audio.Sessions.All(s => Math.Abs(s.Volume - .90f) < .001f),
                        "Rejected volume changed fake provider");
                else
                    Require(audio.VolumeAccepted == 1 && audio.MuteAccepted == 1,
                        "Partial success fixture not exercised");
            }
            if (mode == "popup")
                Require(popup!.IsOpen && ReferenceEquals(Get("_activeAppPopup"), popup),
                    "Popup lifetime changed during failed audio action");
        }
        finally
        {
            if (popup != null)
            {
                (t.GetMethod("CloseActiveAppPopup", flags) ??
                 throw new MissingMethodException("CloseActiveAppPopup")).Invoke(null, null);
                Require(Get("_activeAppPopup") == null, "Popup not closed by canonical owner");
            }
            Set("_ctrl", null);
            Set("_mainWin", null);
            Set("_settingsWin", null);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);
            Set("_isRefreshing", false);
            main?.Close();
            settings.Close();
            controller.Shutdown();
        }
    }
}
