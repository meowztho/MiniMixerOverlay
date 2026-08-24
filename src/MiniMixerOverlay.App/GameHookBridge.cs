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
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const ushort ImageFileMachineUnknown = 0x0;

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

    public static GameHookInjectResult InjectForegroundWindow(string assetsDirectory)
    {
        var target = FindTargetWindow(string.Empty);
        return InjectCandidate(target, assetsDirectory, string.Empty);
    }

    public static bool TryGetForegroundWindowInfo(out GameHookForegroundWindowInfo info)
    {
        info = new GameHookForegroundWindowInfo();

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var candidate = CreateCandidate(hwnd);
        if (candidate.Hwnd == IntPtr.Zero || candidate.ProcessId == 0 || string.IsNullOrWhiteSpace(candidate.Title))
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

    private static GameHookInjectResult InjectCandidate(WindowCandidate target, string assetsDirectory, string titleContains)
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

        var is64BitTarget = Is64BitProcess(target.ProcessId);
        var helperPath = System.IO.Path.Combine(
            assetsDirectory,
            is64BitTarget ? "injector_helper.x64.exe" : "injector_helper.exe");
        var dllPath = System.IO.Path.Combine(
            assetsDirectory,
            is64BitTarget ? "n_overlay.x64.dll" : "n_overlay.dll");

        if (!System.IO.File.Exists(helperPath))
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = $"Injector fehlt: {helperPath}",
                WindowTitle = target.Title,
                ProcessId = target.ProcessId,
                HelperPath = helperPath,
                DllPath = dllPath
            };
        }

        if (!System.IO.File.Exists(dllPath))
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = $"Overlay-DLL fehlt: {dllPath}",
                WindowTitle = target.Title,
                ProcessId = target.ProcessId,
                HelperPath = helperPath,
                DllPath = dllPath
            };
        }

        try
        {
            var hwndArg = unchecked((uint)(ulong)target.Hwnd.ToInt64());
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"{hwndArg} \"{dllPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new GameHookInjectResult
                {
                    Success = false,
                    Message = "Injector konnte nicht gestartet werden.",
                    WindowTitle = target.Title,
                    ProcessId = target.ProcessId,
                    HelperPath = helperPath,
                    DllPath = dllPath
                };
            }

            if (!process.WaitForExit(6000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return new GameHookInjectResult
                {
                    Success = false,
                    Message = "Injector-Timeout. Bitte erneut versuchen.",
                    WindowTitle = target.Title,
                    ProcessId = target.ProcessId,
                    HelperPath = helperPath,
                    DllPath = dllPath
                };
            }

            var success = process.ExitCode == 0;
            return new GameHookInjectResult
            {
                Success = success,
                Message = success
                    ? $"Injection ausgefuehrt fuer '{target.Title}' (PID {target.ProcessId})."
                    : $"Injector ExitCode {process.ExitCode} fuer '{target.Title}'.",
                WindowTitle = target.Title,
                ProcessId = target.ProcessId,
                HelperPath = helperPath,
                DllPath = dllPath
            };
        }
        catch (Exception ex)
        {
            return new GameHookInjectResult
            {
                Success = false,
                Message = $"Injection-Fehler: {ex.Message}",
                WindowTitle = target.Title,
                ProcessId = target.ProcessId,
                HelperPath = helperPath,
                DllPath = dllPath
            };
        }
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

    private static bool Is64BitProcess(uint processId)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            if (TryIsWow64Process2(processHandle, out var processMachine))
            {
                return processMachine == ImageFileMachineUnknown;
            }

            if (IsWow64Process(processHandle, out var wow64))
            {
                return !wow64;
            }
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }

        return true;
    }

    private static bool TryIsWow64Process2(IntPtr processHandle, out ushort processMachine)
    {
        processMachine = ImageFileMachineUnknown;
        try
        {
            if (IsWow64Process2(processHandle, out processMachine, out _))
            {
                return true;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // fallback below
        }

        return false;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(IntPtr processHandle, out ushort processMachine, out ushort nativeMachine);
}
