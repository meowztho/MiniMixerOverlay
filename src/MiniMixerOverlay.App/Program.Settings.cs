using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Models;

namespace MiniMixerOverlay.App
{
    // SYS-UI remains the same Program owner. This file only isolates the settings view implementation.
    public static partial class Program
    {
        private static void ShowSettings()
        {
            if (_settingsWin != null)
            {
                _settingsWin.Activate();
                return;
            }

            var win = new Window
            {
                Title = Ui("Einstellungen", "Settings"),
                Width = 320,
                Height = 620,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = _mainWin?.Topmost ?? true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            if (_mainWin != null)
            {
                win.Owner = _mainWin;
            }

            win.MouseEnter += (_, _) => _isUserInteracting = true;
            win.MouseLeave += (_, _) => _isUserInteracting = false;

            var outer = BuildGlassPanel();
            _settingsGlassBorder = outer;
            ApplyGlassTheme();
            var sp = new StackPanel
            {
                Margin = new Thickness(18, 14, 18, 14)
            };
            var settingsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sp
            };
            Style? sliderTemplateStyle = null;
            Style? comboTemplateStyle = null;
            Style? comboItemTemplateStyle = null;
            Style? actionButtonStyle = null;

            var isSyncingDockSideCombo = false;
            Slider? refreshSliderRef = null;
            TextBlock? refreshLblRef = null;
            Slider? widthSliderRef = null;
            TextBlock? widthLblRef = null;
            Button? btnCenterRef = null;
            Button? btnTopRef = null;
            Button? btnLeftRef = null;
            Button? btnRightRef = null;

            var hg = new Grid();
            hg.Children.Add(new TextBlock
            {
                Text = Ui("\u2699  Einstellungen", "\u2699  Settings"),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeBtn = new Button
            {
                Content = "\u2715",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 220)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (_, _) =>
            {
                _isUserInteracting = false;
                _settingsWin = null;
                _settingsGlassBorder = null;
                _settingsGameHookStatusTxt = null;
                win.Close();
            };

            hg.Children.Add(closeBtn);
            sp.Children.Add(hg);
            sp.Children.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromArgb(80, 80, 80, 120)),
                Margin = new Thickness(0, 6, 0, 8)
            });

            var infoText = new TextBlock
            {
                Text = Ui(
                    "Rand-Andocken blendet das Fenster unaufallig am linken/rechten Rand ein und aus.",
                    "Edge docking subtly hides/shows the window at the left/right edge."),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 210)),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(infoText);

            var languageToolsLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var btnLanguage = new Button
            {
                Content = FormatNextLanguageButtonLabel(),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            ApplyActionButtonTheme(btnLanguage);
            AttachOptionLegend(btnLanguage, Ui("Wechselt zur naechsten Sprache in der Liste.", "Switches to the next language in the list."));
            btnLanguage.Click += (_, _) =>
            {
                var nextLanguage = GetNextUiLanguageCode(_uiLanguage);
                SetUiLanguage(nextLanguage);
            };
            languageToolsLine.Children.Add(btnLanguage);
            sp.Children.Add(languageToolsLine);

            var hotkeyEnabled = new CheckBox
            {
                Name = "ToggleHotkeyEnabled",
                Content = Ui("Globaler Hotkey für Mixer (Strg+Alt+Taste)",
                             "Global mixer shortcut (Ctrl+Alt+key)"),
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 4),
                IsChecked = _ctrl?.Settings.Ui.ToggleHotkeyEnabled == true
            };
            ApplyCheckTheme(hotkeyEnabled);
            var hotkeyOptions = new StackPanel();
            hotkeyOptions.Children.Add(hotkeyEnabled);
            var hotkeyKey = new ComboBox
            {
                Name = "ToggleHotkeyKey",
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                ItemsSource = new[] { "M", "O", "F9", "F10" },
                SelectedItem = _ctrl?.Settings.Ui.ToggleHotkeyKey is "M" or "O" or "F9" or "F10"
                    ? _ctrl.Settings.Ui.ToggleHotkeyKey : "M",
                IsEnabled = hotkeyEnabled.IsChecked == true
            };
            ApplyComboTheme(hotkeyKey);
            hotkeyOptions.Children.Add(hotkeyKey);
            var hotkeyStatus = new TextBlock
            {
                Foreground = Brushes.LightGray,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 7),
                TextWrapping = TextWrapping.Wrap
            };
            hotkeyOptions.Children.Add(hotkeyStatus);
            sp.Children.Add(new Expander
            {
                Header = Ui("Tastenkürzel (optional)", "Keyboard shortcut (optional)"),
                Name = "HotkeyOptions",
                IsExpanded = _ctrl?.Settings.Ui.ToggleHotkeyEnabled == true,
                Foreground = Brushes.White,
                Content = hotkeyOptions,
                Margin = new Thickness(0, 0, 0, 6)
            });
            void RefreshHotkeyStatus()
            {
                hotkeyStatus.Text = hotkeyEnabled.IsChecked != true
                    ? Ui("Deaktiviert (Standard)", "Disabled (default)")
                    : _toggleHotkeyRegistered
                        ? Ui("Aktiv: Strg+Alt+", "Active: Ctrl+Alt+") + hotkeyKey.SelectedItem
                        : !string.IsNullOrEmpty(_toggleHotkeyError)
                            ? _toggleHotkeyError
                            : Ui("Wird beim App-Start aktiviert", "Activates at application start");
            }
            hotkeyEnabled.Click += (_, _) =>
            {
                if (_ctrl != null) _ctrl.Settings.Ui.ToggleHotkeyEnabled = hotkeyEnabled.IsChecked == true;
                hotkeyKey.IsEnabled = hotkeyEnabled.IsChecked == true;
                RebindToggleHotkey();
                RefreshHotkeyStatus();
                QueuePersistCurrentState();
            };
            hotkeyKey.SelectionChanged += (_, _) =>
            {
                if (_ctrl != null && hotkeyKey.SelectedItem is string key)
                    _ctrl.Settings.Ui.ToggleHotkeyKey = key;
                RebindToggleHotkey();
                RefreshHotkeyStatus();
                QueuePersistCurrentState();
            };
            RefreshHotkeyStatus();

            var chkAuto = new CheckBox
            {
                Content = Ui("Autostart mit Windows", "Start with Windows"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            ApplyCheckTheme(chkAuto);
            AttachOptionLegend(chkAuto, "Startet Mini Mixer Overlay automatisch mit Windows.");

            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                chkAuto.IsChecked = k?.GetValue("MiniMixerOverlay") != null;
            }
            catch
            {
                // ignore
            }

            chkAuto.Click += (_, _) =>
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                    if (chkAuto.IsChecked == true)
                    {
                        var exePath = GetCurrentExecutablePath();
                        if (!string.IsNullOrWhiteSpace(exePath))
                        {
                            k.SetValue("MiniMixerOverlay", $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        k.DeleteValue("MiniMixerOverlay", false);
                    }
                }
                catch
                {
                    // ignore
                }
            };
            sp.Children.Add(chkAuto);

            var chkTop = new CheckBox
            {
                Content = Ui("Immer im Vordergrund", "Always on top"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _ctrl?.Settings.Window.AlwaysOnTop ?? _mainWin?.Topmost ?? true
            };
            ApplyCheckTheme(chkTop);
            AttachOptionLegend(chkTop, "Haelt Overlay und Einstellungen immer ueber anderen Fenstern.");
            chkTop.Click += (_, _) =>
            {
                var isTop = chkTop.IsChecked == true;
                if (_ctrl != null)
                    _ctrl.Settings.Window.AlwaysOnTop = isTop;
                ApplyUserTopmostPreference();
            };
            sp.Children.Add(chkTop);

            var chkHoverFront = new CheckBox
            {
                Content = Ui("Bei Hover in den Vordergrund", "Bring to front on hover"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = _bringToFrontOnHover
            };
            ApplyCheckTheme(chkHoverFront);
            AttachOptionLegend(chkHoverFront, "Bringt das Overlay beim Hover sofort nach vorne.");
            chkHoverFront.Click += (_, _) =>
            {
                _bringToFrontOnHover = chkHoverFront.IsChecked == true;
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.BringToFrontOnHover = _bringToFrontOnHover;
                }
            };
            sp.Children.Add(chkHoverFront);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Sichtbare Apps:", "Visible apps:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var appsSlider = new Slider
            {
                Name = "VisibleAppsSlider",
                Minimum = 0,
                Maximum = 12,
                Value = _visibleApps,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            var appsLbl = new TextBlock
            {
                Text = FormatVisibleAppsLabel(_visibleApps),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            AttachOptionLegend(appsSlider, "Bestimmt, wie viele App-Karten maximal sichtbar sind. 0 zeigt nur die Icon-Leiste unten.");
            appsSlider.ValueChanged += (_, _) =>
            {
                _visibleApps = (int)appsSlider.Value;
                appsLbl.Text = FormatVisibleAppsLabel(_visibleApps);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.VisibleApps = _visibleApps;
                }

                if (_mainWin != null)
                    ApplyContextualMainWindowHeight();
                QueueSettingsUiPreviewRefresh();
            };
            sp.Children.Add(appsSlider);
            sp.Children.Add(appsLbl);

            var gameHookControls = new List<UIElement>();
            var gameHookAdvancedControls = new List<UIElement>();

            void AddGameHookControl(UIElement control)
            {
                gameHookControls.Add(control);
                sp.Children.Add(control);
            }

            void AddGameHookAdvancedControl(UIElement control)
            {
                gameHookAdvancedControls.Add(control);
                AddGameHookControl(control);
            }

            var gameHookTitleBox = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                Text = _gameHookTargetWindowTitle
            };
            var gameHookTitleLbl = new TextBlock
            {
                Text = Ui("Game-Fenster Titel (Teilstring):", "Game window title (substring):"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            AddGameHookAdvancedControl(gameHookTitleLbl);
            AddGameHookAdvancedControl(gameHookTitleBox);

            var gameHookAssetsBox = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                Text = _gameHookAssetsPath
            };
            var gameHookAssetsLbl = new TextBlock
            {
                Text = Ui("Game-Hook Assets Pfad:", "Game-hook assets path:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            AddGameHookAdvancedControl(gameHookAssetsLbl);
            AddGameHookAdvancedControl(gameHookAssetsBox);

            Button MakeActionButton(string text)
            {
                var button = new Button
                {
                    Content = text,
                    Height = 28,
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand
                };
                ApplyActionButtonTheme(button);
                return button;
            }

            var gameHookActions = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            gameHookActions.ColumnDefinitions.Add(new ColumnDefinition());
            gameHookActions.ColumnDefinitions.Add(new ColumnDefinition());
            var btnHookCheck = MakeActionButton(Ui("Pfad pruefen", "Check path"));
            var btnHookInject = MakeActionButton("Inject");
            btnHookInject.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(btnHookCheck, 0);
            Grid.SetColumn(btnHookInject, 1);
            gameHookActions.Children.Add(btnHookCheck);
            gameHookActions.Children.Add(btnHookInject);
            AddGameHookAdvancedControl(gameHookActions);

            var gameHookStatusLbl = new TextBlock
            {
                Text = _gameHookRuntime?.IsRunning == true
                    ? Ui("Game-Hook Runtime aktiv.", "Game-hook runtime active.")
                    : Ui("Game-Hook bereit.", "Game-hook ready."),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 210)),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            _settingsGameHookStatusTxt = gameHookStatusLbl;
            AddGameHookAdvancedControl(gameHookStatusLbl);

            var chkGameHookAutoRuntime = new CheckBox
            {
                Content = Ui("Runtime automatisch starten", "Start runtime automatically"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _gameHookAutoStartRuntime
            };
            ApplyCheckTheme(chkGameHookAutoRuntime);
            AttachOptionLegend(chkGameHookAutoRuntime, "Startet die Hook-Runtime automatisch, sobald Game-Hook-Modus aktiv ist.");
            AddGameHookAdvancedControl(chkGameHookAutoRuntime);

            var chkGameHookForwardInput = new CheckBox
            {
                Content = Ui("Game-Input ins Overlay leiten", "Forward game input to overlay"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _gameHookForwardInput
            };
            ApplyCheckTheme(chkGameHookForwardInput);
            AttachOptionLegend(chkGameHookForwardInput, "Leitet Eingaben aus dem Hook an das Overlay-Fenster weiter.");
            AddGameHookAdvancedControl(chkGameHookForwardInput);

            var gameHookRuntimeActions = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            gameHookRuntimeActions.ColumnDefinitions.Add(new ColumnDefinition());
            gameHookRuntimeActions.ColumnDefinitions.Add(new ColumnDefinition());
            gameHookRuntimeActions.ColumnDefinitions.Add(new ColumnDefinition());

            var btnRuntimeStart = MakeActionButton(Ui("Runtime Start", "Start runtime"));
            var btnRuntimeStop = MakeActionButton(Ui("Runtime Stop", "Stop runtime"));
            var btnRuntimeIntercept = MakeActionButton(Ui("Input-Abfang: AUS", "Input intercept: OFF"));
            btnRuntimeStop.Margin = new Thickness(6, 0, 6, 0);
            btnRuntimeIntercept.Margin = new Thickness(0);
            Grid.SetColumn(btnRuntimeStart, 0);
            Grid.SetColumn(btnRuntimeStop, 1);
            Grid.SetColumn(btnRuntimeIntercept, 2);
            gameHookRuntimeActions.Children.Add(btnRuntimeStart);
            gameHookRuntimeActions.Children.Add(btnRuntimeStop);
            gameHookRuntimeActions.Children.Add(btnRuntimeIntercept);
            AddGameHookAdvancedControl(gameHookRuntimeActions);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Andocken:", "Docking:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var chkDock = new CheckBox
            {
                Content = Ui("Rand-Andocken (Ein/Aus)", "Edge snap (On/Off)"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _edgeDockEnabled
            };
            ApplyCheckTheme(chkDock);
            AttachOptionLegend(chkDock, "Aktiviert Snap-Andocken am linken oder rechten Bildschirmrand.");
            sp.Children.Add(chkDock);

            void RefreshSettingsInputStyles()
            {
                sliderTemplateStyle = null;
                comboTemplateStyle = null;
                comboItemTemplateStyle = null;
                actionButtonStyle = null;
            }

            void ApplySliderTheme(Slider slider)
            {
                if (sliderTemplateStyle == null)
                {
                    string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
                    var accent = Hex(_activeGlassAccent);
                    var accentSoft = Hex(BlendColor(_activeGlassAccent, Colors.White, 0.18));
                    var track = Hex(BlendColor(Color.FromRgb(32, 48, 71), _activeGlassAccent, 0.24));

                    var xaml = $@"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Style x:Key='GlassSliderFillButtonStyle' TargetType='RepeatButton'>
        <Setter Property='Focusable' Value='False'/>
        <Setter Property='IsTabStop' Value='False'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='RepeatButton'>
                    <Border Background='#D0{accentSoft}' Height='4' CornerRadius='2'/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key='GlassSliderEmptyButtonStyle' TargetType='RepeatButton'>
        <Setter Property='Focusable' Value='False'/>
        <Setter Property='IsTabStop' Value='False'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='RepeatButton'>
                    <Border Background='Transparent' Height='4' CornerRadius='2'/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key='GlassSliderThumbStyle' TargetType='Thumb'>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='Thumb'>
                    <Border Width='12' Height='20' CornerRadius='4'
                            Background='#FF{accent}'
                            BorderBrush='#FFEAF6FF'
                            BorderThickness='1'/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key='GlassSliderStyle' TargetType='Slider'>
        <Setter Property='Foreground' Value='#FF{accent}'/>
        <Setter Property='Background' Value='#9A{track}'/>
        <Setter Property='Height' Value='20'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='Slider'>
                    <Grid Margin='0,2,0,2'>
                        <Border Height='4' CornerRadius='2'
                                Background='{{TemplateBinding Background}}'
                                VerticalAlignment='Center'/>
                        <Track x:Name='PART_Track'
                               Minimum='{{TemplateBinding Minimum}}'
                               Maximum='{{TemplateBinding Maximum}}'
                               Value='{{TemplateBinding Value}}'
                               VerticalAlignment='Center'>
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command='Slider.DecreaseLarge'
                                              Style='{{StaticResource GlassSliderFillButtonStyle}}'/>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style='{{StaticResource GlassSliderThumbStyle}}'/>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command='Slider.IncreaseLarge'
                                              Style='{{StaticResource GlassSliderEmptyButtonStyle}}'/>
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>";

                    try
                    {
                        var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                        sliderTemplateStyle = dict["GlassSliderStyle"] as Style;
                    }
                    catch
                    {
                        sliderTemplateStyle = null;
                    }
                }

                if (sliderTemplateStyle != null)
                {
                    slider.Style = sliderTemplateStyle;
                    return;
                }

                var trackColor = BlendColor(Color.FromRgb(40, 54, 78), _activeGlassAccent, 0.26);
                slider.Foreground = new SolidColorBrush(_activeGlassAccent);
                slider.Background = new SolidColorBrush(Color.FromArgb(156, trackColor.R, trackColor.G, trackColor.B));
                slider.Height = 20;
            }

            void ApplyCheckTheme(CheckBox checkBox)
            {
                checkBox.Foreground = new SolidColorBrush(Color.FromRgb(242, 248, 255));
                checkBox.Opacity = 1.0;
                checkBox.IsEnabledChanged += (_, _) =>
                {
                    checkBox.Opacity = 1.0;
                };
            }

            void ApplyComboTheme(ComboBox combo)
            {
                string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
                var inputBgBase = BlendColor(Color.FromRgb(19, 30, 48), _activeGlassAccent, 0.20);
                var inputBg = new SolidColorBrush(Color.FromArgb(148, inputBgBase.R, inputBgBase.G, inputBgBase.B));
                var inputBorder = new SolidColorBrush(Color.FromArgb(192, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                var inputText = new SolidColorBrush(Color.FromRgb(242, 248, 255));
                var disabledBg = new SolidColorBrush(Color.FromArgb(124, inputBgBase.R, inputBgBase.G, inputBgBase.B));
                var disabledBorder = new SolidColorBrush(Color.FromArgb(136, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                var disabledText = new SolidColorBrush(Color.FromRgb(198, 214, 232));
                var popupBase = BlendColor(Color.FromRgb(11, 18, 30), _activeGlassAccent, 0.11);
                var popupBg = new SolidColorBrush(Color.FromArgb(244, popupBase.R, popupBase.G, popupBase.B));
                var itemBase = BlendColor(Color.FromRgb(14, 24, 39), _activeGlassAccent, 0.10);
                var itemHoverBase = BlendColor(_activeGlassAccent, Colors.White, 0.14);
                var itemSelectedBase = BlendColor(_activeGlassAccent, Colors.White, 0.08);

                if (comboTemplateStyle == null || comboItemTemplateStyle == null)
                {
                    var xaml = $@"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Style x:Key='GlassComboBoxStyle' TargetType='ComboBox'>
        <Setter Property='Foreground' Value='#FFF2F8FF'/>
        <Setter Property='Background' Value='#94{Hex(inputBgBase)}'/>
        <Setter Property='BorderBrush' Value='#C0{Hex(_activeGlassAccent)}'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Padding' Value='10,0,30,0'/>
        <Setter Property='MinHeight' Value='28'/>
        <Setter Property='HorizontalContentAlignment' Value='Left'/>
        <Setter Property='VerticalContentAlignment' Value='Center'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='ComboBox'>
                    <Grid SnapsToDevicePixels='True'>
                        <Border x:Name='RootBorder'
                                Background='{{TemplateBinding Background}}'
                                BorderBrush='{{TemplateBinding BorderBrush}}'
                                BorderThickness='{{TemplateBinding BorderThickness}}'
                                CornerRadius='4'/>
                        <Grid>
                            <!-- Settings combo-box options are strings: render current selection
                                 directly to avoid stale text in the old ContentPresenter. -->
                            <TextBlock x:Name='SelectedText'
                                       Margin='{{TemplateBinding Padding}}'
                                       VerticalAlignment='Center'
                                       Text='{{Binding SelectedItem, RelativeSource={{RelativeSource TemplatedParent}}}}'
                                       TextTrimming='CharacterEllipsis'
                                       IsHitTestVisible='False'/>
                            <Path x:Name='Arrow'
                                  HorizontalAlignment='Right'
                                  Margin='0,0,10,0'
                                  VerticalAlignment='Center'
                                  Fill='{{TemplateBinding Foreground}}'
                                  Data='M 0 0 L 8 0 L 4 6 Z'/>
                            <ToggleButton x:Name='DropDownToggle'
                                          Focusable='False'
                                          Background='Transparent'
                                          BorderThickness='0'
                                          IsChecked='{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}'
                                          ClickMode='Press'>
                                <ToggleButton.Template>
                                    <ControlTemplate TargetType='ToggleButton'>
                                        <Border Background='Transparent'/>
                                    </ControlTemplate>
                                </ToggleButton.Template>
                            </ToggleButton>
                        </Grid>
                        <Popup x:Name='PART_Popup'
                               Placement='Bottom'
                               IsOpen='{{TemplateBinding IsDropDownOpen}}'
                               AllowsTransparency='True'
                               Focusable='False'
                               PopupAnimation='Fade'>
                            <Border x:Name='PopupBorder'
                                    MinWidth='{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}'
                                    Margin='0,4,0,0'
                                    Background='#F2{Hex(popupBase)}'
                                    BorderBrush='{{TemplateBinding BorderBrush}}'
                                    BorderThickness='1'
                                    CornerRadius='6'>
                                <ScrollViewer CanContentScroll='True' MaxHeight='320' Background='Transparent' Padding='1'>
                                    <ItemsPresenter Margin='0'/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property='HasItems' Value='False'>
                            <Setter TargetName='PopupBorder' Property='MinHeight' Value='28'/>
                        </Trigger>
                        <Trigger Property='IsDropDownOpen' Value='True'>
                            <Setter TargetName='RootBorder' Property='BorderBrush' Value='#FF{Hex(_activeGlassAccent)}'/>
                        </Trigger>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='RootBorder' Property='BorderBrush' Value='#EC{Hex(_activeGlassAccent)}'/>
                        </Trigger>
                        <Trigger Property='IsEnabled' Value='False'>
                            <Setter TargetName='RootBorder' Property='Background' Value='#7C{Hex(inputBgBase)}'/>
                            <Setter TargetName='RootBorder' Property='BorderBrush' Value='#88{Hex(_activeGlassAccent)}'/>
                            <Setter Property='Foreground' Value='#FFC6D6E8'/>
                            <Setter TargetName='Arrow' Property='Opacity' Value='0.72'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key='GlassComboBoxItemStyle' TargetType='ComboBoxItem'>
        <Setter Property='Foreground' Value='#FFF2F8FF'/>
        <Setter Property='Background' Value='#CE{Hex(itemBase)}'/>
        <Setter Property='Padding' Value='10,4,10,4'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='BorderBrush' Value='Transparent'/>
        <Setter Property='HorizontalContentAlignment' Value='Left'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='ComboBoxItem'>
                    <Border x:Name='ItemBorder'
                            Margin='2,1,2,1'
                            CornerRadius='4'
                            Background='{{TemplateBinding Background}}'
                            BorderBrush='{{TemplateBinding BorderBrush}}'
                            BorderThickness='{{TemplateBinding BorderThickness}}'>
                        <ContentPresenter Margin='{{TemplateBinding Padding}}'
                                          VerticalAlignment='Center'
                                          HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsHighlighted' Value='True'>
                            <Setter TargetName='ItemBorder' Property='Background' Value='#B8{Hex(itemHoverBase)}'/>
                            <Setter TargetName='ItemBorder' Property='BorderBrush' Value='#D8{Hex(_activeGlassAccent)}'/>
                            <Setter Property='Foreground' Value='#FFF2F8FF'/>
                        </Trigger>
                        <Trigger Property='IsSelected' Value='True'>
                            <Setter TargetName='ItemBorder' Property='Background' Value='#D0{Hex(itemSelectedBase)}'/>
                            <Setter TargetName='ItemBorder' Property='BorderBrush' Value='#FF{Hex(_activeGlassAccent)}'/>
                            <Setter Property='Foreground' Value='#FFF2F8FF'/>
                        </Trigger>
                        <Trigger Property='IsEnabled' Value='False'>
                            <Setter Property='Foreground' Value='#FFC6D6E8'/>
                            <Setter TargetName='ItemBorder' Property='Opacity' Value='0.72'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>";

                    try
                    {
                        var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                        comboTemplateStyle = dict["GlassComboBoxStyle"] as Style;
                        comboItemTemplateStyle = dict["GlassComboBoxItemStyle"] as Style;
                    }
                    catch
                    {
                        comboTemplateStyle = null;
                        comboItemTemplateStyle = null;
                    }
                }

                combo.IsEditable = false;
                combo.Foreground = inputText;
                combo.Background = inputBg;
                combo.BorderBrush = inputBorder;
                combo.BorderThickness = new Thickness(1);
                combo.Padding = new Thickness(8, 3, 8, 3);
                combo.Opacity = 1.0;
                combo.HorizontalAlignment = HorizontalAlignment.Stretch;
                combo.MinWidth = 0;
                if (comboTemplateStyle != null)
                {
                    combo.Style = comboTemplateStyle;
                }
                else
                {
                    var comboStyle = new Style(typeof(ComboBox));
                    comboStyle.Setters.Add(new Setter(Control.ForegroundProperty, inputText));
                    comboStyle.Setters.Add(new Setter(Control.BackgroundProperty, inputBg));
                    comboStyle.Setters.Add(new Setter(Control.BorderBrushProperty, inputBorder));
                    comboStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    comboStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
                    comboStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));

                    var comboDisabled = new Trigger
                    {
                        Property = UIElement.IsEnabledProperty,
                        Value = false
                    };
                    comboDisabled.Setters.Add(new Setter(Control.ForegroundProperty, disabledText));
                    comboDisabled.Setters.Add(new Setter(Control.BackgroundProperty, disabledBg));
                    comboDisabled.Setters.Add(new Setter(Control.BorderBrushProperty, disabledBorder));
                    comboDisabled.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
                    comboStyle.Triggers.Add(comboDisabled);
                    combo.Style = comboStyle;
                }

                if (comboItemTemplateStyle != null)
                {
                    combo.ItemContainerStyle = comboItemTemplateStyle;
                }
                else
                {
                    var fallbackItemStyle = new Style(typeof(ComboBoxItem));
                    fallbackItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, inputText));
                    fallbackItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(206, itemBase.R, itemBase.G, itemBase.B))));
                    fallbackItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 4, 10, 4)));
                    fallbackItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    fallbackItemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
                    combo.ItemContainerStyle = fallbackItemStyle;
                }

                combo.Resources[SystemColors.WindowBrushKey] = popupBg;
                combo.Resources[SystemColors.WindowTextBrushKey] = inputText;
                combo.Resources[SystemColors.ControlBrushKey] = inputBg;
                combo.Resources[SystemColors.ControlLightBrushKey] = inputBg;
                combo.Resources[SystemColors.ControlLightLightBrushKey] = inputBg;
                combo.Resources[SystemColors.WindowFrameBrushKey] = inputBorder;
                combo.Resources[SystemColors.ControlTextBrushKey] = inputText;
                combo.Resources[SystemColors.MenuBrushKey] = popupBg;
                combo.Resources[SystemColors.MenuTextBrushKey] = inputText;
                combo.Resources[SystemColors.GrayTextBrushKey] = disabledText;
                combo.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush(Color.FromArgb(156, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                combo.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.Black;
                combo.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(_activeGlassAccent);
                combo.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
            }

            void ApplyTextBoxTheme(TextBox box)
            {
                var inputBgBase = BlendColor(Color.FromRgb(19, 30, 48), _activeGlassAccent, 0.20);
                box.Foreground = new SolidColorBrush(Color.FromRgb(242, 248, 255));
                box.Background = new SolidColorBrush(Color.FromArgb(148, inputBgBase.R, inputBgBase.G, inputBgBase.B));
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(192, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                box.BorderThickness = new Thickness(1);
                box.Padding = new Thickness(8, 4, 8, 4);
                box.CaretBrush = new SolidColorBrush(Color.FromRgb(242, 248, 255));
            }

            void ApplyActionButtonTheme(Button button)
            {
                var baseColor = BlendColor(Color.FromRgb(14, 28, 46), _activeGlassAccent, 0.24);
                var hoverColor = BlendColor(baseColor, _activeGlassAccent, 0.42);
                var pressColor = BlendColor(baseColor, _activeGlassAccent, 0.56);
                var borderColor = BlendColor(Color.FromRgb(120, 176, 214), _activeGlassAccent, 0.46);
                var disabledColor = BlendColor(Color.FromRgb(72, 88, 106), _activeGlassAccent, 0.22);
                var disabledBorder = BlendColor(Color.FromRgb(104, 128, 150), _activeGlassAccent, 0.20);

                if (actionButtonStyle == null)
                {
                    var style = new Style(typeof(Button));
                    style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(242, 248, 255))));
                    style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(162, baseColor.R, baseColor.G, baseColor.B))));
                    style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(206, borderColor.R, borderColor.G, borderColor.B))));
                    style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
                    style.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));

                    var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                    hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(198, hoverColor.R, hoverColor.G, hoverColor.B))));
                    hover.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(236, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B))));
                    style.Triggers.Add(hover);

                    var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
                    pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(214, pressColor.R, pressColor.G, pressColor.B))));
                    pressed.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(248, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B))));
                    style.Triggers.Add(pressed);

                    var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
                    disabled.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(120, disabledColor.R, disabledColor.G, disabledColor.B))));
                    disabled.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(146, disabledBorder.R, disabledBorder.G, disabledBorder.B))));
                    disabled.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(194, 210, 226))));
                    disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
                    style.Triggers.Add(disabled);

                    actionButtonStyle = style;
                }

                button.Style = actionButtonStyle;
                button.Cursor = Cursors.Hand;
            }

            void AttachOptionLegend(FrameworkElement optionElement, string legendText)
            {
                var tip = new ToolTip
                {
                    Content = legendText,
                    Placement = PlacementMode.MousePoint,
                    Background = new SolidColorBrush(Color.FromArgb(238, 10, 18, 30)),
                    Foreground = new SolidColorBrush(Color.FromRgb(242, 248, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(200, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6, 8, 6)
                };

                optionElement.ToolTip = tip;
                ToolTipService.SetInitialShowDelay(optionElement, 220);
                ToolTipService.SetBetweenShowDelay(optionElement, 80);
                ToolTipService.SetShowDuration(optionElement, 9000);
            }

            void GuardComboInteraction(ComboBox combo)
            {
                combo.DropDownOpened += (_, _) =>
                {
                    _isUserInteracting = true;
                    _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
                };
                combo.DropDownClosed += (_, _) =>
                {
                    _isUserInteracting = false;
                };
            }
            RefreshSettingsInputStyles();


            ApplyTextBoxTheme(gameHookTitleBox);
            ApplyTextBoxTheme(gameHookAssetsBox);
            ApplySliderTheme(appsSlider);

            var isDesktopModeUi = IsDesktopOverlayMode();
            chkDock.IsEnabled = true;
            gameHookTitleBox.IsEnabled = !isDesktopModeUi;
            gameHookAssetsBox.IsEnabled = !isDesktopModeUi;
            btnHookCheck.IsEnabled = !isDesktopModeUi;
            btnHookInject.IsEnabled = !isDesktopModeUi && !_gameHookIpcOnlyTestRun &&
                                          GameHookBridge.NativePreviewEnabled;
            chkGameHookAutoRuntime.IsEnabled = !isDesktopModeUi;
            chkGameHookForwardInput.IsEnabled = !isDesktopModeUi;
            btnRuntimeStart.IsEnabled = !isDesktopModeUi;
            btnRuntimeStop.IsEnabled = !isDesktopModeUi;
            btnRuntimeIntercept.IsEnabled = !isDesktopModeUi;

            var dockSideCombo = new ComboBox
            {
                Name = "DockSideCombo",
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                ItemsSource = new[] { DockSideLeftLabel(), DockSideRightLabel() },
                SelectedIndex = _edgeDockSide == "left" ? 0 : 1
            };
            ApplyComboTheme(dockSideCombo);
            GuardComboInteraction(dockSideCombo);
            AttachOptionLegend(dockSideCombo, "Waehlt die bevorzugte Seite fuer das Rand-Andocken.");
            // Large-dock settings stay editable even while game-hook mode pauses
            // desktop docking. Sphere drag/snap has its own established behavior.
            sp.Children.Add(dockSideCombo);

            var dockPeekSlider = new Slider
            {
                Name = "DockVisibleWidthSlider",
                Minimum = EdgeDockVisibleMin,
                Maximum = EdgeDockVisibleMax,
                Value = _edgeDockVisibleWidth,
                TickFrequency = 2,
                IsSnapToTickEnabled = true,
                IsEnabled = _edgeDockEnabled
            };
            ApplySliderTheme(dockPeekSlider);
            AttachOptionLegend(dockPeekSlider, "Legt fest, wie viel vom Overlay im angedockten Zustand sichtbar bleibt.");
            var dockPeekLbl = new TextBlock
            {
                Text = FormatDockPeekLabel(_edgeDockVisibleWidth),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var dockRevealSlider = new Slider
            {
                Name = "DockRevealZoneSlider",
                Minimum = EdgeDockRevealZoneMin,
                Maximum = EdgeDockRevealZoneMax,
                Value = _edgeDockRevealZoneWidth,
                TickFrequency = 2,
                IsSnapToTickEnabled = true,
                IsEnabled = _edgeDockEnabled
            };
            var dockRevealLbl = new TextBlock
            {
                Text = FormatDockRevealLabel(_edgeDockRevealZoneWidth),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ApplySliderTheme(dockRevealSlider);
            AttachOptionLegend(dockRevealSlider, "Breite der unsichtbaren Hover-Zone am Rand, die das Overlay einblendet.");
            sp.Children.Add(dockPeekSlider);
            sp.Children.Add(dockPeekLbl);
            sp.Children.Add(dockRevealSlider);
            sp.Children.Add(dockRevealLbl);

            void PersistDockSettings()
            {
                if (_ctrl == null)
                {
                    return;
                }

                _ctrl.Settings.Window.IsDocked = _edgeDockEnabled;
                _ctrl.Settings.Window.DockSide = _edgeDockSide;
                _ctrl.Settings.Window.DockVisibleWidth = _edgeDockVisibleWidth;
                _ctrl.Settings.Window.DockRevealZoneWidth = _edgeDockRevealZoneWidth;
                _ctrl.Settings.Window.CornerSnapAnchor = SerializeCornerSnapAnchor(AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide));
            }

            void SyncDockSideComboDisplay()
            {
                var targetIndex = _edgeDockSide == "left" ? 0 : 1;
                var items = new[] { DockSideLeftLabel(), DockSideRightLabel() };

                isSyncingDockSideCombo = true;
                dockSideCombo.ItemsSource = null;
                dockSideCombo.ItemsSource = items;
                dockSideCombo.SelectedIndex = targetIndex;
                dockSideCombo.InvalidateMeasure();
                dockSideCombo.InvalidateVisual();
                dockSideCombo.UpdateLayout();
                isSyncingDockSideCombo = false;
            }

            chkDock.Click += (_, _) =>
            {
                _edgeDockEnabled = chkDock.IsChecked == true;
                dockPeekSlider.IsEnabled = _edgeDockEnabled;
                dockRevealSlider.IsEnabled = _edgeDockEnabled;

                SetEdgeDockEnabled(_edgeDockEnabled);
                PersistDockSettings();
                QueuePersistCurrentState();
            };

            dockSideCombo.SelectionChanged += (_, _) =>
            {
                if (isSyncingDockSideCombo)
                {
                    return;
                }

                _edgeDockSide = dockSideCombo.SelectedIndex <= 0 ? "left" : "right";
                RememberSnapCorner(_rememberedSnapCorner, syncDockSide: false, persist: false);
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    var expandForEdit = _settingsWin != null || _isUserInteracting || _edgeDockExpanded;
                    ApplyEdgeDockPlacement(expand: expandForEdit, force: true);
                }
                PersistDockSettings();
                SyncDockSideComboDisplay();
                UpdateCornerHintWindow();
                PersistCurrentState();
            };

            dockPeekSlider.ValueChanged += (_, _) =>
            {
                _edgeDockVisibleWidth = (int)dockPeekSlider.Value;
                dockPeekLbl.Text = FormatDockPeekLabel(_edgeDockVisibleWidth);
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    var expandForEdit = _settingsWin != null || _isUserInteracting || _edgeDockExpanded;
                    ApplyEdgeDockPlacement(expand: expandForEdit, force: true);
                }
                PersistDockSettings();
            };

            dockRevealSlider.ValueChanged += (_, _) =>
            {
                _edgeDockRevealZoneWidth = (int)dockRevealSlider.Value;
                dockRevealLbl.Text = FormatDockRevealLabel(_edgeDockRevealZoneWidth);
                PersistDockSettings();
            };

            var chkCornerDot = new CheckBox
            {
                Content = Ui("Hook-Punkt anzeigen", "Show hook dot"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _cornerHintShowDot
            };
            ApplyCheckTheme(chkCornerDot);
            AttachOptionLegend(chkCornerDot, "Blendet den kleinen Hook-Punkt ein oder aus, ohne die Hover-Funktion zu deaktivieren.");
            chkCornerDot.Click += (_, _) =>
            {
                _cornerHintShowDot = chkCornerDot.IsChecked == true;
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintShowDot = _cornerHintShowDot;
                }

                UpdateCornerHintWindow();
                QueuePersistCurrentState();
            };
            sp.Children.Add(chkCornerDot);

            // Transparency belongs to the visible desktop sphere, not to the
            // game's optional input-interception or to the main glass panel.
            var cornerOpacityLabel = new TextBlock
            {
                Text = Ui($"Hook-Punkt-Transparenz: {_cornerHintTransparencyPercent}%",
                    $"Hook dot transparency: {_cornerHintTransparencyPercent}%"),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11
            };
            var cornerOpacitySlider = new Slider
            {
                Minimum = 0,
                Maximum = 80,
                Value = _cornerHintTransparencyPercent,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ApplySliderTheme(cornerOpacitySlider);
            AttachOptionLegend(cornerOpacitySlider,
                "0% = bisherige Deckkraft, 80% = stark transparent. Auch die Zahl wird mit ausgeblendet.");
            cornerOpacitySlider.ValueChanged += (_, _) =>
            {
                _cornerHintTransparencyPercent = (int)Math.Round(cornerOpacitySlider.Value);
                cornerOpacityLabel.Text = Ui(
                    $"Hook-Punkt-Transparenz: {_cornerHintTransparencyPercent}%",
                    $"Hook dot transparency: {_cornerHintTransparencyPercent}%");
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintTransparencyPercent = _cornerHintTransparencyPercent;
                }
                ApplyCornerHintTheme();
                QueuePersistCurrentState();
            };
            sp.Children.Add(cornerOpacitySlider);
            sp.Children.Add(cornerOpacityLabel);

            var chkCornerValue = new CheckBox
            {
                Content = Ui("Hook-Zahl anzeigen", "Show hook value"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _cornerHintShowValue
            };
            ApplyCheckTheme(chkCornerValue);
            AttachOptionLegend(chkCornerValue, "Zeigt die aktuelle Lautstaerke-Zahl im Hook-Punkt an oder blendet sie aus.");
            ComboBox? cornerValueColorCombo = null;
            var chkCornerValueCustomColor = new CheckBox
            {
                Content = Ui("Eigene Hook-Zahl-Farbe (Mixer/Hex)", "Custom hook value color (mixer/hex)"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _cornerHintUseCustomValueColor
            };
            ApplyCheckTheme(chkCornerValueCustomColor);
            AttachOptionLegend(chkCornerValueCustomColor, "Nutze eine eigene Farbe fuer die Lautstaerke-Zahl im Hook-Punkt.");

            var cornerValueCustomColorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            cornerValueCustomColorRow.ColumnDefinitions.Add(new ColumnDefinition());
            cornerValueCustomColorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cornerValueCustomHexBox = new TextBox
            {
                Height = 28,
                MinWidth = 120,
                Text = NormalizeHexColor(_cornerHintCustomValueColorHex, ResolveCornerHintValueColor())
            };
            ApplyTextBoxTheme(cornerValueCustomHexBox);
            AttachOptionLegend(cornerValueCustomHexBox, "Hex fuer Hook-Zahl, z.B. #74F2FF.");
            cornerValueCustomColorRow.Children.Add(cornerValueCustomHexBox);

            var cornerValueCustomPreview = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(cornerValueCustomPreview, 1);
            cornerValueCustomColorRow.Children.Add(cornerValueCustomPreview);

            var cornerValueCustomRgbLbl = new TextBlock
            {
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var cornerValueCustomRedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(cornerValueCustomRedSlider);
            AttachOptionLegend(cornerValueCustomRedSlider, "Rot-Anteil fuer die Hook-Zahl.");

            var cornerValueCustomGreenSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(cornerValueCustomGreenSlider);
            AttachOptionLegend(cornerValueCustomGreenSlider, "Gruen-Anteil fuer die Hook-Zahl.");

            var cornerValueCustomBlueSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ApplySliderTheme(cornerValueCustomBlueSlider);
            AttachOptionLegend(cornerValueCustomBlueSlider, "Blau-Anteil fuer die Hook-Zahl.");

            var isSyncingCornerValueCustomInputs = false;
            Color CurrentCornerValueCustomColor()
            {
                if (TryParseHexColor(_cornerHintCustomValueColorHex, out var parsed))
                {
                    return parsed;
                }

                return ResolveCornerHintValueColor();
            }

            void SyncCornerValueCustomInputs()
            {
                var color = CurrentCornerValueCustomColor();
                isSyncingCornerValueCustomInputs = true;
                cornerValueCustomHexBox.Text = ColorToHex(color);
                cornerValueCustomRedSlider.Value = color.R;
                cornerValueCustomGreenSlider.Value = color.G;
                cornerValueCustomBlueSlider.Value = color.B;
                isSyncingCornerValueCustomInputs = false;

                var rim = BlendColor(color, Colors.White, 0.45);
                cornerValueCustomPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                cornerValueCustomPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, rim.R, rim.G, rim.B));
                cornerValueCustomRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
            }

            void SyncCornerValueColorControlState()
            {
                var valueEnabled = _cornerHintShowValue;
                var customEnabled = valueEnabled && _cornerHintUseCustomValueColor;
                if (cornerValueColorCombo != null)
                {
                    cornerValueColorCombo.IsEnabled = valueEnabled && !_cornerHintUseCustomValueColor;
                }
                chkCornerValueCustomColor.IsEnabled = valueEnabled;
                cornerValueCustomHexBox.IsEnabled = customEnabled;
                cornerValueCustomRedSlider.IsEnabled = customEnabled;
                cornerValueCustomGreenSlider.IsEnabled = customEnabled;
                cornerValueCustomBlueSlider.IsEnabled = customEnabled;
            }

            chkCornerValue.Click += (_, _) =>
            {
                _cornerHintShowValue = chkCornerValue.IsChecked == true;
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintShowValue = _cornerHintShowValue;
                }

                SyncCornerValueColorControlState();
                ApplyCornerHintTheme();
                UpdateCornerHintWindow();
                QueuePersistCurrentState();
            };
            sp.Children.Add(chkCornerValue);

            cornerValueColorCombo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 6),
                ItemsSource = new[] { "Auto", "White", "Cyan", "Emerald", "Amber", "Rose" },
                SelectedItem = NormalizeCornerHintValueColor(_cornerHintValueColor),
                IsEnabled = _cornerHintShowValue
            };
            ApplyComboTheme(cornerValueColorCombo);
            GuardComboInteraction(cornerValueColorCombo);
            AttachOptionLegend(cornerValueColorCombo, "Farbe der Lautstaerke-Zahl im Hook-Punkt.");
            cornerValueColorCombo.SelectionChanged += (_, _) =>
            {
                if (cornerValueColorCombo.SelectedItem is not string selected)
                {
                    return;
                }

                _cornerHintValueColor = NormalizeCornerHintValueColor(selected);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintValueColor = _cornerHintValueColor;
                }

                ApplyCornerHintTheme();
                UpdateCornerHintWindow();
                QueuePersistCurrentState();
            };
            sp.Children.Add(cornerValueColorCombo);
            var customNumberColorOptions = new StackPanel();
            customNumberColorOptions.Children.Add(chkCornerValueCustomColor);
            customNumberColorOptions.Children.Add(cornerValueCustomColorRow);
            customNumberColorOptions.Children.Add(cornerValueCustomRgbLbl);
            customNumberColorOptions.Children.Add(cornerValueCustomRedSlider);
            customNumberColorOptions.Children.Add(cornerValueCustomGreenSlider);
            customNumberColorOptions.Children.Add(cornerValueCustomBlueSlider);
            sp.Children.Add(new Expander
            {
                Header = Ui("Zahlenfarbe anpassen", "Customize number color"),
                Name = "NumberColorOptions",
                IsExpanded = _cornerHintUseCustomValueColor,
                Foreground = Brushes.White,
                Content = customNumberColorOptions,
                Margin = new Thickness(0, 0, 0, 5)
            });

            chkCornerValueCustomColor.Click += (_, _) =>
            {
                var currentColor = ResolveCornerHintValueColor();
                _cornerHintUseCustomValueColor = chkCornerValueCustomColor.IsChecked == true;
                if (_cornerHintUseCustomValueColor)
                {
                    _cornerHintCustomValueColorHex = ColorToHex(currentColor);
                }

                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintUseCustomValueColor = _cornerHintUseCustomValueColor;
                    _ctrl.Settings.Ui.CornerHintCustomValueColorHex = NormalizeHexColor(_cornerHintCustomValueColorHex, currentColor);
                }

                SyncCornerValueCustomInputs();
                SyncCornerValueColorControlState();
                ApplyCornerHintTheme();
                UpdateCornerHintWindow();
                QueuePersistCurrentState();
            };

            void ApplyCornerValueCustomHexFromText()
            {
                if (isSyncingCornerValueCustomInputs)
                {
                    return;
                }

                if (!TryParseHexColor(cornerValueCustomHexBox.Text, out var parsed))
                {
                    SyncCornerValueCustomInputs();
                    return;
                }

                _cornerHintCustomValueColorHex = ColorToHex(parsed);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintCustomValueColorHex = _cornerHintCustomValueColorHex;
                }

                if (_cornerHintUseCustomValueColor && _cornerHintShowValue)
                {
                    ApplyCornerHintTheme();
                    UpdateCornerHintWindow();
                }
                SyncCornerValueCustomInputs();
                QueuePersistCurrentState();
            }

            cornerValueCustomHexBox.LostFocus += (_, _) => ApplyCornerValueCustomHexFromText();
            cornerValueCustomHexBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                ApplyCornerValueCustomHexFromText();
                e.Handled = true;
            };

            void UpdateCornerValueCustomColorFromSliders()
            {
                if (isSyncingCornerValueCustomInputs)
                {
                    return;
                }

                var color = Color.FromRgb(
                    (byte)Math.Clamp(Math.Round(cornerValueCustomRedSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(cornerValueCustomGreenSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(cornerValueCustomBlueSlider.Value), 0, 255));

                _cornerHintCustomValueColorHex = ColorToHex(color);
                cornerValueCustomHexBox.Text = _cornerHintCustomValueColorHex;
                cornerValueCustomRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
                cornerValueCustomPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                var rim = BlendColor(color, Colors.White, 0.45);
                cornerValueCustomPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, rim.R, rim.G, rim.B));

                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerHintCustomValueColorHex = _cornerHintCustomValueColorHex;
                }

                if (_cornerHintUseCustomValueColor && _cornerHintShowValue)
                {
                    ApplyCornerHintTheme();
                    UpdateCornerHintWindow();
                }

                QueuePersistCurrentState();
            }

            cornerValueCustomRedSlider.ValueChanged += (_, _) => UpdateCornerValueCustomColorFromSliders();
            cornerValueCustomGreenSlider.ValueChanged += (_, _) => UpdateCornerValueCustomColorFromSliders();
            cornerValueCustomBlueSlider.ValueChanged += (_, _) => UpdateCornerValueCustomColorFromSliders();
            SyncCornerValueCustomInputs();
            SyncCornerValueColorControlState();

            var chkCornerReveal = new CheckBox
            {
                Content = Ui("Ecken-Reveal (fur alle Apps)", "Corner reveal (for all apps)"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = _cornerRevealEnabled,
                IsEnabled = true
            };
            ApplyCheckTheme(chkCornerReveal);
            AttachOptionLegend(chkCornerReveal, "Blendet das Overlay per Hover in der naechsten Bildschirmecke ein.");
            chkCornerReveal.Click += (_, _) =>
            {
                _cornerRevealEnabled = chkCornerReveal.IsChecked == true;
                _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);

                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.CornerRevealEnabled = _cornerRevealEnabled;
                }

                ApplyCornerRevealVisualState(true);
                UpdateCornerHintWindow();
                QueuePersistCurrentState();
            };
            sp.Children.Add(chkCornerReveal);

            void PersistOverlayModeSettings()
            {
                if (_ctrl == null)
                {
                    return;
                }

                _ctrl.Settings.GameHook ??= new MiniMixerOverlay.Core.Interfaces.GameHookSettings();
                _ctrl.Settings.Ui.OverlayMode = _overlayMode;
                _ctrl.Settings.GameHook.AssetsPath = _gameHookAssetsPath;
                _ctrl.Settings.GameHook.TargetWindowTitle = _gameHookTargetWindowTitle;
                _ctrl.Settings.GameHook.ForwardInputToOverlay = _gameHookForwardInput;
                _ctrl.Settings.GameHook.AutoStartRuntime = _gameHookAutoStartRuntime;
            }

            void SyncGameHookRuntimeUi()
            {
                var desktopMode = IsDesktopOverlayMode();
                var runtimeRunning = _gameHookRuntime?.IsRunning == true;

                chkGameHookAutoRuntime.IsEnabled = !desktopMode;
                chkGameHookForwardInput.IsEnabled = !desktopMode;
                btnRuntimeStart.IsEnabled = !desktopMode && !runtimeRunning;
                btnRuntimeStop.IsEnabled = !desktopMode && runtimeRunning;
                btnRuntimeIntercept.IsEnabled = !desktopMode && runtimeRunning;
                btnRuntimeIntercept.Content = _gameHookInputInterceptEnabled
                    ? Ui("Input-Abfang: AN", "Input intercept: ON")
                    : Ui("Input-Abfang: AUS", "Input intercept: OFF");
            }

            void SyncOverlayModeUi()
            {
                var desktopMode = IsDesktopOverlayMode();
                var showGameHookControls = !desktopMode;

                SyncDockSideComboDisplay();



                foreach (var control in gameHookControls)
                {
                    control.Visibility = showGameHookControls ? Visibility.Visible : Visibility.Collapsed;
                }

                foreach (var control in gameHookAdvancedControls)
                {
                    control.Visibility = Visibility.Collapsed;
                }

                chkDock.IsEnabled = true;
                dockPeekSlider.IsEnabled = _edgeDockEnabled;
                dockRevealSlider.IsEnabled = _edgeDockEnabled;
                chkCornerReveal.IsEnabled = true;

                gameHookTitleBox.IsEnabled = !desktopMode;
                gameHookAssetsBox.IsEnabled = !desktopMode;
                btnHookCheck.IsEnabled = !desktopMode;
                btnHookInject.IsEnabled = !desktopMode && !_gameHookIpcOnlyTestRun &&
                                          GameHookBridge.NativePreviewEnabled;
                SyncGameHookRuntimeUi();
            }

            gameHookTitleBox.TextChanged += (_, _) =>
            {
                _gameHookTargetWindowTitle = gameHookTitleBox.Text.Trim();
                PersistOverlayModeSettings();
            };

            gameHookAssetsBox.TextChanged += (_, _) =>
            {
                _gameHookAssetsPath = gameHookAssetsBox.Text.Trim();
                PersistOverlayModeSettings();
            };

            chkGameHookAutoRuntime.Click += (_, _) =>
            {
                _gameHookAutoStartRuntime = chkGameHookAutoRuntime.IsChecked == true;
                PersistOverlayModeSettings();
                SyncGameHookRuntimeState();
                SyncGameHookRuntimeUi();
            };

            chkGameHookForwardInput.Click += (_, _) =>
            {
                _gameHookForwardInput = chkGameHookForwardInput.IsChecked == true;
                if (_gameHookRuntime != null)
                {
                    _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput || _gameHookIpcSphereInputTest;
                    _gameHookRuntime.ForwardNativeMainInput = _gameHookForwardInput;
                }

                PersistOverlayModeSettings();
            };

            btnRuntimeStart.Click += (_, _) =>
            {
                if (StartGameHookRuntime())
                {
                    gameHookStatusLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                    gameHookStatusLbl.Text = Ui("Game-Hook Runtime gestartet.", "Game-hook runtime started.");
                }
                else
                {
                    gameHookStatusLbl.Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 130));
                    gameHookStatusLbl.Text = Ui("Game-Hook Runtime konnte nicht gestartet werden.", "Game-hook runtime could not be started.");
                }

                SyncGameHookRuntimeUi();
            };

            btnRuntimeStop.Click += (_, _) =>
            {
                StopGameHookRuntime();
                gameHookStatusLbl.Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 210));
                gameHookStatusLbl.Text = Ui("Game-Hook Runtime gestoppt.", "Game-hook runtime stopped.");
                SyncGameHookRuntimeUi();
            };

            btnRuntimeIntercept.Click += (_, _) =>
            {
                if (_gameHookRuntime?.IsRunning != true)
                {
                    gameHookStatusLbl.Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 130));
                    gameHookStatusLbl.Text = Ui("Runtime ist nicht aktiv.", "Runtime is not active.");
                    SyncGameHookRuntimeUi();
                    return;
                }

                if (GameHookBridge.NativePreviewEnabled)
                {
                    SetGameHookStatus("Native Game-Input-Intercept wird noch nicht unterstuetzt; Weiterleitung per Freigabe ist separat.");
                    return;
                }
                _gameHookInputInterceptEnabled = !_gameHookInputInterceptEnabled;
                _gameHookRuntime.SendInputIntercept(_gameHookInputInterceptEnabled);
                gameHookStatusLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                gameHookStatusLbl.Text = _gameHookInputInterceptEnabled
                    ? Ui("Input-Abfang aktiv.", "Input intercept enabled.")
                    : Ui("Input-Abfang aus.", "Input intercept disabled.");
                SyncGameHookRuntimeUi();
            };

            btnHookCheck.Click += (_, _) =>
            {
                var resolvedAssets = ResolveGameHookAssetsPath(gameHookAssetsBox.Text);
                var nativeDll = Path.Combine(resolvedAssets, "MiniMixerLivePresent.x64.dll");
                var ok = File.Exists(nativeDll) && !File.Exists(Path.Combine(resolvedAssets, "n_overlay.x64.dll"));
                var suggestions = GameHookBridge.SuggestTopWindowTitles(gameHookTitleBox.Text, 3);
                var suggestionText = suggestions.Count > 0
                    ? Ui(" Fenster: ", " Windows: ") + string.Join(" | ", suggestions)
                    : string.Empty;

                gameHookStatusLbl.Foreground = ok
                    ? new SolidColorBrush(_activeGlassAccent)
                    : new SolidColorBrush(Color.FromRgb(255, 130, 130));
                gameHookStatusLbl.Text = ok
                    ? $"Native D3D12 DLL vorhanden; Preview nur mit --native-hook-preview: {resolvedAssets}.{suggestionText}"
                    : $"Native D3D12 DLL fehlt; Preview nicht verfuegbar: {resolvedAssets}.{suggestionText}";
            };

            btnHookInject.Click += (_, _) =>
            {
                if (_gameHookIpcOnlyTestRun || !GameHookBridge.NativePreviewEnabled)
                {
                    SetGameHookStatus("Native Hook Preview: bitte --native-hook-preview verwenden.");
                    return;
                }
                if (!IsDesktopOverlayMode() && _gameHookRuntime?.IsRunning != true)
                {
                    _ = StartGameHookRuntime();
                }

                var resolvedAssets = ResolveGameHookAssetsPath(gameHookAssetsBox.Text);
                var result = GameHookBridge.InjectByWindowTitle(gameHookTitleBox.Text.Trim(), resolvedAssets);
                if (result.Success) _gameHookInjectedProcessIds.Add(result.ProcessId);
                gameHookStatusLbl.Foreground = result.Success
                    ? new SolidColorBrush(_activeGlassAccent)
                    : new SolidColorBrush(Color.FromRgb(255, 130, 130));
                gameHookStatusLbl.Text = result.Message;
                SyncGameHookRuntimeUi();
            };

            SyncOverlayModeUi();

            var chkAutoAll = new CheckBox
            {
                Content = Ui("Auto-Limit fur alle neuen Apps", "Auto-limit for all new apps"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _autoApplyToAllNewApps
            };
            ApplyCheckTheme(chkAutoAll);
            AttachOptionLegend(chkAutoAll, "Wenn aktiv, erhalten alle neuen Apps das Auto-Limit. Sonst nur neue Spiele.");
            chkAutoAll.Click += (_, _) =>
            {
                _autoApplyToAllNewApps = chkAutoAll.IsChecked == true;
                _guard?.Configure(_autoVolumePercent, _autoApplyToAllNewApps, _autoLimitMaxInstallAgeDays);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.AutoApplyToAllNewApps = _autoApplyToAllNewApps;
                }
            };
            sp.Children.Add(chkAutoAll);

            var autoVolSlider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = _autoVolumePercent,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            ApplySliderTheme(autoVolSlider);
            AttachOptionLegend(autoVolSlider, "Standard-Lautstaerke fuer neue Apps (einmalig beim ersten Erkennen).");
            var autoVolLbl = new TextBlock
            {
                Text = FormatAutoLimitLabel(_autoVolumePercent),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            autoVolSlider.ValueChanged += (_, _) =>
            {
                _autoVolumePercent = (int)autoVolSlider.Value;
                autoVolLbl.Text = FormatAutoLimitLabel(_autoVolumePercent);
                _guard?.Configure(_autoVolumePercent, _autoApplyToAllNewApps, _autoLimitMaxInstallAgeDays);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.AutoVolumePercent = _autoVolumePercent;
                }
            };
            sp.Children.Add(autoVolSlider);
            sp.Children.Add(autoVolLbl);

            var autoAgeSlider = new Slider
            {
                Minimum = 1,
                Maximum = 60,
                Value = _autoLimitMaxInstallAgeDays,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            ApplySliderTheme(autoAgeSlider);
            AttachOptionLegend(autoAgeSlider, "Auto-Limit wird nur auf Apps angewendet, deren Installation juenger als diese Tage ist.");
            var autoAgeLbl = new TextBlock
            {
                Text = FormatAutoLimitAgeLabel(_autoLimitMaxInstallAgeDays),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            autoAgeSlider.ValueChanged += (_, _) =>
            {
                _autoLimitMaxInstallAgeDays = (int)autoAgeSlider.Value;
                autoAgeLbl.Text = FormatAutoLimitAgeLabel(_autoLimitMaxInstallAgeDays);
                _guard?.Configure(_autoVolumePercent, _autoApplyToAllNewApps, _autoLimitMaxInstallAgeDays);
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.AutoLimitMaxInstallAgeDays = _autoLimitMaxInstallAgeDays;
                }
            };
            sp.Children.Add(autoAgeSlider);
            sp.Children.Add(autoAgeLbl);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Glasdesign:", "Glass design:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var chkWindowsAccent = new CheckBox
            {
                Content = Ui("Windows-Akzentfarbe nutzen", "Use Windows accent color"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _useWindowsAccentForGlass
            };
            ApplyCheckTheme(chkWindowsAccent);
            AttachOptionLegend(chkWindowsAccent, "Nutze die aktuelle Windows-Akzentfarbe fuer das komplette Glasdesign.");
            sp.Children.Add(chkWindowsAccent);

            bool CanUseManualGlassPalette() => !_glassUseCustomColor && (!_useWindowsAccentForGlass || _windowsAccentUnavailable);

            var paletteCombo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                ItemsSource = _glassPalette.Keys.ToArray(),
                SelectedItem = NormalizePaletteName(_glassPaletteName),
                IsEnabled = CanUseManualGlassPalette()
            };
            ApplyComboTheme(paletteCombo);
            GuardComboInteraction(paletteCombo);
            AttachOptionLegend(paletteCombo, "Manuelle Akzentfarbe, wenn Windows-Akzent deaktiviert ist.");
            sp.Children.Add(paletteCombo);

            var accentFallbackLbl = new TextBlock
            {
                Text = Ui(
                    "Windows-Akzentfarbe nicht gefunden - Palette wird verwendet.",
                    "Windows accent color not found - using palette color."),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };
            sp.Children.Add(accentFallbackLbl);

            // Only presentation changes: no new persisted profile or competing glass owner.
            string softLook = Ui("Dezent", "Subtle");
            string balancedLook = Ui("Ausgewogen", "Balanced");
            string vividLook = Ui("Kontrastreich", "High contrast");
            string customLook = Ui("Individuell", "Custom");
            var glassLooks = new[]
            {
                (Name: softLook, Strength: 60, Transparency: 80, Border: 1, Smudge: 60),
                (Name: balancedLook, Strength: 74, Transparency: 72, Border: 1, Smudge: 38),
                (Name: vividLook, Strength: 90, Transparency: 55, Border: 2, Smudge: 25)
            };
            string CurrentGlassLook() => glassLooks.FirstOrDefault(look =>
                look.Strength == _glassStrength &&
                look.Transparency == _glassTransparency &&
                look.Border == _glassBorderThickness &&
                look.Smudge == _glassBorderSmudge).Name ?? customLook;
            sp.Children.Add(new TextBlock
            {
                Text = Ui("Glas-Look:", "Glass appearance:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 3)
            });
            var glassLookPreset = new ComboBox
            {
                Name = "GlassLookPreset",
                ItemsSource = glassLooks.Select(look => look.Name).Append(customLook).ToArray(),
                SelectedItem = CurrentGlassLook(),
                Height = 29,
                Margin = new Thickness(0, 0, 0, 5)
            };
            ApplyComboTheme(glassLookPreset);
            GuardComboInteraction(glassLookPreset);
            sp.Children.Add(glassLookPreset);
            var glassAdvancedPanel = new StackPanel();

            var chkCustomGlassColor = new CheckBox
            {
                Content = Ui("Eigene Glasfarbe (Mixer/Hex)", "Custom glass color (mixer/hex)"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _glassUseCustomColor
            };
            ApplyCheckTheme(chkCustomGlassColor);
            AttachOptionLegend(chkCustomGlassColor, "Ueberschreibt Palette/Windows-Farbe mit eigener RGB- oder Hex-Farbe.");
            glassAdvancedPanel.Children.Add(chkCustomGlassColor);

            var customGlassColorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            customGlassColorRow.ColumnDefinitions.Add(new ColumnDefinition());
            customGlassColorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var customGlassHexBox = new TextBox
            {
                Height = 28,
                MinWidth = 120,
                Text = NormalizeHexColor(_glassCustomColorHex, _glassPalette["Cyan"])
            };
            ApplyTextBoxTheme(customGlassHexBox);
            AttachOptionLegend(customGlassHexBox, "Akzeptiert #RRGGBB, #RGB oder 0xRRGGBB.");
            customGlassColorRow.Children.Add(customGlassHexBox);

            var customGlassPreview = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(customGlassPreview, 1);
            customGlassColorRow.Children.Add(customGlassPreview);
            glassAdvancedPanel.Children.Add(customGlassColorRow);

            var customGlassRgbLbl = new TextBlock
            {
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            glassAdvancedPanel.Children.Add(customGlassRgbLbl);

            var customGlassRedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customGlassRedSlider);
            AttachOptionLegend(customGlassRedSlider, "Rot-Anteil fuer die benutzerdefinierte Glasfarbe.");
            glassAdvancedPanel.Children.Add(customGlassRedSlider);

            var customGlassGreenSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customGlassGreenSlider);
            AttachOptionLegend(customGlassGreenSlider, "Gruen-Anteil fuer die benutzerdefinierte Glasfarbe.");
            glassAdvancedPanel.Children.Add(customGlassGreenSlider);

            var customGlassBlueSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ApplySliderTheme(customGlassBlueSlider);
            AttachOptionLegend(customGlassBlueSlider, "Blau-Anteil fuer die benutzerdefinierte Glasfarbe.");
            glassAdvancedPanel.Children.Add(customGlassBlueSlider);

            var chkCustomBorderColor = new CheckBox
            {
                Content = Ui("Eigene Randfarbe nutzen", "Use custom border color"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4),
                IsChecked = _glassBorderUseCustomColor
            };
            ApplyCheckTheme(chkCustomBorderColor);
            AttachOptionLegend(chkCustomBorderColor, "Nutzt einen separaten Farbton nur fuer den Fensterrand.");
            glassAdvancedPanel.Children.Add(chkCustomBorderColor);

            var customBorderColorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            customBorderColorRow.ColumnDefinitions.Add(new ColumnDefinition());
            customBorderColorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var customBorderHexBox = new TextBox
            {
                Height = 28,
                MinWidth = 120,
                Text = NormalizeHexColor(_glassBorderColorHex, BlendColor(Color.FromRgb(188, 210, 230), _activeGlassAccent, 0.60))
            };
            ApplyTextBoxTheme(customBorderHexBox);
            AttachOptionLegend(customBorderHexBox, "Rand-Farbe als Hex-Code, z.B. #6FD8FF.");
            customBorderColorRow.Children.Add(customBorderHexBox);

            var customBorderPreview = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(customBorderPreview, 1);
            customBorderColorRow.Children.Add(customBorderPreview);
            glassAdvancedPanel.Children.Add(customBorderColorRow);

            var customBorderRgbLbl = new TextBlock
            {
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            glassAdvancedPanel.Children.Add(customBorderRgbLbl);

            var customBorderRedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customBorderRedSlider);
            AttachOptionLegend(customBorderRedSlider, "Rot-Anteil fuer die benutzerdefinierte Randfarbe.");
            glassAdvancedPanel.Children.Add(customBorderRedSlider);

            var customBorderGreenSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customBorderGreenSlider);
            AttachOptionLegend(customBorderGreenSlider, "Gruen-Anteil fuer die benutzerdefinierte Randfarbe.");
            glassAdvancedPanel.Children.Add(customBorderGreenSlider);

            var customBorderBlueSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ApplySliderTheme(customBorderBlueSlider);
            AttachOptionLegend(customBorderBlueSlider, "Blau-Anteil fuer die benutzerdefinierte Randfarbe.");
            glassAdvancedPanel.Children.Add(customBorderBlueSlider);

            var strengthLbl = new TextBlock
            {
                Text = FormatGlassStrengthLabel(_glassStrength),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11
            };
            var strengthSlider = new Slider
            {
                Minimum = 20,
                Maximum = 100,
                Value = _glassStrength,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(strengthSlider);
            AttachOptionLegend(strengthSlider, "Steuert den Farb- und Glow-Anteil des Glas-Effekts.");
            glassAdvancedPanel.Children.Add(strengthSlider);
            glassAdvancedPanel.Children.Add(strengthLbl);

            var transparencyLbl = new TextBlock
            {
                Text = FormatGlassTransparencyLabel(_glassTransparency),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11
            };
            var transparencySlider = new Slider
            {
                Minimum = 20,
                Maximum = 100,
                Value = _glassTransparency,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ApplySliderTheme(transparencySlider);
            AttachOptionLegend(transparencySlider, "Steuert die Durchsichtigkeit der Glasflaechen.");
            glassAdvancedPanel.Children.Add(transparencySlider);
            glassAdvancedPanel.Children.Add(transparencyLbl);

            var borderThicknessLbl = new TextBlock
            {
                Text = FormatGlassBorderThicknessLabel(_glassBorderThickness),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11
            };
            var borderThicknessSlider = new Slider
            {
                Minimum = GlassBorderThicknessMin,
                Maximum = GlassBorderThicknessMax,
                Value = _glassBorderThickness,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            ApplySliderTheme(borderThicknessSlider);
            AttachOptionLegend(borderThicknessSlider, "Staerke des Glas-Rands.");
            glassAdvancedPanel.Children.Add(borderThicknessSlider);
            glassAdvancedPanel.Children.Add(borderThicknessLbl);

            var borderSmudgeLbl = new TextBlock
            {
                Text = FormatGlassBorderSmudgeLabel(_glassBorderSmudge),
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var borderSmudgeSlider = new Slider
            {
                Minimum = GlassBorderSmudgeMin,
                Maximum = GlassBorderSmudgeMax,
                Value = _glassBorderSmudge,
                TickFrequency = 1,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 2)
            };
            ApplySliderTheme(borderSmudgeSlider);
            AttachOptionLegend(borderSmudgeSlider, "Verwischt/softet den Rand wie ein weicher Pinsel.");
            glassAdvancedPanel.Children.Add(borderSmudgeSlider);
            glassAdvancedPanel.Children.Add(borderSmudgeLbl);

            var advancedGlassOptions = new Expander
            {
                Header = Ui("Design-Feineinstellungen", "Advanced design controls"),
                Name = "AdvancedGlassOptions",
                IsExpanded = _glassUseCustomColor || _glassBorderUseCustomColor ||
                             CurrentGlassLook() == customLook,
                Foreground = Brushes.White,
                Content = glassAdvancedPanel,
                Margin = new Thickness(0, 4, 0, 9)
            };
            sp.Children.Add(advancedGlassOptions);

            var isSyncingCustomGlassInputs = false;
            var isSyncingCustomBorderInputs = false;

            Color CurrentCustomGlassColor()
            {
                if (TryParseHexColor(_glassCustomColorHex, out var parsed))
                {
                    return parsed;
                }

                if (_glassPalette.TryGetValue(NormalizePaletteName(_glassPaletteName), out var paletteColor))
                {
                    return paletteColor;
                }

                return _glassPalette["Cyan"];
            }

            Color CurrentCustomBorderColor()
            {
                if (TryParseHexColor(_glassBorderColorHex, out var parsed))
                {
                    return parsed;
                }

                return BlendColor(Color.FromRgb(188, 210, 230), _activeGlassAccent, 0.60);
            }

            void SyncCustomGlassInputs()
            {
                var color = CurrentCustomGlassColor();
                isSyncingCustomGlassInputs = true;
                customGlassHexBox.Text = ColorToHex(color);
                customGlassRedSlider.Value = color.R;
                customGlassGreenSlider.Value = color.G;
                customGlassBlueSlider.Value = color.B;
                isSyncingCustomGlassInputs = false;

                customGlassPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                customGlassPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, BlendColor(color, Colors.White, 0.45).R, BlendColor(color, Colors.White, 0.45).G, BlendColor(color, Colors.White, 0.45).B));
                customGlassRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
                var enabled = _glassUseCustomColor;
                customGlassHexBox.IsEnabled = enabled;
                customGlassRedSlider.IsEnabled = enabled;
                customGlassGreenSlider.IsEnabled = enabled;
                customGlassBlueSlider.IsEnabled = enabled;
            }

            void SyncCustomBorderInputs()
            {
                var color = CurrentCustomBorderColor();
                isSyncingCustomBorderInputs = true;
                customBorderHexBox.Text = ColorToHex(color);
                customBorderRedSlider.Value = color.R;
                customBorderGreenSlider.Value = color.G;
                customBorderBlueSlider.Value = color.B;
                isSyncingCustomBorderInputs = false;

                customBorderPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                customBorderPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, BlendColor(color, Colors.White, 0.45).R, BlendColor(color, Colors.White, 0.45).G, BlendColor(color, Colors.White, 0.45).B));
                customBorderRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
                var enabled = _glassBorderUseCustomColor;
                customBorderHexBox.IsEnabled = enabled;
                customBorderRedSlider.IsEnabled = enabled;
                customBorderGreenSlider.IsEnabled = enabled;
                customBorderBlueSlider.IsEnabled = enabled;
            }

            void SyncGlassLabels()
            {
                RefreshSettingsInputStyles();
                strengthLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                transparencyLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                borderThicknessLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                borderSmudgeLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                customGlassRgbLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                customBorderRgbLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                cornerValueCustomRgbLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                dockPeekLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                dockRevealLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                autoVolLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                autoAgeLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                appsLbl.Foreground = new SolidColorBrush(_activeGlassAccent);

                ApplyComboTheme(dockSideCombo);
                ApplyComboTheme(paletteCombo);
                paletteCombo.IsEnabled = CanUseManualGlassPalette();
                accentFallbackLbl.Foreground = new SolidColorBrush(_activeGlassAccent);
                accentFallbackLbl.Visibility = (!_glassUseCustomColor && _useWindowsAccentForGlass && _windowsAccentUnavailable)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (cornerValueColorCombo != null)
                {
                    ApplyComboTheme(cornerValueColorCombo);
                }
                ApplyTextBoxTheme(customGlassHexBox);
                ApplyTextBoxTheme(customBorderHexBox);
                ApplyTextBoxTheme(cornerValueCustomHexBox);
                ApplyTextBoxTheme(gameHookTitleBox);
                ApplyTextBoxTheme(gameHookAssetsBox);
                ApplySliderTheme(appsSlider);
                ApplySliderTheme(dockPeekSlider);
                ApplySliderTheme(dockRevealSlider);
                ApplySliderTheme(autoVolSlider);
                ApplySliderTheme(autoAgeSlider);
                ApplySliderTheme(customGlassRedSlider);
                ApplySliderTheme(customGlassGreenSlider);
                ApplySliderTheme(customGlassBlueSlider);
                ApplySliderTheme(customBorderRedSlider);
                ApplySliderTheme(customBorderGreenSlider);
                ApplySliderTheme(customBorderBlueSlider);
                ApplySliderTheme(cornerValueCustomRedSlider);
                ApplySliderTheme(cornerValueCustomGreenSlider);
                ApplySliderTheme(cornerValueCustomBlueSlider);
                ApplySliderTheme(strengthSlider);
                ApplySliderTheme(transparencySlider);
                ApplySliderTheme(borderThicknessSlider);
                ApplySliderTheme(borderSmudgeSlider);
                if (refreshSliderRef != null)
                {
                    ApplySliderTheme(refreshSliderRef);
                }
                if (widthSliderRef != null)
                {
                    ApplySliderTheme(widthSliderRef);
                }
                if (refreshLblRef != null)
                {
                    refreshLblRef.Foreground = new SolidColorBrush(_activeGlassAccent);
                }
                if (widthLblRef != null)
                {
                    widthLblRef.Foreground = new SolidColorBrush(_activeGlassAccent);
                }
                ApplyActionButtonTheme(btnLanguage);
                ApplyActionButtonTheme(btnHookCheck);
                ApplyActionButtonTheme(btnHookInject);
                ApplyActionButtonTheme(btnRuntimeStart);
                ApplyActionButtonTheme(btnRuntimeStop);
                ApplyActionButtonTheme(btnRuntimeIntercept);
                if (btnCenterRef != null) ApplyActionButtonTheme(btnCenterRef);
                if (btnTopRef != null) ApplyActionButtonTheme(btnTopRef);
                if (btnLeftRef != null) ApplyActionButtonTheme(btnLeftRef);
                if (btnRightRef != null) ApplyActionButtonTheme(btnRightRef);
                strengthLbl.Text = FormatGlassStrengthLabel(_glassStrength);
                transparencyLbl.Text = FormatGlassTransparencyLabel(_glassTransparency);
                borderThicknessLbl.Text = FormatGlassBorderThicknessLabel(_glassBorderThickness);
                borderSmudgeLbl.Text = FormatGlassBorderSmudgeLabel(_glassBorderSmudge);
                dockPeekLbl.Text = FormatDockPeekLabel(_edgeDockVisibleWidth);
                dockRevealLbl.Text = FormatDockRevealLabel(_edgeDockRevealZoneWidth);
                autoVolLbl.Text = FormatAutoLimitLabel(_autoVolumePercent);
                autoAgeLbl.Text = FormatAutoLimitAgeLabel(_autoLimitMaxInstallAgeDays);
                SyncCustomGlassInputs();
                SyncCustomBorderInputs();
                SyncCornerValueCustomInputs();
                SyncCornerValueColorControlState();
            }

            void PersistGlassSettings()
            {
                if (_ctrl == null)
                {
                    return;
                }

                _ctrl.Settings.Ui.UseWindowsAccentForGlass = _useWindowsAccentForGlass;
                _ctrl.Settings.Ui.GlassPalette = _glassPaletteName;
                _ctrl.Settings.Ui.GlassUseCustomColor = _glassUseCustomColor;
                _ctrl.Settings.Ui.GlassCustomColorHex = NormalizeHexColor(_glassCustomColorHex, _glassPalette["Cyan"]);
                _ctrl.Settings.Ui.GlassStrength = _glassStrength;
                _ctrl.Settings.Ui.GlassTransparency = _glassTransparency;
                _ctrl.Settings.Ui.GlassBorderUseCustomColor = _glassBorderUseCustomColor;
                _ctrl.Settings.Ui.GlassBorderColorHex = NormalizeHexColor(_glassBorderColorHex, BlendColor(Color.FromRgb(188, 210, 230), _activeGlassAccent, 0.60));
                _ctrl.Settings.Ui.GlassBorderThickness = (int)Math.Clamp(_glassBorderThickness, GlassBorderThicknessMin, GlassBorderThicknessMax);
                _ctrl.Settings.Ui.GlassBorderSmudge = (int)Math.Clamp(_glassBorderSmudge, GlassBorderSmudgeMin, GlassBorderSmudgeMax);
            }

            void RefreshGlassLookText()
            {
                // Refresh the existing ComboBox label from its selected item.
                // No separate preference or duplicated persisted glass-look value.
                if (glassLookPreset.Template?.FindName("SelectedText", glassLookPreset) is TextBlock visible)
                    visible.Text = glassLookPreset.SelectedItem as string ?? string.Empty;
            }

            bool isApplyingGlassPreset = false;
            glassLookPreset.SelectionChanged += (_, _) =>
            {
                if (isApplyingGlassPreset || glassLookPreset.SelectedItem is not string choice) return;
                if (choice == customLook)
                {
                    advancedGlassOptions.IsExpanded = true;
                    RefreshGlassLookText();
                    return;
                }

                var values = glassLooks.FirstOrDefault(look => look.Name == choice);
                if (values.Name == null) return;
                isApplyingGlassPreset = true;
                try
                {
                    strengthSlider.Value = values.Strength;
                    transparencySlider.Value = values.Transparency;
                    borderThicknessSlider.Value = values.Border;
                    borderSmudgeSlider.Value = values.Smudge;
                    PersistGlassSettings();
                    ApplyGlassTheme();
                }
                finally { isApplyingGlassPreset = false; }
                RefreshGlassLookText();
            };

            var glassPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(34)
            };

            void CommitGlassPreview()
            {
                PersistGlassSettings();
                ApplyGlassTheme();
                if (!isApplyingGlassPreset)
                {
                    glassLookPreset.SelectedItem = CurrentGlassLook();
                    RefreshGlassLookText();
                }
            }

            void QueueGlassPreview()
            {
                glassPreviewTimer.Stop();
                glassPreviewTimer.Start();
            }

            glassPreviewTimer.Tick += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
            };

            chkWindowsAccent.Click += (_, _) =>
            {
                _useWindowsAccentForGlass = chkWindowsAccent.IsChecked == true;
                PersistGlassSettings();
                ApplyGlassTheme();
                SyncGlassLabels();
            };

            chkCustomGlassColor.Click += (_, _) =>
            {
                _glassUseCustomColor = chkCustomGlassColor.IsChecked == true;
                if (_glassUseCustomColor)
                {
                    _glassCustomColorHex = ColorToHex(_activeGlassAccent);
                }

                PersistGlassSettings();
                ApplyGlassTheme();
                SyncGlassLabels();
            };

            paletteCombo.SelectionChanged += (_, _) =>
            {
                if (paletteCombo.SelectedItem is string palette)
                {
                    _glassPaletteName = NormalizePaletteName(palette);
                    PersistGlassSettings();
                    ApplyGlassTheme();
                    SyncGlassLabels();
                }
            };

            void ApplyCustomGlassHexFromText()
            {
                if (isSyncingCustomGlassInputs)
                {
                    return;
                }

                if (!TryParseHexColor(customGlassHexBox.Text, out var parsed))
                {
                    SyncCustomGlassInputs();
                    return;
                }

                _glassCustomColorHex = ColorToHex(parsed);
                PersistGlassSettings();
                ApplyGlassTheme();
                SyncGlassLabels();
            }

            customGlassHexBox.LostFocus += (_, _) => ApplyCustomGlassHexFromText();
            customGlassHexBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                ApplyCustomGlassHexFromText();
                e.Handled = true;
            };

            void UpdateCustomGlassColorFromSliders()
            {
                if (isSyncingCustomGlassInputs)
                {
                    return;
                }

                var color = Color.FromRgb(
                    (byte)Math.Clamp(Math.Round(customGlassRedSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(customGlassGreenSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(customGlassBlueSlider.Value), 0, 255));

                _glassCustomColorHex = ColorToHex(color);
                customGlassHexBox.Text = _glassCustomColorHex;
                customGlassRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
                customGlassPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                var rim = BlendColor(color, Colors.White, 0.45);
                customGlassPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, rim.R, rim.G, rim.B));

                if (_glassUseCustomColor)
                {
                    QueueGlassPreview();
                }
                else
                {
                    PersistGlassSettings();
                }
            }

            customGlassRedSlider.ValueChanged += (_, _) => UpdateCustomGlassColorFromSliders();
            customGlassGreenSlider.ValueChanged += (_, _) => UpdateCustomGlassColorFromSliders();
            customGlassBlueSlider.ValueChanged += (_, _) => UpdateCustomGlassColorFromSliders();

            chkCustomBorderColor.Click += (_, _) =>
            {
                _glassBorderUseCustomColor = chkCustomBorderColor.IsChecked == true;
                if (_glassBorderUseCustomColor)
                {
                    _glassBorderColorHex = ColorToHex(ResolveGlassBorderColor(_activeGlassAccent));
                }

                PersistGlassSettings();
                ApplyGlassTheme();
                SyncGlassLabels();
            };

            void ApplyCustomBorderHexFromText()
            {
                if (isSyncingCustomBorderInputs)
                {
                    return;
                }

                if (!TryParseHexColor(customBorderHexBox.Text, out var parsed))
                {
                    SyncCustomBorderInputs();
                    return;
                }

                _glassBorderColorHex = ColorToHex(parsed);
                PersistGlassSettings();
                ApplyGlassTheme();
                SyncGlassLabels();
            }

            customBorderHexBox.LostFocus += (_, _) => ApplyCustomBorderHexFromText();
            customBorderHexBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                ApplyCustomBorderHexFromText();
                e.Handled = true;
            };

            void UpdateCustomBorderColorFromSliders()
            {
                if (isSyncingCustomBorderInputs)
                {
                    return;
                }

                var color = Color.FromRgb(
                    (byte)Math.Clamp(Math.Round(customBorderRedSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(customBorderGreenSlider.Value), 0, 255),
                    (byte)Math.Clamp(Math.Round(customBorderBlueSlider.Value), 0, 255));

                _glassBorderColorHex = ColorToHex(color);
                customBorderHexBox.Text = _glassBorderColorHex;
                customBorderRgbLbl.Text = $"RGB: {color.R} / {color.G} / {color.B}";
                customBorderPreview.Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B));
                var rim = BlendColor(color, Colors.White, 0.45);
                customBorderPreview.BorderBrush = new SolidColorBrush(Color.FromArgb(220, rim.R, rim.G, rim.B));

                if (_glassBorderUseCustomColor)
                {
                    QueueGlassPreview();
                }
                else
                {
                    PersistGlassSettings();
                }
            }

            customBorderRedSlider.ValueChanged += (_, _) => UpdateCustomBorderColorFromSliders();
            customBorderGreenSlider.ValueChanged += (_, _) => UpdateCustomBorderColorFromSliders();
            customBorderBlueSlider.ValueChanged += (_, _) => UpdateCustomBorderColorFromSliders();

            strengthSlider.ValueChanged += (_, _) =>
            {
                _glassStrength = (int)Math.Round(strengthSlider.Value);
                strengthLbl.Text = FormatGlassStrengthLabel(_glassStrength);
                QueueGlassPreview();
            };

            transparencySlider.ValueChanged += (_, _) =>
            {
                _glassTransparency = (int)Math.Round(transparencySlider.Value);
                transparencyLbl.Text = FormatGlassTransparencyLabel(_glassTransparency);
                QueueGlassPreview();
            };

            borderThicknessSlider.ValueChanged += (_, _) =>
            {
                _glassBorderThickness = (int)Math.Round(borderThicknessSlider.Value);
                borderThicknessLbl.Text = FormatGlassBorderThicknessLabel(_glassBorderThickness);
                QueueGlassPreview();
            };

            borderSmudgeSlider.ValueChanged += (_, _) =>
            {
                _glassBorderSmudge = (int)Math.Round(borderSmudgeSlider.Value);
                borderSmudgeLbl.Text = FormatGlassBorderSmudgeLabel(_glassBorderSmudge);
                QueueGlassPreview();
            };

            customGlassRedSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            customGlassGreenSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            customGlassBlueSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            customBorderRedSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            customBorderGreenSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            customBorderBlueSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            strengthSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
            };

            transparencySlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
            };

            borderThicknessSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            borderSmudgeSlider.LostMouseCapture += (_, _) =>
            {
                glassPreviewTimer.Stop();
                CommitGlassPreview();
                SyncGlassLabels();
            };

            SyncCustomGlassInputs();
            SyncCustomBorderInputs();

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Refresh-Intervall:", "Refresh interval:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2)
            });

            var refreshSlider = new Slider
            {
                Minimum = 700,
                Maximum = 5000,
                Value = _refreshIntervalMs,
                TickFrequency = 100,
                IsSnapToTickEnabled = true
            };
            refreshSliderRef = refreshSlider;
            var refreshLbl = new TextBlock
            {
                Text = _refreshIntervalMs + " ms",
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            refreshLblRef = refreshLbl;
            ApplySliderTheme(refreshSlider);
            AttachOptionLegend(refreshSlider, "Wie oft Audio-Sessions aktualisiert werden. Niedriger = schneller, hoeher = weniger Last.");
            refreshSlider.ValueChanged += (_, _) =>
            {
                _refreshIntervalMs = (int)refreshSlider.Value;
                refreshLbl.Text = _refreshIntervalMs + " ms";
                if (_ctrl != null)
                {
                    _ctrl.Settings.Ui.RefreshIntervalMs = _refreshIntervalMs;
                }
                ApplyRefreshInterval();
            };
            sp.Children.Add(refreshSlider);
            sp.Children.Add(refreshLbl);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Fensterbreite:", "Window width:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2)
            });

            var widthSlider = new Slider
            {
                Minimum = 280,
                Maximum = 500,
                Value = _mainWin?.Width ?? 380,
                TickFrequency = 10,
                IsSnapToTickEnabled = true
            };
            widthSliderRef = widthSlider;
            var widthLbl = new TextBlock
            {
                Text = ((int)widthSlider.Value) + "px",
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            widthLblRef = widthLbl;
            ApplySliderTheme(widthSlider);
            AttachOptionLegend(widthSlider, "Setzt die Fensterbreite des Overlays.");
            widthSlider.ValueChanged += (_, _) =>
            {
                widthLbl.Text = ((int)widthSlider.Value) + "px";
                if (_mainWin != null)
                {
                    _mainWin.Width = widthSlider.Value;
                }
                RepositionSettingsWindow();
            };
            sp.Children.Add(widthSlider);
            sp.Children.Add(widthLbl);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Position:", "Position:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 4)
            });

            var posGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            posGrid.ColumnDefinitions.Add(new ColumnDefinition());
            posGrid.ColumnDefinitions.Add(new ColumnDefinition());
            posGrid.ColumnDefinitions.Add(new ColumnDefinition());
            posGrid.ColumnDefinitions.Add(new ColumnDefinition());

            Button MakePosBtn(string label)
            {
                var button = new Button
                {
                    Content = label,
                    Height = 30,
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand
                };
                ApplyActionButtonTheme(button);
                return button;
            }

            var btnCenter = MakePosBtn(Ui("Zentrieren", "Center"));
            var btnTop = MakePosBtn(Ui("Oben", "Top"));
            var btnLeft = MakePosBtn(DockSideLeftLabel());
            var btnRight = MakePosBtn(DockSideRightLabel());
            btnCenterRef = btnCenter;
            btnTopRef = btnTop;
            btnLeftRef = btnLeft;
            btnRightRef = btnRight;
            btnRight.Margin = new Thickness(0);
            AttachOptionLegend(btnCenter, "Positioniert das Overlay mittig auf dem aktuellen Bildschirm.");
            AttachOptionLegend(btnTop, "Positioniert das Overlay oben am Bildschirm.");
            AttachOptionLegend(btnLeft, "Positioniert oder dockt das Overlay links.");
            AttachOptionLegend(btnRight, "Positioniert oder dockt das Overlay rechts.");

            Rect GetTargetWorkArea()
            {
                if (TryGetMonitorWorkAreaForMainWindow(out var workArea))
                {
                    return workArea;
                }

                return new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
            }

            void ClampAndStore()
            {
                if (_mainWin == null) return;

                var workArea = GetTargetWorkArea();
                var vLeft = workArea.Left;
                var vTop = workArea.Top;
                var vRight = workArea.Right;
                var vBottom = workArea.Bottom;

                _mainWin.Left = Math.Clamp(_mainWin.Left, vLeft, vRight - _mainWin.Width);
                _mainWin.Top = Math.Clamp(_mainWin.Top, vTop, vBottom - _mainWin.Height);
                _lastWindowLeft = _mainWin.Left;
                _lastWindowTop = _mainWin.Top;
                RepositionSettingsWindow();
            }

            btnCenter.Click += (_, _) =>
            {
                if (_mainWin == null) return;
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    _edgeDockActive = false;
                    _edgeDockExpanded = true;
                    ApplyContextualMainWindowHeight();
                }
                var workArea = GetTargetWorkArea();
                var vLeft = workArea.Left;
                var vTop = workArea.Top;
                var vWidth = workArea.Width;
                var vHeight = workArea.Height;
                _mainWin.Left = vLeft + (vWidth - _mainWin.Width) / 2;
                _mainWin.Top = vTop + (vHeight - _mainWin.Height) / 2;
                ClampAndStore();
            };

            btnTop.Click += (_, _) =>
            {
                if (_mainWin == null) return;
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    _edgeDockActive = false;
                    _edgeDockExpanded = true;
                    ApplyContextualMainWindowHeight();
                }
                _mainWin.Top = GetTargetWorkArea().Top;
                ClampAndStore();
            };

            btnLeft.Click += (_, _) =>
            {
                if (_mainWin == null) return;
                _edgeDockSide = "left";
                RememberSnapCorner(AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide), syncDockSide: false, persist: false);
                dockSideCombo.SelectedIndex = 0;
                PersistDockSettings();
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    var expandForEdit = _settingsWin != null || _isUserInteracting || _edgeDockExpanded;
                    ApplyEdgeDockPlacement(expand: expandForEdit, force: true);
                    return;
                }
                _mainWin.Left = GetTargetWorkArea().Left;
                ClampAndStore();
            };

            btnRight.Click += (_, _) =>
            {
                if (_mainWin == null) return;
                _edgeDockSide = "right";
                RememberSnapCorner(AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide), syncDockSide: false, persist: false);
                dockSideCombo.SelectedIndex = 1;
                PersistDockSettings();
                if (_edgeDockEnabled && _edgeDockActive)
                {
                    var expandForEdit = _settingsWin != null || _isUserInteracting || _edgeDockExpanded;
                    ApplyEdgeDockPlacement(expand: expandForEdit, force: true);
                    return;
                }
                _mainWin.Left = GetTargetWorkArea().Right - _mainWin.Width;
                ClampAndStore();
            };

            Grid.SetColumn(btnCenter, 0);
            Grid.SetColumn(btnTop, 1);
            Grid.SetColumn(btnLeft, 2);
            Grid.SetColumn(btnRight, 3);
            posGrid.Children.Add(btnCenter);
            posGrid.Children.Add(btnTop);
            posGrid.Children.Add(btnLeft);
            posGrid.Children.Add(btnRight);
            sp.Children.Add(posGrid);

            win.Content = outer;
            outer.Child = settingsScroll;

            _settingsWin = win;
            win.Closing += (_, e) =>
            {
                e.Cancel = false;
                _isUserInteracting = false;
                _settingsWin = null;
                _settingsGlassBorder = null;
                _settingsGameHookStatusTxt = null;
                OnSettingsWindowClosed();
            };

            if (_edgeDockEnabled && _edgeDockActive)
            {
                _edgeDockVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(EdgeDockHoldMs);
                ApplyEdgeDockPlacement(expand: true, force: true);
            }

            RepositionSettingsWindow();
            ApplyGlassTheme();
            SyncGlassLabels();
            win.Show();
            LocalizeVisualTree(settingsScroll);
        }

        private static void OnSettingsWindowClosed()
        {
            if (!IsDesktopOverlayMode())
            {
                _cornerRevealVisibleUntilUtc = DateTime.UtcNow;
                ApplyCornerRevealVisualState(false);
                UpdateCornerHintWindow();
            }

            FlushPendingPersistCurrentState();
            PersistCurrentState();
        }
        private static void RepositionSettingsWindow()
        {
            if (_mainWin == null || _settingsWin == null)
            {
                return;
            }

            var left = _mainWin.Left + _mainWin.Width + 4;
            var top = _mainWin.Top;

            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;
            var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

            if (left + _settingsWin.Width > screenRight)
            {
                left = _mainWin.Left - _settingsWin.Width - 4;
            }

            if (left < screenLeft)
            {
                left = screenLeft;
            }

            if (top + _settingsWin.Height > screenBottom)
            {
                top = screenBottom - _settingsWin.Height;
            }

            if (top < screenTop)
            {
                top = screenTop;
            }

            _settingsWin.Left = left;
            _settingsWin.Top = top;
        }

    }
}
