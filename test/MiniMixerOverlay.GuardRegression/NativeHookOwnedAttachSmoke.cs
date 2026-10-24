using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Explicit, separate, owned-process positive test of the preview loader.
// Never starts Program.Main, touches real audio, or injects into a user game.
internal static class NativeHookOwnedAttachSmoke
{
    public static void RunChild()
    {
        using var window = new Form
        {
            Text = "MiniMixer Owned Native Hook Fixture",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-30000, -30000),
            Size = new System.Drawing.Size(160, 90),
            FormBorderStyle = FormBorderStyle.FixedToolWindow
        };
        window.Shown += (_, _) =>
        {
            Console.WriteLine("OWNED_NATIVE_HWND=" + window.Handle.ToInt64());
            Console.Out.Flush();
        };
        _ = Task.Run(() =>
        {
            try { Console.ReadLine(); }
            catch (IOException) { }
            if (window.IsHandleCreated)
                window.BeginInvoke(() => window.Close());
        });
        System.Windows.Forms.Application.Run(window);
    }

    public static int RunParent(string[] args)
    {
        var index = Array.FindIndex(args, arg =>
            string.Equals(arg, "--native-owned-attach", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: --native-owned-attach <directory-containing-MiniMixerLivePresent.x64.dll>");
            return 2;
        }
        var sourceDll = Path.Combine(Path.GetFullPath(args[index + 1]), "MiniMixerLivePresent.x64.dll");
        if (!File.Exists(sourceDll))
        {
            Console.Error.WriteLine("Missing explicitly built owned DLL: " + sourceDll);
            return 2;
        }

        var loader = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
            "MiniMixerOverlay.App.NativeGameHookLoader") ??
            throw new InvalidOperationException("Native loader missing");
        var attach = loader.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException("Attach");
        var approved = loader.GetMethod("IsApprovedPreviewProcessName", BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException("IsApprovedPreviewProcessName");
        bool Allowed(string? processName) => (bool)approved.Invoke(null, new object?[] { processName })!;
        if (!Allowed("OMDDSteam-Win64-Shipping") ||
            !Allowed("MiniMixerOverlay.GuardRegression") ||
            Allowed("Overwatch") || Allowed("Overwatch.exe") ||
            Allowed("other-game") || Allowed("") || Allowed(null))
            throw new InvalidOperationException("Native preview process scope policy failed closed");
        Console.WriteLine("PASS: only explicitly scoped preview game and owned fixture admitted");
        // The negative check must fail without opening, allocating or starting a remote thread.
        var negative = attach.Invoke(null,
            new object[] { 0u, IntPtr.Zero, "invalid", Path.GetDirectoryName(sourceDll)!, (Func<bool>)(() => false) })!;
        if ((bool)negative.GetType().GetProperty("Success")!.GetValue(negative)!)
            throw new InvalidOperationException("Invalid HWND/PID was incorrectly admitted");
        Console.WriteLine("PASS: zero PID and HWND fail closed");

        var ownDir = Path.Combine(Path.GetTempPath(), "MiniMixerOwnedNative_" +
            Environment.ProcessId + "_" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(ownDir);
        File.Copy(sourceDll, Path.Combine(ownDir, "MiniMixerLivePresent.x64.dll"));

        using var child = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory,
                    "MiniMixerOverlay.GuardRegression.exe"),
                ArgumentList = { "--native-owned-child" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            }
        };
        if (!child.Start()) throw new InvalidOperationException("Child could not start");
        var pid = checked((uint)child.Id);
        var logFile = Path.Combine(Path.GetTempPath(), "MiniMixerLivePresent_" + pid + ".txt");
        try
        {
            var first = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12))
                .GetAwaiter().GetResult();
            if (first is null || !first.StartsWith("OWNED_NATIVE_HWND=") ||
                !long.TryParse(first["OWNED_NATIVE_HWND=".Length..], out var hwndLong) ||
                hwndLong == 0)
                throw new InvalidOperationException("Owned GUI HWND unavailable: " + first);
            var hwnd = new IntPtr(hwndLong);
            // A minimized offscreen form may not surface as MainWindowHandle.
            // The verified PID plus nonzero owned HWND from the fixture is authoritative.
            // Exercise the effective GameHookBridge -> native provider route.
            // The fixture must be admitted by the real Win32 title/PID window gate.
            var allowed = args.Contains("--native-hook-preview", StringComparer.OrdinalIgnoreCase) &&
                !args.Contains("--graphics-ipc-only", StringComparer.OrdinalIgnoreCase);
            var bridgeType = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
                "MiniMixerOverlay.App.GameHookBridge") ??
                throw new InvalidOperationException("GameHookBridge unavailable");
            var byTitle = bridgeType.GetMethod("InjectByWindowTitle", BindingFlags.Static | BindingFlags.Public) ??
                throw new MissingMethodException("InjectByWindowTitle");
            var result = byTitle.Invoke(null, new object[]
            {
                "MiniMixer Owned Native Hook Fixture", ownDir
            })!;
            var success = (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
            var message = (string)result.GetType().GetProperty("Message")!.GetValue(result)!;
            if (!allowed)
            {
                if (success || !message.Contains("--native-hook-preview"))
                    throw new InvalidOperationException("Missing preview authorization was not rejected: " + message);
                Console.WriteLine("PASS: default / IPC-only launch fails closed before injection");
                return 0;
            }
            if (!success)
                throw new InvalidOperationException("Owned loader rejected: " + message);
            if ((uint)result.GetType().GetProperty("ProcessId")!.GetValue(result)! != pid)
                throw new InvalidOperationException("Bridge bound different PID");
            Console.WriteLine("PASS: bridge selected owned HWND and loaded native DLL into PID=" + pid);
            var duplicate = byTitle.Invoke(null, new object[]
            {
                "MiniMixer Owned Native Hook Fixture", ownDir
            })!;
            if ((bool)duplicate.GetType().GetProperty("Success")!.GetValue(duplicate)!)
                throw new InvalidOperationException("Duplicate native attach was authorized");
            Console.WriteLine("PASS: duplicate native attach rejected");

            // Independent module readback, not just CreateRemoteThread exit code.
            child.Refresh();
            var moduleFound = child.Modules.Cast<ProcessModule>().Any(m =>
                m.ModuleName.Equals("MiniMixerLivePresent.x64.dll", StringComparison.OrdinalIgnoreCase));
            if (!moduleFound) throw new InvalidOperationException("Loaded module not enumerated");
            Console.WriteLine("PASS: module enumerated inside owned target");

            string status = "";
            for (int retry = 0; retry < 120; ++retry)
            {
                if (File.Exists(logFile))
                {
                    status = ReadSharedLog(logFile);
                    if (status.Contains("REQUIRED_DXGI_HOOKS_READY") ||
                        status.Contains("REQUIRED_DXGI_HOOKS_MISSING_AUTO_STOP"))
                        break;
                }
                Thread.Sleep(100);
            }
            Console.WriteLine("OWNED_DIAGNOSTIC_READY=" +
                status.Contains("REQUIRED_DXGI_HOOKS_READY"));
            if (!status.Contains("INSTALLED"))
                throw new InvalidOperationException("Native diagnostic startup not observed");

            // Verify the actual MiniMixer lifecycle owner, not just the native event helper:
            // a tracked injection must stop when the WPF hook runtime is stopped.
            var program = typeof(MiniMixerOverlay.App.Program);
            var tracked = program.GetField("_gameHookInjectedProcessIds",
                BindingFlags.Static | BindingFlags.NonPublic) ??
                throw new MissingFieldException("_gameHookInjectedProcessIds");
            ((System.Collections.Generic.HashSet<uint>)tracked.GetValue(null)!).Add(pid);
            var stopRuntime = program.GetMethod("StopGameHookRuntime",
                BindingFlags.Static | BindingFlags.NonPublic) ??
                throw new MissingMethodException("StopGameHookRuntime");
            stopRuntime.Invoke(null, null);
            for (int retry = 0; retry < 80; ++retry)
            {
                if (File.Exists(logFile))
                {
                    status = ReadSharedLog(logFile);
                    if (status.Contains("UNHOOKED")) break;
                }
                Thread.Sleep(100);
            }
            if (!status.Contains("UNHOOKED"))
                throw new InvalidOperationException("UNHOOKED not observed after owned stop");
            Console.WriteLine("PASS: MiniMixer runtime stop caused native UNHOOKED in owned process");
            Console.WriteLine("PASS: owned native attach lifecycle smoke complete; no game/real audio");
            return 0;
        }
        finally
        {
            try { child.StandardInput.WriteLine("exit"); } catch (IOException) { }
            if (!child.WaitForExit(3500))
            {
                child.Kill(entireProcessTree: true); // only our owned test child
                child.WaitForExit(3500);
            }
            if (File.Exists(logFile))
                File.WriteAllText(Path.Combine(ownDir, "native-trace.txt"), ReadSharedLog(logFile));
            Console.WriteLine("OWNED_EVIDENCE=" + ownDir);
        }
    }

    private static string ReadSharedLog(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException) { return string.Empty; }
    }
}
