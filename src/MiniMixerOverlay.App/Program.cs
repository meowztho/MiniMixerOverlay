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
    public static partial class Program
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
        private static HwndSource? _toggleHotkeySource;
        private static bool _toggleHotkeyRegistered;
        private static string _toggleHotkeyError = string.Empty;
        private static bool _persistPending;
        private static bool _startupInitializationQueued;
        private static string? _isolatedReviewRoot;

        private static bool _isRefreshing;
        private static bool _isUserInteracting;
        private static bool _isSessionRefreshFromUiPoll;
        private static bool _bringToFrontOnHover = true;
        private static bool _cornerRevealEnabled = true;
        private static bool _cornerRevealVisible = true;
        // Explicit minus-collapse must not immediately reopen because the
        // cursor is still over the former main-window rectangle.
        private static bool _edgeDockEnabled;
        private static bool _edgeDockActive;
        private static string _edgeDockSide = "right";
        private static int _edgeDockVisibleWidth = 72;
        private static int _edgeDockRevealZoneWidth = 72;
        private static bool _edgeDockExpanded = true;
        private static bool _autoApplyToAllNewApps = true;
        private static int _autoVolumePercent = 5;
        private static int _autoLimitMaxInstallAgeDays = 7;
        private static string _overlayMode = OverlayModeGameHook;
        private static string _uiLanguage = UiLanguageGerman;
        private static string _gameHookAssetsPath = DefaultGameHookAssetsRelativePath;
        private static string _gameHookTargetWindowTitle = string.Empty;
        private static bool _gameHookForwardInput;
        private static bool _gameHookAutoStartRuntime = true;
        // Explicit safe live diagnostic mode: attach only the existing WPF
        // IPC host to an independently pre-authorized graphics hook.
        private static bool _gameHookIpcOnlyTestRun;
        private static bool _gameHookIpcSphereInputTest;
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
        private static int _cornerHintTransparencyPercent;
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
        private static bool _cornerHintManualPosition;
        private static double _cornerHintManualX = double.NaN;
        private static double _cornerHintManualY = double.NaN;
        private static bool _cornerHintDragging;
        private static Point _cornerHintDragOffset;
        private static ScreenCorner _rememberedSnapCorner = ScreenCorner.TopRight;
        private static DateTime _foregroundPriorityWarmupUntilUtc = DateTime.MinValue;
        private static DateTime _lastForegroundProbeUtc = DateTime.MinValue;
        private static DateTime _startupFastRefreshUntilUtc = DateTime.MinValue;
        private static string _lastNonSystemForegroundExePath = string.Empty;
        private static DateTime _lastNonSystemForegroundSeenUtc = DateTime.MinValue;

        private const double CardRowPitch = 68;
        private const double CardVisualHeight = 62;
        private const double DesktopHeaderHeight = 42;
        private const double DesktopStatusHeight = 28;
        private const int CornerRevealTickMs = 80;
        private const int CornerRevealHotZonePx = 34;
        // The sphere uses its own tight hit zone, never the desktop dock reveal slider.
        private const double CornerHintHoverPadding = 7;
        private const int CornerRevealHoldMs = 1600;
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
        private const string DefaultGameHookAssetsRelativePath = @"native\GameHook\candidate";
        private const string PreviousGameHookAssetsRelativePath = @"native\GameHook\prebuilt";
        private const string LegacyGameHookAssetsRelativePath = @"goverlay-master\goverlay-master\game-overlay\prebuilt";
        private const double CornerHintDiameter = 24;
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

        // Validate before mutex, tray, HWND and SourceInitialized/native IPC.
        // Review mode selects paths for the existing canonical stores only.
        private static bool TryConfigureIsolatedReviewProfile(string[] args)
        {
            _isolatedReviewRoot = null;
            const string prefix = "--isolated-review-profile=";
            var reviewArgs = args.Skip(1)
                .Where(arg => arg.StartsWith("--isolated-review-profile", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (reviewArgs.Length == 0) return true;
            if (reviewArgs.Length != 1 ||
                !reviewArgs[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                args.Skip(1).Any(arg =>
                    string.Equals(arg, "--native-hook-preview", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--graphics-ipc-", StringComparison.OrdinalIgnoreCase)))
                return false;

            try
            {
                var requested = reviewArgs[0][prefix.Length..];
                if (!Path.IsPathFullyQualified(requested)) return false;
                var path = Path.GetFullPath(requested);
                var roaming = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                var local = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                static bool Inside(string path, string parent) =>
                    path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) +
                        Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                // A uniquely named Temp subfolder is safe; the actual product
                // profile roots are not valid review destinations.
                if (Inside(path, Path.Combine(roaming, "MiniMixerOverlay")) ||
                    Inside(path, Path.Combine(local, "MiniMixerOverlay")) ||
                    path.Equals(roaming, StringComparison.OrdinalIgnoreCase) ||
                    path.Equals(local, StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(path) ||
                    !File.Exists(Path.Combine(path, "review.sentinel")) ||
                    !File.Exists(Path.Combine(path, "settings.json")))
                    return false;
                // Do not follow junctions/symlinks into the real user profile.
                for (var folder = new DirectoryInfo(path); folder != null; folder = folder.Parent)
                {
                    if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                        return false;
                }
                foreach (var target in new[] {
                    "review.sentinel", "settings.json", "rules.json",
                    Path.Combine("logs", "guard.log"), Path.Combine("logs", "runtime-errors.log"),
                    "logs" })
                {
                    var candidate = Path.Combine(path, target);
                    if ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                        (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                        return false;
                }
                _isolatedReviewRoot = path;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [STAThread]
        public static void Main()
        {
            if (!TryConfigureIsolatedReviewProfile(Environment.GetCommandLineArgs()))
            {
                Environment.ExitCode = 2;
                return;
            }
            _gameHookIpcOnlyTestRun = Array.Exists(Environment.GetCommandLineArgs(),
                arg => string.Equals(arg, "--graphics-ipc-only", StringComparison.OrdinalIgnoreCase));
            _gameHookIpcSphereInputTest = _gameHookIpcOnlyTestRun &&
                Array.Exists(Environment.GetCommandLineArgs(),
                    arg => string.Equals(arg, "--graphics-ipc-enable-sphere-input",
                                         StringComparison.OrdinalIgnoreCase));
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
                InitializeToggleHotkey();
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
                MinimizeMainOverlay();
            };
            _mainWin.MouseEnter += (_, _) =>
            {
                // Record the foreground audio process before overlay interaction.
                CaptureForegroundAudioContext();
                _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
                if (_bringToFrontOnHover)
                {
                    // Raising the overlay should not steal the foreground audio process.
                    BringMainWindowToFront(activate: false);
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
                var logDir = _isolatedReviewRoot == null
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MiniMixerOverlay", "logs")
                    : Path.Combine(_isolatedReviewRoot, "logs");
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
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DesktopHeaderHeight) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DesktopStatusHeight) });
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
                    MinimizeMainOverlay();
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

        private static void MinimizeMainOverlay()
        {
            if (_mainWin == null) return;

            PersistCurrentState();
            // Minus and automatic hover-out share the same collapse transition.
            _cornerRevealVisibleUntilUtc = DateTime.MinValue;
            _isUserInteracting = false;
            ApplyCornerRevealVisualState(false);
        }

        private static void ToggleMainWindowVisibility()
        {
            if (_mainWin == null)
            {
                return;
            }

            if (_cornerRevealVisible && _mainWin.IsVisible)
            {
                MinimizeMainOverlay();
                return;
            }

            ApplyOverlayModeRuntimeState();
            _cornerRevealVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(CornerRevealHoldMs);
            ApplyCornerRevealVisualState(true);
            RestoreDockPositionIfEnabled(expand: true);
            BringMainWindowToFront(activate: false);
            UpdateCornerHintWindow();
        }

        // Opt-in keyboard shortcut shares the exact tray-toggle command path.
        // Windows owns shortcut arbitration; a conflict is visible, never silently overridden.
        private const int ToggleHotkeyId = 0x4D31;
        private const int WmHotkey = 0x0312;
        private const uint HotkeyCtrlAltNoRepeat = 0x4003;

        private static uint ResolveToggleHotkeyKey(string? key) => key?.ToUpperInvariant() switch
        {
            "M" => 0x4D,
            "O" => 0x4F,
            "F9" => 0x78,
            "F10" => 0x79,
            _ => 0
        };

        private static void InitializeToggleHotkey()
        {
            if (_mainWin == null) return;
            var hwnd = new WindowInteropHelper(_mainWin).Handle;
            if (hwnd == IntPtr.Zero) return;
            _toggleHotkeySource = HwndSource.FromHwnd(hwnd);
            _toggleHotkeySource?.AddHook(ToggleHotkeyWindowHook);
            RebindToggleHotkey();
        }

        private static void RebindToggleHotkey()
        {
            var source = _toggleHotkeySource;
            if (source == null) return;
            if (_toggleHotkeyRegistered)
            {
                if (!UnregisterHotKey(source.Handle, ToggleHotkeyId))
                {
                    _toggleHotkeyError = Ui("Hotkey konnte nicht freigegeben werden", "Could not release hotkey") +
                                         $" (Win32 {Marshal.GetLastWin32Error()})";
                    return; // Never register a replacement while the previous key may still be owned.
                }
                _toggleHotkeyRegistered = false;
            }
            _toggleHotkeyError = string.Empty;
            var enabled = _ctrl?.Settings.Ui.ToggleHotkeyEnabled == true;
            if (!enabled) return;
            var key = ResolveToggleHotkeyKey(_ctrl?.Settings.Ui.ToggleHotkeyKey);
            if (key == 0)
            {
                _toggleHotkeyError = Ui("Ungültiger Hotkey", "Invalid hotkey");
                return;
            }
            if (!RegisterHotKey(source.Handle, ToggleHotkeyId, HotkeyCtrlAltNoRepeat, key))
            {
                var error = Marshal.GetLastWin32Error();
                _toggleHotkeyError = error == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                    ? Ui("Hotkey bereits belegt", "Hotkey already in use")
                    : Ui("Hotkey-Registrierung fehlgeschlagen", "Hotkey registration failed") + $" (Win32 {error})";
                return;
            }
            _toggleHotkeyRegistered = true;
        }

        private static IntPtr ToggleHotkeyWindowHook(
            IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt64() == ToggleHotkeyId &&
                _toggleHotkeyRegistered)
            {
                ToggleMainWindowVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void StopToggleHotkey()
        {
            if (_toggleHotkeySource == null) return;
            if (_toggleHotkeyRegistered)
                UnregisterHotKey(_toggleHotkeySource.Handle, ToggleHotkeyId);
            _toggleHotkeyRegistered = false;
            _toggleHotkeySource.RemoveHook(ToggleHotkeyWindowHook);
            _toggleHotkeySource = null;
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
                    // A second-instance activation must not silently promote
                    // a user-selected non-topmost mixer into the topmost band.
                    // HWND_TOP raises within the existing band.
                    HwndTop,
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
                // Preflight completed before Program.Main created any product runtime.
                var reviewRoot = _isolatedReviewRoot;
                var audio = new MiniMixerOverlay.Infrastructure.Audio.NAudioSessionManager();
                var rules = new MiniMixerOverlay.Infrastructure.Persistence.JsonRuleStore(
                    reviewRoot == null ? null : Path.Combine(reviewRoot, "rules.json"));
                var settings = new MiniMixerOverlay.Infrastructure.Persistence.JsonSettingsStore(
                    reviewRoot == null ? null : Path.Combine(reviewRoot, "settings.json"));
                settings.Load();
                // Audio-write isolation is immutable at the controller boundary,
                // independent of game classification and later UI configuration.

                _autoApplyToAllNewApps = settings.Settings.Ui.AutoApplyToAllNewApps;
                _autoVolumePercent = (int)Math.Clamp(settings.Settings.Ui.AutoVolumePercent, 1, 100);
                _autoLimitMaxInstallAgeDays = (int)Math.Clamp(settings.Settings.Ui.AutoLimitMaxInstallAgeDays, 1, 365);
                _overlayMode = NormalizeOverlayMode(settings.Settings.Ui.OverlayMode);
                _uiLanguage = NormalizeUiLanguage(settings.Settings.Ui.Language);
                _cornerHintShowDot = settings.Settings.Ui.CornerHintShowDot;
                _cornerHintTransparencyPercent = Math.Clamp(settings.Settings.Ui.CornerHintTransparencyPercent, 0, 80);
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
                _gameHookAssetsPath = NormalizeHookAssetsPath(settings.Settings.GameHook?.AssetsPath);
                _gameHookTargetWindowTitle = settings.Settings.GameHook?.TargetWindowTitle ?? string.Empty;
                _gameHookForwardInput = settings.Settings.GameHook?.ForwardInputToOverlay ?? false;
                _gameHookAutoStartRuntime = settings.Settings.GameHook?.AutoStartRuntime ?? true;
                _cornerHintManualPosition = settings.Settings.Window.CornerHintManualPosition;
                _cornerHintManualX = settings.Settings.Window.CornerHintX ?? double.NaN;
                _cornerHintManualY = settings.Settings.Window.CornerHintY ?? double.NaN;
                _guard = new GuardEngine(
                    rules,
                    new SessionClassifier(),
                    _autoVolumePercent,
                    _autoApplyToAllNewApps,
                    _autoLimitMaxInstallAgeDays);

                _ctrl = new MixerController(audio, _guard, new SessionClassifier(), rules, settings,
                    guardLogFilePath: reviewRoot == null ? null :
                        Path.Combine(reviewRoot, "logs", "guard.log"),
                    allowAudioWrites: reviewRoot == null);
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
                    _cornerHintTransparencyPercent = Math.Clamp(_ctrl.Settings.Ui.CornerHintTransparencyPercent, 0, 80);
                    _cornerHintShowValue = _ctrl.Settings.Ui.CornerHintShowValue;
                    _cornerHintValueColor = NormalizeCornerHintValueColor(_ctrl.Settings.Ui.CornerHintValueColor);
                    _cornerHintUseCustomValueColor = _ctrl.Settings.Ui.CornerHintUseCustomValueColor;
                    _cornerHintCustomValueColorHex = NormalizeHexColor(_ctrl.Settings.Ui.CornerHintCustomValueColorHex, Color.FromRgb(245, 252, 255));
                    _gameHookAssetsPath = NormalizeHookAssetsPath(_ctrl.Settings.GameHook?.AssetsPath);
                    _gameHookTargetWindowTitle = _ctrl.Settings.GameHook?.TargetWindowTitle ?? string.Empty;
                    _gameHookForwardInput = _ctrl.Settings.GameHook?.ForwardInputToOverlay ?? false;
                    _gameHookAutoStartRuntime = _ctrl.Settings.GameHook?.AutoStartRuntime ?? true;
                    _cornerHintManualPosition = ws.CornerHintManualPosition;
                    _cornerHintManualX = ws.CornerHintX ?? double.NaN;
                    _cornerHintManualY = ws.CornerHintY ?? double.NaN;
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
                RebindToggleHotkey();
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
            // Auto-injection has no use in the normal WPF-only product.
            // Avoid waking the Dispatcher every 1.25 seconds just to return.
            if (!GameHookBridge.NativePreviewEnabled)
            {
                StopGameHookAutoDetectLoop();
                return;
            }

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
                return false;
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
                    if (entry.Rule?.Classification == AppClassification.Game &&
                        entry.Sessions.Any(s => s.ProcessId == info.ProcessId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool AreGameHookAssetsAvailable(string resolvedAssetsPath)
        {
            // Preview assets describe a native graphics implementation,
            // not a ready-to-inject product backend.
            return File.Exists(Path.Combine(resolvedAssetsPath, "MiniMixerLivePresent.x64.dll")) &&
                   !File.Exists(Path.Combine(resolvedAssetsPath, "n_overlay.x64.dll"));
        }

        private static async void TryAutoInjectForegroundGame()
        {
            // The archived GOverlay helper cannot be selected by either
            // automatic or manual product paths. This native candidate is
            // NOT yet an authorized unattended injector.
            if (_gameHookIpcOnlyTestRun || !GameHookBridge.NativePreviewEnabled) return;
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

            // Preview consent is limited to the named test game and own fixture.
            // Manual attach is guarded again by the native loader.
            if (!NativeGameHookLoader.IsApprovedPreviewProcessName(foreground.ProcessName))
                return;

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
            var nativeGeneration = Interlocked.Read(ref _nativeRuntimeGeneration);
            try
            {
                result = await Task.Run(() => GameHookBridge.InjectQualifiedForegroundWindowGated(
                    foreground, resolvedAssets,
                    () => Interlocked.Read(ref _nativeRuntimeGeneration) == nativeGeneration));
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
                if (result.Success) _ = NativeGameHookLoader.RequestStop(result.ProcessId);
                return;
            }

            if (result.Success)
            {
                _gameHookInjectedProcessIds.Add(result.ProcessId);
                _gameHookRetryAfterUtcByPid.Remove(result.ProcessId);
                SetGameHookStatus($"{Ui("Native DLL geladen fur", "Auto-hook active for")} {foreground.ProcessName} (PID {result.ProcessId}).");
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
            // Existing user profiles are migrated to the only supported mode.
            return OverlayModeGameHook;
        }

        private static string NormalizeHookAssetsPath(string? configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) ||
                string.Equals(configuredPath.Trim(), LegacyGameHookAssetsRelativePath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(configuredPath.Trim(), PreviousGameHookAssetsRelativePath,
                    StringComparison.OrdinalIgnoreCase))
                return DefaultGameHookAssetsRelativePath;
            return configuredPath.Trim();
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
        private static bool IsMasterVolumeEntry(AppEntry entry)
        {
            // Only the canonical endpoint master. Never identify controls
            // by localized label or the independent System Sounds session.
            return string.Equals(entry.ExePath, "__system_master__", StringComparison.OrdinalIgnoreCase);
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
            var path = NormalizeHookAssetsPath(configuredPath);

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            // The packaged default hook assets are anchored to the executable directory.
            // Preserve existing semantics for explicit user-configured relative paths.
            if (string.Equals(path, DefaultGameHookAssetsRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                var bundledPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
                if (Directory.Exists(bundledPath))
                {
                    return bundledPath;
                }
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
            _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput || _gameHookIpcSphereInputTest;
            _gameHookRuntime.ForwardNativeMainInput = _gameHookForwardInput;
            // Optional auxiliary green-sphere framebuffer is advertised only
            // to native clients with explicit experimental capability.
            _gameHookRuntime.SetAuxiliaryWindow(_cornerHintWin);
        }

        private static bool StartGameHookRuntime()
        {
            // Isolated WPF review must never start native IPC, even from settings.
            if (_isolatedReviewRoot != null) return false;
            if (_mainWin == null)
            {
                return false;
            }

            EnsureGameHookRuntimeCreated();
            if (_gameHookRuntime == null)
            {
                return false;
            }

            _gameHookRuntime.ForwardGameInputToWindow = _gameHookForwardInput || _gameHookIpcSphereInputTest;
            _gameHookRuntime.ForwardNativeMainInput = _gameHookForwardInput;
            if (_gameHookRuntime.IsRunning)
            {
                if (_gameHookInputInterceptEnabled)
                {
                    _gameHookRuntime.SendInputIntercept(true);
                }

                return true;
            }

            var started = _gameHookRuntime.Start();
            if (started)
                NativeGameHookLoader.ResumeAfterRuntimeStart();
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

        private static void SignalNativePreviewStop()
        {
            // Includes ambiguous/timed-out remote threads omitted from the
            // UI success list. Stop intent must survive a late native Ready.
            NativeGameHookLoader.StopAllStartedAttempts();
            foreach (var pid in _gameHookInjectedProcessIds)
                _ = NativeGameHookLoader.RequestStop(pid);
        }

        private static long _nativeRuntimeGeneration;

        private static void StopGameHookRuntime()
        {
            Interlocked.Increment(ref _nativeRuntimeGeneration);
            if (GameHookBridge.NativePreviewEnabled)
                SignalNativePreviewStop();
            if (_gameHookRuntime == null)
            {
                ResetGameHookAutoDetectState();
                return;
            }

            _gameHookRuntime.Stop();
            _gameHookInputInterceptEnabled = false;
            ResetGameHookAutoDetectState();
        }

        private static bool ShouldAutoStartGameHookRuntime()
        {
            // The WPF-only MM-1 launch needs no native IPC host, frame buffers
            // or 33-ms timer. Preserve explicit native preview / IPC test opt-ins.
            return _isolatedReviewRoot == null && _gameHookAutoStartRuntime &&
                (GameHookBridge.NativePreviewEnabled || _gameHookIpcOnlyTestRun);
        }

        private static void SyncGameHookRuntimeState()
        {
            if (IsDesktopOverlayMode())
            {
                StopGameHookRuntime();
                return;
            }

            if (ShouldAutoStartGameHookRuntime())
            {
                EnsureGameHookRuntimeCreated();
                _gameHookRuntime!.ForwardGameInputToWindow = _gameHookForwardInput || _gameHookIpcSphereInputTest;
                _ = StartGameHookRuntime();
                return;
            }

            // An explicit manual start remains active until the user stops it.
            // Do not allocate native buffers just to show 'Runtime aus'.
            if (_gameHookRuntime?.IsRunning == true)
                return;

            SetGameHookStatus(Ui("Game-Hook Runtime ist aus. Starte sie manuell im Settings-Menue.", "Game-hook runtime is off. Start it manually in the settings menu."));
        }
        private static void ApplyUserTopmostPreference()
        {
            // The persisted checkbox is the only main/settings topmost owner,
            // regardless of the legacy overlay-mode normalization. The
            // independent sphere deliberately keeps its own topmost contract.
            var topmost = _ctrl?.Settings.Window.AlwaysOnTop ?? true;
            if (_mainWin != null)
                _mainWin.Topmost = topmost;
            if (_settingsWin != null)
                _settingsWin.Topmost = topmost;
            if (_cornerHintWin != null)
            {
                _cornerHintWin.Topmost = true;
                EnsureCornerHintOnTop();
            }
        }

        private static void ApplyOverlayModeRuntimeState()
        {
            if (_mainWin == null)
                return;

            ApplyUserTopmostPreference();

            if (IsDesktopOverlayMode())
            {
                StopGameHookRuntime();
                return;
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

        private static double ClampWindowTop(double desiredTop, Rect workArea, double windowHeight) =>
            Math.Clamp(desiredTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));

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

            _mainWin.Left = Math.Clamp(_mainWin.Left, minLeft, Math.Max(minLeft, maxLeft));
            _mainWin.Top = ClampWindowTop(_mainWin.Top, workArea, _mainWin.Height);
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
                    UpdateCornerHintWindow();
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

            if (!TryGetCursorPosition(out var cursor))
            {
                ApplyEdgeDockPlacement(expand: true);
                return;
            }

            UpdateEdgeDockStateAtCursor(cursor, DateTime.UtcNow);
        }

        private static void UpdateEdgeDockStateAtCursor(Point cursor, DateTime nowUtc)
        {
            if (!_edgeDockEnabled || !_edgeDockActive || _mainWin == null || _mainWin.WindowState == WindowState.Minimized)
            {
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
            _edgeDockExpanded = expand;
            ApplyContextualMainWindowHeight();

            var top = ClampWindowTop(_mainWin.Top, workArea, _mainWin.Height);
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
            ApplyContextualMainWindowHeight();

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

            // Hover/raise must respect the existing topmost preference,
            // not accidentally re-enable it in normalized gamehook mode.
            var insertAfter = _mainWin.Topmost ? HwndTopmost : HwndTop;
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
            // The independent sphere must stay above a raised mixer HWND.
            EnsureCornerHintOnTop();
        }

        private static void UpdateCornerRevealState()
        {
            if (_mainWin == null || _mainWin.WindowState == WindowState.Minimized)
            {
                UpdateCornerHintWindow();
                return;
            }

            var desktopMode = IsDesktopOverlayMode();

            if (_cornerHintDragging)
            {
                // Do not reveal the main window and hide the sphere while it is dragged.
                _cornerRevealVisibleUntilUtc = DateTime.MinValue;
                ApplyCornerRevealVisualState(false);
                UpdateCornerHintWindow();
                return;
            }

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
                    UpdateCornerHintWindow();
                    UpdateEdgeDockState();
                    return;
                }
            }

            if (!_cornerRevealEnabled)
            {
                // Do not undo explicit Minus when corner reveal is disabled.
                // Reopen a collapsed mixer on the sphere, no secondary state.
                if (!_cornerRevealVisible &&
                    TryGetCursorPosition(out var directCursor) &&
                    IsCursorInRevealHotZone(directCursor))
                {
                    ApplyCornerRevealVisualState(true);
                }
                UpdateCornerHintWindow();
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (!TryGetCursorPosition(out var cursor))
            {
                // Missing cursor data does not imply a hover.
                ApplyCornerRevealVisualState(_settingsWin != null || _isUserInteracting);
                UpdateCornerHintWindow();
                return;
            }

            var cursorInsideMainWindow = IsCursorInsideWindow(cursor, _mainWin);
            var keepVisibleByWindowHover = _cornerRevealVisible && cursorInsideMainWindow;

            // The main-window rectangle retains an already visible mixer,
            // but must never reopen an invisible one. Reopen only via sphere.
            if (keepVisibleByWindowHover || IsCursorInsideWindow(cursor, _settingsWin) || _isUserInteracting || _settingsWin != null)
            {
                _cornerRevealVisibleUntilUtc = nowUtc.AddMilliseconds(CornerRevealHoldMs);
            }

            if (IsCursorInRevealHotZone(cursor))
            {
                _cornerRevealVisibleUntilUtc = nowUtc.AddMilliseconds(CornerRevealHoldMs);
                if (_bringToFrontOnHover && !_cornerRevealVisible && _settingsWin == null)
                {
                    BringMainWindowToFront(activate: false);
                }
            }

            var shouldBeVisible = _settingsWin != null || _isUserInteracting || nowUtc <= _cornerRevealVisibleUntilUtc;
            ApplyCornerRevealVisualState(shouldBeVisible);
            UpdateCornerHintWindow();
        }

        private static void ApplyCornerRevealVisualState(bool visible)
        {
            if (_mainWin == null) return;

            _cornerRevealVisible = visible;
            if (IsDesktopOverlayMode())
            {
                // A transparent WPF HWND still interferes with desktop hover.
                // Hide only the desktop main, never the independently visible sphere.
                if (visible)
                {
                    _mainWin.Opacity = 1.0;
                    _mainWin.IsHitTestVisible = true;
                    if (!_mainWin.IsVisible) _mainWin.Show();
                }
                else
                {
                    _mainWin.IsHitTestVisible = false;
                    if (_mainWin.IsVisible) _mainWin.Hide();
                }
            }
            else
            {
                // Game-Hook uses the WPF render surface; keep its HWND alive.
                _mainWin.Opacity = visible ? 1.0 : 0.0;
                _mainWin.IsHitTestVisible = visible;
            }

            UpdateCornerHintWindow();
            EnsureCornerHintOnTop();
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
                var activationRadius = (CornerHintDiameter / 2.0) + CornerHintHoverPadding;
                return DistanceSquared(cursor, dotCenter) <= activationRadius * activationRadius;
            }

            if (!IsDesktopOverlayMode())
            {
                var hookCorner = ResolveCornerHintCorner(workArea);
                var hookPosition = GetCornerHintPosition(workArea, hookCorner, CornerHintDiameter, CornerHintDiameter);
                var hookCenter = new Point(hookPosition.X + (CornerHintDiameter / 2.0), hookPosition.Y + (CornerHintDiameter / 2.0));
                var activationRadius = (CornerHintDiameter / 2.0) + CornerHintHoverPadding;
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

            // The desktop sphere is an independently configured WPF control,
            // not the visibility indicator for the larger reveal/dock window.
            // Keep the established game-hook visibility gates unchanged.
            if (_mainWin == null || (!_cornerHintShowDot && !_cornerHintShowValue))
            {
                HideCornerHintWindow();
                return;
            }

            // A transient monitor/window transition must not lose the sphere.
            if (!TryGetMonitorWorkAreaForOverlayTarget(out var workArea))
                workArea = GetMainWorkAreaOrVirtual();

            var corner = ResolveCornerHintCorner(workArea);

            EnsureCornerHintWindow();
            if (_cornerHintWin == null)
            {
                return;
            }

            // Settings/mode transitions can change permitted mouse interaction.
            var mayInteract = CanInteractWithCornerHint();
            _cornerHintWin.IsHitTestVisible = mayInteract;
            if (_cornerHintWin.Content is UIElement sphereRoot)
            {
                sphereRoot.IsHitTestVisible = mayInteract;
            }
            UpdateCornerHintVolumeLabel();

            var hintWidth = _cornerHintWin.Width > 1 ? _cornerHintWin.Width : CornerHintDiameter;
            var hintHeight = _cornerHintWin.Height > 1 ? _cornerHintWin.Height : CornerHintDiameter;
            var position = _cornerHintManualPosition
                ? GetValidatedManualCornerHintPosition(hintWidth, hintHeight)
                : GetCornerHintPosition(workArea, corner, hintWidth, hintHeight);

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

        private static Point GetValidatedManualCornerHintPosition(double hintWidth, double hintHeight)
        {
            if (!double.IsFinite(_cornerHintManualX) || !double.IsFinite(_cornerHintManualY))
            {
                _cornerHintManualPosition = false;
                if (TryGetMonitorWorkAreaForOverlayTarget(out var fallbackArea))
                {
                    var fallbackCorner = ResolveCornerHintCorner(fallbackArea);
                    return GetCornerHintPosition(fallbackArea, fallbackCorner, hintWidth, hintHeight);
                }

                return new Point(SystemParameters.VirtualScreenLeft + CornerHintInset, SystemParameters.VirtualScreenTop + CornerHintInset);
            }

            var centerX = _cornerHintManualX + (hintWidth / 2.0);
            var centerY = _cornerHintManualY + (hintHeight / 2.0);
            var monitor = MonitorFromPoint(
                new NativePoint { X = (int)Math.Round(centerX), Y = (int)Math.Round(centerY) },
                MonitorDefaultToNearest);

            if (!TryGetWorkAreaForMonitor(monitor, out var workArea))
            {
                return new Point(_cornerHintManualX, _cornerHintManualY);
            }

            var x = Math.Clamp(_cornerHintManualX, workArea.Left, Math.Max(workArea.Left, workArea.Right - hintWidth));
            var y = Math.Clamp(_cornerHintManualY, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - hintHeight));
            _cornerHintManualX = x;
            _cornerHintManualY = y;
            return new Point(x, y);
        }

        // Optional edge magnetism. A freely placed sphere never jumps to a corner.
        private static Point SnapSphereToEdges(Point position, Rect workArea,
            double width, double height, bool enabled)
        {
            if (!enabled) return position;
            var right = Math.Max(workArea.Left, workArea.Right - width);
            var bottom = Math.Max(workArea.Top, workArea.Bottom - height);
            var x = Math.Abs(position.X - workArea.Left) <= EdgeDockSnapDistancePx
                ? workArea.Left : Math.Abs(position.X - right) <= EdgeDockSnapDistancePx ? right : position.X;
            var y = Math.Abs(position.Y - workArea.Top) <= EdgeDockSnapDistancePx
                ? workArea.Top : Math.Abs(position.Y - bottom) <= EdgeDockSnapDistancePx ? bottom : position.Y;
            return new Point(x, y);
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

        private static void CaptureForegroundAudioContext()
        {
            if (_ctrl == null || _ctrl.AppEntries.Count == 0) return;
            try
            {
                if (!GameHookBridge.TryGetForegroundWindowInfo(out var foreground, requireTitle: false) ||
                    IsOverlayForeground(foreground)) return;
                var entry = FindForegroundAudioEntry(foreground);
                if (entry != null && !IsMasterVolumeEntry(entry))
                    RememberForegroundAudioEntry(entry);
            }
            catch
            {
                // Best-effort context read; never interfere with the user's interaction.
            }
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
                if (!GameHookBridge.TryGetForegroundWindowInfo(out var foreground, requireTitle: false))
                {
                    return TryGetRememberedForegroundAudioEntry();
                }

                var entry = FindForegroundAudioEntry(foreground);
                if (entry != null && !IsMasterVolumeEntry(entry))
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
            if (entry == null || IsMasterVolumeEntry(entry) || string.IsNullOrWhiteSpace(entry.ExePath))
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
                !IsMasterVolumeEntry(entry) &&
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

        // The sphere is an ordinary desktop WPF HWND in both overlay modes.
        // Its mouse handling does not forward input into a game and must not be
        // gated by the separate native game-hook input-interception setting.
        private static bool CanInteractWithCornerHint() => true;

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
                Background = new SolidColorBrush(Color.FromArgb(224, 18, 24, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(236, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B)),
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
                IsHitTestVisible = CanInteractWithCornerHint(),
                Cursor = Cursors.SizeAll
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

            _cornerHintWin.IsHitTestVisible = CanInteractWithCornerHint();

            root.MouseLeftButtonDown += (_, e) =>
            {
                if (_cornerHintWin == null || !CanInteractWithCornerHint() ||
                    e.ChangedButton != MouseButton.Left)
                {
                    return;
                }

                _cornerHintDragging = true;
                _isUserInteracting = true;
                if (TryGetCursorPosition(out var cursor))
                {
                    _cornerHintDragOffset = new Point(cursor.X - _cornerHintWin.Left, cursor.Y - _cornerHintWin.Top);
                }
                root.CaptureMouse();
                e.Handled = true;
            };

            root.MouseMove += (_, e) =>
            {
                if (!_cornerHintDragging || _cornerHintWin == null || e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                if (TryGetCursorPosition(out var cursor))
                {
                    _cornerHintManualPosition = true;
                    _cornerHintManualX = cursor.X - _cornerHintDragOffset.X;
                    _cornerHintManualY = cursor.Y - _cornerHintDragOffset.Y;
                    var pos = GetValidatedManualCornerHintPosition(_cornerHintWin.Width, _cornerHintWin.Height);
                    _cornerHintWin.Left = pos.X;
                    _cornerHintWin.Top = pos.Y;
                    UpdateCornerHintDotCenter();
                }

                e.Handled = true;
            };

            root.LostMouseCapture += (_, _) =>
            {
                // A native WM_CANCELMODE must terminate sphere dragging without
                // forging WM_LBUTTONUP on a control after focus/IPC loss.
                if (!_cornerHintDragging) return;
                _cornerHintDragging = false;
                _isUserInteracting = false;
                QueuePersistCurrentState();
            };

            root.MouseLeftButtonUp += (_, e) =>
            {
                if (!_cornerHintDragging)
                {
                    return;
                }

                _cornerHintDragging = false;
                _isUserInteracting = false;
                if (_cornerHintWin != null && _cornerHintManualPosition)
                {
                    var position = GetValidatedManualCornerHintPosition(_cornerHintWin.Width, _cornerHintWin.Height);
                    var monitor = MonitorFromPoint(new NativePoint
                    {
                        X = (int)Math.Round(position.X + _cornerHintWin.Width / 2),
                        Y = (int)Math.Round(position.Y + _cornerHintWin.Height / 2)
                    }, MonitorDefaultToNearest);
                    if (TryGetWorkAreaForMonitor(monitor, out var area))
                    {
                        position = SnapSphereToEdges(position, area,
                            _cornerHintWin.Width, _cornerHintWin.Height, _edgeDockEnabled);
                    }
                    _cornerHintManualX = _cornerHintWin.Left = position.X;
                    _cornerHintManualY = _cornerHintWin.Top = position.Y;
                    UpdateCornerHintDotCenter();
                }
                root.ReleaseMouseCapture();
                QueuePersistCurrentState();
                e.Handled = true;
            };


            _cornerHintWin.SourceInitialized += (_, _) =>
            {
                EnableNoActivateWindowBehavior(_cornerHintWin);
                EnsureCornerHintOnTop();
            };
            _cornerHintWin.Closed += (_, _) =>
            {
                _gameHookRuntime?.SetAuxiliaryWindow(null);
                _cornerHintWin = null;
                _cornerHintDot = null;
                _cornerHintVolumeText = null;
                _cornerHintDotCenterValid = false;
                _cornerHintDragging = false;
            };
            ApplyCornerHintTheme();
            _gameHookRuntime?.SetAuxiliaryWindow(_cornerHintWin);
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
            // SWP_SHOWWINDOW below must never unhide a deliberately disabled
            // sphere merely because the main window was raised.
            if (_cornerHintWin == null || !_cornerHintWin.IsVisible)
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
            // Card accents are snapshots made by BuildCard; rebind them during
            // live settings color previews, without refreshing real audio.
            QueueSettingsUiPreviewRefresh();
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
            if (_cornerHintWin != null)
            {
                _cornerHintWin.Opacity = 1.0 - Math.Clamp(_cornerHintTransparencyPercent, 0, 80) / 100.0;
            }

            // ZIP reference: dark glass fill and lightly accented rim.
            // Whole-window opacity is still controlled separately above.
            var hintBase = BlendColor(Color.FromRgb(14, 20, 32), _activeGlassAccent, 0.22);
            var hintRim = BlendColor(_activeGlassAccent, Colors.White, 0.18);
            _cornerHintDot.Background = new SolidColorBrush(Color.FromArgb(224, hintBase.R, hintBase.G, hintBase.B));
            _cornerHintDot.BorderBrush = new SolidColorBrush(Color.FromArgb(238, hintRim.R, hintRim.G, hintRim.B));
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

            // Visible Apps is an exact card limit, not a minimum. Icon-only
            // stays zero and the collapsed edge dock still shows one card.
            return (_edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded)
                ? 1 : configured;
        }

        private static void ApplyAppListVisibilityState()
        {
            var showCards = GetEffectiveVisibleApps() > 0;
            var compactDock = _edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded;

            // The quick dock contains only one compact card, not the desktop header/status.
            if (_mainGlassBorder?.Child is Grid layout && layout.RowDefinitions.Count == 3)
            {
                layout.RowDefinitions[0].Height = new GridLength(compactDock ? 0 : DesktopHeaderHeight);
                layout.RowDefinitions[2].Height = new GridLength(compactDock ? 0 : DesktopStatusHeight);
            }

            // On adjacent monitors a window shifted past the edge is still visible.
            // Clip the hidden side so only the interactive handle remains rendered.
            if (_mainGlassBorder != null && _mainWin != null)
            {
                if (compactDock)
                {
                    var visibleWidth = Math.Clamp(_edgeDockVisibleWidth, EdgeDockVisibleMin, EdgeDockVisibleMax);
                    var clipLeft = _edgeDockSide == "left" ? Math.Max(0, _mainWin.Width - visibleWidth) : 0;
                    _mainGlassBorder.Clip = new RectangleGeometry(new Rect(
                        clipLeft, 0, visibleWidth, _mainWin.Height));
                }
                else
                {
                    _mainGlassBorder.Clip = null;
                }
            }

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
            var suppressSliderWrite = false;
            // Popup anchors can outlive a refreshed AppEntry snapshot.
            AppEntry CurrentPopupEntry() => _ctrl?.AppEntries.FirstOrDefault(current =>
                string.Equals(current.ExePath, entry.ExePath, StringComparison.OrdinalIgnoreCase)) ?? entry;
            void ReconcilePopup(bool succeeded)
            {
                suppressSliderWrite = true;
                try
                {
                    var current = CurrentPopupEntry();
                    ReconcileAudioControlUi(current, slider, valueText, iconBorder,
                        accent, mutedBrush, current.IconBytes, failed: !succeeded);
                    muted = current.IsMuted;
                }
                finally { suppressSliderWrite = false; }
            }
            iconBorder.MouseLeftButtonUp += (_, _) =>
            {
                var current = CurrentPopupEntry();
                var accepted = _ctrl?.TrySetMute(current.ExePath, !current.IsMuted) == true;
                ReconcilePopup(accepted);
            };

            slider.GotMouseCapture += (_, _) => _isUserInteracting = true;
            slider.LostMouseCapture += (_, _) => _isUserInteracting = false;
            slider.ValueChanged += (_, _) =>
            {
                if (_isRefreshing || muted || suppressSliderWrite) return;
                var accepted = _ctrl?.TrySetVolume(entry.ExePath, (float)(slider.Value / 100.0)) == true;
                ReconcilePopup(accepted);
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

                if (!_isRefreshing)
                {
                    Refresh();
                }
            };

            _activeAppPopup = popup;
        }

        private static void UpdateActiveAppsStrip(List<AppEntry> displayedEntries)
        {
            if (_activeAppsStrip == null)
            {
                return;
            }

            // The popup is anchored to an icon in this strip. Replacing the icon
            // during a mute/unmute refresh briefly sends WPF's popup elsewhere.
            // Reuse the same element while its application and position are stable.
            var retainIcons = displayedEntries.Count > 0 &&
                _activeAppsStrip.Children.Count == displayedEntries.Count;
            if (retainIcons)
            {
                for (var i = 0; i < displayedEntries.Count; i++)
                {
                    if (_activeAppsStrip.Children[i] is not Border icon ||
                        icon.Tag is not AppEntry oldEntry ||
                        !string.Equals(oldEntry.ExePath, displayedEntries[i].ExePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        retainIcons = false;
                        break;
                    }
                }
            }

            if (retainIcons)
            {
                for (var i = 0; i < displayedEntries.Count; i++)
                {
                    var entry = displayedEntries[i];
                    var icon = (Border)_activeAppsStrip.Children[i];
                    icon.Tag = entry;
                    icon.BorderBrush = entry.IsMuted
                        ? new SolidColorBrush(Color.FromRgb(255, 107, 107))
                        : new SolidColorBrush(Color.FromArgb(110, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                    icon.ToolTip = $"{entry.DisplayName} ({(entry.IsMuted ? "Mute" : $"{entry.CombinedVolume * 100f:F0}%")})";
                    icon.Child = MakeIconElement(entry.ExePath, entry.IconBytes, entry.IsMuted);
                }
                return;
            }

            // An actual change of icon order/identity cannot keep the old anchor.
            // Close that popup deliberately before removing its placement target.
            if (_activeAppPopup is { IsOpen: true } &&
                _activeAppPopup.PlacementTarget is UIElement target &&
                _activeAppsStrip.Children.Contains(target))
            {
                CloseActiveAppPopup();
            }

            // Keep unchanged icon elements (especially pinned master) when
            // foreground order changes. Any open popup is closed above before
            // its anchor is reparented.
            var previousIcons = _activeAppsStrip.Children.OfType<Border>()
                .Where(icon => icon.Tag is AppEntry)
                .GroupBy(icon => ((AppEntry)icon.Tag).ExePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _activeAppsStrip.Children.Clear();
            foreach (var entry in displayedEntries)
            {
                var muted = entry.IsMuted;
                if (!previousIcons.TryGetValue(entry.ExePath, out var iconButton))
                {
                    iconButton = new Border
                    {
                        Width = 22,
                        Height = 22,
                        Margin = new Thickness(2, 0, 2, 0),
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromArgb(160, 20, 34, 56)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand
                    };
                    var clickTarget = iconButton;
                    iconButton.MouseLeftButtonUp += (_, _) =>
                    {
                        if (clickTarget.Tag is AppEntry current)
                            ShowActiveAppPopup(current, clickTarget);
                    };
                }

                iconButton.Tag = entry;
                iconButton.BorderBrush = muted
                    ? new SolidColorBrush(Color.FromRgb(255, 107, 107))
                    : new SolidColorBrush(Color.FromArgb(110, _activeGlassAccent.R, _activeGlassAccent.G, _activeGlassAccent.B));
                iconButton.ToolTip = $"{entry.DisplayName} ({(muted ? "Mute" : $"{entry.CombinedVolume * 100f:F0}%")})";
                iconButton.Child = MakeIconElement(entry.ExePath, entry.IconBytes, muted);
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

        private static bool IsSystemSoundsEntry(AppEntry entry) =>
            string.Equals(entry.ExePath, "__system_sound__", StringComparison.OrdinalIgnoreCase);

        private static List<AppEntry> OrderMixerEntries(IEnumerable<AppEntry> candidates, AppEntry? foreground)
        {
            var ordered = candidates
                .OrderBy(entry => IsMasterVolumeEntry(entry) ? 0 : 1)
                .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (foreground == null || IsMasterVolumeEntry(foreground) || IsSystemSoundsEntry(foreground))
            {
                return ordered;
            }

            var selected = ordered.FirstOrDefault(entry =>
                string.Equals(entry.ExePath, foreground.ExePath, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                ordered.Remove(selected);
                ordered.Insert(ordered.Any(IsMasterVolumeEntry) ? 1 : 0, selected);
            }
            return ordered;
        }

        private static List<AppEntry> OrderMainCardEntries(IEnumerable<AppEntry> eligible, AppEntry? foreground)
        {
            // The full card view is foreground-first. Master is only the
            // first card when no matching foreground audio app is available.
            // The bottom icon strip deliberately keeps its own master-first
            // order: do not change OrderMixerEntries for this preference.
            var ordered = eligible.ToList();
            if (foreground != null && !IsMasterVolumeEntry(foreground) && !IsSystemSoundsEntry(foreground))
            {
                var selected = ordered.FirstOrDefault(entry =>
                    string.Equals(entry.ExePath, foreground.ExePath, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                {
                    ordered.Remove(selected);
                    ordered.Insert(0, selected);
                }
            }
            return ordered;
        }

        // Settings deliberately pause the normal audio/session poll while the
        // user is editing. Preview UI-only changes from the cached snapshot,
        // coalesced to one dispatcher turn (no extra timer or audio writes).
        private static bool _settingsPreviewRefreshQueued;

        private static void QueueSettingsUiPreviewRefresh()
        {
            if (_settingsWin == null || _ctrl == null || _appList == null ||
                _settingsPreviewRefreshQueued)
                return;

            _settingsPreviewRefreshQueued = true;
            _settingsWin.Dispatcher.BeginInvoke(new Action(() =>
            {
                _settingsPreviewRefreshQueued = false;
                if (_settingsWin != null && _ctrl != null && _appList != null && !_isRefreshing)
                    Refresh(); // _settingsWin != null prevents session polling
            }), DispatcherPriority.Background);
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

                var showOnlyActive = _ctrl.Settings.Ui.ShowOnlyActiveAudio;
                var maxVisible = GetEffectiveVisibleApps();

                _appList.Children.Clear();
                var displayedEntries = new List<AppEntry>();
                var stripEntries = new List<AppEntry>();

                var foregroundEntry = TryFindForegroundAudioEntrySafe();
                var foregroundNonSystemEntry = foregroundEntry != null &&
                    !IsMasterVolumeEntry(foregroundEntry) &&
                    !IsSystemSoundsEntry(foregroundEntry)
                    ? foregroundEntry
                    : null;
                if (foregroundNonSystemEntry != null)
                {
                    RememberForegroundAudioEntry(foregroundNonSystemEntry);
                }

                // One ordering owner for both the full cards and compact icon strip:
                // the actual default endpoint master is pinned first, the foreground
                // app second, followed by other eligible audio apps.
                var entries = OrderMixerEntries(_ctrl.AppEntries, foregroundNonSystemEntry);
                var systemEntry = entries.FirstOrDefault(IsMasterVolumeEntry);

                var collapsedDockSingleCardMode = _edgeDockEnabled && _edgeDockActive && !_edgeDockExpanded;
                var preferredCollapsedEntry = foregroundNonSystemEntry ?? systemEntry ?? entries.FirstOrDefault();
                if (showOnlyActive && preferredCollapsedEntry != null &&
                    !preferredCollapsedEntry.HasActiveAudio && !preferredCollapsedEntry.IsMuted &&
                    !IsMasterVolumeEntry(preferredCollapsedEntry))
                {
                    // Keep the compact dock usable when the preferred app is filtered out.
                    preferredCollapsedEntry = entries.FirstOrDefault(entry => entry.HasActiveAudio || entry.IsMuted);
                }

                // Independently select cards and strip icons from the same
                // filtered sessions. The strip stays pinned master-first; the
                // card view puts the current app first and obeys Visible Apps.
                var eligibleEntries = entries.Where(entry =>
                    !showOnlyActive || entry.HasActiveAudio || entry.IsMuted ||
                    IsMasterVolumeEntry(entry) ||
                    (foregroundNonSystemEntry != null &&
                     string.Equals(entry.ExePath, foregroundNonSystemEntry.ExePath,
                         StringComparison.OrdinalIgnoreCase))).ToList();
                stripEntries.AddRange(eligibleEntries.Take(24));

                foreach (var entry in OrderMainCardEntries(eligibleEntries, foregroundNonSystemEntry))
                {
                    if (maxVisible <= 0 || displayedEntries.Count >= maxVisible)
                    {
                        break;
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

        // A rejected or partially accepted command is not a confirmed UI state.
        // Callers suppress Slider.ValueChanged while restoring the last snapshot.
        private static void ReconcileAudioControlUi(
            AppEntry entry, Slider slider, TextBlock volumeText, Border icon,
            Brush accent, Brush mutedBrush, byte[]? iconBytes, bool failed)
        {
            var muted = entry.IsMuted;
            slider.Value = Math.Clamp(entry.CombinedVolume * 100f, 0f, 100f);
            slider.IsEnabled = !muted;
            volumeText.Text = (muted ? "0%" : $"{slider.Value:F0}%") +
                (failed ? " !" : string.Empty);
            volumeText.Foreground = (muted || failed) ? mutedBrush : accent;
            volumeText.ToolTip = failed
                ? Ui("Audio-Befehl nicht bestaetigt – letzter bekannter Wert", "Audio command not confirmed – last known value")
                : null;
            icon.BorderBrush = (muted || failed) ? mutedBrush : Brushes.Transparent;
            icon.BorderThickness = (muted || failed) ? new Thickness(1) : new Thickness(0);
            icon.Child = MakeIconElement(entry.ExePath, iconBytes, muted);
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
                    HorizontalAlignment = _edgeDockSide == "left" ? HorizontalAlignment.Right : HorizontalAlignment.Left,
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
                    Value = volumePercent,
                    Orientation = Orientation.Vertical,
                    Foreground = accent,
                    Height = 50,
                    Width = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsEnabled = !cardMuted
                };

                var suppressQuickSliderWrite = false;
                void ReconcileQuick(bool succeeded)
                {
                    suppressQuickSliderWrite = true;
                    try
                    {
                        ReconcileAudioControlUi(entry, quickSlider, quickVolText, quickIconBorder,
                            accent, mutFgClr, iconBytes, failed: !succeeded);
                        cardMuted = entry.IsMuted;
                    }
                    finally { suppressQuickSliderWrite = false; }
                }
                void ToggleQuickMute()
                {
                    var accepted = _ctrl?.TrySetMute(exePath, !entry.IsMuted) == true;
                    ReconcileQuick(accepted);
                }

                quickIconBorder.MouseLeftButtonUp += (_, _) => ToggleQuickMute();
                quickSlider.GotMouseCapture += (_, _) => _isUserInteracting = true;
                quickSlider.LostMouseCapture += (_, _) => _isUserInteracting = false;
                quickSlider.ValueChanged += (_, _) =>
                {
                    if (_isRefreshing || cardMuted || suppressQuickSliderWrite) return;
                    var accepted = _ctrl?.TrySetVolume(exePath, (float)(quickSlider.Value / 100.0)) == true;
                    ReconcileQuick(accepted);
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
                Text = entry.ExeName + BuildRuleStatusSuffix(entry.Rule) +
                    (!entry.IsMuted && entry.SignalPeakLevel is > 0.005f
                        ? " | " + Ui("Ton", "Signal") : string.Empty),
                ToolTip = entry.SignalPeakLevel is float peak
                    ? Ui("Gemessener Signalpegel (Momentaufnahme): ", "Measured signal peak (snapshot): ") +
                      $"{peak * 100f:F0}%"
                    : Ui("Kein Signalpegel verfügbar", "Audio meter unavailable"),
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
                Value = volumePercent,
                Foreground = accent,
                Height = 20,
                Margin = new Thickness(0, 2, 0, 0),
                IsEnabled = !cardMuted
            };
            Grid.SetColumn(slider, 2);
            Grid.SetRow(slider, 1);
            g.Children.Add(slider);

            var suppressNormalSliderWrite = false;
            void ReconcileNormal(bool succeeded)
            {
                suppressNormalSliderWrite = true;
                try
                {
                    ReconcileAudioControlUi(entry, slider, volText, iconBorder,
                        accent, mutFgClr, iconBytes, failed: !succeeded);
                    cardMuted = entry.IsMuted;
                }
                finally { suppressNormalSliderWrite = false; }
            }
            void ToggleMute()
            {
                var accepted = _ctrl?.TrySetMute(exePath, !entry.IsMuted) == true;
                ReconcileNormal(accepted);
            }
            iconBorder.MouseLeftButtonUp += (_, _) => ToggleMute();
            slider.GotMouseCapture += (_, _) => _isUserInteracting = true;
            slider.LostMouseCapture += (_, _) => _isUserInteracting = false;
            slider.ValueChanged += (_, _) =>
            {
                if (_isRefreshing || cardMuted || suppressNormalSliderWrite) return;
                var accepted = _ctrl?.TrySetVolume(exePath, (float)(slider.Value / 100.0)) == true;
                ReconcileNormal(accepted);
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

        private static UIElement MakeSystemAudioIcon(bool isMaster, bool muted)
        {
            var drawing = new Canvas { Width = 24, Height = 24 };
            if (isMaster)
            {
                drawing.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 3,9 L 7,9 L 12,5 L 12,19 L 7,15 L 3,15 Z"),
                    Fill = Brushes.White
                });
                drawing.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 15,9 C 17,11 17,13 15,15 M 17,6 C 21,9 21,15 17,18"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
            else
            {
                // Two filled noteheads, stems, and a connecting beam.
                foreach (var (left, top) in new[] { (3.0, 16.0), (14.0, 13.0) })
                {
                    var head = new System.Windows.Shapes.Ellipse
                    {
                        Width = 7, Height = 5, Fill = Brushes.White
                    };
                    Canvas.SetLeft(head, left);
                    Canvas.SetTop(head, top);
                    drawing.Children.Add(head);
                }
                drawing.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 9,18 L 9,5 M 20,15 L 20,2"),
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                });
                drawing.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 9,5 L 20,2 L 20,6 L 9,9 Z"),
                    Fill = Brushes.White
                });
            }
            var icon = new Viewbox
            {
                Width = 19, Height = 19, Stretch = Stretch.Uniform,
                Opacity = muted ? 0.55 : 1.0,
                Child = drawing
            };
            System.Windows.Automation.AutomationProperties.SetName(icon,
                isMaster ? Ui("Systemlautstaerke", "System volume") : Ui("Systemtoene", "System sounds"));
            return icon;
        }
        private static UIElement MakeIconElement(string exePath, byte[]? iconBytes, bool muted)
        {
            // System audio uses explicit white vector icons in both displays.
            if (string.Equals(exePath, "__system_master__", StringComparison.OrdinalIgnoreCase))
                return MakeSystemAudioIcon(isMaster: true, muted);
            if (string.Equals(exePath, "__system_sound__", StringComparison.OrdinalIgnoreCase))
                return MakeSystemAudioIcon(isMaster: false, muted);

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
        private static void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopToggleHotkey();
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
            ws.CornerHintManualPosition = _cornerHintManualPosition;
            ws.CornerHintX = _cornerHintManualPosition && double.IsFinite(_cornerHintManualX) ? _cornerHintManualX : null;
            ws.CornerHintY = _cornerHintManualPosition && double.IsFinite(_cornerHintManualY) ? _cornerHintManualY : null;
            // Do not copy an incidental HWND state back into the user's
            // checkbox setting. ApplyUserTopmostPreference reads this value;
            // WriteCurrentStateToSettings must never overwrite it.

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
            _ctrl.Settings.Ui.CornerHintTransparencyPercent = _cornerHintTransparencyPercent;
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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

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

