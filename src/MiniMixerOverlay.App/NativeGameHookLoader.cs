namespace MiniMixerOverlay.App;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

// Native x64 implementation behind the existing GameHookBridge target-selection owner.
// No GOverlay files, USER32 global hook, or automatic retries after uncertain remote execution.
internal static class NativeGameHookLoader
{
    // PROCESS_DUP_HANDLE retains the stop event in the target itself.
    private const uint Access = 0x0002 | 0x0040 | 0x0400 | 0x0008 | 0x0010 | 0x0020;
    private const uint MemCommitReserve = 0x3000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const ushort Amd64 = 0x8664;
    private static readonly object AttachLock = new();
    // Serialize only the irreversible CreateRemoteThread boundary against
    // Stop/Restart (not the five-second remote wait).
    private static readonly object LaunchLock = new();
    // Record a remote attach from CreateRemoteThread onward, regardless of
    // the eventual (possibly unknown) LoadLibrary result.
    private static readonly ConcurrentDictionary<uint, DateTime> StartedRemoteThreads = new();
    private sealed record StopLease(DateTime StartedUtc, EventWaitHandle Handle);
    private static readonly object StopLeaseLock = new();
    private static readonly Dictionary<uint, StopLease> StopLeases = new();
    private static int RuntimeStopRequested;
    private static int RuntimeStopEpoch;
    private const uint DuplicateSameAccess = 0x00000002;
    private sealed record TargetLease(DateTime StartedUtc, IntPtr Hwnd,
        string Title, MemoryMappedFile Map);
    private static readonly Dictionary<uint, TargetLease> TargetLeases = new();
    private const uint TargetMagic = 0x4D4D5447; // "GTMM" little-endian
    private const uint TargetVersion = 1;
    private const int TargetCapacity = 560;

    // One authorized HWND/PID/process-generation/window-title tuple.
    // Created only after the pre-injection GameHookBridge target gate; kept
    // alive by the existing native loader for the target process lifetime.
    private static bool PublishQualifiedTarget(Process target, IntPtr hwnd, string title,
        DateTime startedAt)
    {
        if (hwnd == IntPtr.Zero || title.Length is 0 or > 255) return false;
        lock (StopLeaseLock)
        {
            if (TargetLeases.TryGetValue((uint)target.Id, out var existing))
                return existing.StartedUtc == startedAt &&
                       existing.Hwnd == hwnd &&
                       string.Equals(existing.Title, title, StringComparison.Ordinal);
            var name = @"Local\MiniMixerQualifiedTarget_" + target.Id;
            // Refuse any map pre-created by a different producer.
            MemoryMappedFile map;
            try { map = MemoryMappedFile.CreateNew(name, TargetCapacity); }
            catch (IOException) { return false; }
            try
            {
                using var view = map.CreateViewStream();
                using var writer = new BinaryWriter(view, Encoding.Unicode, leaveOpen: true);
                writer.Write(TargetMagic);
                writer.Write(TargetVersion);
                writer.Write((uint)target.Id);
                writer.Write((uint)Environment.ProcessId);
                writer.Write(hwnd.ToInt64());
                writer.Write(startedAt.ToFileTimeUtc());
                writer.Write((uint)title.Length);
                writer.Write(0u); // fixed 40-byte native header
                writer.Write(Encoding.Unicode.GetBytes(title));
                writer.Flush();
                TargetLeases.Add((uint)target.Id,
                    new TargetLease(startedAt, hwnd, title, map));
                return true;
            }
            catch { map.Dispose(); throw; }
        }
    }

    internal static void ResumeAfterRuntimeStart()
    {
        lock (LaunchLock)
            Volatile.Write(ref RuntimeStopRequested, 0);
    }

    internal static void StopAllStartedAttempts()
    {
        // Sticky epoch: a subsequent Start may reset the boolean but can
        // never turn an already-running old attempt into a valid new attempt.
        lock (LaunchLock)
        {
            Interlocked.Increment(ref RuntimeStopEpoch);
            Volatile.Write(ref RuntimeStopRequested, 1);
            // The stop snapshot cannot accidentally include a new
            // generation's lease after a rapid Start/Restart.
            lock (StopLeaseLock)
            {
                foreach (var pending in StopLeases)
                    if (TryGetProcessStartUtc(pending.Key, out var now) &&
                        now == pending.Value.StartedUtc)
                        _ = pending.Value.Handle.Set();
            }
        }
        PruneExitedStopLeases();
    }

    // No background poller: release event handles on the next normal
    // lifecycle operation once the recorded process instance has exited.
    private static void PruneExitedStopLeases()
    {
        lock (StopLeaseLock)
        {
            foreach (var entry in TargetLeases.ToArray())
            {
                if (TryGetProcessStartUtc(entry.Key, out var current) &&
                    current == entry.Value.StartedUtc) continue;
                entry.Value.Map.Dispose();
                TargetLeases.Remove(entry.Key);
            }
            foreach (var entry in StopLeases.ToArray())
            {
                if (TryGetProcessStartUtc(entry.Key, out var current) &&
                    current == entry.Value.StartedUtc) continue;
                entry.Value.Handle.Dispose();
                StopLeases.Remove(entry.Key);
            }
        }
    }

    private static bool TryGetProcessStartUtc(uint pid, out DateTime started)
    {
        started = default;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            if (process.HasExited) return false;
            started = process.StartTime.ToUniversalTime();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
             or Win32Exception or OverflowException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Explicit, temporary preview scope, not an anti-cheat detection mechanism.
    // Only the previously user-authorized game and our own isolated smoke fixture
    // may receive native injection. Unknown and protected titles fail closed.
    internal static bool IsApprovedPreviewProcessName(string? processName) =>
        string.Equals(processName, "OMDDSteam-Win64-Shipping", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "MiniMixerOverlay.GuardRegression", StringComparison.OrdinalIgnoreCase);

    // Prevent unintended anti-cheat interference. This is a fail-closed indicator,
    // not a guarantee of compatibility with all third-party protection systems.
    private static readonly string[] ProtectionModules =
    {
        "easyanticheat", "eac_", "battleye", "bedaisy", "beclient",
        "vgk", "xigncode", "gameguard", "anticheat"
    };

    internal static GameHookInjectResult Attach(uint pid, IntPtr hwnd, string title, string assetsDirectory,
        Func<bool> revalidateTarget)
    {
        var dllPath = Path.GetFullPath(Path.Combine(assetsDirectory, "MiniMixerLivePresent.x64.dll"));
        GameHookInjectResult Fail(string why) => new()
        {
            Success = false, Message = why, ProcessId = pid, WindowTitle = title, DllPath = dllPath
        };

        if (!Environment.Is64BitProcess)
            return Fail("Native Preview setzt einen x64-MiniMixer voraus.");
        if (pid == 0 || pid == Environment.ProcessId || hwnd == IntPtr.Zero || !revalidateTarget())
            return Fail("Native Attach verweigert: Zielidentitaet ungueltig oder gewechselt.");
        PruneExitedStopLeases();
        if (!File.Exists(dllPath) ||
            dllPath.Contains(Path.DirectorySeparatorChar + "legacy" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(assetsDirectory, "n_overlay.x64.dll")))
            return Fail("Native DLL fehlt oder Legacy-Assetpfad ist nicht zulaessig.");

        lock (AttachLock)
        {
            if (Volatile.Read(ref RuntimeStopRequested) != 0)
                return Fail("Native Preview Runtime gestoppt; neuer Attach nicht zulaessig.");
            var attemptEpoch = Volatile.Read(ref RuntimeStopEpoch);
            if (StartedRemoteThreads.TryGetValue(pid, out var attemptedStart))
            {
                if (TryGetProcessStartUtc(pid, out var currentStart) && currentStart == attemptedStart)
                    return Fail("Fuer diese Prozessinstanz wurde bereits ein Attach-Versuch gestartet; kein Retry.");
                StartedRemoteThreads.TryRemove(pid, out _); // exited/reused PID
            }
            try
            {
                using var target = Process.GetProcessById(checked((int)pid));
                if (target.HasExited) return Fail("Zielprozess bereits beendet.");
                // Check before module enumeration, remote allocations or threads.
                // User consent for one game is not consent to inject into others.
                if (!IsApprovedPreviewProcessName(target.ProcessName))
                    return Fail("Native Preview: Prozess nicht fuer Hook-Tests freigegeben.");
                var startedAt = target.StartTime.ToUniversalTime();
                var remoteModules = ReadModules(target);
                if (remoteModules.Any(m => m.ModuleName.StartsWith("MiniMixerLivePresent.", StringComparison.OrdinalIgnoreCase)))
                    return Fail("Native DLL ist bereits geladen; kein zweiter Hook.");
                if (remoteModules.Any(m => m.ModuleName.StartsWith("n_overlay", StringComparison.OrdinalIgnoreCase)))
                    return Fail("Legacy-GOverlay bereits geladen; keinen zweiten Hook installieren.");
                if (remoteModules.Any(m => ProtectionModules.Any(p =>
                    m.ModuleName.Contains(p, StringComparison.OrdinalIgnoreCase))))
                    return Fail("Schutzmodul erkannt; keine Injection in geschuetztes Spiel.");

                var processHandle = OpenProcess(Access, false, pid);
                if (processHandle == IntPtr.Zero)
                    return Fail("Prozesszugriff verweigert: " + Marshal.GetLastWin32Error());
                try
                {
                    if (!IsWow64Process2(processHandle, out var processMachine, out var nativeMachine) ||
                        processMachine != 0 || nativeMachine != Amd64)
                        return Fail("Zielarchitektur nicht sicher als x64 bestaetigt.");

                    // Resolve forwarded LoadLibraryW (often KernelBase) by module-relative RVA.
                    // Never assume kernel32/KernelBase have identical bases in both processes.
                    var kernel = GetModuleHandleW("kernel32.dll");
                    var entry = kernel != IntPtr.Zero ? GetProcAddress(kernel, "LoadLibraryW") : IntPtr.Zero;
                    if (entry == IntPtr.Zero) return Fail("LoadLibraryW nicht aufloesbar.");
                    using var self = Process.GetCurrentProcess();
                    var localModule = ReadModules(self).FirstOrDefault(m =>
                        entry.ToInt64() >= m.BaseAddress.ToInt64() &&
                        entry.ToInt64() - m.BaseAddress.ToInt64() < m.ModuleMemorySize);
                    if (localModule == null) return Fail("LoadLibraryW-Modul nicht verifizierbar.");
                    var remoteModule = remoteModules.FirstOrDefault(m =>
                        string.Equals(m.ModuleName, localModule.ModuleName, StringComparison.OrdinalIgnoreCase));
                    if (remoteModule == null) return Fail("LoadLibraryW-Modul im Zielprozess fehlt.");
                    long offset = entry.ToInt64() - localModule.BaseAddress.ToInt64();
                    if (offset < 0 || offset >= remoteModule.ModuleMemorySize)
                        return Fail("LoadLibraryW-RVA ausserhalb Zielmodul.");
                    var remoteEntry = new IntPtr(checked(remoteModule.BaseAddress.ToInt64() + offset));

                    if (!revalidateTarget() || target.HasExited ||
                        target.StartTime.ToUniversalTime() != startedAt)
                        return Fail("Zielidentitaet vor Native Attach gewechselt.");
                    // Native side can now independently reject auxiliary
                    // windows and replaced/unknown swapchain identities.
                    if (!PublishQualifiedTarget(target, hwnd, title, startedAt))
                        return Fail("Qualifizierter Native HWND-Vertrag nicht verfuegbar.");

                    var bytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
                    var remoteString = VirtualAllocEx(processHandle, IntPtr.Zero, (nuint)bytes.Length,
                        MemCommitReserve, PageReadWrite);
                    if (remoteString == IntPtr.Zero)
                        return Fail("Remote-Pfadspeicher konnte nicht reserviert werden.");
                    bool mayFree = true;
                    try
                    {
                        if (!WriteProcessMemory(processHandle, remoteString, bytes, (nuint)bytes.Length,
                            out var written) || written != (nuint)bytes.Length)
                            return Fail("Remote-DLL-Pfad konnte nicht geschrieben werden.");
                        IntPtr thread;
                        lock (LaunchLock)
                        {
                            // Last eligibility check, durable stop handoff,
                            // remote launch and generation registration are
                            // indivisible with respect to Stop/Restart.
                            if (!revalidateTarget() ||
                                Volatile.Read(ref RuntimeStopRequested) != 0 ||
                                Volatile.Read(ref RuntimeStopEpoch) != attemptEpoch)
                                return Fail("Native Attach aus veralteter Runtime-Session verweigert.");
                            if (!EnsureDurableStopEvent(pid, startedAt, processHandle))
                                return Fail("Native Stop-Event-Handoff verweigert; kein Attach.");
                            thread = CreateRemoteThread(processHandle, IntPtr.Zero, 0,
                                remoteEntry, remoteString, 0, out _);
                            if (thread == IntPtr.Zero)
                                return Fail("Native Attach vom Betriebssystem verweigert: " +
                                    Marshal.GetLastWin32Error());
                            StartedRemoteThreads[pid] = startedAt;
                        }
                        // The old session may have stopped after registration.
                        // If so the generation-aware stop below will signal
                        // the duplicated target-owned event.
                        if (Volatile.Read(ref RuntimeStopRequested) != 0 ||
                            Volatile.Read(ref RuntimeStopEpoch) != attemptEpoch ||
                            !revalidateTarget())
                            _ = RequestStopExpected(pid, startedAt);
                        try
                        {
                            var wait = WaitForSingleObject(thread, 5000);
                            if (wait != WaitObject0)
                            {
                                mayFree = false; // worker may still be reading path
                                return Fail(wait == WaitTimeout
                                    ? "Native Attach Timeout; kein automatischer Wiederholungsversuch."
                                    : "Native Attach Status unbekannt; kein Wiederholungsversuch.");
                            }
                            if (!GetExitCodeThread(thread, out var exitCode) || exitCode == 0)
                                return Fail("LoadLibraryW hat keinen erfolgreichen DLL-Load gemeldet.");
                        }
                        finally { CloseHandle(thread); }
                    }
                    finally
                    {
                        if (mayFree) VirtualFreeEx(processHandle, remoteString, 0, MemRelease);
                    }

                    target.Refresh();
                    if (target.HasExited || target.StartTime.ToUniversalTime() != startedAt)
                        return Fail("Zielprozess nach Native Attach nicht mehr gueltig.");
                    if (Volatile.Read(ref RuntimeStopRequested) != 0 ||
                        Volatile.Read(ref RuntimeStopEpoch) != attemptEpoch ||
                        !revalidateTarget())
                    {
                        _ = RequestStopExpected(pid, startedAt);
                        return Fail("Native Attach ist aus einer bereits beendeten Runtime-Session.");
                    }
                    // A successful thread exit is NOT proof of IPC, GPU frames, or input.
                    var installed = ReadModules(target).Any(m =>
                        string.Equals(m.FileName, dllPath, StringComparison.OrdinalIgnoreCase));
                    if (!installed)
                        return Fail("LoadLibraryW beendet, DLL im Zielprozess nicht nachgewiesen.");
                    return new GameHookInjectResult
                    {
                        Success = true, ProcessId = pid, WindowTitle = title, DllPath = dllPath,
                        Message = "Native DLL im Zielprozess geladen; IPC/Frames/Input noch ungeprueft."
                    };
                }
                finally { CloseHandle(processHandle); }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException
                or IOException or UnauthorizedAccessException or OverflowException)
            {
                return Fail("Native Attach abgebrochen: " + ex.Message);
            }
        }
    }

    // A stopped/obsolete PID MUST NOT signal a newly started generation.
    internal static bool RequestStop(uint pid) =>
        StartedRemoteThreads.TryGetValue(pid, out var expected) &&
        RequestStopExpected(pid, expected);

    private static bool RequestStopExpected(uint pid, DateTime expected)
    {
        if (!TryGetProcessStartUtc(pid, out var now) || now != expected)
            return false;
        lock (StopLeaseLock)
        {
            if (!StopLeases.TryGetValue(pid, out var lease) ||
                lease.StartedUtc != expected) return false;
            try { return lease.Handle.Set(); }
            catch (ObjectDisposedException) { return false; }
        }
    }

    private static bool EnsureDurableStopEvent(uint pid, DateTime expected, IntPtr processHandle)
    {
        lock (StopLeaseLock)
        {
            if (StopLeases.TryGetValue(pid, out var existing))
                return existing.StartedUtc == expected;
            EventWaitHandle? stop = null;
            try
            {
                stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                    @"Local\MiniMixerOverlayStop_" + pid, out var createdNew);
                if (!createdNew) return false; // foreign/stale event: deny
                if (!DuplicateHandle(GetCurrentProcess(), stop.SafeWaitHandle.DangerousGetHandle(),
                    processHandle, out var remoteCopy, 0, false, DuplicateSameAccess) ||
                    remoteCopy == IntPtr.Zero)
                    return false;
                // Both processes now own the same kernel event. On normal
                // host Stop it is signaled; target-side duplicate keeps it
                // alive even if the host exits before native CreateEventW.
                StopLeases[pid] = new StopLease(expected, stop);
                stop = null; // ownership transferred to StopLeases
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
            finally { stop?.Dispose(); }
        }
    }

    private static List<ProcessModule> ReadModules(Process process)
    {
        process.Refresh();
        return process.Modules.Cast<ProcessModule>().ToList();
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle,
        IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string symbol);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, nuint length,
        out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, nuint stackSize,
        IntPtr startAddress, IntPtr argument, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint timeoutMs);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);
}
