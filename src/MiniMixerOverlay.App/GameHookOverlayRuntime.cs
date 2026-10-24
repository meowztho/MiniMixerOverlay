namespace MiniMixerOverlay.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal sealed partial class GameHookOverlayRuntime : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private static readonly IntPtr HwndMessage = new(-3);
    private const int GwlWndProc = -4;

    private const int WmCopyData = 0x004A;
    private const int WmApp = 0x8000;
    private const int WmIpcMsg = WmApp + 0x200;
    private const int WmIpcConnectLink = WmIpcMsg + 1;
    private const int WmIpcConnectLinkAck = WmIpcConnectLink + 1;
    private const int WmIpcCloseLink = WmIpcConnectLinkAck + 1;

    private const int OverlayIpcMsgId = 100;
    // Bound each IPC wait to 40ms; a failed peer is evicted after one timeout.
    // SendMessageTimeout must remain synchronous for WM_COPYDATA's temporary pointer.
    private const uint IpcSendTimeoutMs = 40;
    private const long RecoveryRetryMs = 1000;
    private const long RecoveryWindowMs = 30000;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;

    private readonly Window _overlayWindow;
    private readonly Action<string>? _status;
    private readonly Dispatcher _dispatcher;
    private readonly string _hostName;

    private IntPtr _ipcWindow = IntPtr.Zero;
    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;

    private readonly Dictionary<uint, RemoteClient> _clients = new();
    // Timeout eviction removes the active client, but its PID-only close may
    // still be queued. Carry that legacy ambiguity into a later reconnect.
    private readonly Dictionary<uint, int> _pendingCloseAfterEviction = new();
    // One bounded retry candidate per PID. No new timer or injector is used.
    private readonly Dictionary<uint, RecoveryCandidate> _recoveryCandidates = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private DispatcherTimer? _frameTimer;
    private MemoryMappedFile? _frameBufferMap;
    private MemoryMappedViewAccessor? _frameBufferView;
    private Mutex? _shareMutex;
    private string _shareMutexName = string.Empty;
    private string _bufferName = string.Empty;
    private int _frameBufferCapacity;
    private long _nextFrameBufferVersion = 1;

    // Optional second WPF surface for explicitly compatible native clients.
    // Clients without the independent auxiliary-frame capability receive no auxiliary frames.
    private Window? _auxiliaryWindow;
    private MemoryMappedFile? _auxiliaryMap;
    private MemoryMappedViewAccessor? _auxiliaryView;
    private string _auxiliaryBufferName = string.Empty;
    private int _auxiliaryCapacity;
    private uint _auxiliaryWindowId;
    private Rect _lastAuxiliaryRect = Rect.Empty;
    private bool _auxiliaryWasVisible;

    private uint _overlayWindowId;
    private uint _overlayNativeHandle;

    private Rect _lastSentRect = Rect.Empty;
    private bool _inputInterceptEnabled;
    private bool _forwardGameInputToWindow;
    private bool _forwardNativeMainInput;

    // Main native cursor forwarding is deliberately separate from the
    // existing sphere-only experimental test toggle.
    public bool ForwardNativeMainInput
    {
        get => _forwardNativeMainInput;
        set
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(() => ForwardNativeMainInput = value);
                return;
            }
            if (_forwardNativeMainInput == value) return;
            _forwardNativeMainInput = value;
            PublishMainInputPolicy();
        }
    }

    public bool IsRunning => _ipcWindow != IntPtr.Zero;

    public void SetAuxiliaryWindow(Window? window)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => SetAuxiliaryWindow(window));
            return;
        }
        if (ReferenceEquals(_auxiliaryWindow, window)) return;
        _auxiliaryWindow = window;
        if (_auxiliaryWasVisible)
            BroadcastAuxiliaryMessage("experimental.aux.hidden",
                JsonSerializer.Serialize(new { type = "experimental.aux.hidden" }, _jsonOptions));
        _auxiliaryWindowId = 0;
        _lastAuxiliaryRect = Rect.Empty;
        _auxiliaryWasVisible = false;
        ReleaseAuxiliaryBuffer();
    }

    public bool ForwardGameInputToWindow
    {
        get => _forwardGameInputToWindow;
        set
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(() => ForwardGameInputToWindow = value);
                return;
            }
            if (_forwardGameInputToWindow == value) return;
            _forwardGameInputToWindow = value;
            PublishAuxiliaryInputPolicy();
            PublishMainInputPolicy();
        }
    }

    public GameHookOverlayRuntime(Window overlayWindow, Action<string>? statusCallback = null, string hostName = "minimixer_graphics_v1")
    {
        _overlayWindow = overlayWindow;
        _status = statusCallback;
        _dispatcher = overlayWindow.Dispatcher;
        _hostName = hostName;
    }

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        if (!_dispatcher.CheckAccess())
        {
            return _dispatcher.Invoke(Start);
        }

        var mainHandle = new WindowInteropHelper(_overlayWindow).Handle;
        if (mainHandle == IntPtr.Zero)
        {
            Notify("Game-Hook Runtime wartet auf Main-Window Handle.");
            return false;
        }

        _overlayNativeHandle = unchecked((uint)(ulong)mainHandle.ToInt64());
        _overlayWindowId = _overlayNativeHandle;

        _ipcWindow = CreateWindowExA(
            0,
            "STATIC",
            _hostName,
            WsPopup,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_ipcWindow == IntPtr.Zero)
        {
            Notify("Game-Hook Runtime konnte IPC Host Window nicht erstellen.");
            return false;
        }

        _wndProcDelegate = WndProc;
        var newProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = SetWindowLongPtr(_ipcWindow, GwlWndProc, newProcPtr);

        TryAllowMessageThroughUipi(_ipcWindow, WmCopyData);
        TryAllowMessageThroughUipi(_ipcWindow, WmIpcConnectLink);
        TryAllowMessageThroughUipi(_ipcWindow, WmIpcConnectLinkAck);
        TryAllowMessageThroughUipi(_ipcWindow, WmIpcCloseLink);

        _shareMutexName = BuildShareMutexName();
        _shareMutex = new Mutex(false, _shareMutexName);

        CreateOrResizeSharedFrameBuffer(1280, 720);
        SendWindowBoundsIfChanged(force: true, includeBufferName: true);

        _overlayWindow.LocationChanged += OnOverlayWindowBoundsChanged;
        _overlayWindow.SizeChanged += OnOverlayWindowBoundsChanged;

        _frameTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _frameTimer.Tick += (_, _) => PushFrame();
        _frameTimer.Start();

        Notify("Game-Hook Runtime gestartet.");
        return true;
    }

    public void Stop()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Stop);
            return;
        }

        if (_frameTimer != null)
        {
            _frameTimer.Stop();
            _frameTimer = null;
        }

        _overlayWindow.LocationChanged -= OnOverlayWindowBoundsChanged;
        _overlayWindow.SizeChanged -= OnOverlayWindowBoundsChanged;

        foreach (var client in _clients.Values)
        {
            _ = PostMessage(client.RemoteWindow, WmIpcCloseLink, new IntPtr((int)GetCurrentProcessId()), IntPtr.Zero);
        }
        _clients.Clear();
        _pendingCloseAfterEviction.Clear();
        _recoveryCandidates.Clear();

        if (_ipcWindow != IntPtr.Zero)
        {
            if (_oldWndProc != IntPtr.Zero)
            {
                _ = SetWindowLongPtr(_ipcWindow, GwlWndProc, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }

            _ = DestroyWindow(_ipcWindow);
            _ipcWindow = IntPtr.Zero;
        }

        _shareMutex?.Dispose();
        _shareMutex = null;
        _shareMutexName = string.Empty;

        _frameBufferView?.Dispose();
        _frameBufferView = null;
        _frameBufferMap?.Dispose();
        _frameBufferMap = null;
        _frameBufferCapacity = 0;
        _bufferName = string.Empty;
        ReleaseAuxiliaryBuffer();
        _auxiliaryWasVisible = false;
        _lastAuxiliaryRect = Rect.Empty;

        Notify("Game-Hook Runtime gestoppt.");
    }

    public void SendInputIntercept(bool intercept)
    {
        _inputInterceptEnabled = intercept;
        var command = new
        {
            type = "command.input.intercept",
            intercept
        };

        BroadcastOverlayMessage("command.input.intercept", JsonSerializer.Serialize(command, _jsonOptions));
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnOverlayWindowBoundsChanged(object? sender, EventArgs e)
    {
        SendWindowBoundsIfChanged(force: false, includeBufferName: false);
    }

    private void PushFrame()
    {
        RecoverResponsiveClients();
        if (_clients.Count == 0) return;
        PushAuxiliaryFrame();

        // The independently visible sphere still publishes while the main
        // WPF mixer is hidden/collapsed. Original main-surface IPC unchanged.
        if (_overlayWindow.WindowState == WindowState.Minimized || !_overlayWindow.IsVisible)
            return;

        if (!TryCaptureWindowPixels(_overlayWindow, out var pixelWidth, out var pixelHeight, out var pixels))
        {
            return;
        }

        if (pixelWidth <= 0 || pixelHeight <= 0 || pixels.Length == 0)
        {
            return;
        }

        CreateOrResizeSharedFrameBuffer(pixelWidth, pixelHeight);

        TryWriteAndNotifyFrame(_shareMutex, _frameBufferView, pixelWidth, pixelHeight,
            pixels, () =>
            {
                var frameMessage = new
                {
                    type = "window.framebuffer",
                    windowId = _overlayWindowId
                };
                BroadcastOverlayMessage("window.framebuffer",
                    JsonSerializer.Serialize(frameMessage, _jsonOptions));
            });
    }

    // A timeout is not ownership. The frame and its notification must be
    // committed together from the consumer's point of view: never publish a
    // failed/partial write. Abandonment is fail-closed, but still requires release
    // because WaitOne transfers ownership before throwing.
    private static bool TryWriteAndNotifyFrame(
        Mutex? shareMutex, MemoryMappedViewAccessor? view,
        int width, int height, byte[] pixels, Action notifyFrame)
    {
        if (shareMutex == null || view == null) return false;

        var acquired = false;
        var released = false;
        try
        {
            acquired = shareMutex.WaitOne(8);
            if (!acquired) return false;

            view.Write(0, width);
            view.Write(4, height);
            view.WriteArray(8, pixels, 0, pixels.Length);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    shareMutex.ReleaseMutex();
                    released = true;
                }
                catch (ApplicationException) { /* no frame published */ }
            }
        }

        if (!released) return false;
        notifyFrame();
        return true;
    }

    private void CreateOrResizeSharedFrameBuffer(int width, int height)
    {
        width = Math.Max(width, 2);
        height = Math.Max(height, 2);

        var required = (width * height * 4) + 8;
        if (_frameBufferView != null && required <= _frameBufferCapacity)
        {
            return;
        }

        _frameBufferView?.Dispose();
        _frameBufferMap?.Dispose();
        _frameBufferView = null;
        _frameBufferMap = null;

        _bufferName = BuildFrameBufferName();
        _frameBufferCapacity = required;

        _frameBufferMap = MemoryMappedFile.CreateOrOpen(
            _bufferName,
            _frameBufferCapacity,
            MemoryMappedFileAccess.ReadWrite);
        _frameBufferView = _frameBufferMap.CreateViewAccessor(0, _frameBufferCapacity, MemoryMappedFileAccess.ReadWrite);

        SendWindowBoundsIfChanged(force: true, includeBufferName: true);
        Notify($"Game-Hook SharedBuffer: {_bufferName}");
    }

    private string BuildShareMutexName()
    {
        var pid = GetCurrentProcessId();
        var tick = Environment.TickCount64;
        return $"electron-overlay-sharemem-{{4C4BD948-0F75-413F-9667-AC64A7944D8E}}{pid}-{tick}";
    }

    private string BuildFrameBufferName()
    {
        var pid = GetCurrentProcessId();
        var tick = Environment.TickCount64;
        return $"electron-overlay-{pid}-{tick}-{_overlayWindowId}-image-{_nextFrameBufferVersion++}";
    }

    private void SendWindowBoundsIfChanged(bool force, bool includeBufferName)
    {
        if (_ipcWindow == IntPtr.Zero)
        {
            return;
        }

        var currentRect = GetOverlayWindowRect();
        if (!force && currentRect == _lastSentRect)
        {
            return;
        }

        _lastSentRect = currentRect;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "window.bounds",
            ["windowId"] = _overlayWindowId,
            ["rect"] = new
            {
                x = (int)Math.Round(currentRect.X),
                y = (int)Math.Round(currentRect.Y),
                width = (int)Math.Round(currentRect.Width),
                height = (int)Math.Round(currentRect.Height)
            }
        };

        if (includeBufferName && !string.IsNullOrWhiteSpace(_bufferName))
        {
            payload["bufferName"] = _bufferName;
        }

        BroadcastOverlayMessage("window.bounds", JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private Rect GetOverlayWindowRect()
    {
        var dpi = VisualTreeHelper.GetDpi(_overlayWindow);
        var width = Math.Max(2, (int)Math.Round((_overlayWindow.ActualWidth > 1 ? _overlayWindow.ActualWidth : _overlayWindow.Width) * dpi.DpiScaleX));
        var height = Math.Max(2, (int)Math.Round((_overlayWindow.ActualHeight > 1 ? _overlayWindow.ActualHeight : _overlayWindow.Height) * dpi.DpiScaleY));
        var x = (int)Math.Round(_overlayWindow.Left);
        var y = (int)Math.Round(_overlayWindow.Top);
        return new Rect(x, y, width, height);
    }

    private bool TryCaptureWindowPixels(Window window, out int pixelWidth, out int pixelHeight, out byte[] pixels)
    {
        pixelWidth = 0;
        pixelHeight = 0;
        pixels = Array.Empty<byte>();

        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            pixelWidth = Math.Max(2, (int)Math.Round((window.ActualWidth > 1 ? window.ActualWidth : window.Width) * dpi.DpiScaleX));
            pixelHeight = Math.Max(2, (int)Math.Round((window.ActualHeight > 1 ? window.ActualHeight : window.Height) * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(window);

            var stride = pixelWidth * 4;
            pixels = new byte[stride * pixelHeight];
            rtb.CopyPixels(pixels, stride, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmIpcConnectLink:
                HandleClientConnect(wParam);
                return IntPtr.Zero;
            case WmIpcCloseLink:
                HandleClientClose(wParam);
                return IntPtr.Zero;
            case WmCopyData:
                if (HandleCopyData(lParam))
                {
                    return new IntPtr(1);
                }
                break;
        }

        return _oldWndProc != IntPtr.Zero
            ? CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleClientConnect(IntPtr remoteWindow)
    {
        if (remoteWindow == IntPtr.Zero)
        {
            return;
        }

        _ = GetWindowThreadProcessId(remoteWindow, out var processId);
        if (processId == 0)
        {
            return;
        }

        _recoveryCandidates.Remove(processId);
        // Legacy WM_IPC_CLOSELINK identifies only the PID, not the HWND.
        // A replacement connection must survive a late close for the previous
        // window. A duplicate connect for the same HWND is not a replacement.
        var supersededCloses = 0;
        if (_pendingCloseAfterEviction.Remove(processId, out var pendingCloses))
            supersededCloses = pendingCloses;
        if (_clients.TryGetValue(processId, out var previous))
        {
            supersededCloses = previous.SupersededCloseCount;
            if (previous.RemoteWindow != remoteWindow &&
                supersededCloses < int.MaxValue)
            {
                supersededCloses++;
            }
        }

        _clients[processId] = new RemoteClient
        {
            ProcessId = processId,
            RemoteWindow = remoteWindow,
            OverlayInitialized = false,
            SupersededCloseCount = supersededCloses
        };

        _ = PostMessage(remoteWindow, WmIpcConnectLinkAck, new IntPtr((int)GetCurrentProcessId()), IntPtr.Zero);
        Notify($"Game-Hook Client verbunden (PID {processId}).");
    }

    private void HandleClientClose(IntPtr processIdPtr)
    {
        var pid = (uint)processIdPtr.ToInt64();
        if (!_clients.TryGetValue(pid, out var current))
        {
            // A Close from a timed-out client means it is not a recovery target.
            _recoveryCandidates.Remove(pid);
            // No active connection, but a late close for a timeout-evicted HWND
            // must not be charged to a future connection of the same PID.
            if (_pendingCloseAfterEviction.TryGetValue(pid, out var pending))
            {
                if (pending <= 1) _pendingCloseAfterEviction.Remove(pid);
                else _pendingCloseAfterEviction[pid] = pending - 1;
            }
            return;
        }

        // A dead current HWND can never recover: do not let missing older
        // close messages keep its PID entry artificially alive.
        if (!IsWindow(current.RemoteWindow))
        {
            _recoveryCandidates.Remove(pid);
            _clients.Remove(pid);
            Notify($"Game-Hook Client-Fenster geschlossen (PID {pid}).");
            return;
        }

        if (current.SupersededCloseCount > 0)
        {
            // PID-only legacy close: consume a superseded connection's close
            // before removing the newer HWND. No sender identity is available.
            current.SupersededCloseCount--;
            _clients[pid] = current;
            return;
        }

        if (_clients.Remove(pid))
        {
            _recoveryCandidates.Remove(pid);
            Notify($"Game-Hook Client getrennt (PID {pid}).");
        }
    }

    private bool HandleCopyData(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return false;
        }

        var cds = Marshal.PtrToStructure<CopyDataStruct>(lParam);
        if (cds.CbData <= 0 || cds.LpData == IntPtr.Zero)
        {
            return false;
        }

        var bytes = new byte[cds.CbData];
        Marshal.Copy(cds.LpData, bytes, 0, cds.CbData);

        if (bytes.Length < 12)
        {
            return false;
        }

        var direction = BitConverter.ToInt32(bytes, 0);
        var senderPid = unchecked((uint)cds.DwData.ToInt64());
        if (direction != 0)
        {
            return true;
        }

        var msgBody = new byte[bytes.Length - 12];
        Buffer.BlockCopy(bytes, 12, msgBody, 0, msgBody.Length);
        HandleOverlayIpcMessage(senderPid, msgBody);
        return true;
    }

    private void HandleOverlayIpcMessage(uint senderPid, byte[] payload)
    {
        var offset = 0;
        if (!TryReadInt(payload, ref offset, out var msgId) || msgId != OverlayIpcMsgId)
        {
            return;
        }

        if (!TryReadString(payload, ref offset, out var msgType))
        {
            return;
        }

        if (!TryReadString(payload, ref offset, out var msgJson))
        {
            return;
        }

        switch (msgType)
        {
            case "game.process":
                if (_clients.TryGetValue(senderPid, out var client))
                {
                    client.OverlayInitialized = true;
                    // Older IPC peers without the capability must NOT receive newer
                    // messages. Only a candidate explicitly advertising this
                    // optional capability may receive replay on re-init.
                    client.SupportsInputStateReplay = HasInputStateReplay(msgJson);
                    client.SupportsAuxiliarySurface =
                        HasClientCapability(msgJson, "experimentalAuxiliarySurface");
                    client.SupportsAuxiliaryInput =
                        client.SupportsAuxiliarySurface &&
                        HasClientCapability(msgJson, "experimentalAuxiliaryInput");
                    client.SupportsNativeMainInput =
                        HasClientCapability(msgJson, "experimentalMainInput");
                    _clients[senderPid] = client;
                    if (SendOverlayInit(client))
                        Notify($"Game-Prozess meldet sich (PID {senderPid}). Overlay init gesendet.");
                }
                break;
            case "graphics.window":
            case "graphics.window.event.resize":
            case "graphics.window.event.focus":
            case "graphics.fps":
            case "game.hotkey.down":
            case "game.window.focused":
                Notify($"{msgType}: {msgJson}");
                break;
            case "game.input.intercept":
                Notify($"game.input.intercept: {msgJson}");
                break;
            case "game.input":
                if ((_forwardNativeMainInput || IsNativeInputCancel(msgJson)) &&
                    _clients.TryGetValue(senderPid, out var mainClient) &&
                    mainClient.OverlayInitialized && mainClient.SupportsNativeMainInput)
                    ForwardGameInput(msgJson);
                break;
            case "experimental.aux.input":
                // New protocol: only this exact registered PID may submit
                // opt-in input to the separately hosted WPF sphere. Legacy
                // Peers lacking the capability do not advertise support.
                if (_clients.TryGetValue(senderPid, out var auxClient) &&
                    auxClient.OverlayInitialized && auxClient.SupportsAuxiliaryInput &&
                    (_forwardGameInputToWindow || IsNativeInputCancel(msgJson)))
                    ForwardAuxiliaryInput(msgJson);
                break;
        }
    }

    private static bool IsNativeInputCancel(string msgJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(msgJson);
            return doc.RootElement.TryGetProperty("msg", out var msg) &&
                   msg.TryGetUInt32(out var code) && code == 0x001F;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool HasClientCapability(string msgJson, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(msgJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(name, out var flag) &&
                flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static bool HasInputStateReplay(string msgJson)
    {
        try
        {
            using var document = JsonDocument.Parse(msgJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("inputStateReplay", out var flag) &&
                flag.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ForwardGameInput(string msgJson)
    {
        if (!_overlayWindow.IsVisible ||
            _overlayWindow.WindowState == WindowState.Minimized) return;
        try
        {
            using var doc = JsonDocument.Parse(msgJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("windowId", out var id) ||
                !id.TryGetUInt32(out var expected) || expected != _overlayWindowId ||
                !root.TryGetProperty("msg", out var message) ||
                !message.TryGetUInt32(out var msg) ||
                (msg != 0x0200 && msg != 0x0201 &&
                 msg != 0x0202 && msg != 0x02A3 && msg != 0x001F) ||
                !root.TryGetProperty("wparam", out var wpEl) ||
                !wpEl.TryGetUInt32(out var wp) || wp > 1 ||
                !root.TryGetProperty("lparam", out var lpEl) ||
                !lpEl.TryGetUInt32(out var lp))return;
            if (msg == 0x001F && (wp != 0 || lp != 0)) return;
            if (msg != 0x02A3 && msg != 0x001F)
            {
                var x = (int)(lp & 0xFFFF);
                var y = (int)((lp >> 16) & 0xFFFF);
                var dpi = VisualTreeHelper.GetDpi(_overlayWindow);
                if (x >= _overlayWindow.ActualWidth * dpi.DpiScaleX ||
                    y >= _overlayWindow.ActualHeight * dpi.DpiScaleY)
                    return;
            }
            var hwnd = new WindowInteropHelper(_overlayWindow).Handle;
            if (hwnd != IntPtr.Zero &&
                unchecked((uint)(ulong)hwnd.ToInt64()) == expected)
                _ = PostMessage(hwnd, unchecked((int)msg), new IntPtr((int)wp),
                    new IntPtr(unchecked((int)lp)));
        }
        catch (Exception e) when (e is JsonException or FormatException
                                    or OverflowException or InvalidOperationException)
        {
            // Never forward arbitrary or malformed WM_* from the game.
        }
    }

    private void PublishMainInputPolicy(RemoteClient? target = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "experimental.main.input.policy",
            enabled = _forwardNativeMainInput
        }, _jsonOptions);
        if (target.HasValue)
        {
            if (target.Value.SupportsNativeMainInput)
                SendOverlayMessage(target.Value, "experimental.main.input.policy", json);
            return;
        }
        foreach (var client in new List<RemoteClient>(_clients.Values))
            if (client.OverlayInitialized && client.SupportsNativeMainInput)
                SendOverlayMessage(client, "experimental.main.input.policy", json);
    }

    private bool SendOverlayInit(RemoteClient client)
    {
        var rect = GetOverlayWindowRect();
        var display = SystemParameters.WorkArea;
        var payload = new
        {
            type = "overlay.init",
            processEnabled = true,
            shareMemMutex = _shareMutexName,
            hotkeys = new[]
            {
                new
                {
                    name = "overlay.hotkey.toggleInputIntercept",
                    keyCode = 113,
                    ctrl = true,
                    shift = false,
                    alt = false,
                    passthrough = false
                }
            },
            windows = new[]
            {
                new
                {
                    type = "window",
                    windowId = _overlayWindowId,
                    nativeHandle = _overlayNativeHandle,
                    name = "MiniMixerOverlay",
                    transparent = true,
                    resizable = false,
                    maxWidth = (uint)Math.Max(640, display.Width),
                    maxHeight = (uint)Math.Max(360, display.Height),
                    minWidth = (uint)Math.Max(280, rect.Width),
                    minHeight = (uint)Math.Max(120, rect.Height),
                    dragBorderWidth = 0,
                    bufferName = _bufferName,
                    rect = new
                    {
                        x = (int)Math.Round(rect.X),
                        y = (int)Math.Round(rect.Y),
                        width = (int)Math.Round(rect.Width),
                        height = (int)Math.Round(rect.Height)
                    },
                    caption = (object?)null
                }
            },
            showfps = false,
            fpsPosition = 1,
            dragMode = 1
        };

        if (!SendOverlayMessage(client, "overlay.init",
                JsonSerializer.Serialize(payload, _jsonOptions)))
            return false;

        // The baseline WPF IPC protocol is retained for capability-limited peers.
        // Only opt-in candidates can request input state replay on recovery.
        // A separately hosted WPF sphere is published only to clients
        // explicitly advertising the optional auxiliary-surface contract.
        if (client.SupportsAuxiliarySurface)
            SendAuxiliaryBoundsIfChanged(force: true, recipient: client);
        if (client.SupportsAuxiliaryInput)
            PublishAuxiliaryInputPolicy(client);
        if (client.SupportsNativeMainInput)
            PublishMainInputPolicy(client);

        if (!client.SupportsInputStateReplay) return true;
        var inputState = new { type = "command.input.intercept", intercept = _inputInterceptEnabled };
        return SendOverlayMessage(client, "command.input.intercept",
            JsonSerializer.Serialize(inputState, _jsonOptions));
    }

    private void BroadcastOverlayMessage(string type, string jsonPayload)
    {
        // Make a snapshot: a timed-out client is removed, but must never
        // invalidate iteration or prevent healthy clients from receiving.
        foreach (var client in new List<RemoteClient>(_clients.Values))
        {
            SendOverlayMessage(client, type, jsonPayload);
        }
    }

    private bool SendOverlayMessage(RemoteClient client, string type, string jsonPayload)
    {
        if (client.RemoteWindow == IntPtr.Zero)
        {
            DropUnresponsiveClient(client);
            return false;
        }

        var overlayBody = PackOverlayIpc(type, jsonPayload);
        var packet = new byte[12 + overlayBody.Length];
        WriteInt(packet, 0, 1);
        WriteInt(packet, 4, 0);
        WriteInt(packet, 8, 0);
        Buffer.BlockCopy(overlayBody, 0, packet, 12, overlayBody.Length);

        if (SendCopyData(client.RemoteWindow, packet)) return true;
        DropUnresponsiveClient(client);
        return false;
    }

    private void DropUnresponsiveClient(RemoteClient client)
    {
        // A newer connection for the same PID must never be evicted due to
        // the prior HWND's timeout.
        if (_clients.TryGetValue(client.ProcessId, out var current) &&
            current.RemoteWindow == client.RemoteWindow)
        {
            // The eviction does not cancel a legacy PID-only close already
            // queued by this HWND or by a predecessor it superseded.
            var debt = current.SupersededCloseCount;
            if (debt < int.MaxValue) debt++;
            _pendingCloseAfterEviction[client.ProcessId] = debt;
            _clients.Remove(client.ProcessId);
            var now = Environment.TickCount64;
            _recoveryCandidates[client.ProcessId] = new RecoveryCandidate
            {
                Client = current,
                NextAttemptAtMs = now + RecoveryRetryMs,
                ExpiresAtMs = now + RecoveryWindowMs
            };
            Notify($"Game-Hook Client antwortet nicht (PID {client.ProcessId}); Wiederherstellung wird bis zu 30 s versucht.");
        }
    }

    // Existing 33-ms frame tick is also the recovery scheduler. Only one
    // bounded IPC probe per candidate/second; no volume, input or injection.
    private void RecoverResponsiveClients()
    {
        if (_recoveryCandidates.Count == 0) return;
        var now = Environment.TickCount64;
        foreach (var (pid, recovery) in new List<KeyValuePair<uint, RecoveryCandidate>>(_recoveryCandidates))
        {
            if (_clients.ContainsKey(pid))
            {
                _recoveryCandidates.Remove(pid);
                continue;
            }

            _ = GetWindowThreadProcessId(recovery.Client.RemoteWindow, out var windowPid);
            if (now >= recovery.ExpiresAtMs || !IsWindow(recovery.Client.RemoteWindow) ||
                windowPid != pid)
            {
                _recoveryCandidates.Remove(pid);
                _pendingCloseAfterEviction.Remove(pid);
                continue;
            }

            if (now < recovery.NextAttemptAtMs) continue;
            recovery.NextAttemptAtMs = now + RecoveryRetryMs;

            // Sending overlay.init to an already-connected native IPC link is
            // understood by the legacy protocol and refreshes the shared map.
            // A failed probe leaves the candidate quarantined and rate-limited.
            if (!SendOverlayInit(recovery.Client)) continue;
            _recoveryCandidates.Remove(pid);
            _pendingCloseAfterEviction.Remove(pid);
            _clients[pid] = recovery.Client;
            Notify($"Game-Hook Client wieder erreichbar (PID {pid}).");
        }
    }

    private byte[] PackOverlayIpc(string type, string jsonPayload)
    {
        var bytes = new List<byte>(256);
        AppendInt(bytes, OverlayIpcMsgId);
        AppendString(bytes, type);
        AppendString(bytes, jsonPayload);
        return bytes.ToArray();
    }

    private bool SendCopyData(IntPtr remoteWindow, byte[] payload)
    {
        var ptr = Marshal.AllocHGlobal(payload.Length);
        try
        {
            Marshal.Copy(payload, 0, ptr, payload.Length);
            var cds = new CopyDataStruct
            {
                DwData = new IntPtr((int)GetCurrentProcessId()),
                CbData = payload.Length,
                LpData = ptr
            };
            // SMTO_BLOCK prevents reentrant calls while waiting. The return
            // value indicates delivery, independently of receiver's LRESULT.
            return SendMessageTimeout(remoteWindow, WmCopyData, IntPtr.Zero,
                ref cds, SmtoAbortIfHung | SmtoBlock, IpcSendTimeoutMs,
                out _) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void AppendInt(List<byte> bytes, int value)
    {
        bytes.AddRange(BitConverter.GetBytes(value));
    }

    private static void AppendString(List<byte> bytes, string value)
    {
        var data = Encoding.UTF8.GetBytes(value ?? string.Empty);
        AppendInt(bytes, data.Length);
        bytes.AddRange(data);
    }

    private static bool TryReadInt(byte[] data, ref int offset, out int value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = BitConverter.ToInt32(data, offset);
        offset += 4;
        return true;
    }

    private static bool TryReadString(byte[] data, ref int offset, out string value)
    {
        value = string.Empty;
        if (!TryReadInt(data, ref offset, out var len))
        {
            return false;
        }

        if (len < 0 || offset + len > data.Length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(data, offset, len);
        offset += len;
        return true;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        var data = BitConverter.GetBytes(value);
        Buffer.BlockCopy(data, 0, buffer, offset, 4);
    }

    private void Notify(string text)
    {
        _status?.Invoke(text);
        Debug.WriteLine("[GameHook] " + text);
    }

    private static void TryAllowMessageThroughUipi(IntPtr hwnd, uint message)
    {
        try
        {
            _ = ChangeWindowMessageFilterEx(hwnd, message, 1, IntPtr.Zero);
        }
        catch
        {
            // ignore on unsupported builds
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr DwData;
        public int CbData;
        public IntPtr LpData;
    }

    private sealed class RecoveryCandidate
    {
        public RemoteClient Client;
        public long NextAttemptAtMs;
        public long ExpiresAtMs;
    }

    private struct RemoteClient
    {
        public uint ProcessId;
        public IntPtr RemoteWindow;
        public bool OverlayInitialized;
        public bool SupportsInputStateReplay;
        public bool SupportsAuxiliarySurface;
        public bool SupportsAuxiliaryInput;
        public bool SupportsNativeMainInput;
        public int SupersededCloseCount;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr CreateWindowExA(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg,
        IntPtr wParam, ref CopyDataStruct lParam, uint flags, uint timeoutMs,
        out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);
}
