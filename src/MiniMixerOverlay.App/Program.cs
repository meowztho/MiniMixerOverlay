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
    public static class Program
    {
        private static Window? _mainWin;
        private static Window? _settingsWin;
        private static Window? _cornerHintWin;
        private static Border? _mainGlassBorder;
        private static Border? _settingsGlassBorder;
        private static Border? _cornerHintDot;
        private static TextBlock? _cornerHintVolumeText;
        private static StackPanel? _appList;
        private static ScrollViewer? _appListScrollViewer;
        private static RowDefinition? _appListRowDefinition;
        private static TextBlock? _statusTxt;
        private static StackPanel? _activeAppsStrip;
        private static TextBlock? _settingsGameHookStatusTxt;

        private static MixerController? _ctrl;
        private static GuardEngine? _guard;
        private static GameHookOverlayRuntime? _gameHookRuntime;
        private static DispatcherTimer? _refreshTimer;
        private static DispatcherTimer? _sessionEventRefreshTimer;
        private static DispatcherTimer? _cornerRevealTimer;
        private static DispatcherTimer? _gameHookAutoDetectTimer;
        private static DispatcherTimer? _persistDebounceTimer;
        private static Forms.NotifyIcon? _trayIcon;
        private static Forms.ToolStripMenuItem? _trayToggleItem;
        private static Forms.ToolStripMenuItem? _traySettingsItem;
        private static Forms.ToolStripMenuItem? _trayExitItem;
        private static Popup? _activeAppPopup;
        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _singleInstanceShowEvent;
        private static RegisteredWaitHandle? _singleInstanceShowWaitRegistration;
        private static bool _persistPending;
        private static bool _startupInitializationQueued;

        private static bool _isRefreshing;
        private static bool _isUserInteracting;
        private static bool _isSessionRefreshFromUiPoll;
        private static bool _bringToFrontOnHover = true;
        private static bool _cornerRevealEnabled = true;
        private static bool _cornerRevealVisible = true;
        private static bool _edgeDockEnabled;
        private static bool _edgeDockActive;
        private static string _edgeDockSide = "right";
        private static int _edgeDockVisibleWidth = 72;
        private static int _edgeDockRevealZoneWidth = 72;
        private static bool _edgeDockExpanded = true;
        private static bool _autoApplyToAllNewApps = true;
        private static int _autoVolumePercent = 5;
        private static int _autoLimitMaxInstallAgeDays = 7;
        private static string _overlayMode = OverlayModeDesktop;
        private static string _uiLanguage = UiLanguageGerman;
        private static string _gameHookAssetsPath = DefaultGameHookAssetsRelativePath;
        private static string _gameHookTargetWindowTitle = string.Empty;
        private static bool _gameHookForwardInput;
        private static bool _gameHookAutoStartRuntime = true;
        private static bool _gameHookInputInterceptEnabled;
        private static readonly HashSet<uint> _gameHookInjectedProcessIds = new();
        private static readonly Dictionary<uint, DateTime> _gameHookRetryAfterUtcByPid = new();
        private static string _gameHookLastForegroundTitle = string.Empty;
        private static uint _gameHookLastForegroundPid;
        private static bool _gameHookAutoInjectInProgress;
        private static bool _useWindowsAccentForGlass = true;
        private static string _glassPaletteName = "Cyan";
        private static bool _glassUseCustomColor;
        private static string _glassCustomColorHex = "#00D4FF";
        private static bool _glassBorderUseCustomColor;
        private static string _glassBorderColorHex = "#9CD2E8";
        private static int _glassBorderThickness = 1;
        private static int _glassBorderSmudge = 38;
        private static int _glassStrength = 74;
        private static int _glassTransparency = 72;
        private static int _refreshIntervalMs = 1800;
        private static int _visibleApps = 5;
        private static bool _cornerHintShowDot = true;
        private static bool _cornerHintShowValue = true;
        private static string _cornerHintValueColor = "Auto";
        private static bool _cornerHintUseCustomValueColor;
        private static string _cornerHintCustomValueColorHex = "#F5FCFF";
        private static double _lastWindowLeft = 200;
        private static double _lastWindowTop = 200;
        private static DateTime _lastCollapsedDockRefreshUtc = DateTime.MinValue;
        private static DateTime _edgeDockVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(EdgeDockHoldMs);
        private static DateTime _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(1600);
        private static DateTime _cornerHintVolumeCacheUtc = DateTime.MinValue;
        private static string _cornerHintVolumeCacheText = string.Empty;
        private static Point _cornerHintDotCenter = default;
        private static bool _cornerHintDotCenterValid;
        private static ScreenCorner _rememberedSnapCorner = ScreenCorner.TopRight;
        private static DateTime _foregroundPriorityWarmupUntilUtc = DateTime.MinValue;
        private static DateTime _lastForegroundProbeUtc = DateTime.MinValue;
        private static DateTime _startupFastRefreshUntilUtc = DateTime.MinValue;
        private static string _lastNonSystemForegroundExePath = string.Empty;
        private static DateTime _lastNonSystemForegroundSeenUtc = DateTime.MinValue;

        private const double CardRowPitch = 68;
        private const double CardVisualHeight = 62;
        private const int CornerRevealTickMs = 80;
        private const int CornerRevealHotZonePx = 34;
        private const int CornerRevealHoldMs = 1600;
        private const double CornerRevealHiddenOpacity = 0.24;
        private const int EdgeDockRevealZonePx = 5;
        private const int EdgeDockSnapDistancePx = 26;
        private const int EdgeDockHoldMs = 1400;
        private const int EdgeDockCollapsedRefreshMinMs = 1200;
        private const int EdgeDockVisibleMin = 28;
        private const int EdgeDockVisibleMax = 180;
        private const int EdgeDockRevealZoneMin = 5;
        private const int EdgeDockRevealZoneMax = 220;
        private const int GlassBorderThicknessMin = 1;
        private const int GlassBorderThicknessMax = 8;
        private const int GlassBorderSmudgeMin = 0;
        private const int GlassBorderSmudgeMax = 100;
        private const string OverlayModeDesktop = "desktop";
        private const string OverlayModeGameHook = "gamehook";
        private const string UiLanguageGerman = "de";
        private const string UiLanguageEnglish = "en";
        private static readonly string[] UiLanguageOrder =
        {
            UiLanguageGerman,
            UiLanguageEnglish
        };
        private const string DefaultGameHookAssetsRelativePath = @"goverlay-master\goverlay-master\game-overlay\prebuilt";
        private const double CornerHintDiameter = 20;
        private const double CornerHintInset = 7;
        private const int CornerHintVolumeCacheMs = 500;
        private const int ForegroundPriorityWarmupMs = 2200;
        private const int ForegroundProbeThrottleMs = 160;
        private const int StartupFastRefreshDurationMs = 26000;
        private const int StartupFastRefreshIntervalMs = 650;
        private const string SingleInstanceMutexName = @"Local\MiniMixerOverlay.App.Singleton";
        private const string SingleInstanceShowEventName = @"Local\MiniMixerOverlay.App.ShowMain";
        private static readonly double _cardH = CardRowPitch;
        private static readonly Dictionary<string, BitmapImage?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _gameHookBlockedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer",
            "dwm",
            "taskmgr",
            "searchhost",
            "shellexperiencehost",
            "startmenuexperiencehost",
            "applicationframehost",
            "msedge",
            "msedgewebview2"
        };
        private static readonly Dictionary<string, Color> _glassPalette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cyan"] = Color.FromRgb(0, 212, 255),
            ["Ice"] = Color.FromRgb(126, 220, 255),
            ["Sky"] = Color.FromRgb(68, 188, 255),
            ["Azure"] = Color.FromRgb(59, 136, 255),
            ["Indigo"] = Color.FromRgb(96, 112, 255),
            ["Violet"] = Color.FromRgb(142, 114, 255),
            ["Purple"] = Color.FromRgb(172, 108, 255),
            ["Magenta"] = Color.FromRgb(238, 102, 226),
            ["Emerald"] = Color.FromRgb(68, 219, 164),
            ["Mint"] = Color.FromRgb(112, 235, 182),
            ["Lime"] = Color.FromRgb(178, 226, 88),
            ["Olive"] = Color.FromRgb(146, 182, 74),
            ["Forest"] = Color.FromRgb(58, 168, 118),
            ["Teal"] = Color.FromRgb(56, 192, 176),
            ["Amber"] = Color.FromRgb(255, 181, 74),
            ["Gold"] = Color.FromRgb(246, 198, 88),
            ["Orange"] = Color.FromRgb(255, 146, 78),
            ["Coral"] = Color.FromRgb(255, 124, 118),
            ["Red"] = Color.FromRgb(255, 98, 98),
            ["Rose"] = Color.FromRgb(255, 122, 166),
            ["Slate"] = Color.FromRgb(148, 170, 196)
        };
        private static readonly Dictionary<string, Color> _cornerValuePalette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["White"] = Color.FromRgb(245, 252, 255),
            ["Cyan"] = Color.FromRgb(84, 236, 255),
            ["Emerald"] = Color.FromRgb(118, 245, 187),
            ["Amber"] = Color.FromRgb(255, 214, 110),
            ["Rose"] = Color.FromRgb(255, 156, 196)
        };
        private static Color _activeGlassAccent = Color.FromRgb(0, 212, 255);
        private static bool _windowsAccentUnavailable;

        private enum ScreenCorner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [STAThread]
        public static void Main()
        {
            if (!TryAcquireSingleInstance())
            {
                return;
            }

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            InstallGlobalExceptionGuards(app);
            ApplyScrollBarStyle(app);

            _mainWin = new Window
            {
                Title = "Mini Mixer Overlay",
                Width = 380,
                Height = ComputeHeight(_visibleApps),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = _lastWindowLeft,
                Top = _lastWindowTop,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            TryApplyMainWindowIcon();
            InitializeTrayIcon(app);
            InitializeSingleInstanceActivationListener();

            var outer = BuildGlass();
            _mainGlassBorder = outer;
            var grid = BuildMainLayout(app);
            outer.Child = grid;
            _mainWin.Content = outer;
            ApplyGlassTheme();

            _mainWin.LocationChanged += (_, _) =>
            {
                _lastWindowLeft = _mainWin.Left;
                _lastWindowTop = _mainWin.Top;
                RepositionSettingsWindow();
                UpdateCornerHintWindow();
            };
            _mainWin.SourceInitialized += (_, _) =>
            {
                EnableNoActivateWindowBehavior(_mainWin);
                SyncGameHookRuntimeState();
            };
            _mainWin.ContentRendered += (_, _) =>
            {
                SyncGameHookRuntimeState();
                QueueStartupInitialization();
            };

            _mainWin.SizeChanged += (_, _) =>
            {
                RepositionSettingsWindow();
                UpdateCornerHintWindow();
            };
            _mainWin.StateChanged += (_, _) =>
            {
                if (_mainWin.WindowState != WindowState.Minimized)
                {
                    return;
                }

                _mainWin.WindowState = WindowState.Normal;
                PersistCurrentState();
                _mainWin.Hide();
                HideCornerHintWindow();
            };
            _mainWin.MouseEnter += (_, _) =>
            {
                _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
                if (_bringToFrontOnHover)
                {
                    BringMainWindowToFront(activate: IsDesktopOverlayMode());
                }
            };
            _mainWin.PreviewMouseDown += (_, _) => CloseActiveAppPopup();
            _mainWin.Closing += OnWindowClosing;
            _mainWin.Closed += (_, _) =>
            {
                if (_settingsWin != null)
                {
                    _settingsWin.Close();
                    _settingsWin = null;
                }
                CloseActiveAppPopup();
                CloseCornerHintWindow();

                if (!app.Dispatcher.HasShutdownStarted)
                {
                    app.Shutdown();
                }
            };

            app.Run(_mainWin);
        }

        private static void QueueStartupInitialization()
        {
            if (_startupInitializationQueued || _mainWin == null)
            {
                return;
            }

            _startupInitializationQueued = true;
            if (_statusTxt != null)
            {
                _statusTxt.Visibility = Visibility.Visible;
                _statusTxt.Text = Ui("Initialisiere Audio ...", "Initializing audio ...");
            }

            _mainWin.Dispatcher.BeginInvoke(new Action(() =>
            {
                StartAudio();
                StartRefreshLoop();
                StartCornerRevealLoop();
                StartGameHookAutoDetectLoop();
            }), DispatcherPriority.ApplicationIdle);
        }

        private static void InstallGlobalExceptionGuards(Application app)
        {
            app.DispatcherUnhandledException += (_, e) =>
            {
                LogUnhandledException("DispatcherUnhandledException", e.Exception);
                SetGameHookStatus(Ui("Interner UI-Fehler wurde abgefangen. Details stehen im Log.", "Internal UI error captured. See log for details."));
                e.Handled = true;
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogUnhandledException("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                LogUnhandledException("AppDomainUnhandledException", ex, e.ExceptionObject?.ToString());
            };
        }

        private static void LogUnhandledException(string source, Exception? ex, string? details = null)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiniMixerOverlay",
                    "logs");
                Directory.CreateDirectory(logDir);

                var logFile = Path.Combine(logDir, "runtime-errors.log");
                var sb = new StringBuilder();
                sb.AppendLine("-----");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine(source);
                if (ex != null)
                {
                    sb.AppendLine(ex.ToString());
                }
                else if (!string.IsNullOrWhiteSpace(details))
                {
                    sb.AppendLine(details);
                }
                sb.AppendLine();

                File.AppendAllText(logFile, sb.ToString());
            }
            catch
            {
                // keep process alive even if logging fails
            }
        }

        private static Grid BuildMainLayout(Application app)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            _appListRowDefinition = grid.RowDefinitions[1];

            var header = BuildHeader(app);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            _appList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _appList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12, 8, 12, 8)
            };
            scroll.PreviewMouseWheel += (_, e) =>
            {
                var nextOffset = scroll.VerticalOffset - (e.Delta / 3.0);
                scroll.ScrollToVerticalOffset(Math.Max(0, nextOffset));
                e.Handled = true;
            };
            _appListScrollViewer = scroll;
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            _statusTxt = new TextBlock
            {
                Text = string.Empty,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 136)),
                FontSize = 9,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            _activeAppsStrip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stripScroll = new ScrollViewer
            {
                Content = _activeAppsStrip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(8, 0, 8, 0)
            };

            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(_statusTxt, 0);
            Grid.SetColumn(stripScroll, 1);
            statusGrid.Children.Add(_statusTxt);
            statusGrid.Children.Add(stripScroll);

            var statusBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(102, 18, 18, 42)),
                CornerRadius = new CornerRadius(0, 0, 18, 18),
                Child = statusGrid
            };
            Grid.SetRow(statusBar, 2);
            grid.Children.Add(statusBar);
            ApplyAppListVisibilityState();

            return grid;
        }

        private static Border BuildHeader(Application app)
        {
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(102, 18, 18, 42)),
                CornerRadius = new CornerRadius(18, 18, 0, 0),
                Padding = new Thickness(14, 0, 14, 0)
            };

            header.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left || _mainWin == null)
                {
                    return;
                }

                var source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (source is Button)
                    {
                        return;
                    }

                    source = source switch
                    {
                        Visual visual => VisualTreeHelper.GetParent(visual),
                        System.Windows.Media.Media3D.Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                        FrameworkContentElement contentElement => contentElement.Parent,
                        ContentElement content => ContentOperations.GetParent(content),
                        _ => null
                    };
                }

                _isUserInteracting = true;

                try
                {
                    _mainWin.DragMove();
                }
                catch
                {
                    // DragMove wirft, wenn der Drag extern unterbrochen wird.
                }
                finally
                {
                    _isUserInteracting = false;
                    EvaluateDockSnapFromCurrentPosition();
                    _lastWindowLeft = _mainWin.Left;
                    _lastWindowTop = _mainWin.Top;
                    RepositionSettingsWindow();
                }
            };

            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            hg.Children.Add(new TextBlock
            {
                Text = "Mini Mixer",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnSet = MakeHeaderBtn("\u2699", new SolidColorBrush(Color.FromRgb(170, 170, 170)));
            btnSet.Click += (_, _) => ShowSettings();
            btns.Children.Add(btnSet);

            var btnMin = MakeHeaderBtn("\u2500", Brushes.White);
            btnMin.Click += (_, _) =>
            {
                if (_mainWin != null)
                {
                    PersistCurrentState();
                    _mainWin.Hide();
                    HideCornerHintWindow();
                }
            };
            btns.Children.Add(btnMin);

            var btnExit = MakeHeaderBtn("\u2715", new SolidColorBrush(Color.FromRgb(255, 107, 107)));
            btnExit.Click += (_, _) =>
            {
                if (_mainWin != null)
                {
                    _mainWin.Close();
                    return;
                }

                if (!app.Dispatcher.HasShutdownStarted)
                {
                    app.Shutdown();
                }
            };
            btns.Children.Add(btnExit);

            Grid.SetColumn(btns, 1);
            hg.Children.Add(btns);
            header.Child = hg;

            return header;
        }

        private static Button MakeHeaderBtn(string text, Brush fg) => new()
        {
            Content = text,
            Width = 30,
            Height = 28,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = fg,
            FontSize = 13,
            Cursor = Cursors.Hand
        };

        private static void TryApplyMainWindowIcon()
        {
            if (_mainWin == null)
            {
                return;
            }

            try
            {
                string? logoPath = null;
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Logo.png"),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Logo.png")),
                    Path.Combine(Environment.CurrentDirectory, "Logo.png")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        logoPath = candidate;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(logoPath))
                {
                    return;
                }

                var icon = new BitmapImage();
                icon.BeginInit();
                icon.UriSource = new Uri(logoPath, UriKind.Absolute);
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.EndInit();
                icon.Freeze();
                _mainWin.Icon = icon;
            }
            catch
            {
                // ignore icon load issues
            }
        }

        private static void InitializeTrayIcon(Application app)
        {
            if (_trayIcon != null)
            {
                return;
            }

            try
            {
                var trayIconPath = Path.Combine(AppContext.BaseDirectory, "AppIcon.ico");
                if (!File.Exists(trayIconPath))
                {
                    trayIconPath = Path.Combine(AppContext.BaseDirectory, "Logo.png");
                }

                System.Drawing.Icon? icon = null;
                if (File.Exists(trayIconPath) && trayIconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    icon = new System.Drawing.Icon(trayIconPath);
                }
                else if (File.Exists(trayIconPath))
                {
                    var exePath = GetCurrentExecutablePath();
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                }

                if (icon == null)
                {
                    var exePath = GetCurrentExecutablePath();
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                }

                if (icon == null)
                {
                    return;
                }

                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "Mini Mixer Overlay",
                    Visible = true
                };

                var menu = new Forms.ContextMenuStrip();
                _trayToggleItem = new Forms.ToolStripMenuItem(Ui("Anzeigen / Verstecken", "Show / Hide"), null, (_, _) =>
                {
                    _mainWin?.Dispatcher.BeginInvoke(ToggleMainWindowVisibility);
                });
                _traySettingsItem = new Forms.ToolStripMenuItem(Ui("Einstellungen", "Settings"), null, (_, _) =>
                {
                    _mainWin?.Dispatcher.BeginInvoke(() =>
                    {
                        if (_mainWin != null && !_mainWin.IsVisible)
                        {
                            _mainWin.Show();
                        }

                        ShowSettings();
                    });
                });
                _trayExitItem = new Forms.ToolStripMenuItem(Ui("Beenden", "Exit"), null, (_, _) =>
                {
                    _mainWin?.Dispatcher.BeginInvoke(() =>
                    {
                        app.Shutdown();
                    });
                });

                menu.Items.Add(_trayToggleItem);
                menu.Items.Add(_traySettingsItem);
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(_trayExitItem);

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (_, _) => _mainWin?.Dispatcher.BeginInvoke(ToggleMainWindowVisibility);
                ApplyTrayLanguageTexts();
            }
            catch
            {
                // ignore tray init problems
            }
        }

        private static void ToggleMainWindowVisibility()
        {
            if (_mainWin == null)
            {
                return;
            }

            if (_mainWin.IsVisible)
            {
                PersistCurrentState();
                _mainWin.Hide();
                HideCornerHintWindow();
                return;
            }

            _mainWin.Show();
            ApplyOverlayModeRuntimeState();
            RestoreDockPositionIfEnabled(expand: true);
            BringMainWindowToFront();
        }

        private static bool TryAcquireSingleInstance()
        {
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
                if (createdNew)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            SignalExistingInstanceToShow();
            TryBringExistingWindowToFront();
            try
            {
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
                // ignore mutex dispose failures
            }

            _singleInstanceMutex = null;
            return false;
        }

        private static void InitializeSingleInstanceActivationListener()
        {
            try
            {
                _singleInstanceShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceShowEventName);
                _singleInstanceShowWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _singleInstanceShowEvent,
                    static (_, _) =>
                    {
                        if (_mainWin == null)
                        {
                            return;
                        }

                        _mainWin.Dispatcher.BeginInvoke(new Action(ShowMainWindowFromExternalRequest));
                    },
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch
            {
                // ignore single-instance listener errors
            }
        }

        private static void ShowMainWindowFromExternalRequest()
        {
            if (_mainWin == null)
            {
                return;
            }

            if (!_mainWin.IsVisible)
            {
                _mainWin.Show();
                ApplyOverlayModeRuntimeState();
                RestoreDockPositionIfEnabled(expand: true);
            }
            else if (_mainWin.WindowState == WindowState.Minimized)
            {
                _mainWin.WindowState = WindowState.Normal;
            }

            _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
            BringMainWindowToFront();
        }

        private static void SignalExistingInstanceToShow()
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(SingleInstanceShowEventName);
                showEvent.Set();
            }
            catch
            {
                // older instances may not expose the event yet
            }
        }

        private static void TryBringExistingWindowToFront()
        {
            try
            {
                var hwnd = FindWindow(null, "Mini Mixer Overlay");
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                ShowWindow(hwnd, SwShowNoActivate);
                SetWindowPos(
                    hwnd,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            }
            catch
            {
                // ignore fronting errors
            }
        }

        private static void CleanupSingleInstanceResources()
        {
            try
            {
                _singleInstanceShowWaitRegistration?.Unregister(null);
            }
            catch
            {
                // ignore unregister errors
            }

            _singleInstanceShowWaitRegistration = null;

            try
            {
                _singleInstanceShowEvent?.Dispose();
            }
            catch
            {
                // ignore dispose errors
            }

            _singleInstanceShowEvent = null;

            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // ignore release errors
            }

            try
            {
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
                // ignore dispose errors
            }

            _singleInstanceMutex = null;
        }

        private static string GetCurrentExecutablePath()
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                return exePath;
            }

            return Path.Combine(AppContext.BaseDirectory, "MiniMixerOverlay.App.exe");
        }

        private static void DisposeTrayIcon()
        {
            if (_trayIcon == null)
            {
                return;
            }

            try
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            catch
            {
                // ignore tray dispose errors
            }
            finally
            {
                _trayIcon = null;
                _trayToggleItem = null;
                _traySettingsItem = null;
                _trayExitItem = null;
            }
        }

        private static void StartAudio()
        {
            try
            {
                var audio = new MiniMixerOverlay.Infrastructure.Audio.NAudioSessionManager();
                var rules = new MiniMixerOverlay.Infrastructure.Persistence.JsonRuleStore();
                var settings = new MiniMixerOverlay.Infrastructure.Persistence.JsonSettingsStore();
                settings.Load();

                _autoApplyToAllNewApps = settings.Settings.Ui.AutoApplyToAllNewApps;
                _autoVolumePercent = (int)Math.Clamp(settings.Settings.Ui.AutoVolumePercent, 1, 100);
                _autoLimitMaxInstallAgeDays = (int)Math.Clamp(settings.Settings.Ui.AutoLimitMaxInstallAgeDays, 1, 365);
                _overlayMode = NormalizeOverlayMode(settings.Settings.Ui.OverlayMode);
                _uiLanguage = NormalizeUiLanguage(settings.Settings.Ui.Language);
                _cornerHintShowDot = settings.Settings.Ui.CornerHintShowDot;
                _cornerHintShowValue = settings.Settings.Ui.CornerHintShowValue;
                _cornerHintValueColor = NormalizeCornerHintValueColor(settings.Settings.Ui.CornerHintValueColor);
                _cornerHintUseCustomValueColor = settings.Settings.Ui.CornerHintUseCustomValueColor;
                _cornerHintCustomValueColorHex = NormalizeHexColor(settings.Settings.Ui.CornerHintCustomValueColorHex, Color.FromRgb(245, 252, 255));
                _useWindowsAccentForGlass = settings.Settings.Ui.UseWindowsAccentForGlass;
                _glassPaletteName = NormalizePaletteName(settings.Settings.Ui.GlassPalette);
                _glassUseCustomColor = settings.Settings.Ui.GlassUseCustomColor;
                _glassCustomColorHex = NormalizeHexColor(settings.Settings.Ui.GlassCustomColorHex, _glassPalette["Cyan"]);
                _glassStrength = (int)Math.Clamp(settings.Settings.Ui.GlassStrength, 20, 100);
                _glassTransparency = (int)Math.Clamp(settings.Settings.Ui.GlassTransparency, 20, 100);
                _glassBorderUseCustomColor = settings.Settings.Ui.GlassBorderUseCustomColor;
                _glassBorderColorHex = NormalizeHexColor(settings.Settings.Ui.GlassBorderColorHex, BlendColor(Color.FromRgb(188, 210, 230), _glassPalette["Cyan"], 0.60));
                _glassBorderThickness = (int)Math.Clamp(settings.Settings.Ui.GlassBorderThickness, GlassBorderThicknessMin, GlassBorderThicknessMax);
                _glassBorderSmudge = (int)Math.Clamp(settings.Settings.Ui.GlassBorderSmudge, GlassBorderSmudgeMin, GlassBorderSmudgeMax);
                _gameHookAssetsPath = string.IsNullOrWhiteSpace(settings.Settings.GameHook?.AssetsPath)
                    ? DefaultGameHookAssetsRelativePath
                    : settings.Settings.GameHook.AssetsPath;
                _gameHookTargetWindowTitle = settings.Settings.GameHook?.TargetWindowTitle ?? string.Empty;
                _gameHookForwardInput = settings.Settings.GameHook?.ForwardInputToOverlay ?? false;
                _gameHookAutoStartRuntime = settings.Settings.GameHook?.AutoStartRuntime ?? true;
                _guard = new GuardEngine(
                    rules,
                    new SessionClassifier(),
                    _autoVolumePercent,
                    _autoApplyToAllNewApps,
                    _autoLimitMaxInstallAgeDays);

                _ctrl = new MixerController(audio, _guard, new SessionClassifier(), rules, settings);
                _ctrl.Initialize();
                _ctrl.Settings.Ui.ShowOnlyActiveAudio = true;
                _foregroundPriorityWarmupUntilUtc = DateTime.UtcNow.AddMilliseconds(ForegroundPriorityWarmupMs);
                _lastForegroundProbeUtc = DateTime.MinValue;
                _startupFastRefreshUntilUtc = DateTime.UtcNow.AddMilliseconds(StartupFastRefreshDurationMs);
                _ctrl.OnSessionsChanged += () =>
                {
                if (_isSessionRefreshFromUiPoll)
                {
                    return;
                    }

                    if (_mainWin == null)
                    {
                        return;
                    }

                    _mainWin.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isRefreshing)
                        {
                            return;
                        }

                        if (_sessionEventRefreshTimer == null)
                        {
                            _sessionEventRefreshTimer = new DispatcherTimer
                            {
                                Interval = TimeSpan.FromMilliseconds(140)
                            };
                            _sessionEventRefreshTimer.Tick += (_, _) =>
                            {
                                _sessionEventRefreshTimer.Stop();
                                if (_isUserInteracting || _isRefreshing || _settingsWin != null)
                                {
                                    return;
                                }

                                Refresh();
                            };
                        }

                        _sessionEventRefreshTimer.Stop();
                        _sessionEventRefreshTimer.Start();
                    }));
                };

                if (_mainWin != null)
                {
                    var ws = _ctrl.Settings.Window;
                    _visibleApps = (int)Math.Clamp(_ctrl.Settings.Ui.VisibleApps, 0, 12);
                    _refreshIntervalMs = (int)Math.Clamp(_ctrl.Settings.Ui.RefreshIntervalMs, 700, 5000);
                    _bringToFrontOnHover = _ctrl.Settings.Ui.BringToFrontOnHover;
                    _cornerRevealEnabled = _ctrl.Settings.Ui.CornerRevealEnabled;
                    _autoApplyToAllNewApps = _ctrl.Settings.Ui.AutoApplyToAllNewApps;
                    _autoVolumePercent = (int)Math.Clamp(_ctrl.Settings.Ui.AutoVolumePercent, 1, 100);
                    _autoLimitMaxInstallAgeDays = (int)Math.Clamp(_ctrl.Settings.Ui.AutoLimitMaxInstallAgeDays, 1, 365);
                    _overlayMode = NormalizeOverlayMode(_ctrl.Settings.Ui.OverlayMode);
                    _uiLanguage = NormalizeUiLanguage(_ctrl.Settings.Ui.Language);
                    _cornerHintShowDot = _ctrl.Settings.Ui.CornerHintShowDot;
                    _cornerHintShowValue = _ctrl.Settings.Ui.CornerHintShowValue;
                    _cornerHintValueColor = NormalizeCornerHintValueColor(_ctrl.Settings.Ui.CornerHintValueColor);
                    _cornerHintUseCustomValueColor = _ctrl.Settings.Ui.CornerHintUseCustomValueColor;
                    _cornerHintCustomValueColorHex = NormalizeHexColor(_ctrl.Settings.Ui.CornerHintCustomValueColorHex, Color.FromRgb(245, 252, 255));
                    _gameHookAssetsPath = string.IsNullOrWhiteSpace(_ctrl.Settings.GameHook?.AssetsPath)
                        ? DefaultGameHookAssetsRelativePath
                        : _ctrl.Settings.GameHook.AssetsPath;
                    _gameHookTargetWindowTitle = _ctrl.Settings.GameHook?.TargetWindowTitle ?? string.Empty;
                    _gameHookForwardInput = _ctrl.Settings.GameHook?.ForwardInputToOverlay ?? false;
                    _gameHookAutoStartRuntime = _ctrl.Settings.GameHook?.AutoStartRuntime ?? true;
                    _edgeDockEnabled = ws.IsDocked;
                    _edgeDockSide = NormalizeDockSide(ws.DockSide);
                    _rememberedSnapCorner = AlignCornerToDockSide(
                        NormalizeCornerSnapAnchor(ws.CornerSnapAnchor),
                        _edgeDockSide);
                    _edgeDockVisibleWidth = (int)Math.Clamp(ws.DockVisibleWidth, EdgeDockVisibleMin, EdgeDockVisibleMax);
                    _edgeDockRevealZoneWidth = (int)Math.Clamp(ws.DockRevealZoneWidth, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);
                    _useWindowsAccentForGlass = _ctrl.Settings.Ui.UseWindowsAccentForGlass;
                    _glassPaletteName = NormalizePaletteName(_ctrl.Settings.Ui.GlassPalette);
                    _glassUseCustomColor = _ctrl.Settings.Ui.GlassUseCustomColor;
                    _glassCustomColorHex = NormalizeHexColor(_ctrl.Settings.Ui.GlassCustomColorHex, _glassPalette["Cyan"]);
                    _glassStrength = (int)Math.Clamp(_ctrl.Settings.Ui.GlassStrength, 20, 100);
                    _glassTransparency = (int)Math.Clamp(_ctrl.Settings.Ui.GlassTransparency, 20, 100);
                    _glassBorderUseCustomColor = _ctrl.Settings.Ui.GlassBorderUseCustomColor;
                    _glassBorderColorHex = NormalizeHexColor(_ctrl.Settings.Ui.GlassBorderColorHex, BlendColor(Color.FromRgb(188, 210, 230), _glassPalette["Cyan"], 0.60));
                    _glassBorderThickness = (int)Math.Clamp(_ctrl.Settings.Ui.GlassBorderThickness, GlassBorderThicknessMin, GlassBorderThicknessMax);
                    _glassBorderSmudge = (int)Math.Clamp(_ctrl.Settings.Ui.GlassBorderSmudge, GlassBorderSmudgeMin, GlassBorderSmudgeMax);
                    _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
                    _guard?.Configure(_autoVolumePercent, _autoApplyToAllNewApps, _autoLimitMaxInstallAgeDays);

                    if (ws.Width is >= 280 and <= 500)
                    {
                        _mainWin.Width = ws.Width;
                    }

                    _mainWin.Topmost = ws.AlwaysOnTop;
                    RestoreSavedWindowPosition(ws.X, ws.Y);
                    _mainWin.Height = ComputeHeight(GetEffectiveVisibleApps());
                    ApplyAppListVisibilityState();
                    EnsureMainWindowInBounds(allowDockHidden: false);

                    if (_edgeDockEnabled)
                    {
                        if (IsDesktopOverlayMode())
                        {
                            _edgeDockVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(EdgeDockHoldMs);
                            RestoreDockPositionIfEnabled(expand: true);
                        }
                        else
                        {
                            SetEdgeDockEnabled(true);
                            RestoreDockPositionIfEnabled(expand: true);
                        }
                    }

                    ApplyOverlayModeRuntimeState();
                }

                ApplyTrayLanguageTexts();
                if (IsEnglishUi())
                {
                    RebuildMainWindowContent();
                }
                else
                {
                    Refresh();
                }

                if (_statusTxt != null)
                {
                    _statusTxt.Text = Ui("Bereit", "Ready");
                }

                ApplyGlassTheme();
                ApplyCornerRevealVisualState(true);
            }
            catch (Exception ex)
            {
                if (_statusTxt != null)
                {
                    _statusTxt.Text = Ui("Fehler: ", "Error: ") + ex.Message;
                }
            }
        }

        private static void RestoreSavedWindowPosition(double x, double y)
        {
            if (_mainWin == null)
            {
                return;
            }

            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;

            var looksValid = x >= left && x <= right - 40 && y >= top && y <= bottom - 40;
            if (looksValid)
            {
                _mainWin.Left = x;
                _mainWin.Top = y;
            }

            _lastWindowLeft = _mainWin.Left;
            _lastWindowTop = _mainWin.Top;
        }

        private static void StartRefreshLoop()
        {
            var initialIntervalMs = DateTime.UtcNow < _startupFastRefreshUntilUtc
                ? Math.Min(_refreshIntervalMs, StartupFastRefreshIntervalMs)
                : _refreshIntervalMs;
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(initialIntervalMs)
            };

            _refreshTimer.Tick += (_, _) =>
            {
                if (_isUserInteracting || _isRefreshing || _settingsWin != null)
                {
                    return;
                }

                if (DateTime.UtcNow < _startupFastRefreshUntilUtc)
                {
                    var boosted = TimeSpan.FromMilliseconds(Math.Min(_refreshIntervalMs, StartupFastRefreshIntervalMs));
                    if (_refreshTimer.Interval != boosted)
                    {
                        _refreshTimer.Interval = boosted;
                    }
                }
                else
                {
                    var configured = TimeSpan.FromMilliseconds(_refreshIntervalMs);
                    if (_refreshTimer.Interval != configured)
                    {
                        _refreshTimer.Interval = configured;
                    }
                }

                if (_edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded && _settingsWin == null)
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _lastCollapsedDockRefreshUtc).TotalMilliseconds < EdgeDockCollapsedRefreshMinMs)
                    {
                        return;
                    }

                    _lastCollapsedDockRefreshUtc = nowUtc;
                }

                Refresh();
            };

            _refreshTimer.Start();
        }

        private static void StopRefreshLoop()
        {
            if (_refreshTimer == null)
            {
                return;
            }

            _refreshTimer.Stop();
            _refreshTimer = null;
        }

        private static void StartCornerRevealLoop()
        {
            _cornerRevealTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CornerRevealTickMs)
            };

            _cornerRevealTimer.Tick += (_, _) => UpdateCornerRevealState();
            _cornerRevealTimer.Start();
        }

        private static void StopCornerRevealLoop()
        {
            if (_cornerRevealTimer == null)
            {
                return;
            }

            _cornerRevealTimer.Stop();
            _cornerRevealTimer = null;
        }

        private static void StartGameHookAutoDetectLoop()
        {
            _gameHookAutoDetectTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1250)
            };

            _gameHookAutoDetectTimer.Tick -= OnGameHookAutoDetectTick;
            _gameHookAutoDetectTimer.Tick += OnGameHookAutoDetectTick;
            _gameHookAutoDetectTimer.Start();
        }

        private static void StopGameHookAutoDetectLoop()
        {
            if (_gameHookAutoDetectTimer == null)
            {
                return;
            }

            _gameHookAutoDetectTimer.Stop();
            _gameHookAutoDetectTimer.Tick -= OnGameHookAutoDetectTick;
        }

        private static void ResetGameHookAutoDetectState()
        {
            _gameHookInjectedProcessIds.Clear();
            _gameHookRetryAfterUtcByPid.Clear();
            _gameHookLastForegroundPid = 0;
            _gameHookLastForegroundTitle = string.Empty;
            _gameHookAutoInjectInProgress = false;
        }

        private static void OnGameHookAutoDetectTick(object? sender, EventArgs e)
        {
            TryAutoInjectForegroundGame();
        }

        private static bool IsLikelyGameCandidate(GameHookForegroundWindowInfo info)
        {
            if (info.ProcessId == 0 || string.IsNullOrWhiteSpace(info.WindowTitle))
            {
                return false;
            }

            var processName = info.ProcessName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            var currentPid = (uint)Environment.ProcessId;
            if (info.ProcessId == currentPid)
            {
                return false;
            }

            if (info.WindowTitle.IndexOf("Mini Mixer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (_gameHookBlockedProcessNames.Contains(processName))
            {
                return false;
            }

            if (processName.StartsWith("startmenu", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_ctrl == null)
            {
                return true;
            }

            foreach (var entry in _ctrl.AppEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.ExePath))
                {
                    continue;
                }

                var exeName = Path.GetFileNameWithoutExtension(entry.ExePath);
                if (string.Equals(exeName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Rule?.Classification == AppClassification.Game || entry.HasActiveAudio)
                    {
                        return true;
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool AreGameHookAssetsAvailable(string resolvedAssetsPath)
        {
            var helperX86 = Path.Combine(resolvedAssetsPath, "injector_helper.exe");
            var helperX64 = Path.Combine(resolvedAssetsPath, "injector_helper.x64.exe");
            var dllX86 = Path.Combine(resolvedAssetsPath, "n_overlay.dll");
            var dllX64 = Path.Combine(resolvedAssetsPath, "n_overlay.x64.dll");
            return File.Exists(helperX86) && File.Exists(helperX64) && File.Exists(dllX86) && File.Exists(dllX64);
        }

        private static async void TryAutoInjectForegroundGame()
        {
            if (IsDesktopOverlayMode())
            {
                return;
            }

            if (_gameHookRuntime?.IsRunning != true)
            {
                return;
            }

            if (!GameHookBridge.TryGetForegroundWindowInfo(out var foreground))
            {
                return;
            }

            if (!IsLikelyGameCandidate(foreground))
            {
                return;
            }

            _gameHookLastForegroundPid = foreground.ProcessId;
            _gameHookLastForegroundTitle = foreground.WindowTitle;
            _gameHookTargetWindowTitle = foreground.WindowTitle;
            if (_ctrl != null)
            {
                _ctrl.Settings.GameHook ??= new MiniMixerOverlay.Core.Interfaces.GameHookSettings();
                _ctrl.Settings.GameHook.TargetWindowTitle = _gameHookTargetWindowTitle;
            }

            if (_gameHookInjectedProcessIds.Contains(foreground.ProcessId))
            {
                SetGameHookStatus($"{Ui("Game erkannt", "Game detected")}: {foreground.ProcessName} ({foreground.WindowTitle})");
                return;
            }

            if (_gameHookRetryAfterUtcByPid.TryGetValue(foreground.ProcessId, out var retryAfterUtc) &&
                DateTime.UtcNow < retryAfterUtc)
            {
                return;
            }

            if (_gameHookAutoInjectInProgress)
            {
                return;
            }

            var resolvedAssets = ResolveGameHookAssetsPath(_gameHookAssetsPath);
            if (!AreGameHookAssetsAvailable(resolvedAssets))
            {
                SetGameHookStatus($"{Ui("Game-Hook Assets unvollstaendig", "Game-hook assets incomplete")}: {resolvedAssets}");
                return;
            }

            _gameHookAutoInjectInProgress = true;
            SetGameHookStatus($"{Ui("Auto-Hook prueft", "Auto-hook checking")} {foreground.ProcessName} ...");
            GameHookInjectResult result;
            try
            {
                result = await Task.Run(() => GameHookBridge.InjectForegroundWindow(resolvedAssets));
            }
            catch (Exception ex)
            {
                _gameHookRetryAfterUtcByPid[foreground.ProcessId] = DateTime.UtcNow.AddSeconds(8);
                SetGameHookStatus($"{Ui("Auto-Hook Fehler", "Auto-hook error")}: {ex.Message}");
                _gameHookAutoInjectInProgress = false;
                return;
            }
            finally
            {
                _gameHookAutoInjectInProgress = false;
            }

            if (IsDesktopOverlayMode() || _gameHookRuntime?.IsRunning != true)
            {
                return;
            }

            if (result.Success)
            {
                _gameHookInjectedProcessIds.Add(result.ProcessId);
                _gameHookRetryAfterUtcByPid.Remove(result.ProcessId);
                SetGameHookStatus($"{Ui("Auto-Hook aktiv fur", "Auto-hook active for")} {foreground.ProcessName} (PID {result.ProcessId}).");
                return;
            }

            _gameHookRetryAfterUtcByPid[foreground.ProcessId] = DateTime.UtcNow.AddSeconds(8);
            SetGameHookStatus($"{Ui("Auto-Hook wartet", "Auto-hook waiting")}: {result.Message}");
        }

        private static void ApplyRefreshInterval()
        {
            if (_refreshTimer != null)
            {
                var intervalMs = DateTime.UtcNow < _startupFastRefreshUntilUtc
                    ? Math.Min(_refreshIntervalMs, StartupFastRefreshIntervalMs)
                    : _refreshIntervalMs;
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            }
        }

        private static string NormalizeDockSide(string? side)
        {
            return string.Equals(side, "left", StringComparison.OrdinalIgnoreCase) ? "left" : "right";
        }

        private static ScreenCorner NormalizeCornerSnapAnchor(string? anchor)
        {
            return anchor?.Trim().ToLowerInvariant() switch
            {
                "top-left" => ScreenCorner.TopLeft,
                "bottom-left" => ScreenCorner.BottomLeft,
                "bottom-right" => ScreenCorner.BottomRight,
                _ => ScreenCorner.TopRight
            };
        }

        private static string SerializeCornerSnapAnchor(ScreenCorner corner)
        {
            return corner switch
            {
                ScreenCorner.TopLeft => "top-left",
                ScreenCorner.BottomLeft => "bottom-left",
                ScreenCorner.BottomRight => "bottom-right",
                _ => "top-right"
            };
        }

        private static bool IsLeftCorner(ScreenCorner corner)
        {
            return corner == ScreenCorner.TopLeft || corner == ScreenCorner.BottomLeft;
        }

        private static bool IsTopCorner(ScreenCorner corner)
        {
            return corner == ScreenCorner.TopLeft || corner == ScreenCorner.TopRight;
        }

        private static ScreenCorner AlignCornerToDockSide(ScreenCorner corner, string side)
        {
            var normalizedSide = NormalizeDockSide(side);
            var preferTop = IsTopCorner(corner);
            return normalizedSide == "left"
                ? (preferTop ? ScreenCorner.TopLeft : ScreenCorner.BottomLeft)
                : (preferTop ? ScreenCorner.TopRight : ScreenCorner.BottomRight);
        }

        private static ScreenCorner ResolveCornerForSideFromWindowPosition(string side, Rect workArea)
        {
            var normalizedSide = NormalizeDockSide(side);
            var upperHalf = true;
            if (_mainWin != null)
            {
                var windowCenterY = _mainWin.Top + (_mainWin.Height / 2.0);
                var workCenterY = workArea.Top + (workArea.Height / 2.0);
                upperHalf = windowCenterY <= workCenterY;
            }

            return normalizedSide == "left"
                ? (upperHalf ? ScreenCorner.TopLeft : ScreenCorner.BottomLeft)
                : (upperHalf ? ScreenCorner.TopRight : ScreenCorner.BottomRight);
        }

        private static void RememberSnapCorner(ScreenCorner corner, bool syncDockSide, bool persist)
        {
            var nextCorner = corner;
            var dockSideBefore = _edgeDockSide;
            if (syncDockSide)
            {
                _edgeDockSide = IsLeftCorner(corner) ? "left" : "right";
            }
            else
            {
                nextCorner = AlignCornerToDockSide(corner, _edgeDockSide);
            }

            var changed = _rememberedSnapCorner != nextCorner || dockSideBefore != _edgeDockSide;
            _rememberedSnapCorner = nextCorner;

            if (changed && persist)
            {
                PersistCurrentState();
            }
        }

        private static string NormalizeOverlayMode(string? mode)
        {
            return string.Equals(mode, OverlayModeGameHook, StringComparison.OrdinalIgnoreCase)
                ? OverlayModeGameHook
                : OverlayModeDesktop;
        }

        private static bool IsDesktopOverlayMode()
        {
            return string.Equals(_overlayMode, OverlayModeDesktop, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUiLanguage(string? language)
        {
            return string.Equals(language, UiLanguageEnglish, StringComparison.OrdinalIgnoreCase)
                ? UiLanguageEnglish
                : UiLanguageGerman;
        }

        private static string NormalizeCornerHintValueColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Auto";
            }

            var normalized = value.Trim();
            if (string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return "Auto";
            }

            foreach (var paletteName in _cornerValuePalette.Keys)
            {
                if (string.Equals(paletteName, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return paletteName;
                }
            }

            return "Auto";
        }

        private static bool IsEnglishUi()
        {
            return string.Equals(_uiLanguage, UiLanguageEnglish, StringComparison.OrdinalIgnoreCase);
        }

        private static string Ui(string german, string english)
        {
            return IsEnglishUi() ? english : german;
        }

        private static string GetUiLanguageName(string languageCode)
        {
            if (string.Equals(languageCode, UiLanguageEnglish, StringComparison.OrdinalIgnoreCase))
            {
                return Ui("Englisch", "English");
            }

            return Ui("Deutsch", "German");
        }

        private static string GetNextUiLanguageCode(string currentLanguage)
        {
            var normalizedCurrent = NormalizeUiLanguage(currentLanguage);
            var currentIndex = Array.FindIndex(
                UiLanguageOrder,
                lang => string.Equals(lang, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                return UiLanguageOrder[0];
            }

            var nextIndex = (currentIndex + 1) % UiLanguageOrder.Length;
            return UiLanguageOrder[nextIndex];
        }

        private static string FormatNextLanguageButtonLabel()
        {
            var nextLanguage = GetNextUiLanguageCode(_uiLanguage);
            var title = Ui("Sprache wechseln", "Switch language");
            return $"{title}: {GetUiLanguageName(nextLanguage)}";
        }

        private static string DockSideLeftLabel()
        {
            return Ui("Links", "Left");
        }

        private static string DockSideRightLabel()
        {
            return Ui("Rechts", "Right");
        }

        private static string FormatVisibleAppsLabel(int value)
        {
            return value == 0
                ? Ui("0 Apps (nur Icon-Leiste)", "0 Apps (icons row only)")
                : $"{value} {Ui("Apps", "apps")}";
        }

        private static string FormatDockPeekLabel(int value)
        {
            return $"{Ui("Sichtbare Breite (angedockt)", "Visible Width (docked)")}: {value}px";
        }

        private static string FormatDockRevealLabel(int value)
        {
            return $"{Ui("Hover-Zone (zeigen)", "Hover Zone (show)")}: {value}px";
        }

        private static string FormatAutoLimitLabel(int value)
        {
            return $"{Ui("Auto-Limit fur neue Apps", "Auto limit for new apps")}: {value}%";
        }

        private static string FormatAutoLimitAgeLabel(int value)
        {
            return $"{Ui("Max. Installationsalter fur Auto-Limit", "Max install age for auto limit")}: {value} {Ui("Tage", "days")}";
        }

        private static string FormatGlassStrengthLabel(int value)
        {
            return $"{Ui("Glas-Starke", "Glass Strength")}: {value}%";
        }

        private static string FormatGlassTransparencyLabel(int value)
        {
            return $"{Ui("Transparenz", "Transparency")}: {value}%";
        }

        private static string FormatGlassBorderThicknessLabel(int value)
        {
            return $"{Ui("Rand-Starke", "Border strength")}: {value}px";
        }

        private static string FormatGlassBorderSmudgeLabel(int value)
        {
            return $"{Ui("Rand-Verwischen", "Border smudge")}: {value}%";
        }
        private static bool IsSystemSoundEntry(AppEntry entry)
        {
            return string.Equals(entry.ExePath, "__system_sound__", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.ExeName, "SystemSound", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.DisplayName, "System Sound", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.DisplayName, "Lautstaerke", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.DisplayName, "Lautstärke", StringComparison.OrdinalIgnoreCase);
        }
        private static string TranslateUiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (IsEnglishUi())
            {
                if (text.StartsWith("Sichtbare Breite (angedockt): ", StringComparison.Ordinal))
                {
                    return "Visible Width (docked): " + text["Sichtbare Breite (angedockt): ".Length..];
                }

                if (text.StartsWith("Hover-Zone (zeigen): ", StringComparison.Ordinal))
                {
                    return "Hover Zone (show): " + text["Hover-Zone (zeigen): ".Length..];
                }

                if (text.StartsWith("Auto-Limit fur neue Apps: ", StringComparison.Ordinal))
                {
                    return "Auto limit for new apps: " + text["Auto-Limit fur neue Apps: ".Length..];
                }

                if (text.StartsWith("Max. Installationsalter fur Auto-Limit: ", StringComparison.Ordinal))
                {
                    return "Max install age for auto limit: " + text["Max. Installationsalter fur Auto-Limit: ".Length..].Replace(" Tage", " days", StringComparison.Ordinal);
                }

                if (text.StartsWith("Glas-Starke: ", StringComparison.Ordinal))
                {
                    return "Glass Strength: " + text["Glas-Starke: ".Length..];
                }

                if (text.StartsWith("Transparenz: ", StringComparison.Ordinal))
                {
                    return "Transparency: " + text["Transparenz: ".Length..];
                }

                if (text.StartsWith("Fehler: ", StringComparison.Ordinal))
                {
                    return "Error: " + text["Fehler: ".Length..];
                }

                return text switch
                {
                    "Einstellungen" => "Settings",
                    "\u2699  Einstellungen" => "\u2699  Settings",
                    "Rand-Andocken blendet das Fenster unaufallig am linken/rechten Rand ein und aus." => "Edge docking subtly hides/shows the window at the left/right edge.",
                    "Autostart mit Windows" => "Start with Windows",
                    "Immer im Vordergrund" => "Always on top",
                    "Bei Hover in den Vordergrund" => "Bring to front on hover",
                    "Overlay-Modus:" => "Overlay mode:",
                    "Sichtbare Apps:" => "Visible apps:",
                    "Andocken:" => "Docking:",
                    "Am Rand andocken" => "Dock to screen edge",
                    "Ecken-Reveal (fur alle Apps)" => "Corner reveal (for all apps)",
                    "Auto-Limit fur alle neuen Apps" => "Auto-limit for all new apps",
                    "Glasdesign:" => "Glass design:",
                    "Windows-Akzentfarbe nutzen" => "Use Windows accent color",
                    "Refresh-Intervall:" => "Refresh interval:",
                    "Fensterbreite:" => "Window width:",
                    "Position:" => "Position:",
                    "Zentrieren" => "Center",
                    "Oben" => "Top",
                    "Links" => "Left",
                    "Rechts" => "Right",
                    "Keine aktiven Apps" => "No active apps",
                    "Keine Audio-Apps aktiv" => "No audio apps active",
                    "Alle Apps sind gerade inaktiv" => "All apps are currently inactive",
                    "Bereit" => "Ready",
                    "Anzeigen / Verstecken" => "Show / Hide",
                    "Beenden" => "Exit",
                    _ => text
                };
            }

            if (text.StartsWith("Visible Width (docked): ", StringComparison.Ordinal))
            {
                return "Sichtbare Breite (angedockt): " + text["Visible Width (docked): ".Length..];
            }

            if (text.StartsWith("Hover Zone (show): ", StringComparison.Ordinal))
            {
                return "Hover-Zone (zeigen): " + text["Hover Zone (show): ".Length..];
            }

            if (text.StartsWith("Auto limit for new apps: ", StringComparison.Ordinal))
            {
                return "Auto-Limit fur neue Apps: " + text["Auto limit for new apps: ".Length..];
            }

            if (text.StartsWith("Max install age for auto limit: ", StringComparison.Ordinal))
            {
                return "Max. Installationsalter fur Auto-Limit: " + text["Max install age for auto limit: ".Length..].Replace(" days", " Tage", StringComparison.Ordinal);
            }

            if (text.StartsWith("Glass Strength: ", StringComparison.Ordinal))
            {
                return "Glas-Starke: " + text["Glass Strength: ".Length..];
            }

            if (text.StartsWith("Transparency: ", StringComparison.Ordinal))
            {
                return "Transparenz: " + text["Transparency: ".Length..];
            }

            if (text.StartsWith("Error: ", StringComparison.Ordinal))
            {
                return "Fehler: " + text["Error: ".Length..];
            }

            return text switch
            {
                "Settings" => "Einstellungen",
                "\u2699  Settings" => "\u2699  Einstellungen",
                "Edge docking subtly hides/shows the window at the left/right edge." => "Rand-Andocken blendet das Fenster unaufallig am linken/rechten Rand ein und aus.",
                "Start with Windows" => "Autostart mit Windows",
                "Always on top" => "Immer im Vordergrund",
                "Bring to front on hover" => "Bei Hover in den Vordergrund",
                "Overlay mode:" => "Overlay-Modus:",
                "Visible apps:" => "Sichtbare Apps:",
                "Docking:" => "Andocken:",
                "Dock to screen edge" => "Am Rand andocken",
                "Corner reveal (for all apps)" => "Ecken-Reveal (fur alle Apps)",
                "Auto-limit for all new apps" => "Auto-Limit fur alle neuen Apps",
                "Glass design:" => "Glasdesign:",
                "Use Windows accent color" => "Windows-Akzentfarbe nutzen",
                "Refresh interval:" => "Refresh-Intervall:",
                "Window width:" => "Fensterbreite:",
                "Center" => "Zentrieren",
                "Top" => "Oben",
                "Left" => "Links",
                "Right" => "Rechts",
                "No active apps" => "Keine aktiven Apps",
                "No audio apps active" => "Keine Audio-Apps aktiv",
                "All apps are currently inactive" => "Alle Apps sind gerade inaktiv",
                "Ready" => "Bereit",
                "Show / Hide" => "Anzeigen / Verstecken",
                "Exit" => "Beenden",
                _ => text
            };
        }

        private static void LocalizeVisualTree(DependencyObject? root)
        {
            if (root == null)
            {
                return;
            }

            if (root is TextBlock textBlock)
            {
                textBlock.Text = TranslateUiText(textBlock.Text);
            }
            else if (root is CheckBox checkBox && checkBox.Content is string checkText)
            {
                checkBox.Content = TranslateUiText(checkText);
            }
            else if (root is Button button && button.Content is string buttonText)
            {
                button.Content = TranslateUiText(buttonText);
            }

            if (root is FrameworkElement element && element.ToolTip is ToolTip toolTip && toolTip.Content is string tipText)
            {
                toolTip.Content = TranslateUiText(tipText);
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                LocalizeVisualTree(VisualTreeHelper.GetChild(root, i));
            }
        }

        private static void RebuildMainWindowContent()
        {
            if (_mainWin == null || Application.Current == null)
            {
                return;
            }

            var outer = BuildGlass();
            _mainGlassBorder = outer;
            outer.Child = BuildMainLayout(Application.Current);
            _mainWin.Content = outer;
            ApplyGlassTheme();
            Refresh();
            ApplyCornerRevealVisualState(true);
        }

        private static void ApplyTrayLanguageTexts()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Text = "Mini Mixer Overlay";
            }

            if (_trayToggleItem != null)
            {
                _trayToggleItem.Text = Ui("Anzeigen / Verstecken", "Show / Hide");
            }

            if (_traySettingsItem != null)
            {
                _traySettingsItem.Text = Ui("Einstellungen", "Settings");
            }

            if (_trayExitItem != null)
            {
                _trayExitItem.Text = Ui("Beenden", "Exit");
            }
        }

        private static void SetUiLanguage(string language)
        {
            var normalizedLanguage = NormalizeUiLanguage(language);
            if (string.Equals(_uiLanguage, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var reopenSettings = _settingsWin != null;
            if (_settingsWin != null)
            {
                _settingsWin.Close();
                _settingsWin = null;
                _settingsGlassBorder = null;
                _settingsGameHookStatusTxt = null;
            }

            _uiLanguage = normalizedLanguage;
            if (_ctrl != null)
            {
                _ctrl.Settings.Ui.Language = _uiLanguage;
            }

            RebuildMainWindowContent();
            ApplyTrayLanguageTexts();

            if (reopenSettings)
            {
                ShowSettings();
            }
        }

        private static string ResolveGameHookAssetsPath(string? configuredPath)
        {
            var path = string.IsNullOrWhiteSpace(configuredPath)
                ? DefaultGameHookAssetsRelativePath
                : configuredPath.Trim();

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
        }

        private static void SetGameHookStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (_mainWin != null && !_mainWin.Dispatcher.CheckAccess())
            {
                _mainWin.Dispatcher.BeginInvoke(() => SetGameHookStatus(message));
                return;
            }

            if (!IsDesktopOverlayMode() && _statusTxt != null)
            {
                _statusTxt.Visibility = Visibility.Collapsed;
            }

            if (_settingsGameHookStatusTxt != null)
            {
                _settingsGameHookStatusTxt.Text = TranslateUiText(message);
            }
        }

        private static void EnsureGameHookRuntimeCreated()
        {
            if (_mainWin == null || _gameHookRuntime != null)
            {
                return;
            }

            _gameHookRuntime = new GameHookOverlayRuntime(_mainWin, SetGameHookStatus);
            _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput;
        }

        private static bool StartGameHookRuntime()
        {
            if (_mainWin == null)
            {
                return false;
            }

            EnsureGameHookRuntimeCreated();
            if (_gameHookRuntime == null)
            {
                return false;
            }

            _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput;
            if (_gameHookRuntime.IsRunning)
            {
                if (_gameHookInputInterceptEnabled)
                {
                    _gameHookRuntime.SendInputIntercept(true);
                }

                return true;
            }

            var started = _gameHookRuntime.Start();
            if (!started)
            {
                SetGameHookStatus(Ui("Game-Hook Runtime konnte nicht starten (Fenster-Handle fehlt evtl. noch).", "Game-hook runtime could not start (window handle may still be missing)."));
                return false;
            }

            if (_gameHookInputInterceptEnabled)
            {
                _gameHookRuntime.SendInputIntercept(true);
            }

            TryAutoInjectForegroundGame();
            return true;
        }

        private static void StopGameHookRuntime()
        {
            if (_gameHookRuntime == null)
            {
                ResetGameHookAutoDetectState();
                return;
            }

            _gameHookRuntime.Stop();
            _gameHookInputInterceptEnabled = false;
            ResetGameHookAutoDetectState();
        }

        private static void SyncGameHookRuntimeState()
        {
            if (IsDesktopOverlayMode())
            {
                StopGameHookRuntime();
                return;
            }

            EnsureGameHookRuntimeCreated();
            _gameHookRuntime!.ForwardGameInputToWindow = _gameHookForwardInput;

            if (_gameHookAutoStartRuntime)
            {
                _ = StartGameHookRuntime();
                return;
            }

            if (_gameHookRuntime.IsRunning)
            {
                return;
            }

            SetGameHookStatus(Ui("Game-Hook Runtime ist aus. Starte sie manuell im Settings-Menue.", "Game-hook runtime is off. Start it manually in the settings menu."));
        }

        private static void ApplyOverlayModeRuntimeState()
        {
            if (_mainWin == null)
            {
                return;
            }

            if (IsDesktopOverlayMode())
            {
                if (_ctrl != null)
                {
                    _mainWin.Topmost = _ctrl.Settings.Window.AlwaysOnTop;
                }

                if (_settingsWin != null)
                {
                    _settingsWin.Topmost = _mainWin.Topmost;
                }

                StopGameHookRuntime();
                return;
            }

            _mainWin.Topmost = true;
            if (_settingsWin != null)
            {
                _settingsWin.Topmost = true;
            }
            if (_cornerHintWin != null)
            {
                _cornerHintWin.Topmost = true;
                EnsureCornerHintOnTop();
            }

            if (_edgeDockActive)
            {
                _edgeDockActive = false;
                _edgeDockExpanded = true;
            }

            ApplyContextualMainWindowHeight();
            EnsureMainWindowInBounds(allowDockHidden: false);
            _cornerRevealVisibleUntilUtc = DateTime.UtcNow;
            ApplyCornerRevealVisualState(false);
            UpdateCornerHintWindow();
            RepositionSettingsWindow();
            SyncGameHookRuntimeState();
            TryAutoInjectForegroundGame();
        }

        private static Rect GetMainWorkAreaOrVirtual()
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

        private static void EnsureMainWindowInBounds(bool allowDockHidden)
        {
            if (_mainWin == null)
            {
                return;
            }

            var workArea = GetMainWorkAreaOrVirtual();
            var minLeft = workArea.Left;
            var maxLeft = workArea.Right - _mainWin.Width;

            if (allowDockHidden && _edgeDockEnabled && _edgeDockActive)
            {
                if (_edgeDockSide == "left")
                {
                    minLeft = workArea.Left - (_mainWin.Width - _edgeDockVisibleWidth);
                    maxLeft = workArea.Left;
                }
                else
                {
                    minLeft = workArea.Right - _mainWin.Width;
                    maxLeft = workArea.Right - _edgeDockVisibleWidth;
                }
            }

            _mainWin.Left = Math.Clamp(_mainWin.Left, minLeft, maxLeft);
            _mainWin.Top = Math.Clamp(_mainWin.Top, workArea.Top, workArea.Bottom - _mainWin.Height);
        }

        private static void SetEdgeDockEnabled(bool enabled)
        {
            if (_mainWin == null)
            {
                _edgeDockEnabled = enabled;
                return;
            }

            if (!IsDesktopOverlayMode())
            {
                _edgeDockEnabled = enabled;
                _edgeDockActive = false;
                _edgeDockExpanded = true;
                ApplyContextualMainWindowHeight();
                EnsureMainWindowInBounds(allowDockHidden: false);
                RepositionSettingsWindow();
                return;
            }

            _edgeDockEnabled = enabled;
            _edgeDockSide = NormalizeDockSide(_edgeDockSide);
            _edgeDockVisibleWidth = (int)Math.Clamp(_edgeDockVisibleWidth, EdgeDockVisibleMin, EdgeDockVisibleMax);
            _edgeDockRevealZoneWidth = (int)Math.Clamp(_edgeDockRevealZoneWidth, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);

            if (_edgeDockEnabled)
            {
                EvaluateDockSnapFromCurrentPosition();
                ApplyContextualMainWindowHeight();
                _edgeDockVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(EdgeDockHoldMs);
                _edgeDockExpanded = true;
                if (_edgeDockActive)
                {
                    var expandForEdit = _settingsWin != null || _isUserInteracting;
                    ApplyEdgeDockPlacement(expand: expandForEdit, force: true);
                    ApplyCornerRevealVisualState(true);
                    HideCornerHintWindow();
                }
                return;
            }

            _edgeDockActive = false;
            _edgeDockExpanded = true;
            ApplyContextualMainWindowHeight();
            EnsureMainWindowInBounds(allowDockHidden: false);
            _lastWindowLeft = _mainWin.Left;
            _lastWindowTop = _mainWin.Top;
            RepositionSettingsWindow();
        }

        private static void EvaluateDockSnapFromCurrentPosition()
        {
            if (_mainWin == null || !_edgeDockEnabled)
            {
                return;
            }

            var workArea = GetMainWorkAreaOrVirtual();
            var leftDist = Math.Abs(_mainWin.Left - workArea.Left);
            var rightDist = Math.Abs((_mainWin.Left + _mainWin.Width) - workArea.Right);

            var nearLeft = leftDist <= EdgeDockSnapDistancePx;
            var nearRight = rightDist <= EdgeDockSnapDistancePx;

            if (nearLeft || nearRight)
            {
                _edgeDockSide = nearLeft && nearRight
                    ? (leftDist <= rightDist ? "left" : "right")
                    : (nearLeft ? "left" : "right");
                var snappedCorner = ResolveCornerForSideFromWindowPosition(_edgeDockSide, workArea);
                RememberSnapCorner(snappedCorner, syncDockSide: false, persist: true);

                _edgeDockActive = true;
                _edgeDockExpanded = true;
                _edgeDockVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(EdgeDockHoldMs);
                ApplyEdgeDockPlacement(expand: true, force: true);
                return;
            }

            if (_edgeDockActive)
            {
                _edgeDockActive = false;
                _edgeDockExpanded = true;
                ApplyContextualMainWindowHeight();
                EnsureMainWindowInBounds(allowDockHidden: false);
                _lastWindowLeft = _mainWin.Left;
                _lastWindowTop = _mainWin.Top;
                RepositionSettingsWindow();
            }
        }

        private static void UpdateEdgeDockState()
        {
            if (!_edgeDockEnabled || !_edgeDockActive || _mainWin == null || _mainWin.WindowState == WindowState.Minimized)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (!TryGetCursorPosition(out var cursor))
            {
                ApplyEdgeDockPlacement(expand: true);
                return;
            }

            var workArea = GetMainWorkAreaOrVirtual();
            var windowHeight = _mainWin.ActualHeight > 1 ? _mainWin.ActualHeight : _mainWin.Height;
            var revealWidth = ComputeEffectiveDockRevealZonePx(windowHeight);
            var verticalPadding = 20 + (int)Math.Round(revealWidth * 0.35);
            var yMin = _mainWin.Top - verticalPadding;
            var yMax = _mainWin.Top + windowHeight + verticalPadding;
            var visibleWidth = (int)Math.Clamp(_edgeDockVisibleWidth, EdgeDockVisibleMin, EdgeDockVisibleMax);
            var visibleStripRect = _edgeDockSide == "left"
                ? new Rect(workArea.Left, yMin, visibleWidth, Math.Max(1, yMax - yMin))
                : new Rect(workArea.Right - visibleWidth, yMin, visibleWidth, Math.Max(1, yMax - yMin));
            var revealRect = _edgeDockSide == "left"
                ? new Rect(workArea.Left + visibleWidth, yMin, revealWidth, Math.Max(1, yMax - yMin))
                : new Rect(workArea.Right - visibleWidth - revealWidth, yMin, revealWidth, Math.Max(1, yMax - yMin));
            var inEdgeZone = revealRect.Contains(cursor) || visibleStripRect.Contains(cursor);

            var keepVisibleByWindowHover = IsCursorInsideWindow(cursor, _mainWin);

            if (inEdgeZone || keepVisibleByWindowHover || IsCursorInsideWindow(cursor, _settingsWin) || _isUserInteracting || _settingsWin != null)
            {
                _edgeDockVisibleUntilUtc = nowUtc.AddMilliseconds(EdgeDockHoldMs);

                if (inEdgeZone && !_edgeDockExpanded && _bringToFrontOnHover && _settingsWin == null)
                {
                    BringMainWindowToFront(activate: IsDesktopOverlayMode());
                }
            }

            var wasExpanded = _edgeDockExpanded;
            var shouldExpand = _settingsWin != null || _isUserInteracting || nowUtc <= _edgeDockVisibleUntilUtc;
            ApplyEdgeDockPlacement(shouldExpand);

            // Rebuild card list immediately when dock state flips, otherwise stale 1-app view can remain.
            if (wasExpanded != _edgeDockExpanded)
            {
                Refresh();
            }
        }

        private static int ComputeEffectiveDockRevealZonePx(double currentWindowHeight)
        {
            var configured = (int)Math.Clamp(_edgeDockRevealZoneWidth, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);
            if (_mainWin == null)
            {
                return configured;
            }

            var width = _mainWin.ActualWidth > 1 ? _mainWin.ActualWidth : _mainWin.Width;
            var widthFactor = Math.Clamp(width / 380.0, 0.7, 1.8);
            var heightFactor = Math.Clamp(currentWindowHeight / 154.0, 0.7, 1.6);
            var sizeFactor = (widthFactor * 0.65) + (heightFactor * 0.35);
            var effective = (int)Math.Round(configured * sizeFactor);
            return (int)Math.Clamp(effective, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);
        }

        private static void ApplyEdgeDockPlacement(bool expand, bool force = false)
        {
            if (_mainWin == null || !_edgeDockEnabled || !_edgeDockActive)
            {
                return;
            }

            var workArea = GetMainWorkAreaOrVirtual();
            _edgeDockVisibleWidth = (int)Math.Clamp(_edgeDockVisibleWidth, EdgeDockVisibleMin, EdgeDockVisibleMax);
            _edgeDockSide = NormalizeDockSide(_edgeDockSide);
            ApplyContextualMainWindowHeight();

            var top = Math.Clamp(_mainWin.Top, workArea.Top, workArea.Bottom - _mainWin.Height);
            var expandedLeft = _edgeDockSide == "left" ? workArea.Left : workArea.Right - _mainWin.Width;
            var hiddenLeft = _edgeDockSide == "left"
                ? workArea.Left - (_mainWin.Width - _edgeDockVisibleWidth)
                : workArea.Right - _edgeDockVisibleWidth;
            var targetLeft = expand ? expandedLeft : hiddenLeft;

            if (force || Math.Abs(_mainWin.Left - targetLeft) > 0.5)
            {
                _mainWin.Left = targetLeft;
            }

            if (force || Math.Abs(_mainWin.Top - top) > 0.5)
            {
                _mainWin.Top = top;
            }

            _edgeDockExpanded = expand;
            _lastWindowLeft = _mainWin.Left;
            _lastWindowTop = _mainWin.Top;
            RepositionSettingsWindow();
        }

        private static void RestoreDockPositionIfEnabled(bool expand)
        {
            if (_mainWin == null || !_edgeDockEnabled)
            {
                return;
            }

            _edgeDockActive = true;
            _edgeDockExpanded = expand;
            _rememberedSnapCorner = AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide);

            var workArea = GetMainWorkAreaOrVirtual();
            _mainWin.Top = IsTopCorner(_rememberedSnapCorner)
                ? workArea.Top
                : Math.Max(workArea.Top, workArea.Bottom - _mainWin.Height);

            ApplyEdgeDockPlacement(expand, force: true);
        }

        private static void BringMainWindowToFront(bool activate = false)
        {
            if (_mainWin == null || _settingsWin != null)
            {
                return;
            }

            var handle = new WindowInteropHelper(_mainWin).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var forceTopmost = !IsDesktopOverlayMode();
            var insertAfter = (forceTopmost || _mainWin.Topmost) ? HwndTopmost : HwndTop;
            var flags = SwpNoMove | SwpNoSize | SwpShowWindow;
            if (!activate)
            {
                flags |= SwpNoActivate;
            }

            SetWindowPos(
                handle,
                insertAfter,
                0,
                0,
                0,
                0,
                flags);

            if (activate)
            {
                ShowWindow(handle, SwShow);
                SetForegroundWindow(handle);
            }
        }

        private static void UpdateCornerRevealState()
        {
            if (_mainWin == null || _mainWin.WindowState == WindowState.Minimized)
            {
                HideCornerHintWindow();
                return;
            }

            var desktopMode = IsDesktopOverlayMode();

            if (!_isUserInteracting)
            {
                EnsureMainWindowInBounds(allowDockHidden: desktopMode && _edgeDockEnabled && _edgeDockActive);
            }

            if (desktopMode && _edgeDockEnabled)
            {
                if (!_edgeDockActive)
                {
                    EvaluateDockSnapFromCurrentPosition();
                }

                if (_edgeDockActive)
                {
                    ApplyCornerRevealVisualState(true);
                    HideCornerHintWindow();
                    UpdateEdgeDockState();
                    return;
                }
            }

            if (!_cornerRevealEnabled)
            {
                ApplyCornerRevealVisualState(true);
                HideCornerHintWindow();
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (!TryGetCursorPosition(out var cursor))
            {
                ApplyCornerRevealVisualState(true);
                HideCornerHintWindow();
                return;
            }

            var cursorInsideMainWindow = IsCursorInsideWindow(cursor, _mainWin);
            var keepVisibleByWindowHover = _cornerRevealVisible && cursorInsideMainWindow;

            // Desktop-Overlay: optionales Fenster-Hover-Reveal ueber Checkbox.
            // Hook-Modus: Sichtbarwerden nur ueber den Kreis/Hotzone.
            if (desktopMode && _bringToFrontOnHover && cursorInsideMainWindow)
            {
                _cornerRevealVisibleUntilUtc = nowUtc.AddMilliseconds(CornerRevealHoldMs);
                if (!_cornerRevealVisible && _settingsWin == null)
                {
                    BringMainWindowToFront(activate: true);
                }
            }

            if (keepVisibleByWindowHover || IsCursorInsideWindow(cursor, _settingsWin) || _isUserInteracting || _settingsWin != null)
            {
                _cornerRevealVisibleUntilUtc = nowUtc.AddMilliseconds(CornerRevealHoldMs);
            }

            if (IsCursorInRevealHotZone(cursor))
            {
                _cornerRevealVisibleUntilUtc = nowUtc.AddMilliseconds(CornerRevealHoldMs);
                if (_bringToFrontOnHover && !_cornerRevealVisible && _settingsWin == null)
                {
                    BringMainWindowToFront(activate: IsDesktopOverlayMode());
                }
            }

            var shouldBeVisible = _settingsWin != null || _isUserInteracting || nowUtc <= _cornerRevealVisibleUntilUtc;
            ApplyCornerRevealVisualState(shouldBeVisible);
            UpdateCornerHintWindow();
        }

        private static void ApplyCornerRevealVisualState(bool visible)
        {
            if (_mainWin == null)
            {
                return;
            }

            if (!_cornerRevealEnabled)
            {
                visible = true;
            }

            if (_cornerRevealVisible == visible)
            {
                UpdateCornerHintWindow();
                return;
            }

            _cornerRevealVisible = visible;
            var hiddenOpacity = IsDesktopOverlayMode() ? CornerRevealHiddenOpacity : 0.0;
            _mainWin.Opacity = visible ? 1.0 : hiddenOpacity;
            _mainWin.IsHitTestVisible = visible;
            UpdateCornerHintWindow();
        }

        private static bool IsCursorInsideWindow(Point cursor, Window? window)
        {
            if (window == null || !window.IsVisible)
            {
                return false;
            }

            var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
            var height = window.ActualHeight > 1 ? window.ActualHeight : window.Height;
            var rect = new Rect(window.Left, window.Top, width, height);
            return rect.Contains(cursor);
        }

        private static bool IsCursorInRevealHotZone(Point cursor)
        {
            if (_mainWin == null || !TryGetMonitorWorkAreaForOverlayTarget(out var workArea))
            {
                return false;
            }

            if (TryGetCornerHintDotCenter(out var dotCenter))
            {
                var activationRadius = (CornerHintDiameter / 2.0) + Math.Clamp(_edgeDockRevealZoneWidth, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);
                return DistanceSquared(cursor, dotCenter) <= activationRadius * activationRadius;
            }

            if (!IsDesktopOverlayMode())
            {
                var hookCorner = ResolveCornerHintCorner(workArea);
                var hookPosition = GetCornerHintPosition(workArea, hookCorner, CornerHintDiameter, CornerHintDiameter);
                var hookCenter = new Point(hookPosition.X + (CornerHintDiameter / 2.0), hookPosition.Y + (CornerHintDiameter / 2.0));
                var activationRadius = (CornerHintDiameter / 2.0) + Math.Clamp(_edgeDockRevealZoneWidth, EdgeDockRevealZoneMin, EdgeDockRevealZoneMax);
                return DistanceSquared(cursor, hookCenter) <= activationRadius * activationRadius;
            }

            var corner = ResolveCornerHintCorner(workArea);
            var hotZone = CreateHotZoneForCorner(workArea, corner);
            return hotZone.Contains(cursor);
        }

        private static bool TryGetCornerHintDotCenter(out Point center)
        {
            center = default;
            if (_cornerHintWin == null || !_cornerHintWin.IsVisible || !_cornerHintDotCenterValid)
            {
                return false;
            }

            center = _cornerHintDotCenter;
            return true;
        }

        private static ScreenCorner GetClosestScreenCorner(Point point, Rect workArea)
        {
            var topLeft = new Point(workArea.Left, workArea.Top);
            var topRight = new Point(workArea.Right, workArea.Top);
            var bottomLeft = new Point(workArea.Left, workArea.Bottom);
            var bottomRight = new Point(workArea.Right, workArea.Bottom);

            var best = ScreenCorner.TopRight;
            var bestDist = DistanceSquared(point, topRight);

            var distTopLeft = DistanceSquared(point, topLeft);
            if (distTopLeft < bestDist)
            {
                best = ScreenCorner.TopLeft;
                bestDist = distTopLeft;
            }

            var distBottomLeft = DistanceSquared(point, bottomLeft);
            if (distBottomLeft < bestDist)
            {
                best = ScreenCorner.BottomLeft;
                bestDist = distBottomLeft;
            }

            var distBottomRight = DistanceSquared(point, bottomRight);
            if (distBottomRight < bestDist)
            {
                best = ScreenCorner.BottomRight;
            }

            return best;
        }

        private static double DistanceSquared(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (dx * dx) + (dy * dy);
        }

        private static Rect CreateHotZoneForCorner(Rect workArea, ScreenCorner corner)
        {
            return corner switch
            {
                ScreenCorner.TopLeft => new Rect(workArea.Left, workArea.Top, CornerRevealHotZonePx, CornerRevealHotZonePx),
                ScreenCorner.TopRight => new Rect(workArea.Right - CornerRevealHotZonePx, workArea.Top, CornerRevealHotZonePx, CornerRevealHotZonePx),
                ScreenCorner.BottomLeft => new Rect(workArea.Left, workArea.Bottom - CornerRevealHotZonePx, CornerRevealHotZonePx, CornerRevealHotZonePx),
                _ => new Rect(workArea.Right - CornerRevealHotZonePx, workArea.Bottom - CornerRevealHotZonePx, CornerRevealHotZonePx, CornerRevealHotZonePx)
            };
        }

        private static bool TryGetMonitorWorkAreaForOverlayTarget(out Rect workArea)
        {
            if (!IsDesktopOverlayMode())
            {
                try
                {
                    if (GameHookBridge.TryGetForegroundWindowInfo(out var foreground) && foreground.Hwnd != IntPtr.Zero)
                    {
                        var foregroundMonitor = MonitorFromWindow(foreground.Hwnd, MonitorDefaultToNearest);
                        if (TryGetWorkAreaForMonitor(foregroundMonitor, out workArea))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // fallback to main window monitor below
                }
            }

            return TryGetMonitorWorkAreaForMainWindow(out workArea);
        }

        private static bool TryGetMonitorWorkAreaForMainWindow(out Rect workArea)
        {
            workArea = Rect.Empty;
            if (_mainWin == null)
            {
                return false;
            }

            var monitor = IntPtr.Zero;
            if (_edgeDockEnabled && _edgeDockActive && TryGetDockVisibleAnchor(out var dockAnchor))
            {
                monitor = MonitorFromPoint(
                    new NativePoint
                    {
                        X = (int)Math.Round(dockAnchor.X),
                        Y = (int)Math.Round(dockAnchor.Y)
                    },
                    MonitorDefaultToNearest);
            }

            if (monitor == IntPtr.Zero)
            {
                var handle = new WindowInteropHelper(_mainWin).Handle;
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            }

            return TryGetWorkAreaForMonitor(monitor, out workArea);
        }

        private static bool TryGetDockVisibleAnchor(out Point anchor)
        {
            anchor = default;
            if (_mainWin == null)
            {
                return false;
            }

            var width = _mainWin.ActualWidth > 1 ? _mainWin.ActualWidth : _mainWin.Width;
            var height = _mainWin.ActualHeight > 1 ? _mainWin.ActualHeight : _mainWin.Height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var visibleWidth = Math.Clamp(_edgeDockVisibleWidth, EdgeDockVisibleMin, (int)Math.Max(EdgeDockVisibleMin, Math.Round(width)));
            var x = _edgeDockSide == "left"
                ? _mainWin.Left + width - (visibleWidth / 2.0)
                : _mainWin.Left + (visibleWidth / 2.0);

            anchor = new Point(x, _mainWin.Top + (height / 2.0));
            return true;
        }

        private static bool TryGetWorkAreaForMonitor(IntPtr monitor, out Rect workArea)
        {
            workArea = Rect.Empty;
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MonitorInfo();
            monitorInfo.CbSize = Marshal.SizeOf<MonitorInfo>();
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            var width = monitorInfo.RcWork.Right - monitorInfo.RcWork.Left;
            var height = monitorInfo.RcWork.Bottom - monitorInfo.RcWork.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            workArea = new Rect(monitorInfo.RcWork.Left, monitorInfo.RcWork.Top, width, height);
            return true;
        }

        private static bool TryGetCursorPosition(out Point cursor)
        {
            cursor = default;
            if (!GetCursorPos(out var nativePoint))
            {
                return false;
            }

            cursor = new Point(nativePoint.X, nativePoint.Y);
            return true;
        }

        private static void UpdateCornerHintWindow()
        {
            var desktopMode = IsDesktopOverlayMode();
            var hideForDocking = desktopMode && _edgeDockEnabled && _edgeDockActive;
            var hideWhenMainVisible = desktopMode && _cornerRevealVisible;

            if (_mainWin == null || hideForDocking || !_cornerRevealEnabled || hideWhenMainVisible || _settingsWin != null || _mainWin.WindowState == WindowState.Minimized || (!_cornerHintShowDot && !_cornerHintShowValue))
            {
                HideCornerHintWindow();
                return;
            }

            if (!TryGetMonitorWorkAreaForOverlayTarget(out var workArea))
            {
                HideCornerHintWindow();
                return;
            }

            var corner = ResolveCornerHintCorner(workArea);

            EnsureCornerHintWindow();
            if (_cornerHintWin == null)
            {
                return;
            }

            UpdateCornerHintVolumeLabel();

            var hintWidth = _cornerHintWin.Width > 1 ? _cornerHintWin.Width : CornerHintDiameter;
            var hintHeight = _cornerHintWin.Height > 1 ? _cornerHintWin.Height : CornerHintDiameter;
            var position = GetCornerHintPosition(workArea, corner, hintWidth, hintHeight);

            _cornerHintWin.Topmost = true;
            _cornerHintWin.Left = position.X;
            _cornerHintWin.Top = position.Y;
            UpdateCornerHintDotCenter();

            if (!_cornerHintWin.IsVisible)
            {
                _cornerHintWin.Show();
            }

            EnsureCornerHintOnTop();
        }

        private static ScreenCorner ResolveCornerHintCorner(Rect workArea)
        {
            if (_mainWin == null)
            {
                return ScreenCorner.TopRight;
            }

            if (!IsDesktopOverlayMode())
            {
                return AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide);
            }

            if (_edgeDockEnabled)
            {
                return AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide);
            }

            var center = new Point(_mainWin.Left + (_mainWin.Width / 2), _mainWin.Top + (_mainWin.Height / 2));
            var liveCorner = GetClosestScreenCorner(center, workArea);
            _rememberedSnapCorner = liveCorner;
            return liveCorner;
        }

        private static Point GetCornerHintPosition(Rect workArea, ScreenCorner corner, double hintWidth, double hintHeight)
        {
            return corner switch
            {
                ScreenCorner.TopLeft => new Point(workArea.Left + CornerHintInset, workArea.Top + CornerHintInset),
                ScreenCorner.TopRight => new Point(workArea.Right - CornerHintInset - hintWidth, workArea.Top + CornerHintInset),
                ScreenCorner.BottomLeft => new Point(workArea.Left + CornerHintInset, workArea.Bottom - CornerHintInset - hintHeight),
                _ => new Point(workArea.Right - CornerHintInset - hintWidth, workArea.Bottom - CornerHintInset - hintHeight)
            };
        }

        private static void UpdateCornerHintDotCenter()
        {
            if (_cornerHintWin == null)
            {
                _cornerHintDotCenterValid = false;
                return;
            }

            var width = _cornerHintWin.Width > 1 ? _cornerHintWin.Width : CornerHintDiameter;
            var height = _cornerHintWin.Height > 1 ? _cornerHintWin.Height : CornerHintDiameter;
            var dotX = _cornerHintWin.Left + (width / 2.0);
            var dotY = _cornerHintWin.Top + (height / 2.0);

            _cornerHintDotCenter = new Point(dotX, dotY);
            _cornerHintDotCenterValid = true;
        }

        private static void UpdateCornerHintVolumeLabel()
        {
            if (_cornerHintDot == null || _cornerHintVolumeText == null)
            {
                return;
            }

            _cornerHintDot.Visibility = _cornerHintShowDot ? Visibility.Visible : Visibility.Collapsed;
            var volumeHint = GetForegroundVolumeHintText();
            var showVolume = _cornerHintShowValue && !string.IsNullOrWhiteSpace(volumeHint);
            _cornerHintVolumeText.Text = showVolume ? volumeHint : string.Empty;
            _cornerHintVolumeText.Visibility = showVolume ? Visibility.Visible : Visibility.Collapsed;
            _cornerHintVolumeText.FontSize = volumeHint.Length switch
            {
                <= 2 => 9.2,
                3 => 7.8,
                _ => 7.2
            };
        }

        private static AppEntry? TryFindForegroundAudioEntrySafe(bool allowWhenSettingsVisible = false)
        {
            if (_ctrl == null || _ctrl.AppEntries.Count == 0)
            {
                return null;
            }

            if (!allowWhenSettingsVisible && _settingsWin != null)
            {
                return TryGetRememberedForegroundAudioEntry();
            }

            if (DateTime.UtcNow < _foregroundPriorityWarmupUntilUtc)
            {
                return TryGetRememberedForegroundAudioEntry();
            }

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastForegroundProbeUtc).TotalMilliseconds < ForegroundProbeThrottleMs)
            {
                return TryGetRememberedForegroundAudioEntry();
            }

            _lastForegroundProbeUtc = nowUtc;

            try
            {
                if (!GameHookBridge.TryGetForegroundWindowInfo(out var foreground))
                {
                    return TryGetRememberedForegroundAudioEntry();
                }

                var entry = FindForegroundAudioEntry(foreground);
                if (entry != null && !IsSystemSoundEntry(entry))
                {
                    RememberForegroundAudioEntry(entry);
                }

                if (entry == null && IsOverlayForeground(foreground))
                {
                    return TryGetRememberedForegroundAudioEntry();
                }

                return entry;
            }
            catch
            {
                return TryGetRememberedForegroundAudioEntry();
            }
        }

        private static string GetForegroundVolumeHintText()
        {
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _cornerHintVolumeCacheUtc).TotalMilliseconds < CornerHintVolumeCacheMs)
            {
                return _cornerHintVolumeCacheText;
            }

            _cornerHintVolumeCacheUtc = nowUtc;
            _cornerHintVolumeCacheText = string.Empty;

            if (_ctrl == null || _ctrl.AppEntries.Count == 0)
            {
                return _cornerHintVolumeCacheText;
            }

            var entry = TryFindForegroundAudioEntrySafe(allowWhenSettingsVisible: true);
            if (entry != null)
            {
                var percent = entry.IsMuted
                    ? 0
                    : (int)Math.Round(Math.Clamp(entry.CombinedVolume, 0f, 1f) * 100f);
                _cornerHintVolumeCacheText = $"{percent:F0}";
            }

            return _cornerHintVolumeCacheText;
        }

        private static AppEntry? FindForegroundAudioEntry(GameHookForegroundWindowInfo foreground)
        {
            if (_ctrl == null || foreground.ProcessId == 0)
            {
                return null;
            }

            if (IsOverlayForeground(foreground))
            {
                return TryGetRememberedForegroundAudioEntry();
            }

            foreach (var entry in _ctrl.AppEntries)
            {
                if (entry.Sessions.Any(s => s.ProcessId == foreground.ProcessId))
                {
                    return entry;
                }
            }

            var foregroundName = NormalizeProcessNameKey(foreground.ProcessName);
            var normalizedTitle = (foreground.WindowTitle ?? string.Empty).Trim();
            var hasForegroundName = !string.IsNullOrWhiteSpace(foregroundName);

            foreach (var entry in _ctrl.AppEntries)
            {
                var exeName = NormalizeProcessNameKey(entry.ExeName);
                if (hasForegroundName && string.Equals(exeName, foregroundName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }

                var pathName = NormalizeProcessNameKey(Path.GetFileNameWithoutExtension(entry.ExePath));
                if (hasForegroundName && string.Equals(pathName, foregroundName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }

                var displayName = (entry.DisplayName ?? string.Empty).Trim();
                if (hasForegroundName &&
                    !string.IsNullOrWhiteSpace(displayName) &&
                    displayName.IndexOf(foregroundName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return entry;
                }

                if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
                    !string.IsNullOrWhiteSpace(displayName) &&
                    (normalizedTitle.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     displayName.IndexOf(normalizedTitle, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return entry;
                }
            }

            return null;
        }

        private static void RememberForegroundAudioEntry(AppEntry? entry)
        {
            if (entry == null || IsSystemSoundEntry(entry) || string.IsNullOrWhiteSpace(entry.ExePath))
            {
                return;
            }

            _lastNonSystemForegroundExePath = entry.ExePath;
            _lastNonSystemForegroundSeenUtc = DateTime.UtcNow;
        }

        private static AppEntry? TryGetRememberedForegroundAudioEntry()
        {
            if (_ctrl == null || string.IsNullOrWhiteSpace(_lastNonSystemForegroundExePath))
            {
                return null;
            }

            var remembered = _ctrl.AppEntries.FirstOrDefault(entry =>
                !IsSystemSoundEntry(entry) &&
                string.Equals(entry.ExePath, _lastNonSystemForegroundExePath, StringComparison.OrdinalIgnoreCase));

            if (remembered != null)
            {
                return remembered;
            }

            if ((DateTime.UtcNow - _lastNonSystemForegroundSeenUtc).TotalMinutes > 30)
            {
                _lastNonSystemForegroundExePath = string.Empty;
            }

            return null;
        }

        private static bool IsOverlayForeground(GameHookForegroundWindowInfo foreground)
        {
            if (foreground.ProcessId != 0 && foreground.ProcessId == (uint)Environment.ProcessId)
            {
                return true;
            }

            var normalized = NormalizeProcessNameKey(foreground.ProcessName);
            return normalized.IndexOf("minimixeroverlay", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeProcessNameKey(string? value)
        {
            var key = (value ?? string.Empty).Trim();
            if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^4];
            }

            return key;
        }

        private static void EnsureCornerHintWindow()
        {
            if (_cornerHintWin != null)
            {
                return;
            }

            var dot = new Border
            {
                Width = CornerHintDiameter,
                Height = CornerHintDiameter,
                CornerRadius = new CornerRadius(CornerHintDiameter / 2),
                Background = new SolidColorBrush(Color.FromArgb(176, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(235, 220, 245, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = _activeGlassAccent,
                    Opacity = 0.7,
                    BlurRadius = 12,
                    ShadowDepth = 0
                }
            };
            _cornerHintDot = dot;

            var volumeText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 252, 255)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _cornerHintVolumeText = volumeText;

            var root = new Grid
            {
                Width = CornerHintDiameter,
                Height = CornerHintDiameter,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            root.Children.Add(dot);
            root.Children.Add(volumeText);

            _cornerHintWin = new Window
            {
                Width = CornerHintDiameter,
                Height = CornerHintDiameter,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Topmost = true,
                Content = root
            };

            _cornerHintWin.IsHitTestVisible = false;
            _cornerHintWin.SourceInitialized += (_, _) =>
            {
                EnableNoActivateWindowBehavior(_cornerHintWin);
                EnsureCornerHintOnTop();
            };
            _cornerHintWin.Closed += (_, _) =>
            {
                _cornerHintWin = null;
                _cornerHintDot = null;
                _cornerHintVolumeText = null;
                _cornerHintDotCenterValid = false;
            };
            ApplyCornerHintTheme();
        }

        private static void HideCornerHintWindow()
        {
            if (_cornerHintWin != null && _cornerHintWin.IsVisible)
            {
                _cornerHintWin.Hide();
            }
            _cornerHintDotCenterValid = false;
        }

        private static void CloseCornerHintWindow()
        {
            if (_cornerHintWin != null)
            {
                _cornerHintWin.Close();
                _cornerHintWin = null;
            }
            _cornerHintDot = null;
            _cornerHintVolumeText = null;
            _cornerHintDotCenterValid = false;
            _cornerHintVolumeCacheUtc = DateTime.MinValue;
            _cornerHintVolumeCacheText = string.Empty;
        }

        private static void EnsureCornerHintOnTop()
        {
            if (_cornerHintWin == null)
            {
                return;
            }

            var handle = new WindowInteropHelper(_cornerHintWin).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        private static void ApplyGlassTheme()
        {
            _activeGlassAccent = ResolveGlassAccentColor();
            var borderAccent = ResolveGlassBorderColor(_activeGlassAccent);

            var strength = _glassStrength / 100.0;
            var transparency = _glassTransparency / 100.0;

            ApplyGlassBorderTheme(
                _mainGlassBorder,
                isSettingsPanel: false,
                _activeGlassAccent,
                borderAccent,
                strength,
                transparency,
                _glassBorderThickness,
                _glassBorderSmudge);
            ApplyGlassBorderTheme(
                _settingsGlassBorder,
                isSettingsPanel: true,
                _activeGlassAccent,
                borderAccent,
                strength,
                transparency,
                _glassBorderThickness,
                _glassBorderSmudge);
            ApplyCornerHintTheme();
        }

        private static void ApplyGlassBorderTheme(
            Border? border,
            bool isSettingsPanel,
            Color accent,
            Color borderAccent,
            double strength,
            double transparency,
            int borderThicknessPx,
            int borderSmudgePercent)
        {
            if (border == null)
            {
                return;
            }

            var topBase = BlendColor(Color.FromRgb(29, 38, 64), accent, 0.16 + (strength * 0.30));
            var midBase = BlendColor(Color.FromRgb(21, 30, 52), accent, 0.10 + (strength * 0.24));
            var bottomBase = BlendColor(Color.FromRgb(14, 20, 38), accent, 0.08 + (strength * 0.20));

            var panelFactor = isSettingsPanel ? 1.18 : 1.0;
            var topAlpha = (byte)Math.Clamp((42 + (transparency * 140)) * panelFactor, 30, 220);
            var midAlpha = (byte)Math.Clamp((35 + (transparency * 120)) * panelFactor, 26, 205);
            var bottomAlpha = (byte)Math.Clamp((24 + (transparency * 108)) * panelFactor, 18, 188);
            if (isSettingsPanel)
            {
                topAlpha = (byte)Math.Max((int)topAlpha, 180);
                midAlpha = (byte)Math.Max((int)midAlpha, 164);
                bottomAlpha = (byte)Math.Max((int)bottomAlpha, 148);
            }

            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(topAlpha, topBase.R, topBase.G, topBase.B), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(midAlpha, midBase.R, midBase.G, midBase.B), 0.36));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(bottomAlpha, bottomBase.R, bottomBase.G, bottomBase.B), 1));

            border.Background = bg;
            border.BorderThickness = new Thickness(Math.Clamp(borderThicknessPx, GlassBorderThicknessMin, GlassBorderThicknessMax));

            var smudge = Math.Clamp(borderSmudgePercent, GlassBorderSmudgeMin, GlassBorderSmudgeMax) / 100.0;
            var borderColor = BlendColor(Color.FromRgb(188, 210, 230), borderAccent, 0.44 + (strength * 0.36));
            var borderAlpha = (byte)Math.Clamp(98 + (strength * 122) + (smudge * 70), 86, 255);
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, borderColor.R, borderColor.G, borderColor.B));

            var glowColor = BlendColor(Colors.Black, borderAccent, 0.62);
            border.Effect = new DropShadowEffect
            {
                Color = glowColor,
                Opacity = Math.Clamp(0.16 + (strength * 0.42) + (smudge * 0.22), 0.16, 0.84),
                BlurRadius = Math.Clamp(16 + (strength * 30) + (smudge * 34), 16, 88),
                ShadowDepth = 0
            };
        }

        private static void ApplyCornerHintTheme()
        {
            if (_cornerHintDot == null)
            {
                return;
            }

            var strength = _glassStrength / 100.0;
            var glow = Math.Clamp(0.34 + (strength * 0.48), 0.30, 0.82);

            _cornerHintDot.Background = new SolidColorBrush(Color.FromArgb(178, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
            _cornerHintDot.BorderBrush = new SolidColorBrush(Color.FromArgb(236, 226, 246, 255));
            _cornerHintDot.Effect = new DropShadowEffect
            {
                Color = _activeGlassAccent,
                Opacity = glow,
                BlurRadius = Math.Clamp(9 + (strength * 14), 9, 22),
                ShadowDepth = 0
            };

            if (_cornerHintVolumeText != null)
            {
                var valueColor = ResolveCornerHintValueColor();

                _cornerHintVolumeText.Foreground = new SolidColorBrush(valueColor);
                _cornerHintVolumeText.Effect = new DropShadowEffect
                {
                    Color = Color.FromArgb(220, 4, 10, 16),
                    Opacity = 0.95,
                    BlurRadius = 4,
                    ShadowDepth = 0
                };
            }
        }

        private static Color ResolveCornerHintValueColor()
        {
            if (_cornerHintUseCustomValueColor && TryParseHexColor(_cornerHintCustomValueColorHex, out var customColor))
            {
                return customColor;
            }

            if (string.Equals(_cornerHintValueColor, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return BlendColor(Color.FromRgb(245, 252, 255), _activeGlassAccent, 0.35);
            }

            if (_cornerValuePalette.TryGetValue(NormalizeCornerHintValueColor(_cornerHintValueColor), out var valueColor))
            {
                return valueColor;
            }

            return Color.FromRgb(245, 252, 255);
        }

        private static Color ResolveGlassAccentColor()
        {
            _windowsAccentUnavailable = false;
            if (_glassUseCustomColor && TryParseHexColor(_glassCustomColorHex, out var customColor))
            {
                return customColor;
            }

            if (_useWindowsAccentForGlass)
            {
                if (TryGetWindowsAccentColor(out var windowsAccent))
                {
                    return windowsAccent;
                }

                _windowsAccentUnavailable = true;
            }

            if (_glassPalette.TryGetValue(NormalizePaletteName(_glassPaletteName), out var paletteColor))
            {
                return paletteColor;
            }

            return _glassPalette["Cyan"];
        }

        private static bool TryGetWindowsAccentColor(out Color accent)
        {
            accent = default;

            try
            {
                using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (TryReadRegistryColor(dwm, "ColorizationColor", out accent))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                using var accentKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
                if (TryReadRegistryColor(accentKey, "AccentColorMenu", out accent))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool TryReadRegistryColor(RegistryKey? key, string valueName, out Color color)
        {
            color = default;
            if (key == null)
            {
                return false;
            }

            var raw = key.GetValue(valueName);
            uint argb;
            switch (raw)
            {
                case int i:
                    argb = unchecked((uint)i);
                    break;
                case uint u:
                    argb = u;
                    break;
                default:
                    return false;
            }

            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);

            if (a == 0)
            {
                a = 255;
            }

            color = Color.FromRgb(r, g, b);
            return argb != 0;
        }

        private static string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static bool TryParseHexColor(string? value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var raw = value.Trim();
            if (raw.StartsWith("#", StringComparison.Ordinal))
            {
                raw = raw[1..];
            }
            else if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[2..];
            }

            if (raw.Length == 3)
            {
                raw = string.Concat(raw.Select(ch => $"{ch}{ch}"));
            }

            if (raw.Length == 8)
            {
                raw = raw[2..];
            }

            if (raw.Length != 6)
            {
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return false;
            }

            var r = (byte)((rgb >> 16) & 0xFF);
            var g = (byte)((rgb >> 8) & 0xFF);
            var b = (byte)(rgb & 0xFF);
            color = Color.FromRgb(r, g, b);
            return true;
        }

        private static string NormalizeHexColor(string? value, Color fallback)
        {
            if (TryParseHexColor(value, out var parsed))
            {
                return ColorToHex(parsed);
            }

            return ColorToHex(fallback);
        }

        private static Color ResolveGlassBorderColor(Color glassAccent)
        {
            if (_glassBorderUseCustomColor && TryParseHexColor(_glassBorderColorHex, out var borderColor))
            {
                return borderColor;
            }

            return BlendColor(Color.FromRgb(188, 210, 230), glassAccent, 0.60);
        }

        private static string NormalizePaletteName(string? paletteName)
        {
            if (string.IsNullOrWhiteSpace(paletteName))
            {
                return "Cyan";
            }

            foreach (var key in _glassPalette.Keys)
            {
                if (string.Equals(key, paletteName, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return "Cyan";
        }

        private static Color BlendColor(Color from, Color to, double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            var inv = 1 - ratio;
            return Color.FromRgb(
                (byte)Math.Clamp((from.R * inv) + (to.R * ratio), 0, 255),
                (byte)Math.Clamp((from.G * inv) + (to.G * ratio), 0, 255),
                (byte)Math.Clamp((from.B * inv) + (to.B * ratio), 0, 255));
        }

        private static int GetEffectiveVisibleApps()
        {
            var configured = (int)Math.Clamp(_visibleApps, 0, 12);
            if (configured == 0)
            {
                return 0;
            }

            return (_edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded) ? 1 : configured;
        }

        private static void ApplyAppListVisibilityState()
        {
            var showCards = GetEffectiveVisibleApps() > 0;

            if (_appListRowDefinition != null)
            {
                _appListRowDefinition.Height = showCards
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
            }

            if (_appListScrollViewer != null)
            {
                _appListScrollViewer.Visibility = showCards ? Visibility.Visible : Visibility.Collapsed;
                _appListScrollViewer.IsHitTestVisible = showCards;
            }
        }

        private static void ApplyContextualMainWindowHeight()
        {
            if (_mainWin == null)
            {
                return;
            }

            var targetHeight = ComputeHeight(GetEffectiveVisibleApps());
            if (Math.Abs(_mainWin.Height - targetHeight) > 0.5)
            {
                _mainWin.Height = targetHeight;
            }

            ApplyAppListVisibilityState();
        }

        private static void CloseActiveAppPopup()
        {
            if (_activeAppPopup == null)
            {
                return;
            }

            _activeAppPopup.IsOpen = false;
            _activeAppPopup.Child = null;
            _activeAppPopup = null;
        }

        private static void ShowActiveAppPopup(AppEntry entry, FrameworkElement targetElement)
        {
            if (_mainWin == null || _ctrl == null)
            {
                return;
            }

            CloseActiveAppPopup();

            var accent = new SolidColorBrush(_activeGlassAccent);
            var mutedBrush = new SolidColorBrush(Color.FromRgb(255, 107, 107));
            var popupBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B)),
                Background = new SolidColorBrush(Color.FromArgb(230, 15, 28, 46)),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var popupLayout = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            popupLayout.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 210,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var iconBorder = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(170, 25, 38, 58)),
                BorderBrush = entry.IsMuted ? mutedBrush : Brushes.Transparent,
                BorderThickness = entry.IsMuted ? new Thickness(1) : new Thickness(0),
                Cursor = Cursors.Hand,
                Child = MakeIconElement(entry.ExePath, entry.IconBytes, entry.IsMuted)
            };
            Grid.SetColumn(iconBorder, 0);
            row.Children.Add(iconBorder);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(entry.CombinedVolume * 100f, 0, 100),
                Width = 150,
                Height = 20,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = accent,
                IsEnabled = !entry.IsMuted
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);

            var valueText = new TextBlock
            {
                Text = entry.IsMuted ? "0%" : $"{slider.Value:F0}%",
                Foreground = entry.IsMuted ? mutedBrush : accent,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            var muted = entry.IsMuted;
            void ApplyMuteVisual()
            {
                valueText.Text = muted ? "0%" : $"{slider.Value:F0}%";
                valueText.Foreground = muted ? mutedBrush : accent;
                slider.IsEnabled = !muted;
                iconBorder.BorderBrush = muted ? mutedBrush : Brushes.Transparent;
                iconBorder.BorderThickness = muted ? new Thickness(1) : new Thickness(0);
                iconBorder.Child = MakeIconElement(entry.ExePath, entry.IconBytes, muted);
            }

            iconBorder.MouseLeftButtonUp += (_, _) =>
            {
                muted = !muted;
                _ctrl.SetMute(entry.ExePath, muted);
                ApplyMuteVisual();
            };

            slider.GotMouseCapture += (_, _) => _isUserInteracting = true;
            slider.LostMouseCapture += (_, _) => _isUserInteracting = false;
            slider.ValueChanged += (_, _) =>
            {
                if (_isRefreshing || muted)
                {
                    return;
                }

                _ctrl.SetVolume(entry.ExePath, (float)(slider.Value / 100.0));
                valueText.Text = $"{slider.Value:F0}%";
            };

            popupBorder.PreviewMouseLeftButtonDown += (_, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (ReferenceEquals(source, slider))
                    {
                        return;
                    }

                    if (ReferenceEquals(source, iconBorder))
                    {
                        return;
                    }

                    if (source is Thumb || source is RepeatButton)
                    {
                        return;
                    }

                    source = source switch
                    {
                        Visual visual => VisualTreeHelper.GetParent(visual),
                        System.Windows.Media.Media3D.Visual3D visual3D => VisualTreeHelper.GetParent(visual3D),
                        FrameworkContentElement contentElement => contentElement.Parent,
                        ContentElement content => ContentOperations.GetParent(content),
                        _ => null
                    };
                }

                if (_activeAppPopup != null)
                {
                    _activeAppPopup.IsOpen = false;
                }
            };

            popupLayout.Children.Add(row);
            popupBorder.Child = popupLayout;

            var popup = new Popup
            {
                PlacementTarget = targetElement,
                Placement = PlacementMode.Top,
                AllowsTransparency = true,
                StaysOpen = false,
                Child = popupBorder,
                IsOpen = true
            };

            popup.Closed += (_, _) =>
            {
                _isUserInteracting = false;
                if (_activeAppPopup == popup)
                {
                    _activeAppPopup = null;
                }

                Refresh();
            };

            _activeAppPopup = popup;
        }

        private static void UpdateActiveAppsStrip(List<AppEntry> displayedEntries)
        {
            if (_activeAppsStrip == null)
            {
                return;
            }

            _activeAppsStrip.Children.Clear();
            foreach (var entry in displayedEntries)
            {
                var muted = entry.IsMuted;
                var iconButton = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(160, 20, 34, 56)),
                    BorderBrush = muted
                        ? new SolidColorBrush(Color.FromRgb(255, 107, 107))
                        : new SolidColorBrush(Color.FromArgb(110, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{entry.DisplayName} ({(muted ? "Mute" : $"{entry.CombinedVolume * 100f:F0}%")})"
                };

                iconButton.Child = MakeIconElement(entry.ExePath, entry.IconBytes, muted);
                iconButton.MouseLeftButtonUp += (_, _) => ShowActiveAppPopup(entry, iconButton);
                _activeAppsStrip.Children.Add(iconButton);
            }

            if (displayedEntries.Count == 0)
            {
                _activeAppsStrip.Children.Add(new TextBlock
                {
                    Text = Ui("Keine aktiven Apps", "No active apps"),
                    Foreground = new SolidColorBrush(Color.FromRgb(124, 138, 168)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                });
            }
        }

        private static void Refresh()
        {
            if (_ctrl == null || _appList == null)
            {
                return;
            }

            if (_edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded)
            {
                ApplyAppListVisibilityState();
            }
            else
            {
                ApplyContextualMainWindowHeight();
            }
            _isRefreshing = true;

            try
            {
                var shouldPollSessions = _settingsWin == null;
                if (shouldPollSessions)
                {
                    _isSessionRefreshFromUiPoll = true;
                    try
                    {
                        _ctrl.RefreshSessions();
                    }
                    finally
                    {
                        _isSessionRefreshFromUiPoll = false;
                    }
                }

                var showOnlyActive = false;
                var maxVisible = GetEffectiveVisibleApps();

                _appList.Children.Clear();
                var displayedEntries = new List<AppEntry>();
                var stripEntries = new List<AppEntry>();

                var preferSystemFallbackFirst = true;
                try
                {
                    if (GameHookBridge.TryGetForegroundWindowInfo(out var foregroundInfo) && IsOverlayForeground(foregroundInfo))
                    {
                        preferSystemFallbackFirst = false;
                    }
                }
                catch
                {
                    // keep default fallback behavior
                }

                var entries = _ctrl.AppEntries
                    .OrderBy(entry => IsSystemSoundEntry(entry)
                        ? (preferSystemFallbackFirst ? 0 : 2)
                        : 1)
                    .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var systemEntry = entries.FirstOrDefault(IsSystemSoundEntry);
                var foregroundEntry = TryFindForegroundAudioEntrySafe();

                var foregroundNonSystemEntry = foregroundEntry != null && !IsSystemSoundEntry(foregroundEntry)
                    ? foregroundEntry
                    : null;

                // Vordergrund-App kommt fuer schnelle Bedienung ganz nach vorne.
                // Falls keine Vordergrund-App erkannt wird, bleibt "Lautstaerke" zuerst.
                if (foregroundNonSystemEntry != null)
                {
                    RememberForegroundAudioEntry(foregroundNonSystemEntry);
                    entries.Remove(foregroundNonSystemEntry);
                    entries.Insert(0, foregroundNonSystemEntry);
                }

                var collapsedDockSingleCardMode = _edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded;
                var preferredCollapsedEntry = foregroundNonSystemEntry ?? systemEntry ?? entries.FirstOrDefault();

                foreach (var entry in entries)
                {
                    if (showOnlyActive && !entry.HasActiveAudio && !entry.IsMuted)
                    {
                        continue;
                    }

                    if (stripEntries.Count < 24)
                    {
                        stripEntries.Add(entry);
                    }

                    if (maxVisible <= 0)
                    {
                        continue;
                    }

                    if (collapsedDockSingleCardMode)
                    {
                        if (preferredCollapsedEntry != null &&
                            !ReferenceEquals(entry, preferredCollapsedEntry) &&
                            !string.Equals(entry.ExePath, preferredCollapsedEntry.ExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (displayedEntries.Count >= 1)
                        {
                            continue;
                        }
                    }

                    _appList.Children.Add(BuildCard(entry));
                    displayedEntries.Add(entry);
                }

                if (displayedEntries.Count == 0 && maxVisible > 0)
                {
                    _appList.Children.Add(new TextBlock
                    {
                        Text = _ctrl.AppEntries.Count == 0
                            ? Ui("Keine Audio-Apps aktiv", "No audio apps active")
                            : Ui("Alle Apps sind gerade inaktiv", "All apps are currently inactive"),
                        Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 136)),
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    });
                }

                if (_statusTxt != null)
                {
                    _statusTxt.Visibility = Visibility.Collapsed;
                    _statusTxt.Text = string.Empty;
                }

                UpdateActiveAppsStrip(stripEntries);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private static Border BuildCard(AppEntry entry)
        {
            var accent = new SolidColorBrush(_activeGlassAccent);
            var dimClr = new SolidColorBrush(Color.FromRgb(130, 130, 170));
            var iconBg = new SolidColorBrush(Color.FromRgb(26, 26, 52));
            var mutFgClr = new SolidColorBrush(Color.FromRgb(255, 107, 107));

            var cardMuted = entry.IsMuted;
            var volumePercent = entry.CombinedVolume * 100f;
            var exePath = entry.ExePath;
            var iconBytes = entry.IconBytes;
            var quickDockMode = _edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded;

            if (quickDockMode)
            {
                var compactCard = new Border
                {
                    Height = 96,
                    Background = new SolidColorBrush(Color.FromArgb(32, 46, 58, 88)),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(4, 6, 4, 6),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(64, 118, 150, 188)),
                    BorderThickness = new Thickness(1)
                };

                var compactPanel = new StackPanel
                {
                    Width = Math.Clamp(_edgeDockVisibleWidth - 14, 26, 96),
                    HorizontalAlignment = _edgeDockSide == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var quickIconBorder = new Border
                {
                    Width = 28,
                    Height = 28,
                    Background = iconBg,
                    CornerRadius = new CornerRadius(7),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                    BorderThickness = cardMuted ? new Thickness(1) : new Thickness(0),
                    BorderBrush = cardMuted ? mutFgClr : Brushes.Transparent
                };
                quickIconBorder.Child = MakeIconElement(exePath, iconBytes, cardMuted);

                var quickVolText = new TextBlock
                {
                    Text = cardMuted ? "0%" : volumePercent.ToString("F0") + "%",
                    Foreground = cardMuted ? mutFgClr : accent,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                var quickSlider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = cardMuted ? 0 : volumePercent,
                    Orientation = Orientation.Vertical,
                    Foreground = accent,
                    Height = 50,
                    Width = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsEnabled = !cardMuted
                };

                void ToggleQuickMute()
                {
                    cardMuted = !cardMuted;
                    _ctrl?.SetMute(exePath, cardMuted);

                    quickVolText.Text = cardMuted ? "0%" : quickSlider.Value.ToString("F0") + "%";
                    quickVolText.Foreground = cardMuted ? mutFgClr : accent;
                    quickSlider.IsEnabled = !cardMuted;
                    quickIconBorder.BorderThickness = cardMuted ? new Thickness(1) : new Thickness(0);
                    quickIconBorder.BorderBrush = cardMuted ? mutFgClr : Brushes.Transparent;
                    quickIconBorder.Child = MakeIconElement(exePath, iconBytes, cardMuted);
                }

                quickIconBorder.MouseLeftButtonUp += (_, _) => ToggleQuickMute();

                quickSlider.GotMouseCapture += (_, _) => _isUserInteracting = true;
                quickSlider.LostMouseCapture += (_, _) => _isUserInteracting = false;
                quickSlider.ValueChanged += (_, _) =>
                {
                    if (_isRefreshing || cardMuted)
                    {
                        return;
                    }

                    quickVolText.Text = quickSlider.Value.ToString("F0") + "%";
                    _ctrl?.SetVolume(exePath, (float)(quickSlider.Value / 100.0));
                };

                compactPanel.Children.Add(quickIconBorder);
                compactPanel.Children.Add(quickVolText);
                compactPanel.Children.Add(quickSlider);
                compactCard.Child = compactPanel;
                return compactCard;
            }

            var card = new Border
            {
                Height = CardVisualHeight,
                Background = new SolidColorBrush(Color.FromArgb(30, 46, 58, 88)),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, 118, 150, 188)),
                BorderThickness = new Thickness(1)
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var iconBorder = new Border
            {
                Width = 32,
                Height = 32,
                Background = iconBg,
                CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand,
                BorderThickness = cardMuted ? new Thickness(1) : new Thickness(0),
                BorderBrush = cardMuted ? mutFgClr : Brushes.Transparent
            };
            iconBorder.Child = MakeIconElement(exePath, iconBytes, cardMuted);
            Grid.SetColumn(iconBorder, 0);
            Grid.SetRow(iconBorder, 0);
            Grid.SetRowSpan(iconBorder, 2);
            g.Children.Add(iconBorder);

            var nameText = new TextBlock
            {
                Text = entry.DisplayName,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 2, 0, 0)
            };
            Grid.SetColumn(nameText, 1);
            Grid.SetRow(nameText, 0);
            g.Children.Add(nameText);

            var exeText = new TextBlock
            {
                Text = entry.ExeName + BuildRuleStatusSuffix(entry.Rule),
                Foreground = dimClr,
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(exeText, 1);
            Grid.SetRow(exeText, 1);
            g.Children.Add(exeText);

            var volText = new TextBlock
            {
                Text = cardMuted ? "0%" : volumePercent.ToString("F0") + "%",
                Foreground = cardMuted ? mutFgClr : accent,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(volText, 2);
            Grid.SetRow(volText, 0);
            g.Children.Add(volText);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = cardMuted ? 0 : volumePercent,
                Foreground = accent,
                Height = 20,
                Margin = new Thickness(0, 2, 0, 0),
                IsEnabled = !cardMuted
            };
            Grid.SetColumn(slider, 2);
            Grid.SetRow(slider, 1);
            g.Children.Add(slider);

            void ToggleMute()
            {
                cardMuted = !cardMuted;
                _ctrl?.SetMute(exePath, cardMuted);

                volText.Text = cardMuted ? "0%" : slider.Value.ToString("F0") + "%";
                volText.Foreground = cardMuted ? mutFgClr : accent;
                slider.IsEnabled = !cardMuted;
                iconBorder.BorderThickness = cardMuted ? new Thickness(1) : new Thickness(0);
                iconBorder.BorderBrush = cardMuted ? mutFgClr : Brushes.Transparent;
                iconBorder.Child = MakeIconElement(exePath, iconBytes, cardMuted);
            }

            iconBorder.MouseLeftButtonUp += (_, _) => ToggleMute();

            slider.GotMouseCapture += (_, _) => _isUserInteracting = true;
            slider.LostMouseCapture += (_, _) => _isUserInteracting = false;

            slider.ValueChanged += (_, _) =>
            {
                if (_isRefreshing || cardMuted)
                {
                    return;
                }

                volText.Text = slider.Value.ToString("F0") + "%";
                _ctrl?.SetVolume(exePath, (float)(slider.Value / 100.0));
            };

            card.Child = g;
            return card;
        }

        private static string BuildRuleStatusSuffix(AppRule? rule)
        {
            if (rule == null)
            {
                return string.Empty;
            }

            var flags = new List<string>();
            if (rule.Favorite) flags.Add("FAV");
            if (rule.Locked) flags.Add("LOCK");
            if (rule.ManualOverride) flags.Add("MANUAL");
            else if (rule.AutoApplied) flags.Add("AUTO");

            return flags.Count == 0 ? string.Empty : " | " + string.Join(" ", flags);
        }

        private static UIElement MakeIconElement(string exePath, byte[]? iconBytes, bool muted)
        {
            BitmapImage? bmp = null;

            if (!string.IsNullOrWhiteSpace(exePath))
            {
                _bitmapCache.TryGetValue(exePath, out bmp);
            }

            if (bmp == null && iconBytes != null && iconBytes.Length > 0)
            {
                try
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(iconBytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        _bitmapCache[exePath] = bmp;
                    }
                }
                catch
                {
                    bmp = null;
                    if (!string.IsNullOrWhiteSpace(exePath) && !_bitmapCache.ContainsKey(exePath))
                    {
                        _bitmapCache[exePath] = null;
                    }
                }
            }

            if (bmp != null)
            {
                return new Image
                {
                    Source = bmp,
                    Width = 22,
                    Height = 22,
                    Opacity = muted ? 0.45 : 1.0
                };
            }

            return new TextBlock
            {
                Text = muted ? "\uD83D\uDD07" : "\uD83D\uDD0A",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = muted ? 0.65 : 1.0
            };
        }
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
            var isSyncingOverlayModeCombo = false;
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
                IsChecked = _mainWin?.Topmost
            };
            ApplyCheckTheme(chkTop);
            AttachOptionLegend(chkTop, "Haelt Overlay und Einstellungen immer ueber anderen Fenstern.");
            chkTop.Click += (_, _) =>
            {
                var isTop = chkTop.IsChecked == true;
                if (_mainWin != null)
                {
                    _mainWin.Topmost = IsDesktopOverlayMode() ? isTop : true;
                }
                if (_settingsWin != null)
                {
                    _settingsWin.Topmost = IsDesktopOverlayMode() ? isTop : true;
                }
                if (_cornerHintWin != null)
                {
                    _cornerHintWin.Topmost = true;
                    EnsureCornerHintOnTop();
                }
                if (_ctrl != null) _ctrl.Settings.Window.AlwaysOnTop = isTop;
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
                Text = Ui("Overlay-Modus:", "Overlay mode:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var overlayModeCombo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                ItemsSource = new[] { "Desktop Overlay", "Game-Hook (Experimental)" },
                SelectedIndex = IsDesktopOverlayMode() ? 0 : 1
            };
            AttachOptionLegend(overlayModeCombo, "Desktop: klassisches Fenster mit Docking. Game-Hook: Ingame-Overlay mit Auto-Erkennung.");
            sp.Children.Add(overlayModeCombo);

            var overlayModeHintLbl = new TextBlock
            {
                Text = IsDesktopOverlayMode()
                    ? Ui("Desktop-Modus: Docking, Hover-Reveal und Glasfenster aktiv.", "Desktop mode: docking, hover reveal and glass window active.")
                    : Ui("Game-Hook-Modus: Desktop-Docking pausiert, Injection-Werkzeuge aktiv.", "Game-hook mode: desktop docking paused, injection tools active."),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 210)),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(overlayModeHintLbl);

            sp.Children.Add(new TextBlock
            {
                Text = Ui("Sichtbare Apps:", "Visible apps:"),
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var appsSlider = new Slider
            {
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
                {
                    ApplyContextualMainWindowHeight();
                }
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
                Content = Ui("Am Rand andocken", "Dock to screen edge"),
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
                            <ContentPresenter x:Name='ContentSite'
                                              Margin='{{TemplateBinding Padding}}'
                                              HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}'
                                              VerticalAlignment='{{TemplateBinding VerticalContentAlignment}}'
                                              Content='{{TemplateBinding SelectionBoxItem}}'
                                              ContentTemplate='{{TemplateBinding SelectionBoxItemTemplate}}'
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
            ApplyComboTheme(overlayModeCombo);
            GuardComboInteraction(overlayModeCombo);
            ApplyTextBoxTheme(gameHookTitleBox);
            ApplyTextBoxTheme(gameHookAssetsBox);
            ApplySliderTheme(appsSlider);

            var isDesktopModeUi = IsDesktopOverlayMode();
            chkDock.IsEnabled = true;
            gameHookTitleBox.IsEnabled = !isDesktopModeUi;
            gameHookAssetsBox.IsEnabled = !isDesktopModeUi;
            btnHookCheck.IsEnabled = !isDesktopModeUi;
            btnHookInject.IsEnabled = !isDesktopModeUi;
            chkGameHookAutoRuntime.IsEnabled = !isDesktopModeUi;
            chkGameHookForwardInput.IsEnabled = !isDesktopModeUi;
            btnRuntimeStart.IsEnabled = !isDesktopModeUi;
            btnRuntimeStop.IsEnabled = !isDesktopModeUi;
            btnRuntimeIntercept.IsEnabled = !isDesktopModeUi;

            var dockSideCombo = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4),
                ItemsSource = new[] { DockSideLeftLabel(), DockSideRightLabel() },
                SelectedIndex = _edgeDockSide == "left" ? 0 : 1
            };
            ApplyComboTheme(dockSideCombo);
            GuardComboInteraction(dockSideCombo);
            AttachOptionLegend(dockSideCombo, "Waehlt die bevorzugte Seite fuer das Rand-Andocken.");
            sp.Children.Add(dockSideCombo);

            var dockPeekSlider = new Slider
            {
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
            sp.Children.Add(dockPeekSlider);
            sp.Children.Add(dockPeekLbl);

            var dockRevealSlider = new Slider
            {
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

            void SyncOverlayModeComboDisplay()
            {
                var targetIndex = IsDesktopOverlayMode() ? 0 : 1;
                var items = new[] { "Desktop Overlay", "Game-Hook (Experimental)" };

                isSyncingOverlayModeCombo = true;
                overlayModeCombo.ItemsSource = null;
                overlayModeCombo.ItemsSource = items;
                overlayModeCombo.SelectedIndex = targetIndex;
                overlayModeCombo.InvalidateMeasure();
                overlayModeCombo.InvalidateVisual();
                overlayModeCombo.UpdateLayout();
                isSyncingOverlayModeCombo = false;
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
            sp.Children.Add(chkCornerValueCustomColor);
            sp.Children.Add(cornerValueCustomColorRow);
            sp.Children.Add(cornerValueCustomRgbLbl);
            sp.Children.Add(cornerValueCustomRedSlider);
            sp.Children.Add(cornerValueCustomGreenSlider);
            sp.Children.Add(cornerValueCustomBlueSlider);

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
                SyncOverlayModeComboDisplay();
                SyncDockSideComboDisplay();

                overlayModeHintLbl.Text = desktopMode
                    ? Ui("Desktop-Modus: Docking, Hover-Reveal und Glasfenster aktiv.", "Desktop mode: docking, hover reveal and glass window active.")
                    : Ui("Game-Hook-Modus: Desktop-Docking pausiert, Injection-Werkzeuge aktiv.", "Game-hook mode: desktop docking paused, injection tools active.");

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
                btnHookInject.IsEnabled = !desktopMode;
                SyncGameHookRuntimeUi();
            }

            overlayModeCombo.SelectionChanged += (_, _) =>
            {
                if (isSyncingOverlayModeCombo)
                {
                    return;
                }

                try
                {
                    _overlayMode = overlayModeCombo.SelectedIndex == 1
                        ? OverlayModeGameHook
                        : OverlayModeDesktop;

                    PersistOverlayModeSettings();
                    SyncOverlayModeComboDisplay();
                    SetEdgeDockEnabled(_edgeDockEnabled);
                    ApplyOverlayModeRuntimeState();
                    SyncOverlayModeUi();
                    UpdateCornerHintWindow();
                    PersistCurrentState();
                    Refresh();
                }
                catch (Exception ex)
                {
                    gameHookStatusLbl.Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 130));
                    gameHookStatusLbl.Text = Ui("Moduswechsel fehlgeschlagen: ", "Mode switch failed: ") + ex.Message;
                }
            };

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
                    _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput;
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
                var helperX86 = Path.Combine(resolvedAssets, "injector_helper.exe");
                var helperX64 = Path.Combine(resolvedAssets, "injector_helper.x64.exe");
                var dllX86 = Path.Combine(resolvedAssets, "n_overlay.dll");
                var dllX64 = Path.Combine(resolvedAssets, "n_overlay.x64.dll");

                var ok = File.Exists(helperX86) && File.Exists(helperX64) && File.Exists(dllX86) && File.Exists(dllX64);
                var suggestions = GameHookBridge.SuggestTopWindowTitles(gameHookTitleBox.Text, 3);
                var suggestionText = suggestions.Count > 0
                    ? Ui(" Fenster: ", " Windows: ") + string.Join(" | ", suggestions)
                    : string.Empty;

                gameHookStatusLbl.Foreground = ok
                    ? new SolidColorBrush(_activeGlassAccent)
                    : new SolidColorBrush(Color.FromRgb(255, 130, 130));
                gameHookStatusLbl.Text = ok
                    ? $"{Ui("Assets gefunden", "Assets found")}: {resolvedAssets}.{suggestionText}"
                    : $"{Ui("Assets unvollstaendig", "Assets incomplete")}: {resolvedAssets}.{suggestionText}";
            };

            btnHookInject.Click += (_, _) =>
            {
                if (!IsDesktopOverlayMode() && _gameHookRuntime?.IsRunning != true)
                {
                    _ = StartGameHookRuntime();
                }

                var resolvedAssets = ResolveGameHookAssetsPath(gameHookAssetsBox.Text);
                var result = GameHookBridge.InjectByWindowTitle(gameHookTitleBox.Text.Trim(), resolvedAssets);
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
            sp.Children.Add(chkCustomGlassColor);

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
            sp.Children.Add(customGlassColorRow);

            var customGlassRgbLbl = new TextBlock
            {
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            sp.Children.Add(customGlassRgbLbl);

            var customGlassRedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customGlassRedSlider);
            AttachOptionLegend(customGlassRedSlider, "Rot-Anteil fuer die benutzerdefinierte Glasfarbe.");
            sp.Children.Add(customGlassRedSlider);

            var customGlassGreenSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customGlassGreenSlider);
            AttachOptionLegend(customGlassGreenSlider, "Gruen-Anteil fuer die benutzerdefinierte Glasfarbe.");
            sp.Children.Add(customGlassGreenSlider);

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
            sp.Children.Add(customGlassBlueSlider);

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
            sp.Children.Add(chkCustomBorderColor);

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
            sp.Children.Add(customBorderColorRow);

            var customBorderRgbLbl = new TextBlock
            {
                Foreground = new SolidColorBrush(_activeGlassAccent),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
            sp.Children.Add(customBorderRgbLbl);

            var customBorderRedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customBorderRedSlider);
            AttachOptionLegend(customBorderRedSlider, "Rot-Anteil fuer die benutzerdefinierte Randfarbe.");
            sp.Children.Add(customBorderRedSlider);

            var customBorderGreenSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 1,
                IsSnapToTickEnabled = false
            };
            ApplySliderTheme(customBorderGreenSlider);
            AttachOptionLegend(customBorderGreenSlider, "Gruen-Anteil fuer die benutzerdefinierte Randfarbe.");
            sp.Children.Add(customBorderGreenSlider);

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
            sp.Children.Add(customBorderBlueSlider);

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
            sp.Children.Add(strengthSlider);
            sp.Children.Add(strengthLbl);

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
            sp.Children.Add(transparencySlider);
            sp.Children.Add(transparencyLbl);

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
            sp.Children.Add(borderThicknessSlider);
            sp.Children.Add(borderThicknessLbl);

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
            sp.Children.Add(borderSmudgeSlider);
            sp.Children.Add(borderSmudgeLbl);

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
                ApplyComboTheme(overlayModeCombo);
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

            var glassPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(34)
            };

            void CommitGlassPreview()
            {
                PersistGlassSettings();
                ApplyGlassTheme();
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

        private static void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopRefreshLoop();
            if (_sessionEventRefreshTimer != null)
            {
                _sessionEventRefreshTimer.Stop();
                _sessionEventRefreshTimer = null;
            }
            StopCornerRevealLoop();
            StopGameHookAutoDetectLoop();

            if (_settingsWin != null)
            {
                _settingsWin.Close();
                _settingsWin = null;
            }
            CloseActiveAppPopup();
            _settingsGameHookStatusTxt = null;
            CloseCornerHintWindow();
            StopGameHookRuntime();
            _gameHookRuntime?.Dispose();
            _gameHookRuntime = null;

            FlushPendingPersistCurrentState();
            WriteCurrentStateToSettings();

            _ctrl?.Shutdown();
            DisposeTrayIcon();
            CleanupSingleInstanceResources();
        }

        private static void QueuePersistCurrentState()
        {
            _persistPending = true;

            var dispatcher = _mainWin?.Dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                PersistCurrentState();
                _persistPending = false;
                return;
            }

            if (_persistDebounceTimer == null)
            {
                _persistDebounceTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(220),
                    DispatcherPriority.Background,
                    (_, _) =>
                    {
                        _persistDebounceTimer?.Stop();
                        if (!_persistPending)
                        {
                            return;
                        }

                        _persistPending = false;
                        PersistCurrentState();
                    },
                    dispatcher);
            }

            _persistDebounceTimer.Stop();
            _persistDebounceTimer.Start();
        }

        private static void FlushPendingPersistCurrentState()
        {
            if (_persistDebounceTimer != null)
            {
                _persistDebounceTimer.Stop();
            }

            if (_persistPending)
            {
                _persistPending = false;
                PersistCurrentState();
            }
        }

        private static void PersistCurrentState()
        {
            if (_ctrl == null)
            {
                return;
            }

            WriteCurrentStateToSettings();
            _ctrl.SaveState();
        }

        private static void WriteCurrentStateToSettings()
        {
            if (_ctrl == null || _mainWin == null)
            {
                return;
            }

            var ws = _ctrl.Settings.Window;
            var dockWorkArea = GetMainWorkAreaOrVirtual();
            _edgeDockSide = NormalizeDockSide(_edgeDockSide);
            ws.X = _edgeDockActive
                ? (_edgeDockSide == "left" ? dockWorkArea.Left : dockWorkArea.Right - _mainWin.Width)
                : _mainWin.Left;
            ws.Y = _mainWin.Top;
            ws.Width = _mainWin.Width;
            ws.Height = _mainWin.Height;
            ws.IsDocked = _edgeDockEnabled;
            ws.DockSide = _edgeDockSide;
            ws.DockVisibleWidth = _edgeDockVisibleWidth;
            ws.DockRevealZoneWidth = _edgeDockRevealZoneWidth;
            ws.CornerSnapAnchor = SerializeCornerSnapAnchor(AlignCornerToDockSide(_rememberedSnapCorner, _edgeDockSide));
            ws.AlwaysOnTop = _mainWin.Topmost;

            _ctrl.Settings.Ui.VisibleApps = _visibleApps;
            _ctrl.Settings.Ui.RefreshIntervalMs = _refreshIntervalMs;
            _ctrl.Settings.Ui.BringToFrontOnHover = _bringToFrontOnHover;
            _ctrl.Settings.Ui.CornerRevealEnabled = _cornerRevealEnabled;
            _ctrl.Settings.Ui.AutoApplyToAllNewApps = _autoApplyToAllNewApps;
            _ctrl.Settings.Ui.AutoVolumePercent = _autoVolumePercent;
            _ctrl.Settings.Ui.AutoLimitMaxInstallAgeDays = _autoLimitMaxInstallAgeDays;
            _ctrl.Settings.Ui.OverlayMode = _overlayMode;
            _ctrl.Settings.Ui.Language = _uiLanguage;
            _ctrl.Settings.Ui.CornerHintShowDot = _cornerHintShowDot;
            _ctrl.Settings.Ui.CornerHintShowValue = _cornerHintShowValue;
            _ctrl.Settings.Ui.CornerHintValueColor = NormalizeCornerHintValueColor(_cornerHintValueColor);
            _ctrl.Settings.Ui.CornerHintUseCustomValueColor = _cornerHintUseCustomValueColor;
            _ctrl.Settings.Ui.CornerHintCustomValueColorHex = NormalizeHexColor(_cornerHintCustomValueColorHex, Color.FromRgb(245, 252, 255));
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

            _ctrl.Settings.GameHook ??= new MiniMixerOverlay.Core.Interfaces.GameHookSettings();
            _ctrl.Settings.GameHook.AssetsPath = _gameHookAssetsPath;
            _ctrl.Settings.GameHook.TargetWindowTitle = _gameHookTargetWindowTitle;
            _ctrl.Settings.GameHook.ForwardInputToOverlay = _gameHookForwardInput;
            _ctrl.Settings.GameHook.AutoStartRuntime = _gameHookAutoStartRuntime;
        }

        private static double ComputeHeight(int visibleApps)
        {
            if (visibleApps <= 0)
            {
                return 42 + 28 + 8;
            }

            return 42 + 28 + (_cardH * visibleApps) + 16;
        }

        private static Border BuildGlass()
        {
            var o = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(166, 162, 206, 236))
            };

            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(184, 28, 40, 66), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(142, 16, 24, 44), 1));
            o.Background = bg;
            o.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.42,
                BlurRadius = 36
            };

            return o;
        }

        private static Border BuildGlassPanel()
        {
            var p = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(152, 156, 198, 226))
            };

            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(170, 24, 36, 60), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(146, 16, 25, 44), 1));
            p.Background = bg;
            p.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.38,
                BlurRadius = 30
            };

            return p;
        }

        private static void EnableNoActivateWindowBehavior(Window? window)
        {
            if (window == null)
            {
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            HwndSource.FromHwnd(handle)?.AddHook(NoActivateWindowHook);

            var exStyle = GetWindowLongPtr(handle, GwlExStyle);
            var styleValue = exStyle.ToInt64() | WsExNoActivate;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(styleValue));

            SetWindowPos(
                handle,
                window.Topmost ? HwndTopmost : HwndTop,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        private static IntPtr NoActivateWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmMouseActivate)
            {
                handled = true;
                return new IntPtr(MaNoActivate);
            }

            return IntPtr.Zero;
        }

        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;
        private const int GwlExStyle = -20;
        private const long WsExNoActivate = 0x08000000L;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const int SwShow = 5;
        private const int SwShowNoActivate = 4;
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private static readonly IntPtr HwndTopmost = new(-1);

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int CbSize;
            public NativeRect RcMonitor;
            public NativeRect RcWork;
            public uint DwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void ApplyScrollBarStyle(Application app)
        {
            try
            {
                const string xaml = @"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

    <Style x:Key='GlassThumb' TargetType='Thumb'>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='3' Background='#9000D4FF' Margin='1,3,1,3'/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key='GlassPageBtn' TargetType='RepeatButton'>
        <Setter Property='IsTabStop' Value='false'/>
        <Setter Property='Focusable' Value='false'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='RepeatButton'>
                    <Border Background='Transparent'/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='ScrollBar'>
        <Setter Property='Width' Value='6'/>
        <Setter Property='MinWidth' Value='6'/>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='ScrollBar'>
                    <Border Background='#18001A2E' CornerRadius='3' Width='6' Margin='2,4,2,4'>
                        <Track Name='PART_Track' IsDirectionReversed='True'>
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Style='{StaticResource GlassPageBtn}'
                                              Command='ScrollBar.PageUpCommand'/>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style='{StaticResource GlassThumb}'/>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Style='{StaticResource GlassPageBtn}'
                                              Command='ScrollBar.PageDownCommand'/>
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>";

                var dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                app.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScrollBar style] {ex.Message}");
            }
        }

    }
}

