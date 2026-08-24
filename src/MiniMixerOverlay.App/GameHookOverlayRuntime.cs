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

internal sealed class GameHookOverlayRuntime : IDisposable
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

    private readonly Window _overlayWindow;
    private readonly Action<string>? _status;
    private readonly Dispatcher _dispatcher;
    private readonly string _hostName;

    private IntPtr _ipcWindow = IntPtr.Zero;
    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;

    private readonly Dictionary<uint, RemoteClient> _clients = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private DispatcherTimer? _frameTimer;
    private MemoryMappedFile? _frameBufferMap;
    private MemoryMappedViewAccessor? _frameBufferView;
    private Mutex? _shareMutex;
    private string _shareMutexName = string.Empty;
    private string _bufferName = string.Empty;
    private int _frameBufferCapacity;
    private long _nextFrameBufferVersion = 1;

    private uint _overlayWindowId;
    private uint _overlayNativeHandle;

    private Rect _lastSentRect = Rect.Empty;
    private bool _inputInterceptEnabled;
    private bool _forwardGameInputToWindow;

    public bool IsRunning => _ipcWindow != IntPtr.Zero;

    public bool ForwardGameInputToWindow
    {
        get => _forwardGameInputToWindow;
        set => _forwardGameInputToWindow = value;
    }

    public GameHookOverlayRuntime(Window overlayWindow, Action<string>? statusCallback = null, string hostName = "n_overlay_1a1y2o8l0b")
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
        if (_clients.Count == 0 || _overlayWindow.WindowState == WindowState.Minimized || !_overlayWindow.IsVisible)
        {
            return;
        }

        if (!TryCaptureWindowPixels(_overlayWindow, out var pixelWidth, out var pixelHeight, out var pixels))
        {
            return;
        }

        if (pixelWidth <= 0 || pixelHeight <= 0 || pixels.Length == 0)
        {
            return;
        }

        CreateOrResizeSharedFrameBuffer(pixelWidth, pixelHeight);

        try
        {
            _shareMutex?.WaitOne(8);
            _frameBufferView?.Write(0, pixelWidth);
            _frameBufferView?.Write(4, pixelHeight);
            _frameBufferView?.WriteArray(8, pixels, 0, pixels.Length);
        }
        catch
        {
            // ignore frame write hiccups
        }
        finally
        {
            try
            {
                _shareMutex?.ReleaseMutex();
            }
            catch
            {
                // ignore if not held
            }
        }

        var frameMessage = new
        {
            type = "window.framebuffer",
            windowId = _overlayWindowId
        };
        BroadcastOverlayMessage("window.framebuffer", JsonSerializer.Serialize(frameMessage, _jsonOptions));
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

        _clients[processId] = new RemoteClient
        {
            ProcessId = processId,
            RemoteWindow = remoteWindow,
            OverlayInitialized = false
        };

        _ = PostMessage(remoteWindow, WmIpcConnectLinkAck, new IntPtr((int)GetCurrentProcessId()), IntPtr.Zero);
        Notify($"Game-Hook Client verbunden (PID {processId}).");
    }

    private void HandleClientClose(IntPtr processIdPtr)
    {
        var pid = (uint)processIdPtr.ToInt64();
        if (_clients.Remove(pid))
        {
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
                    _clients[senderPid] = client;
                    SendOverlayInit(client);
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
                if (_forwardGameInputToWindow)
                {
                    ForwardGameInput(msgJson);
                }
                break;
        }
    }

    private void ForwardGameInput(string msgJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(msgJson);
            var root = doc.RootElement;
            var windowId = root.TryGetProperty("windowId", out var winIdEl) ? winIdEl.GetUInt32() : 0;
            if (windowId != _overlayWindowId)
            {
                return;
            }

            var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetUInt32() : 0;
            var wparam = root.TryGetProperty("wparam", out var wpEl) ? wpEl.GetUInt32() : 0;
            var lparam = root.TryGetProperty("lparam", out var lpEl) ? lpEl.GetUInt32() : 0;

            var hwnd = new WindowInteropHelper(_overlayWindow).Handle;
            if (hwnd != IntPtr.Zero)
            {
                _ = PostMessage(hwnd, unchecked((int)msg), new IntPtr((int)wparam), new IntPtr((int)lparam));
            }
        }
        catch
        {
            // ignore malformed input events
        }
    }

    private void SendOverlayInit(RemoteClient client)
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

        SendOverlayMessage(client, "overlay.init", JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private void BroadcastOverlayMessage(string type, string jsonPayload)
    {
        foreach (var client in _clients.Values)
        {
            SendOverlayMessage(client, type, jsonPayload);
        }
    }

    private void SendOverlayMessage(RemoteClient client, string type, string jsonPayload)
    {
        if (client.RemoteWindow == IntPtr.Zero)
        {
            return;
        }

        var overlayBody = PackOverlayIpc(type, jsonPayload);
        var packet = new byte[12 + overlayBody.Length];
        WriteInt(packet, 0, 1);
        WriteInt(packet, 4, 0);
        WriteInt(packet, 8, 0);
        Buffer.BlockCopy(overlayBody, 0, packet, 12, overlayBody.Length);

        SendCopyData(client.RemoteWindow, packet);
    }

    private byte[] PackOverlayIpc(string type, string jsonPayload)
    {
        var bytes = new List<byte>(256);
        AppendInt(bytes, OverlayIpcMsgId);
        AppendString(bytes, type);
        AppendString(bytes, jsonPayload);
        return bytes.ToArray();
    }

    private void SendCopyData(IntPtr remoteWindow, byte[] payload)
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
            _ = SendMessage(remoteWindow, WmCopyData, IntPtr.Zero, ref cds);
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

    private struct RemoteClient
    {
        public uint ProcessId;
        public IntPtr RemoteWindow;
        public bool OverlayInitialized;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);
}
