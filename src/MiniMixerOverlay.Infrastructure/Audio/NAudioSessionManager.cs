namespace MiniMixerOverlay.Infrastructure.Audio;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MiniMixerOverlay.Core.Interfaces;
using MiniMixerOverlay.Core.Models;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

public class NAudioSessionManager : IAudioSessionManager
{
    private static readonly Role[] CandidateRoles =
    {
        Role.Console,
        Role.Multimedia,
        Role.Communications
    };

    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly Dictionary<string, AudioSessionControl> _sessions = new();
    private readonly Dictionary<string, MMDevice> _monitoredDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _deferIconExtractionUntilUtc = DateTime.UtcNow.AddSeconds(18);
    private Action<AudioSessionInfo>? _onCreated;
    private Action<string>? _onDestroyed;

    // Icons only once per exe path to avoid repeated disk I/O
    private static readonly Dictionary<string, byte[]?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _exePathByNameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, ProcessMetaCacheItem> _processMetaCache = new();
    private static readonly object _processMetaLock = new();
    private static readonly object _exePathByNameLock = new();
    private static readonly TimeSpan ProcessMetaCacheTtl = TimeSpan.FromSeconds(8);
    private const int ProcessQueryLimitedInformation = 0x1000;

    private sealed class ProcessMetaCacheItem
    {
        public DateTime CapturedUtc { get; init; }
        public string ExePath { get; init; } = string.Empty;
        public string ExeName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public string OriginalFilename { get; init; } = string.Empty;
    }

    public NAudioSessionManager()
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
    }

    public List<AudioSessionInfo> EnumerateSessions()
    {
        var result = new List<AudioSessionInfo>();
        _sessions.Clear();
        try
        {
            var seenLogicalSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defaultDeviceIds = GetDefaultRenderDeviceIds();
            foreach (var device in EnumerateCandidateDevices())
            {
                var deviceId = device.ID ?? string.Empty;
                var deviceName = TryGetFriendlyName(device);
                var isDefaultDevice = defaultDeviceIds.Contains(deviceId);
                var mgr = device.AudioSessionManager;
                var sessions = mgr.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                    {
                        continue;
                    }

                    var rawSid = string.Empty;
                    uint pid = 0;
                    try { rawSid = session.GetSessionIdentifier ?? string.Empty; } catch { }
                    try { pid = session.GetProcessID; } catch { }

                    // Session identifiers can be identical on different render endpoints.
                    // Device identity is part of the logical session key by design.
                    var logicalKey = string.IsNullOrWhiteSpace(rawSid)
                        ? $"{deviceId}|pid:{pid}:display:{session.DisplayName}"
                        : $"{deviceId}|{rawSid}";
                    if (!seenLogicalSessions.Add(logicalKey))
                    {
                        continue;
                    }

                    var controlKey = BuildControlKey(deviceId, rawSid, pid, i, session.DisplayName ?? string.Empty);
                    var info = ExtractSessionInfo(
                        session,
                        controlKey,
                        deviceId,
                        deviceName,
                        isDefaultDevice);
                    if (info != null)
                    {
                        Debug.WriteLine(
                            $"[Audio] {info.DisplayName} | {info.ExeName} | {info.OutputDeviceName} | {info.Volume * 100:F0}%");
                        result.Add(info);
                        _sessions[info.SessionIdentifier] = session;
                    }
                }
            }

            Debug.WriteLine($"[Audio] Total sessions: {result.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] Enumerate error: {ex.Message}");
        }

        return result;
    }

    private static string BuildControlKey(string deviceId, string sid, uint pid, int index, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            return $"{deviceId}|{sid}";
        }

        if (pid != 0 || !string.IsNullOrWhiteSpace(displayName))
        {
            return $"{deviceId}|pid:{pid}|display:{displayName}";
        }

        return $"{deviceId}|idx:{index}";
    }

    private IEnumerable<MMDevice> EnumerateCandidateDevices()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Enumerate every active render endpoint, not only the three current defaults.
        // Windows keeps per-app session volume per endpoint, so MiniMixer must see all of them.
        MMDeviceCollection? endpoints = null;
        try
        {
            endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        }
        catch
        {
            // fall back to default-role endpoints below
        }

        if (endpoints != null)
        {
            foreach (var device in endpoints)
            {
                if (device == null || string.IsNullOrWhiteSpace(device.ID) || !seen.Add(device.ID))
                {
                    continue;
                }

                yield return device;
            }
        }

        foreach (var role in CandidateRoles)
        {
            MMDevice? device = null;
            try
            {
                device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            }
            catch
            {
                // ignore unavailable endpoint for this role
            }

            if (device == null || string.IsNullOrWhiteSpace(device.ID) || !seen.Add(device.ID))
            {
                continue;
            }

            yield return device;
        }

        if (seen.Count == 0)
        {
            yield return _device;
        }
    }

    private HashSet<string> GetDefaultRenderDeviceIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in CandidateRoles)
        {
            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                if (device != null && !string.IsNullOrWhiteSpace(device.ID))
                {
                    result.Add(device.ID);
                }
            }
            catch
            {
                // best effort only
            }
        }

        return result;
    }

    private static string TryGetFriendlyName(MMDevice device)
    {
        try
        {
            return device.FriendlyName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryResolveProcessMetadata(uint pid, out string exePath, out string exeName, out string displayName, out string companyName, out string productName, out string originalFilename)
    {
        exePath = string.Empty;
        exeName = string.Empty;
        displayName = string.Empty;
        companyName = string.Empty;
        productName = string.Empty;
        originalFilename = string.Empty;

        var now = DateTime.UtcNow;
        lock (_processMetaLock)
        {
            if (_processMetaCache.TryGetValue(pid, out var cached) &&
                (now - cached.CapturedUtc) <= ProcessMetaCacheTtl)
            {
                exePath = cached.ExePath;
                exeName = cached.ExeName;
                displayName = cached.DisplayName;
                companyName = cached.CompanyName;
                productName = cached.ProductName;
                originalFilename = cached.OriginalFilename;
                return true;
            }
        }

        var resolvedPath = string.Empty;
        var resolvedExeName = string.Empty;
        var resolvedDisplay = string.Empty;
        var resolvedCompany = string.Empty;
        var resolvedProduct = string.Empty;
        var resolvedOriginalFilename = string.Empty;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            try
            {
                resolvedPath = proc.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // access denied for some processes; ignore
                if (TryQueryFullProcessImagePath(pid, out var queryPath))
                {
                    resolvedPath = queryPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                resolvedExeName = Path.GetFileName(resolvedPath);
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(resolvedPath);
                    resolvedDisplay = fvi.FileDescription ?? fvi.ProductName ?? string.Empty;
                    resolvedCompany = fvi.CompanyName ?? string.Empty;
                    resolvedProduct = fvi.ProductName ?? string.Empty;
                    resolvedOriginalFilename = fvi.OriginalFilename ?? string.Empty;
                }
                catch
                {
                    // ignore file version lookup errors
                }
            }
            else
            {
                resolvedExeName = (proc.ProcessName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(resolvedExeName))
                {
                    resolvedPath = resolvedExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? resolvedExeName
                        : $"{resolvedExeName}.exe";
                }
            }

            if ((!Path.IsPathRooted(resolvedPath) || !File.Exists(resolvedPath)) &&
                !string.IsNullOrWhiteSpace(resolvedExeName))
            {
                var resolvedByName = ResolveExecutablePathByName(resolvedExeName);
                if (!string.IsNullOrWhiteSpace(resolvedByName))
                {
                    resolvedPath = resolvedByName;
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedDisplay) && File.Exists(resolvedPath))
            {
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(resolvedPath);
                    resolvedDisplay = fvi.FileDescription ?? fvi.ProductName ?? string.Empty;
                    resolvedCompany = fvi.CompanyName ?? resolvedCompany;
                    resolvedProduct = fvi.ProductName ?? resolvedProduct;
                    resolvedOriginalFilename = fvi.OriginalFilename ?? resolvedOriginalFilename;
                }
                catch
                {
                    // ignore file version lookup errors
                }
            }
        }
        catch
        {
            // ignore process lookup failures
        }

        lock (_processMetaLock)
        {
            _processMetaCache[pid] = new ProcessMetaCacheItem
            {
                CapturedUtc = now,
                ExePath = resolvedPath,
                ExeName = resolvedExeName,
                DisplayName = resolvedDisplay,
                CompanyName = resolvedCompany,
                ProductName = resolvedProduct,
                OriginalFilename = resolvedOriginalFilename
            };
        }

        exePath = resolvedPath;
        exeName = resolvedExeName;
        displayName = resolvedDisplay;
        companyName = resolvedCompany;
        productName = resolvedProduct;
        originalFilename = resolvedOriginalFilename;
        return !string.IsNullOrWhiteSpace(exePath) || !string.IsNullOrWhiteSpace(exeName);
    }

    private static bool TryQueryFullProcessImagePath(uint pid, out string fullPath)
    {
        fullPath = string.Empty;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var capacity = 1024;
            var buffer = new System.Text.StringBuilder(capacity);
            if (!QueryFullProcessImageName(handle, 0, buffer, ref capacity))
            {
                return false;
            }

            var path = buffer.ToString();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            fullPath = path.Trim();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private AudioSessionInfo? ExtractSessionInfo(AudioSessionControl session, string controlKey, string outputDeviceId, string outputDeviceName, bool isDefaultOutputDevice)
    {
        try
        {
            var sid = session.GetSessionIdentifier ?? string.Empty;
            var displayName = session.DisplayName ?? string.Empty;
            uint pid = 0;
            var exePath = string.Empty;
            var exeName = string.Empty;
            var companyName = string.Empty;
            var productName = string.Empty;
            var originalFilename = string.Empty;
            byte[]? iconBytes = null;

            var isSystemSound = false;
            if (session.IsSystemSoundsSession)
            {
                isSystemSound = true;
                displayName = "Lautstaerke";
                exeName = "SystemSound";
                exePath = "__system_sound__";
            }

            try { pid = session.GetProcessID; } catch { }

            if (string.IsNullOrWhiteSpace(exePath) && pid > 0)
            {
                TryResolveProcessMetadata(pid, out exePath, out exeName, out var processDisplayName, out companyName, out productName, out originalFilename);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = processDisplayName;
                }
            }

            // Fallback: parse exe path from session identifier string
            if (string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(sid))
            {
                exePath = ParseExePathFromSid(sid);
                exeName = Path.GetFileName(exePath);
            }

            if ((!Path.IsPathRooted(exePath) || !File.Exists(exePath)) &&
                !string.IsNullOrWhiteSpace(exeName) &&
                !string.Equals(exePath, "__system_sound__", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedByName = ResolveExecutablePathByName(exeName);
                if (!string.IsNullOrWhiteSpace(resolvedByName))
                {
                    exePath = resolvedByName;
                }
            }

            // Extract app icon from exe path (cached)
            if (!string.IsNullOrWhiteSpace(exePath) && !string.Equals(exePath, "__system_sound__", StringComparison.OrdinalIgnoreCase))
            {
                var isDeferredPhase = DateTime.UtcNow < _deferIconExtractionUntilUtc;
                if (!_iconCache.TryGetValue(exePath, out iconBytes) && !isDeferredPhase)
                {
                    iconBytes = null;
                    if (File.Exists(exePath))
                    {
                        try
                        {
                            var icon = Icon.ExtractAssociatedIcon(exePath);
                            if (icon != null)
                            {
                                using var ms = new MemoryStream();
                                icon.ToBitmap().Save(ms, ImageFormat.Png);
                                iconBytes = ms.ToArray();
                                Debug.WriteLine($"[Icon] {exeName}: {iconBytes.Length} bytes");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Icon] Error for {exeName}: {ex.Message}");
                        }
                    }

                    _iconCache[exePath] = iconBytes;
                }
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = string.IsNullOrWhiteSpace(exeName)
                    ? "Audio App"
                    : Path.GetFileNameWithoutExtension(exeName);
            }

            if (string.IsNullOrWhiteSpace(exeName))
            {
                exeName = displayName;
                exePath = displayName;
            }

            return new AudioSessionInfo
            {
                SessionIdentifier = controlKey,
                ProcessId = pid,
                Volume = session.SimpleAudioVolume.Volume,
                IsMuted = session.SimpleAudioVolume.Mute,
                DisplayName = displayName,
                AppIdentityKey = AppIdentity.CreateKey(exePath, exeName, displayName, companyName, productName, originalFilename),
                ExePath = exePath,
                ExeName = exeName,
                CompanyName = companyName,
                ProductName = productName,
                OriginalFilename = originalFilename,
                IconBytes = iconBytes,
                IsSystemSound = isSystemSound,
                OutputDeviceId = outputDeviceId,
                OutputDeviceName = outputDeviceName,
                IsDefaultOutputDevice = isDefaultOutputDevice
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] Extract error: {ex.Message}");
            return null;
        }
    }

    private static string ParseExePathFromSid(string sid)
    {
        try
        {
            var slashIdx = sid.IndexOf('\\');
            if (slashIdx < 0) return string.Empty;

            var pathPart = sid.Substring(slashIdx);
            var pctIdx = pathPart.IndexOf("%b{", StringComparison.OrdinalIgnoreCase);
            if (pctIdx < 0) pctIdx = pathPart.IndexOf('%');
            if (pctIdx > 0) pathPart = pathPart[..pctIdx];

            if (pathPart.StartsWith("\\Device\\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
            {
                var secondSlash = pathPart.IndexOf('\\', 1);
                if (secondSlash < 0) return string.Empty;
                var afterDevice = pathPart.Substring(secondSlash);

                // Try C first, then D-Z
                foreach (var drive in "CDEFGHIJKLMNOPQRSTUVWXYZ")
                {
                    var testPath = drive + ":" + afterDevice;
                    if (File.Exists(testPath)) return testPath;
                }

                return "C:" + afterDevice;
            }

            return pathPart;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveExecutablePathByName(string exeName)
    {
        var normalizedExe = NormalizeExeFileName(exeName);
        if (string.IsNullOrWhiteSpace(normalizedExe))
        {
            return string.Empty;
        }

        lock (_exePathByNameLock)
        {
            if (_exePathByNameCache.TryGetValue(normalizedExe, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }
        }

        string Cache(string path)
        {
            lock (_exePathByNameLock)
            {
                _exePathByNameCache[normalizedExe] = path;
            }

            return path;
        }

        var processName = Path.GetFileNameWithoutExtension(normalizedExe);
        try
        {
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return Cache(path);
                    }
                }
                catch
                {
                    // ignore inaccessible process module
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // ignore process enumeration failures
        }

        var appPath = ReadAppPathFromRegistry(normalizedExe);
        if (!string.IsNullOrWhiteSpace(appPath) && File.Exists(appPath))
        {
            return Cache(appPath);
        }

        var knownPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", normalizedExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", normalizedExe)
        };

        foreach (var candidate in knownPaths)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Cache(candidate);
            }
        }

        return string.Empty;
    }

    private static string NormalizeExeFileName(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.exe";
    }

    private static string ReadAppPathFromRegistry(string exeName)
    {
        static string ReadFromHive(RegistryKey hive, string exe)
        {
            try
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                var raw = key?.GetValue(string.Empty) as string;
                return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        var userPath = ReadFromHive(Registry.CurrentUser, exeName);
        if (!string.IsNullOrWhiteSpace(userPath))
        {
            return userPath;
        }

        return ReadFromHive(Registry.LocalMachine, exeName);
    }

    public void SetVolume(string sessionIdentifier, float volume)
    {
        try
        {
            if (_sessions.TryGetValue(sessionIdentifier, out var session))
            {
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] SetVolume error: {ex.Message}");
        }
    }

    public void SetMute(string sessionIdentifier, bool mute)
    {
        try
        {
            if (_sessions.TryGetValue(sessionIdentifier, out var session))
            {
                session.SimpleAudioVolume.Mute = mute;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] SetMute error: {ex.Message}");
        }
    }

    public void OnSessionCreated(Action<AudioSessionInfo> callback) { _onCreated = callback; }
    public void OnSessionDestroyed(Action<string> callback) { _onDestroyed = callback; }

    public void StartMonitoring()
    {
        try
        {
            foreach (var device in EnumerateCandidateDevices())
            {
                if (_monitoredDevices.ContainsKey(device.ID))
                {
                    continue;
                }

                device.AudioSessionManager.OnSessionCreated += OnNewSessionCreated;
                _monitoredDevices[device.ID] = device;
            }
        }
        catch
        {
            // ignore monitor start failures
        }
    }

    public void StopMonitoring()
    {
        try
        {
            foreach (var pair in _monitoredDevices)
            {
                try
                {
                    pair.Value.AudioSessionManager.OnSessionCreated -= OnNewSessionCreated;
                }
                catch
                {
                    // ignore per-device unsubscribe failures
                }
            }
        }
        catch
        {
            // ignore monitor stop failures
        }
        finally
        {
            _monitoredDevices.Clear();
        }
    }

    private void OnNewSessionCreated(object sender, object newSession)
    {
        // Brief delay to let the session fully initialize before enumerating
        System.Threading.Tasks.Task.Delay(140).ContinueWith(_ =>
        {
            try
            {
                var sessions = EnumerateSessions();
                if (sessions.Count > 0)
                {
                    _onCreated?.Invoke(sessions[sessions.Count - 1]);
                }
            }
            catch
            {
                // ignore callback failures
            }
        });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, System.Text.StringBuilder text, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
