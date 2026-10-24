namespace MiniMixerOverlay.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class GameHookInjectResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public string HelperPath { get; init; } = string.Empty;
    public string DllPath { get; init; } = string.Empty;
}

internal sealed class GameHookForegroundWindowInfo
{
    public IntPtr Hwnd { get; init; }
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
}

internal static class GameHookBridge
{
    // Migration safety gate: archived vendor helper and n_overlay DLL
    // must NEVER be loaded by the active product, even if an old custom
    // AssetsPath points directly into the legacy archive.
    internal static bool LegacyVendorInjectionDisabled => true;
    // Deliberate experimental opt-in; default app launch stays fail-closed.
    internal static bool NativePreviewEnabled => Array.Exists(Environment.GetCommandLineArgs(),
        arg => string.Equals(arg, "--native-hook-preview", StringComparison.OrdinalIgnoreCase)) &&
        !Array.Exists(Environment.GetCommandLineArgs(),
        arg => string.Equals(arg, "--graphics-ipc-only", StringComparison.OrdinalIgnoreCase));

    public static GameHookInjectResult InjectByWindowTitle(string titleContains, string assetsDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetsDirectory))
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = "Kein Asset-Pfad fuer Game-Hook gesetzt."
            };
        }

        var target = FindTargetWindow(titleContains);
        return InjectCandidate(target, assetsDirectory, titleContains);
    }

    // Auto-Hook is bound to the HWND/PID/title approved by the selection owner.
    // Never select an unrelated foreground window on a background worker.
    public static GameHookInjectResult InjectQualifiedForegroundWindow(
        GameHookForegroundWindowInfo qualified, string assetsDirectory)
    {
        if (!TryResolveQualifiedForegroundWindow(qualified, out var target))
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = "Auto-Hook abgebrochen: Vordergrundfenster oder Prozess hat gewechselt.",
                ProcessId = qualified?.ProcessId ?? 0,
                WindowTitle = qualified?.WindowTitle ?? string.Empty
            };
        }

        return InjectCandidate(target, assetsDirectory, string.Empty, qualified);
    }

    // Same canonical qualification path plus an optional runtime-session
    // cancellation gate for background auto-attach after Stop/restart.
    internal static GameHookInjectResult InjectQualifiedForegroundWindowGated(
        GameHookForegroundWindowInfo qualified, string assetsDirectory,
        Func<bool> sessionEligible)
    {
        if (sessionEligible == null || !sessionEligible())
            return new GameHookInjectResult
            {
                Message = "Native Preview: Runtime-Session bereits beendet.",
                ProcessId = qualified?.ProcessId ?? 0
            };
        if (!TryResolveQualifiedForegroundWindow(qualified, out var target))
            return new GameHookInjectResult
            {
                Message = "Auto-Hook abgebrochen: qualifiziertes Fenster gewechselt.",
                ProcessId = qualified?.ProcessId ?? 0
            };
        return InjectCandidate(target, assetsDirectory, string.Empty, qualified, sessionEligible);
    }

    private static bool TryResolveQualifiedForegroundWindow(
        GameHookForegroundWindowInfo qualified, out WindowCandidate current)
    {
        current = default;
        if (qualified == null || qualified.Hwnd == IntPtr.Zero)
        {
            return false;
        }

        var hwnd = GetForegroundWindow();
        if (hwnd != qualified.Hwnd)
        {
            return false;
        }

        current = CreateCandidate(hwnd);
        return MatchesQualifiedWindow(qualified, hwnd, current.ProcessId, current.Title);
    }

    // Pure identity gate: independently testable without launching an injector.
    private static bool MatchesQualifiedWindow(
        GameHookForegroundWindowInfo qualified, IntPtr foregroundHwnd,
        uint currentProcessId, string currentTitle)
    {
        return qualified != null &&
               qualified.Hwnd != IntPtr.Zero &&
               qualified.ProcessId != 0 &&
               !string.IsNullOrWhiteSpace(qualified.WindowTitle) &&
               qualified.Hwnd == foregroundHwnd &&
               qualified.ProcessId == currentProcessId &&
               string.Equals(qualified.WindowTitle, currentTitle, StringComparison.Ordinal);
    }

    private static bool IsUsableForegroundWindow(uint pid, string? title, bool requireTitle) =>
        pid != 0 && (!requireTitle || !string.IsNullOrWhiteSpace(title));

    // Foreground audio needs only a PID. Game-Hook callers keep the strict title gate.
    public static bool TryGetForegroundWindowInfo(out GameHookForegroundWindowInfo info,
        bool requireTitle = true)
    {
        info = new GameHookForegroundWindowInfo();

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var candidate = CreateCandidate(hwnd);
        if (candidate.Hwnd == IntPtr.Zero || !IsUsableForegroundWindow(candidate.ProcessId, candidate.Title, requireTitle))
        {
            return false;
        }

        var processName = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)candidate.ProcessId);
            processName = process.ProcessName;
        }
        catch
        {
            // ignore process lookup errors
        }

        info = new GameHookForegroundWindowInfo
        {
            Hwnd = candidate.Hwnd,
            ProcessId = candidate.ProcessId,
            ProcessName = processName,
            WindowTitle = candidate.Title
        };

        return true;
    }

    private static GameHookInjectResult InjectCandidate(WindowCandidate target, string assetsDirectory, string titleContains,
        GameHookForegroundWindowInfo? qualified = null, Func<bool>? sessionEligible = null)
    {
        if (target.Hwnd == IntPtr.Zero || target.ProcessId == 0)
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(titleContains)
                    ? "Kein aktives Fenster fuer Injection gefunden."
                    : $"Kein Fenster mit Titel '{titleContains}' gefunden."
            };
        }

        if (!NativePreviewEnabled)
        {
            return new GameHookInjectResult
            {
                Message = "Native Hook ist ein Preview; Start mit --native-hook-preview erforderlich.",
                ProcessId = target.ProcessId,
                WindowTitle = target.Title
            };
        }

        // Keep the established HWND/PID/title selection as the only authority.
        // The native implementation never looks up a different foreground target.
        bool StillSameTarget()
        {
            var current = CreateCandidate(target.Hwnd);
            return current.Hwnd == target.Hwnd &&
                   current.ProcessId == target.ProcessId &&
                   string.Equals(current.Title, target.Title, StringComparison.Ordinal) &&
                   (qualified == null || TryResolveQualifiedForegroundWindow(qualified, out _)) &&
                   (sessionEligible == null || sessionEligible());
        }

        return NativeGameHookLoader.Attach(target.ProcessId, target.Hwnd, target.Title,
            assetsDirectory, StillSameTarget);
    }
    public static List<string> SuggestTopWindowTitles(string titleContains, int limit = 8)
    {
        var list = new List<string>();
        foreach (var candidate in EnumerateVisibleTopWindows())
        {
            if (string.IsNullOrWhiteSpace(candidate.Title))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(titleContains) &&
                candidate.Title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            list.Add(candidate.Title);
            if (list.Count >= limit)
            {
                break;
            }
        }

        return list;
    }

    private static WindowCandidate FindTargetWindow(string titleContains)
    {
        if (string.IsNullOrWhiteSpace(titleContains))
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                return default;
            }

            return CreateCandidate(fg);
        }

        foreach (var candidate in EnumerateVisibleTopWindows())
        {
            if (candidate.Title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate;
            }
        }

        return default;
    }

    private static List<WindowCandidate> EnumerateVisibleTopWindows()
    {
        var result = new List<WindowCandidate>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            var candidate = CreateCandidate(hwnd);
            if (candidate.Hwnd != IntPtr.Zero &&
                candidate.ProcessId != 0 &&
                !string.IsNullOrWhiteSpace(candidate.Title))
            {
                result.Add(candidate);
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static WindowCandidate CreateCandidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return default;
        }

        var titleLen = GetWindowTextLength(hwnd);
        if (titleLen <= 0)
        {
            return default;
        }

        var titleBuffer = new StringBuilder(titleLen + 1);
        _ = GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return new WindowCandidate
        {
            Hwnd = hwnd,
            ProcessId = processId,
            Title = titleBuffer.ToString()
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowCandidate
    {
        public IntPtr Hwnd { get; init; }
        public uint ProcessId { get; init; }
        public string Title { get; init; }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();


}
