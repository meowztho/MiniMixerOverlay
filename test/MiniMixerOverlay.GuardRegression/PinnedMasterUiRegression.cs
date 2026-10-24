using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Models;
using MiniMixerOverlay.Infrastructure.Persistence;
using Border = System.Windows.Controls.Border;

internal static class PinnedMasterUiRegression
{
    internal static void Run()
    {
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            try { Test(); }
            catch (Exception ex) { failure = ex; }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        if (failure != null) throw new InvalidOperationException("Pinned master UI: " + failure.Message, failure);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Test()
    {
        // Exercise the actual JSON store in an isolated temporary profile.
        var settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "mm1-nabyscape-hotkey-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var firstStore = new JsonSettingsStore(settingsPath);
            firstStore.Load();
            Require(!firstStore.Settings.Ui.ToggleHotkeyEnabled &&
                    firstStore.Settings.Ui.ToggleHotkeyKey == "M",
                "New profiles must not capture global keys without opt-in");
            firstStore.Settings.Ui.ToggleHotkeyEnabled = true;
            firstStore.Settings.Ui.ToggleHotkeyKey = "F9";
            firstStore.Save();
            var reopenedStore = new JsonSettingsStore(settingsPath);
            reopenedStore.Load();
            Require(reopenedStore.Settings.Ui.ToggleHotkeyEnabled &&
                    reopenedStore.Settings.Ui.ToggleHotkeyKey == "F9",
                "Opt-in global hotkey did not persist through the production JSON owner");
        }
        finally
        {
            if (System.IO.File.Exists(settingsPath)) System.IO.File.Delete(settingsPath);
        }

        var ui = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var orderMethod = ui.GetMethod("OrderMixerEntries", flags) ??
                          throw new MissingMethodException("OrderMixerEntries");
        var cardOrderMethod = ui.GetMethod("OrderMainCardEntries", flags) ??
                              throw new MissingMethodException("OrderMainCardEntries");
        var iconMethod = ui.GetMethod("MakeIconElement", flags) ??
                         throw new MissingMethodException("MakeIconElement");
        var rememberMethod = ui.GetMethod("RememberForegroundAudioEntry", flags) ??
                             throw new MissingMethodException("RememberForegroundAudioEntry");
        var refreshMethod = ui.GetMethod("Refresh", flags) ??
                            throw new MissingMethodException("Refresh");

        AppEntry Entry(string path, string name, bool audible = true) => new()
        {
            ExePath = path,
            DisplayName = name,
            CombinedVolume = audible ? 0.4f : 0f,
            HasActiveAudio = audible
        };

        var master = Entry("__system_master__", "System Master");
        var sounds = Entry("__system_sound__", "System Sounds");
        sounds.IsSystemSound = true;
        var appA = Entry("C:\\temp\\alpha.exe", "Alpha");
        var appB = Entry("C:\\temp\\beta.exe", "Beta");
        var other = Entry("C:\\temp\\other.exe", "Other");

        List<AppEntry> Order(IEnumerable<AppEntry> data, AppEntry? foreground) =>
            (List<AppEntry>)(orderMethod.Invoke(null, new object?[] { data, foreground }) ??
                             throw new InvalidOperationException("No strip ordering"));
        List<AppEntry> Cards(IEnumerable<AppEntry> data, AppEntry? foreground) =>
            (List<AppEntry>)(cardOrderMethod.Invoke(null, new object?[] { data, foreground }) ??
                             throw new InvalidOperationException("No card ordering"));

        var pool = new[] { sounds, other, appB, master, appA };
        var first = Order(pool, appB);
        Require(ReferenceEquals(first[0], master) && ReferenceEquals(first[1], appB),
            "Master/foreground must be first and second");
        Require(first.Select(e => e.ExePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 5,
            "Sorting introduced duplicate controls");

        var second = Order(pool, appA);
        Require(ReferenceEquals(second[0], master) && ReferenceEquals(second[1], appA),
            "Foreground change did not move new active app to second");
        var withoutForeground = Order(pool, null);
        Require(ReferenceEquals(withoutForeground[0], master),
            "Fallback lost pinned master");
        var withSoundForeground = Order(pool, sounds);
        Require(ReferenceEquals(withSoundForeground[0], master) &&
                !ReferenceEquals(withSoundForeground[1], sounds),
            "System Sounds must not impersonate an active application");
        var onlyApps = Order(new[] { appA, appB }, appB);
        Require(ReferenceEquals(onlyApps[0], appB), "Missing master invented an entry");
        var cardsWithApp = Cards(first, appB);
        Require(ReferenceEquals(cardsWithApp[0], appB) && ReferenceEquals(cardsWithApp[1], master),
            "Expanded cards must put current app ahead of system master");
        var cardsWithoutApp = Cards(withoutForeground, null);
        Require(ReferenceEquals(cardsWithoutApp[0], master),
            "Expanded cards must fall back to master when no foreground app exists");
        Require(ReferenceEquals(Cards(first, appA)[0], appA),
            "New foreground app did not become first card");
        Require(ReferenceEquals(Cards(first, sounds)[0], master),
            "System sounds should not displace master without an audio app");

        var white = System.Windows.Media.Brushes.White;
        Viewbox SystemIcon(string id, bool muted = false) =>
            (Viewbox)(iconMethod.Invoke(null, new object?[] { id, new byte[] { 1, 2, 3 }, muted }) ??
                      throw new InvalidOperationException("System icon is null"));
        var volumeIcon = SystemIcon("__system_master__");
        var soundsIcon = SystemIcon("__system_sound__");
        var mutedIcon = SystemIcon("__system_master__", muted: true);
        Require(volumeIcon.Child is Canvas && soundsIcon.Child is Canvas,
            "System icons must be vector drawings, not Windows bitmaps");
        var speakerCanvas = (Canvas)volumeIcon.Child;
        var noteCanvas = (Canvas)soundsIcon.Child;
        Require(speakerCanvas.Children.OfType<System.Windows.Shapes.Path>().Count() == 2 &&
                speakerCanvas.Children.OfType<System.Windows.Shapes.Path>()
                    .All(p => ReferenceEquals(p.Fill, white) || ReferenceEquals(p.Stroke, white)),
            "System master speaker was not drawn white");
        Require(noteCanvas.Children.OfType<System.Windows.Shapes.Ellipse>().Count() == 2 &&
                noteCanvas.Children.OfType<System.Windows.Shapes.Ellipse>()
                    .All(e => ReferenceEquals(e.Fill, white)),
            "System Sounds musical notes were not drawn white");
        Require(mutedIcon.Opacity < volumeIcon.Opacity, "Mute indication lost");

        // Exercise the actual Refresh UI path with fake sessions and remembered
        // foreground: settings window suppresses real foreground/audio polling.
        var sess = new[]
        {
            new AudioSessionInfo { ExePath = master.ExePath, ExeName = "SystemMaster",
                DisplayName = master.DisplayName, SessionIdentifier = "master", Volume = 0.38f },
            new AudioSessionInfo { ExePath = sounds.ExePath, ExeName = "SystemSounds",
                DisplayName = sounds.DisplayName, SessionIdentifier = "sounds", Volume = 0.09f,
                IsSystemSound = true },
            new AudioSessionInfo { ExePath = appA.ExePath, ExeName = "alpha.exe",
                DisplayName = appA.DisplayName, SessionIdentifier = "alpha",
                Volume = 0.4f, SignalPeakLevel = 0.55f },
            new AudioSessionInfo { ExePath = appB.ExePath, ExeName = "beta.exe",
                DisplayName = appB.DisplayName, SessionIdentifier = "beta", Volume = 0f },
            new AudioSessionInfo { ExePath = other.ExePath, ExeName = "other.exe",
                DisplayName = other.DisplayName, SessionIdentifier = "other", Volume = 0.6f }
        };
        var audio = new FakeAudio(sess);
        var rules = new MemoryRules(DateTime.UtcNow.AddDays(-40));
        var classifier = new FakeClassifier();
        var controller = new MixerController(audio, new GuardEngine(rules, classifier),
            classifier, rules, new FakeSettings(),
            guardLogFilePath: System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "mini-mixer-regression-logs-" + Environment.ProcessId + "-" +
                System.Diagnostics.Process.GetCurrentProcess().StartTime.Ticks, "guard.log"));
        var list = new StackPanel();
        var strip = new StackPanel();

        object? Read(string name) => (ui.GetField(name, flags) ??
            throw new MissingFieldException(name)).GetValue(null);
        void Set(string name, object? value) => (ui.GetField(name, flags) ??
            throw new MissingFieldException(name)).SetValue(null, value);
        var previousRemembered = Read("_lastNonSystemForegroundExePath");
        var previousSeen = Read("_lastNonSystemForegroundSeenUtc");
        var previousVisible = Read("_visibleApps");
        var previousSphereManual = Read("_cornerHintManualPosition");
        var previousSphereX = Read("_cornerHintManualX");
        var previousSphereY = Read("_cornerHintManualY");
        try
        {
            controller.Initialize();
            Require(controller.AppEntries.Single(e => e.ExePath == appA.ExePath).SignalPeakLevel == 0.55f,
                "App peak must reflect measured session signal, not slider volume");
            Require(controller.AppEntries.Single(e => e.ExePath == appB.ExePath).SignalPeakLevel == null,
                "Unsupported meter must remain unknown, not silently declared as silence");
            controller.Settings.Ui.ShowOnlyActiveAudio = true;
            Set("_ctrl", controller);
            Set("_appList", list);
            Set("_activeAppsStrip", strip);
            Set("_mainWin", null);
            Set("_settingsWin", new Window());
            Set("_visibleApps", 5);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);

            var countMethod = ui.GetMethod("GetEffectiveVisibleApps", flags) ??
                throw new MissingMethodException("GetEffectiveVisibleApps");
            int VisibleSlots() => (int)(countMethod.Invoke(null, null) ?? 0);
            Set("_visibleApps", 1);
            Require(VisibleSlots() == 1, "One visible app must mean exactly one large card");
            Set("_visibleApps", 3);
            Require(VisibleSlots() == 3, "Expanded mixer ignored configured three-card limit");
            Set("_visibleApps", 1);
            Set("_edgeDockEnabled", true);
            Set("_edgeDockActive", true);
            Set("_edgeDockExpanded", false);
            Require(VisibleSlots() == 1, "Collapsed dock must retain single card");
            Set("_visibleApps", 0);
            Require(VisibleSlots() == 0, "Icon-only configuration must remain icon-only");
            Set("_visibleApps", 5);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);

            void Remember(string path)
            {
                var selected = controller.AppEntries.First(e =>
                    e.ExePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                rememberMethod.Invoke(null, new object?[] { selected });
            }
            void Refresh() => refreshMethod.Invoke(null, null);
            AppEntry StripAt(int index) => (AppEntry)((Border)strip.Children[index]).Tag;
            string CardAt(int index)
            {
                var grid = ((Border)list.Children[index]).Child as Grid ??
                           throw new InvalidOperationException("Expected normal mixer card");
                return grid.Children.OfType<TextBlock>().First(t =>
                    t.Text == master.DisplayName || t.Text == appA.DisplayName ||
                    t.Text == appB.DisplayName || t.Text == other.DisplayName).Text;
            }

            Remember(appB.ExePath); // intentionally inactive (0%) foreground audio
            Set("_visibleApps", 1);
            Refresh();
            Require(StripAt(0).ExePath == master.ExePath && StripAt(1).ExePath == appB.ExePath,
                "Mini strip must still pin master and inactive foreground");
            Require(list.Children.Count == 1 && CardAt(0) == appB.DisplayName,
                "One visible app must show just the foreground card, not master");
            Set("_visibleApps", 2);
            Refresh();
            Require(list.Children.Count == 2 && CardAt(0) == appB.DisplayName &&
                    CardAt(1) == master.DisplayName,
                "Two visible apps must show foreground first and master second");
            Set("_visibleApps", 3);
            Refresh();
            Require(list.Children.Count == 3 && CardAt(0) == appB.DisplayName,
                "Three visible apps must show exactly three eligible cards");
            var masterAnchor = (Border)strip.Children[0];
            Refresh();
            Require(ReferenceEquals(masterAnchor, strip.Children[0]),
                "Identical strip order replaced popup anchor during refresh");

            Remember(appA.ExePath);
            Set("_visibleApps", 1);
            Refresh();
            Require(StripAt(0).ExePath == master.ExePath && StripAt(1).ExePath == appA.ExePath,
                "Foreground switch broke strip pinning");
            Require(list.Children.Count == 1 && CardAt(0) == appA.DisplayName,
                "Foreground switch did not replace the one visible card");
            Set("_visibleApps", 3);
            Refresh();
            Require(list.Children.Count == 3 && CardAt(0) == appA.DisplayName &&
                    CardAt(1) == master.DisplayName,
                "Multi-card display did not put foreground ahead of master");
            Require(ReferenceEquals(masterAnchor, strip.Children[0]),
                "Stable first-icon anchor changed during foreground switch");
            var unique = Enumerable.Range(0, strip.Children.Count)
                .Select(i => StripAt(i).ExePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Require(unique == strip.Children.Count, "Duplicate icon after foreground change");

            // No matching foreground: master is fallback #1 in the large view.
            Set("_lastNonSystemForegroundExePath", string.Empty);
            Set("_visibleApps", 1);
            Refresh();
            Require(list.Children.Count == 1 && CardAt(0) == master.DisplayName,
                "Master was not first large card without a foreground audio app");
            Require(StripAt(0).ExePath == master.ExePath,
                "Strip lost pinned master during fallback");
            Set("_visibleApps", 0);
            Refresh();
            Require(list.Children.Count == 0 && strip.Children.Count > 0,
                "Icon-only mode must hide large cards but retain mini strip");
            Require(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
                "UI render performed real/fake audio writes");

            // Exercise the real Settings slider while its window stays open.
            // WPF's ordinary Refresh timer is deliberately paused in this state.
            // The preview must rebind cards from FakeAudio without waiting for Close.
            Set("_settingsWin", null);
            Set("_visibleApps", 3);
            Remember(appA.ExePath);
            Refresh();
            var showSettings = ui.GetMethod("ShowSettings", flags) ??
                               throw new MissingMethodException("ShowSettings");
            showSettings.Invoke(null, null);
            var settingsWindow = Read("_settingsWin") as Window ??
                                 throw new InvalidOperationException("Real Settings window missing");
            try
            {
                T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
                {
                    if (parent is T element && element.Name == name)
                        return element;
                    var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                    for (var i = 0; i < children; i++)
                    {
                        try { return Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(parent, i), name); }
                        catch (InvalidOperationException) { }
                    }
                    throw new InvalidOperationException("Settings control not found: " + name);
                }

                void DrainPreview()
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new Action(() => frame.Continue = false),
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }

                settingsWindow.UpdateLayout();
                var glassLooks = Find<System.Windows.Controls.ComboBox>(settingsWindow, "GlassLookPreset");
                Require(glassLooks.SelectedItem is string,
                    "Compact glass appearance selector must exist");
                glassLooks.SelectedItem = glassLooks.Items[0];
                DrainPreview();
                Require(Equals(glassLooks.SelectedItem, glassLooks.Items[0]),
                    "Glass preset selection must remain logically consistent after applying its values");
                var glassLookText = glassLooks.Template.FindName("SelectedText", glassLooks) as TextBlock;
                Require(glassLookText?.Text == glassLooks.Items[0] as string,
                    "Glass preset template retained a stale visible label after selection changed");
                Require((int)Read("_glassStrength")! == 60 &&
                        (int)Read("_glassTransparency")! == 80 &&
                        (int)Read("_glassBorderSmudge")! == 60 &&
                        controller.Settings.Ui.GlassStrength == 60 &&
                        controller.Settings.Ui.GlassTransparency == 80,
                    "Subtle preset must use the existing persisted glass settings owner");
                glassLooks.SelectedItem = glassLooks.Items[1];
                Require((int)Read("_glassStrength")! == 74 &&
                        (int)Read("_glassTransparency")! == 72 &&
                        (int)Read("_glassBorderThickness")! == 1,
                    "Balanced preset must restore original glass defaults");
                var glassDetails = Find<System.Windows.Controls.Expander>(settingsWindow, "AdvancedGlassOptions");
                Require(!glassDetails.IsExpanded,
                    "Ordinary default should not expose every design slider");
                glassDetails.IsExpanded = true;
                DrainPreview();
                Require(glassDetails.Content is StackPanel advancedControls &&
                        advancedControls.Children.OfType<Slider>().Count() >= 4,
                    "Advanced options must retain existing fine controls");
                glassDetails.IsExpanded = false;
                glassLooks.SelectedItem = glassLooks.Items[3];
                Require(glassDetails.IsExpanded &&
                        controller.Settings.Ui.GlassStrength == 74,
                    "Selecting Custom must reveal the existing controls without rewriting settings");

                var hotkeyOptions = Find<System.Windows.Controls.Expander>(settingsWindow, "HotkeyOptions");
                Require(!hotkeyOptions.IsExpanded, "Optional hotkey must not clutter default settings");
                hotkeyOptions.IsExpanded = true;
                DrainPreview();
                var hotkeyCheck = Find<System.Windows.Controls.CheckBox>(settingsWindow,
                    "ToggleHotkeyEnabled");
                var hotkeyKey = Find<System.Windows.Controls.ComboBox>(settingsWindow,
                    "ToggleHotkeyKey");
                Require(hotkeyCheck.IsChecked == false && !hotkeyKey.IsEnabled,
                    "Global hotkey must default off");
                hotkeyCheck.IsChecked = true;
                hotkeyCheck.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(controller.Settings.Ui.ToggleHotkeyEnabled && hotkeyKey.IsEnabled,
                    "Hotkey enabling not routed through existing settings owner");
                hotkeyKey.SelectedItem = "F9";
                Require(controller.Settings.Ui.ToggleHotkeyKey == "F9",
                    "Hotkey key selection not persisted through settings owner");
                hotkeyCheck.IsChecked = false;
                hotkeyCheck.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(!controller.Settings.Ui.ToggleHotkeyEnabled,
                    "Hotkey disable did not update settings");
                var keyResolver = ui.GetMethod("ResolveToggleHotkeyKey", flags) ??
                    throw new MissingMethodException("ResolveToggleHotkeyKey");
                Require((uint)keyResolver.Invoke(null, new object?[] { "F9" })! == 0x78 &&
                        (uint)keyResolver.Invoke(null, new object?[] { "invalid" })! == 0,
                    "Global hotkey accepted an unsupported key");
                var visibleSlider = Find<Slider>(settingsWindow, "VisibleAppsSlider");
                Require(settingsWindow.IsVisible, "Settings preview window was not open");
                visibleSlider.Value = 1;
                DrainPreview();
                Require(settingsWindow.IsVisible && list.Children.Count == 1 &&
                        CardAt(0) == appA.DisplayName,
                    "Visible Apps=1 did not refresh the full card view while Settings remained open");
                visibleSlider.Value = 2;
                DrainPreview();
                Require(settingsWindow.IsVisible && list.Children.Count == 2 &&
                        CardAt(1) == master.DisplayName,
                    "Visible Apps=2 did not refresh before closing Settings");
                visibleSlider.Value = 0;
                DrainPreview();
                Require(settingsWindow.IsVisible && list.Children.Count == 0 &&
                        strip.Children.Count > 0,
                    "Visible Apps=0 did not switch to icon-only while Settings remained open");

                visibleSlider.Value = 1;
                DrainPreview();
                var previousCard = list.Children[0];
                var applyGlassTheme = ui.GetMethod("ApplyGlassTheme", flags) ??
                                      throw new MissingMethodException("ApplyGlassTheme");
                applyGlassTheme.Invoke(null, null);
                DrainPreview();
                Require(settingsWindow.IsVisible && list.Children.Count == 1 &&
                        !ReferenceEquals(previousCard, list.Children[0]),
                    "Glass changes did not update the card visual until Settings closed");
                Require(audio.VolumeWrites.Count == 0 && audio.MuteWrites.Count == 0,
                    "Live Settings preview performed an audio write");
            }
            finally
            {
                settingsWindow.Close();
                Set("_settingsWin", null);
            }
        }
        finally
        {
            Set("_ctrl", null);
            Set("_appList", null);
            Set("_activeAppsStrip", null);
            Set("_settingsWin", null);
            Set("_mainWin", null);
            Set("_edgeDockEnabled", false);
            Set("_edgeDockActive", false);
            Set("_edgeDockExpanded", true);
            Set("_visibleApps", previousVisible);
            Set("_cornerHintManualPosition", previousSphereManual);
            Set("_cornerHintManualX", previousSphereX);
            Set("_cornerHintManualY", previousSphereY);
            Set("_lastNonSystemForegroundExePath", previousRemembered);
            Set("_lastNonSystemForegroundSeenUtc", previousSeen);
            controller.Shutdown();
        }
    }
}
