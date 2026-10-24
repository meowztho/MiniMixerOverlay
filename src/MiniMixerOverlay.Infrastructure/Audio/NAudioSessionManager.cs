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
    internal const string MasterExeKey = "__system_master__";
    internal const string MasterControlPrefix = "__system_master__|";
    internal static string BuildMasterControlKey(string deviceId) => MasterControlPrefix + deviceId;

    private static readonly Role[] CandidateRoles =
    {
        Role.Console,
        Role.Multimedia,
        Role.Communications
    };

    private readonly MMDeviceEnumerator _enumerator;
    private readonly object _enumerationSync = new();
    private readonly SessionSnapshotIndex<AudioSessionControl> _sessions = new();
    // Polling owns these devices across refreshes. NAudio activates COM
    // audio managers and creates callback infrastructure per new MMDevice;
    // retaining an unchanged endpoint avoids repeating that native work.
    // The role->ID mapping is re-read on EVERY scan, so unplugged devices
    // are removed before publishing the new session map.
    private readonly Dictionary<string, MMDevice> _scanDevices = new(StringComparer.OrdinalIgnoreCase);
    private List<AudioSessionInfo> _lastCompleteEnumeration = new();
    private readonly Dictionary<string, MMDevice> _monitoredDevices = new(StringComparer.OrdinalIgnoreCase);
    private bool _monitoringActive; // Guarded by _enumerationSync.

    private readonly DateTime _deferIconExtractionUntilUtc = DateTime.UtcNow.AddSeconds(18);
    private Action<AudioSessionInfo>? _onCreated;
    private Action<string>? _onDestroyed;
    private int _monitoringGeneration;

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
    }

    public NAudioSessionManager()
    {
        _enumerator = new MMDeviceEnumerator();
    }

    public List<AudioSessionInfo> EnumerateSessions() => EnumerateSessions(out _);

    private List<AudioSessionInfo> EnumerateSessions(out IReadOnlyList<AudioSessionInfo> addedSessions)
    {
        addedSessions = Array.Empty<AudioSessionInfo>();
        var removed = Array.Empty<string>();
        List<MMDevice> obsoleteDevices = new();
        List<AudioSessionInfo> result;
        lock (_enumerationSync)
        {
            result = new List<AudioSessionInfo>();
            var controls = new Dictionary<string, AudioSessionControl>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var scanDevices = ResolveCurrentScanDevices(
                    out var multimediaDevice, out obsoleteDevices);
                if (_monitoringActive)
                    SynchronizeMonitoringWithCurrentEndpoints(scanDevices);
                foreach (var device in scanDevices)
                {
                    // SessionCollection is a snapshot of the endpoint's
                    // sessions. Update its enumeration while retaining the
                    // existing endpoint/AudioSessionManager COM owner.
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired)
                        {
                            session.Dispose();
                            continue;
                        }

                        var rawSid = string.Empty;
                        uint pid = 0;
                        try { rawSid = session.GetSessionIdentifier ?? string.Empty; } catch { }
                        try { pid = session.GetProcessID; } catch { }

                        // Identity includes the endpoint: identical WASAPI SIDs on two devices
                        // represent different volume controls.
                        var controlKey = BuildControlKey(device.ID, rawSid, pid, i);
                        if (controls.ContainsKey(controlKey))
                        {
                            session.Dispose();
                            continue;
                        }
                        var info = ExtractSessionInfo(session, controlKey);
                        if (info == null)
                        {
                            // A short-lived/unreadable session must not block updates for other apps.
                            // Keep only its previous known state, if any, until a later scan resolves it.
                            if (_sessions.TryGet(controlKey, out var previous) && previous != null)
                            {
                                var previousInfo = _lastCompleteEnumeration.FirstOrDefault(
                                    known => string.Equals(known.SessionIdentifier, controlKey, StringComparison.OrdinalIgnoreCase));
                                if (previousInfo != null)
                                {
                                    controls.Add(controlKey, previous);
                                    result.Add(previousInfo);
                                }
                            }
                            // The current wrapper was not published, even when
                            // its previously published control is reused.
                            session.Dispose();
                            continue;
                        }

                        controls.Add(controlKey, session);
                        result.Add(info);
                        Debug.WriteLine($"[Audio] {info.DisplayName} | {info.ExeName} | {info.Volume * 100:F0}%");
                    }
                }

                // System master volume is a device endpoint control, NOT the
                // "System Sounds" CoreAudio application session. Bind only to
                // the current default Multimedia render endpoint, which is
                // also the target Windows uses for system-wide output volume.
                try
                {
                    // This owner was resolved from the current Multimedia
                    // default in this scan; no additional endpoint activation.
                    if (multimediaDevice == null)
                        throw new InvalidOperationException("No current default Multimedia render endpoint");
                    var master = multimediaDevice.AudioEndpointVolume;
                    result.Add(new AudioSessionInfo
                    {
                        SessionIdentifier = BuildMasterControlKey(multimediaDevice.ID),
                        ProcessId = 0,
                        ExePath = MasterExeKey,
                        ExeName = "SystemMaster",
                        DisplayName = "Systemlautstärke",
                        Volume = master.MasterVolumeLevelScalar,
                        IsMuted = master.Mute,
                        IsSystemSound = false
                    });
                }
                catch (Exception ex)
                {
                    // Never substitute a system-sounds session or another
                    // endpoint when default master audio is unavailable.
                    Debug.WriteLine($"[Audio] Master volume unavailable: {ex.Message}");
                }

                removed = _sessions.Replace(controls, out var addedKeys, out var retiredControls).ToArray();
                // SessionControl implements IDisposable and owns an underlying
                // COM session interface. Dispose previous scan wrappers now,
                // rather than accumulating them until the next full GC.
                // SetVolume/SetMute share this lock to exclude in-flight writes.
                foreach (var retired in retiredControls)
                {
                    try
                    {
                        // NAudio caches SimpleAudioVolume on the session
                        // control. It must NOT be disposed on every read or
                        // manual mute/volume operation, only when the
                        // published session wrapper is retired under the
                        // shared command/refresh lock.
                        retired.SimpleAudioVolume.Dispose();
                        retired.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Audio] Retired session release failed: {ex.Message}");
                        try { retired.Dispose(); } catch { }
                    }
                }
                var addedSet = new HashSet<string>(addedKeys, StringComparer.OrdinalIgnoreCase);
                addedSessions = result.Where(session => addedSet.Contains(session.SessionIdentifier)).ToArray();
                _lastCompleteEnumeration = result;
                Debug.WriteLine($"[Audio] Total sessions: {result.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Enumerate error: {ex.Message}");
                return new List<AudioSessionInfo>(_lastCompleteEnumeration);
            }
            finally
            {
                foreach (var previousDevice in obsoleteDevices)
                {
                    try { previousDevice.Dispose(); }
                    catch (Exception ex) { Debug.WriteLine($"[Audio] Retired endpoint release failed: {ex.Message}"); }
                }
            }
        }

        // Never invoke a subscriber while holding the enumeration lock.
        foreach (var sessionId in removed)
        {
            try { _onDestroyed?.Invoke(sessionId); }
            catch (Exception ex) { Debug.WriteLine($"[Audio] Destroy callback error: {ex.Message}"); }
        }

        return result;
    }

    internal static string BuildControlKey(string deviceId, string sid, uint pid, int index)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            return $"{deviceId}|{sid}";
        }

        return $"{deviceId}|pid:{pid}|idx:{index}";
    }

    // Call only while holding _enumerationSync. Caller disposes retired
    // devices AFTER retiring session wrappers that referred to them.
    private List<MMDevice> ResolveCurrentScanDevices(
        out MMDevice? multimediaDevice, out List<MMDevice> retiredDevices)
    {
        multimediaDevice = null;
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new List<MMDevice>();
        foreach (var role in CandidateRoles)
        {
            MMDevice? candidate = null;
            try
            {
                candidate = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                var id = candidate.ID;
                if (activeIds.Add(id))
                {
                    if (!_scanDevices.TryGetValue(id, out var existing))
                    {
                        existing = candidate;
                        _scanDevices.Add(id, existing);
                        candidate = null; // Ownership transferred to persistent scan cache.
                    }
                    active.Add(existing);
                }
                if (role == Role.Multimedia)
                {
                    _scanDevices.TryGetValue(id, out multimediaDevice);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Current {role} render endpoint unavailable: {ex.Message}");
            }
            finally
            {
                // Includes duplicate Console/Multimedia/Communications wrappers.
                candidate?.Dispose();
            }
        }

        retiredDevices = new List<MMDevice>();
        foreach (var pair in _scanDevices.ToArray())
        {
            if (activeIds.Contains(pair.Key)) continue;
            _scanDevices.Remove(pair.Key);
            retiredDevices.Add(pair.Value);
        }
        return active;
    }

    private static bool TryResolveProcessMetadata(uint pid, out string exePath, out string exeName, out string displayName)
    {
        exePath = string.Empty;
        exeName = string.Empty;
        displayName = string.Empty;

        var now = DateTime.UtcNow;
        lock (_processMetaLock)
        {
            if (_processMetaCache.TryGetValue(pid, out var cached) &&
                (now - cached.CapturedUtc) <= ProcessMetaCacheTtl)
            {
                exePath = cached.ExePath;
                exeName = cached.ExeName;
                displayName = cached.DisplayName;
                return true;
            }
        }

        var resolvedPath = string.Empty;
        var resolvedExeName = string.Empty;
        var resolvedDisplay = string.Empty;

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
                DisplayName = resolvedDisplay
            };
        }

        exePath = resolvedPath;
        exeName = resolvedExeName;
        displayName = resolvedDisplay;
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

    private AudioSessionInfo? ExtractSessionInfo(AudioSessionControl session, string controlKey)
    {
        try
        {
            var sid = session.GetSessionIdentifier ?? string.Empty;
            var displayName = session.DisplayName ?? string.Empty;
            uint pid = 0;
            var exePath = string.Empty;
            var exeName = string.Empty;
            byte[]? iconBytes = null;

            var isSystemSound = false;
            if (session.IsSystemSoundsSession)
            {
                isSystemSound = true;
                displayName = "Systemklänge";
                exeName = "SystemSound";
                exePath = "__system_sound__";
            }

            try { pid = session.GetProcessID; } catch { }

            if (string.IsNullOrWhiteSpace(exePath) && pid > 0)
            {
                TryResolveProcessMetadata(pid, out exePath, out exeName, out var processDisplayName);
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

            // The NAudio volume wrapper is cached by its session owner.
            // Keep it valid through manual volume/mute writes and release
            // only when the corresponding session snapshot is replaced.
            var sessionVolume = session.SimpleAudioVolume;
            // Reuse this scan's existing AudioSessionControl, without a second
            // enumerator, sampling loop, or audio write. Unsupported meters are unknown.
            float? signalPeakLevel = null;
            try
            {
                var peak = session.AudioMeterInformation.MasterPeakValue;
                if (float.IsFinite(peak)) signalPeakLevel = Math.Clamp(peak, 0f, 1f);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Meter unavailable: {ex.Message}");
            }
            return new AudioSessionInfo
            {
                SessionIdentifier = controlKey,
                SignalPeakLevel = signalPeakLevel,
                ProcessId = pid,
                Volume = sessionVolume.Volume,
                IsMuted = sessionVolume.Mute,
                DisplayName = displayName,
                ExePath = exePath,
                ExeName = exeName,
                IconBytes = iconBytes,
                IsSystemSound = isSystemSound
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

    // The selected endpoint is revalidated before every master write.
    // A device switch must never apply stale UI commands to a different
    // physical output or accidentally target System Sounds.
    private void ApplyMasterControl(string controlKey, Action<AudioEndpointVolume> update)
    {
        try
        {
            using var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (!string.Equals(controlKey, BuildMasterControlKey(current.ID),
                    StringComparison.OrdinalIgnoreCase))
                return;
            // MMDevice owns and disposes its cached AudioEndpointVolume.
            // Never dispose a child twice while its endpoint is still alive.
            update(current.AudioEndpointVolume);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audio] Master control failed closed: {ex.Message}");
        }
    }

    public void SetVolume(string sessionIdentifier, float volume) =>
        _ = TrySetVolume(sessionIdentifier, volume);

    public bool TrySetVolume(string sessionIdentifier, float volume)
    {
        var requested = Math.Clamp(volume, 0f, 1f);
        if (sessionIdentifier.StartsWith(MasterControlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (!string.Equals(sessionIdentifier, BuildMasterControlKey(current.ID),
                        StringComparison.OrdinalIgnoreCase)) return false;
                current.AudioEndpointVolume.MasterVolumeLevelScalar = requested;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Master SetVolume error: {ex.Message}");
                return false;
            }
        }
        lock (_enumerationSync)
        {
            try
            {
                if (!_sessions.TryGet(sessionIdentifier, out var session) || session == null)
                    return false;
                session.SimpleAudioVolume.Volume = requested;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] SetVolume error: {ex.Message}");
                return false;
            }
        }
    }

    public void SetMute(string sessionIdentifier, bool mute) =>
        _ = TrySetMute(sessionIdentifier, mute);

    public bool TrySetMute(string sessionIdentifier, bool mute)
    {
        if (sessionIdentifier.StartsWith(MasterControlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (!string.Equals(sessionIdentifier, BuildMasterControlKey(current.ID),
                        StringComparison.OrdinalIgnoreCase)) return false;
                current.AudioEndpointVolume.Mute = mute;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Master SetMute error: {ex.Message}");
                return false;
            }
        }
        lock (_enumerationSync)
        {
            try
            {
                if (!_sessions.TryGet(sessionIdentifier, out var session) || session == null)
                    return false;
                session.SimpleAudioVolume.Mute = mute;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] SetMute error: {ex.Message}");
                return false;
            }
        }
    }

    public void OnSessionCreated(Action<AudioSessionInfo> callback) { _onCreated = callback; }
    public void OnSessionDestroyed(Action<string> callback) { _onDestroyed = callback; }

    // No second poll of the OS is needed: the same current role->ID
    // identities drive both the snapshot and the monitor subscriptions.
    // This method is called only while holding _enumerationSync.
    private void SynchronizeMonitoringWithCurrentEndpoints(IReadOnlyList<MMDevice> devices)
    {
        var activeIds = new HashSet<string>(
            devices.Select(d => d.ID), StringComparer.OrdinalIgnoreCase);
        var retiredIds = _monitoredDevices.Keys
            .Where(id => !activeIds.Contains(id)).ToArray();
        var addedIds = activeIds
            .Where(id => !_monitoredDevices.ContainsKey(id)).ToArray();
        if (retiredIds.Length == 0 && addedIds.Length == 0) return;

        // Callbacks from the former default device must never be forwarded
        // as newly created sessions on the replacement device.
        System.Threading.Interlocked.Increment(ref _monitoringGeneration);
        foreach (var id in retiredIds)
        {
            var former = _monitoredDevices[id];
            _monitoredDevices.Remove(id);
            try { former.AudioSessionManager.OnSessionCreated -= OnNewSessionCreated; }
            catch (Exception ex) { Debug.WriteLine($"[Audio] Unsubscribe retired endpoint failed: {ex.Message}"); }
            finally { former.Dispose(); }
        }
        foreach (var id in addedIds)
        {
            MMDevice? monitor = null;
            try
            {
                // Distinct COM owner from the long-lived polling device.
                // A removed endpoint may fail this lookup: fail closed and
                // retry during the following normal poll.
                monitor = _enumerator.GetDevice(id);
                monitor.AudioSessionManager.OnSessionCreated += OnNewSessionCreated;
                _monitoredDevices.Add(id, monitor);
                monitor = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Monitor registration failed for endpoint {id}: {ex.Message}");
            }
            finally { monitor?.Dispose(); }
        }
    }

    public void StartMonitoring()
    {
        lock (_enumerationSync)
        {
            _monitoringActive = true;
            // Initialize normally runs an initial enumeration before this.
            // If not, the next normal poll will populate and subscribe.
            SynchronizeMonitoringWithCurrentEndpoints(_scanDevices.Values.ToArray());
        }
    }

    public void StopMonitoring()
    {
        lock (_enumerationSync)
        {
            _monitoringActive = false;
            System.Threading.Interlocked.Increment(ref _monitoringGeneration);
            foreach (var device in _monitoredDevices.Values)
            {
                try { device.AudioSessionManager.OnSessionCreated -= OnNewSessionCreated; }
                catch (Exception ex) { Debug.WriteLine($"[Audio] Monitor stop failed: {ex.Message}"); }
                finally { device.Dispose(); }
            }
            _monitoredDevices.Clear();

            // Only command-free shutdown may withdraw all remaining snapshot
            // controls and endpoint objects. No callback runs inside this lock.
            _sessions.Replace(new Dictionary<string, AudioSessionControl>(), out _, out var retiredControls);
            foreach (var retired in retiredControls)
            {
                try { retired.SimpleAudioVolume.Dispose(); retired.Dispose(); }
                catch { try { retired.Dispose(); } catch { } }
            }
            foreach (var device in _scanDevices.Values)
            {
                try { device.Dispose(); } catch { }
            }
            _scanDevices.Clear();
        }
    }

    private void OnNewSessionCreated(object sender, object newSession)
    {
        var generation = System.Threading.Volatile.Read(ref _monitoringGeneration);
        // The NAudio notification is only a scan trigger. Its callback does not identify
        // a published endpoint-scoped key; the last enumerated session may be unrelated.
        System.Threading.Tasks.Task.Delay(140).ContinueWith(_ =>
        {
            if (generation != System.Threading.Volatile.Read(ref _monitoringGeneration)) return;
            try
            {
                EnumerateSessions(out var addedSessions);
                foreach (var session in addedSessions)
                {
                    if (generation != System.Threading.Volatile.Read(ref _monitoringGeneration)) break;
                    try { _onCreated?.Invoke(session); }
                    catch (Exception ex) { Debug.WriteLine($"[Audio] Create callback error: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Audio] Create scan error: {ex.Message}");
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
