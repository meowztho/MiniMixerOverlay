// Optional second WPF surface for clients advertising
// experimentalAuxiliarySurface. No new UI/audio owner, no legacy message.
namespace MiniMixerOverlay.App;

using System;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

internal sealed partial class GameHookOverlayRuntime
{
    private void PublishAuxiliaryInputPolicy(RemoteClient? target = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "experimental.aux.input.policy",
            enabled = _forwardGameInputToWindow
        }, _jsonOptions);
        if (target.HasValue)
        {
            if (target.Value.SupportsAuxiliaryInput)
                SendOverlayMessage(target.Value, "experimental.aux.input.policy", json);
            return;
        }
        foreach (var client in new System.Collections.Generic.List<RemoteClient>(_clients.Values))
        {
            if (client.OverlayInitialized && client.SupportsAuxiliaryInput)
                SendOverlayMessage(client, "experimental.aux.input.policy", json);
        }
    }

    private void ForwardAuxiliaryInput(string json)
    {
        var window = _auxiliaryWindow;
        if (window == null || !window.IsVisible ||
            window.WindowState == WindowState.Minimized || _auxiliaryWindowId == 0)
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("windowId", out var id) ||
                !id.TryGetUInt32(out var expected) || expected != _auxiliaryWindowId ||
                !root.TryGetProperty("msg", out var messageEl) ||
                !messageEl.TryGetUInt32(out var msg))
                return;

            // Explicitly NO keyboard, arbitrary WM_* or cross-HWND dispatch.
            // Mouse only: MOVE/DOWN/UP/LEAVE, plus WM_CANCELMODE to
            // release a stale WPF capture without synthesizing a click.
            if (msg != 0x0200 && msg != 0x0201 &&
                msg != 0x0202 && msg != 0x02A3 &&
                msg != 0x001F) return;
            if (!root.TryGetProperty("wparam", out var wEl) ||
                !wEl.TryGetUInt32(out var wp) || wp > 0x0001 ||
                !root.TryGetProperty("lparam", out var lEl) ||
                !lEl.TryGetUInt32(out var lp)) return;

            if (msg == 0x001F && (wp != 0 || lp != 0)) return;
            if (msg != 0x02A3 && msg != 0x001F)
            {
                var x = (int)(lp & 0xFFFF);
                var y = (int)((lp >> 16) & 0xFFFF);
                if (x >= window.ActualWidth * VisualTreeHelper.GetDpi(window).DpiScaleX ||
                    y >= window.ActualHeight * VisualTreeHelper.GetDpi(window).DpiScaleY)
                    return;
            }
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero &&
                unchecked((uint)(ulong)hwnd.ToInt64()) == expected)
                _ = PostMessage(hwnd, unchecked((int)msg),
                    new IntPtr((int)wp), new IntPtr(unchecked((int)lp)));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
                                   or FormatException or OverflowException)
        {
            // Invalid or stale packets must never throw into the WPF dispatcher.
        }
    }

    private void ReleaseAuxiliaryBuffer()
    {
        _auxiliaryView?.Dispose();
        _auxiliaryMap?.Dispose();
        _auxiliaryView = null;
        _auxiliaryMap = null;
        _auxiliaryBufferName = string.Empty;
        _auxiliaryCapacity = 0;
    }

    private void BroadcastAuxiliaryMessage(string type, string payload)
    {
        // This is a separate opt-in contract, not a new vendor command.
        foreach (var client in new System.Collections.Generic.List<RemoteClient>(_clients.Values))
            if (client.SupportsAuxiliarySurface && client.OverlayInitialized)
                SendOverlayMessage(client, type, payload);
    }

    private void SendAuxiliaryBoundsIfChanged(bool force, RemoteClient? recipient = null)
    {
        var window = _auxiliaryWindow;
        if (window == null || !window.IsVisible || _auxiliaryWindowId == 0 ||
            string.IsNullOrEmpty(_auxiliaryBufferName)) return;

        var dpi = VisualTreeHelper.GetDpi(window);
        var rect = new Rect(
            Math.Round(window.Left), Math.Round(window.Top),
            Math.Max(2, (int)Math.Round(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(2, (int)Math.Round(window.ActualHeight * dpi.DpiScaleY)));
        if (!force && rect == _lastAuxiliaryRect) return;
        _lastAuxiliaryRect = rect;
        var data = new
        {
            type = "experimental.aux.bounds",
            windowId = _auxiliaryWindowId,
            bufferName = _auxiliaryBufferName,
            x = (int)rect.X,
            y = (int)rect.Y,
            width = (int)rect.Width,
            height = (int)rect.Height
        };
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        if (recipient.HasValue)
            SendOverlayMessage(recipient.Value, "experimental.aux.bounds", json);
        else
            BroadcastAuxiliaryMessage("experimental.aux.bounds", json);
    }

    private bool PrepareAuxiliaryBuffer(int width, int height)
    {
        // Bound both allocations and per-frame WPF/GPU copy cost.
        if (width < 2 || height < 2 || (long)width * height > 1048576)
            return false;
        var required = checked(width * height * 4 + 8);
        if (_auxiliaryView != null && required <= _auxiliaryCapacity) return true;

        ReleaseAuxiliaryBuffer();
        _auxiliaryBufferName =
            $"electron-overlay-aux-{GetCurrentProcessId()}-{Environment.TickCount64}-{_auxiliaryWindowId}-image-{_nextFrameBufferVersion++}";
        _auxiliaryMap = MemoryMappedFile.CreateOrOpen(
            _auxiliaryBufferName, required, MemoryMappedFileAccess.ReadWrite);
        _auxiliaryView = _auxiliaryMap.CreateViewAccessor(
            0, required, MemoryMappedFileAccess.ReadWrite);
        _auxiliaryCapacity = required;
        _lastAuxiliaryRect = Rect.Empty;
        SendAuxiliaryBoundsIfChanged(force: true);
        return true;
    }

    private void PushAuxiliaryFrame()
    {
        if (!_clients.Values.Any(c => c.SupportsAuxiliarySurface && c.OverlayInitialized))
            return;
        var window = _auxiliaryWindow;
        if (window == null || !window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            if (_auxiliaryWasVisible)
            {
                _auxiliaryWasVisible = false;
                _lastAuxiliaryRect = Rect.Empty;
                BroadcastAuxiliaryMessage("experimental.aux.hidden",
                    JsonSerializer.Serialize(new { type = "experimental.aux.hidden" }, _jsonOptions));
            }
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var id = unchecked((uint)(ulong)hwnd.ToInt64());
        if (id != _auxiliaryWindowId)
        {
            ReleaseAuxiliaryBuffer();
            _auxiliaryWindowId = id;
            _lastAuxiliaryRect = Rect.Empty;
        }
        _auxiliaryWasVisible = true;
        if (!TryCaptureWindowPixels(window, out var width, out var height, out var pixels))
            return;
        if (pixels.Length == 0 || !PrepareAuxiliaryBuffer(width, height))
            return;
        SendAuxiliaryBoundsIfChanged(force: false);
        TryWriteAndNotifyFrame(_shareMutex, _auxiliaryView, width, height, pixels, () =>
        {
            var message = new
            {
                type = "experimental.aux.framebuffer",
                windowId = _auxiliaryWindowId
            };
            BroadcastAuxiliaryMessage("experimental.aux.framebuffer",
                JsonSerializer.Serialize(message, _jsonOptions));
        });
    }
}
