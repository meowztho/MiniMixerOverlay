using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using MiniMixerOverlay.Core;
using MiniMixerOverlay.Core.Interfaces;

// Actual Windows RegisterHotKey conflict / rebind / unregistration against the production WPF HWND.
// No global keyboard input, audio writes, user settings or real MiniMixer process are involved.
internal static class HotkeyOsRegression
{
    private const uint ModCtrlAltNoRepeat = 0x4003;
    private const int ConflictId = 0x6D52;
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public static int Run()
    {
        Exception? failure = null;
        var worker = new Thread(() => { try { Probe(); } catch (Exception ex) { failure = ex; } });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        worker.Join();
        if (failure != null)
        {
            Console.Error.WriteLine("REAL_WIN32_HOTKEY_FAIL: " + failure);
            return 1;
        }
        Console.WriteLine("REAL_WIN32_HOTKEY_PASS: conflict, register, rebind, disable, release");
        return 0;
    }

    private static void Probe()
    {
        var program = typeof(MiniMixerOverlay.App.Program);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        object? Get(string field) => (program.GetField(field, flags) ?? throw new MissingFieldException(field)).GetValue(null);
        void Set(string field, object? value) => (program.GetField(field, flags) ?? throw new MissingFieldException(field)).SetValue(null, value);
        void Call(string name) => (program.GetMethod(name, flags) ?? throw new MissingMethodException(name)).Invoke(null, null);
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }

        // The allowed candidate must be genuinely free before running; never hijack an occupied user shortcut.
        var blocker = new Window { Width = 100, Height = 80, ShowInTaskbar = false, ShowActivated = false };
        var target = new Window { Width = 140, Height = 90, ShowInTaskbar = false, ShowActivated = false };
        var blockerHandle = new WindowInteropHelper(blocker).EnsureHandle();
        _ = new WindowInteropHelper(target).EnsureHandle();
        uint key = 0;
        string keyName = "";
        foreach (var candidate in new[] { (Name: "F10", Key: 0x79u), (Name: "F9", Key: 0x78u),
                    (Name: "O", Key: 0x4Fu), (Name: "M", Key: 0x4Du) })
        {
            if (!RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, candidate.Key)) continue;
            key = candidate.Key;
            keyName = candidate.Name;
            break;
        }
        if (key == 0) throw new InvalidOperationException("All allowed global shortcuts occupied; cannot safely run OS conflict probe");

        var originalWindow = Get("_mainWin");
        var originalController = Get("_ctrl");
        try
        {
            var rules = new MemoryRules(DateTime.UtcNow.AddYears(-1));
            var controller = new MixerController(new FakeAudio(Array.Empty<MiniMixerOverlay.Core.Models.AudioSessionInfo>()),
                new GuardEngine(rules, new FakeClassifier()), new FakeClassifier(), rules, new FakeSettings(),
                guardLogFilePath: System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "mini-mixer-regression-hotkey-" + Environment.ProcessId, "guard.log"));
            Set("_mainWin", target);
            Set("_ctrl", controller);
            controller.Settings.Ui.ToggleHotkeyKey = keyName;
            controller.Settings.Ui.ToggleHotkeyEnabled = true;
            Call("InitializeToggleHotkey");
            Check(!(bool)Get("_toggleHotkeyRegistered")!,
                "Production registered a shortcut while blocker owns the exact key");
            Check(((string)Get("_toggleHotkeyError")!).Contains("belegt", StringComparison.OrdinalIgnoreCase) ||
                  ((string)Get("_toggleHotkeyError")!).Contains("already", StringComparison.OrdinalIgnoreCase),
                "Conflict was not distinguished from an unrelated Win32 registration failure");

            Check(UnregisterHotKey(blockerHandle, ConflictId), "Cannot release conflict owner");
            Call("RebindToggleHotkey");
            Check((bool)Get("_toggleHotkeyRegistered")!, "Production failed to register once key was freed");
            Check(!RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, key),
                "Other window registered key while production owns it");

            // Changing the configured key must atomically release the old Windows registration.
            var alternate = new[] { (Name: "F9", Key: 0x78u), (Name: "F10", Key: 0x79u),
                                    (Name: "O", Key: 0x4Fu), (Name: "M", Key: 0x4Du) }
                .FirstOrDefault(candidate => candidate.Key != key &&
                    RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, candidate.Key));
            var alternateName = alternate.Name ??
                throw new InvalidOperationException("No unused alternate shortcut for OS rebind probe");
            Check(UnregisterHotKey(blockerHandle, ConflictId), "Cannot release alternate probe key");
            controller.Settings.Ui.ToggleHotkeyKey = alternateName;
            Call("RebindToggleHotkey");
            Check((bool)Get("_toggleHotkeyRegistered")!, "Switching hotkeys lost registration");
            Check(RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, key),
                "Rebind did not release old key");
            Check(UnregisterHotKey(blockerHandle, ConflictId), "Cannot release old-key proof registration");
            key = alternate.Key;
            keyName = alternateName;
            Check(!RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, key),
                "Rebound key was not exclusively registered");

            controller.Settings.Ui.ToggleHotkeyEnabled = false;
            Call("RebindToggleHotkey");
            Check(!(bool)Get("_toggleHotkeyRegistered")!, "Disable left active registration");
            Check(RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, key),
                "Windows did not release registration after disable");
            Check(UnregisterHotKey(blockerHandle, ConflictId), "Cannot cleanup post-disable probe key");

            controller.Settings.Ui.ToggleHotkeyEnabled = true;
            Call("RebindToggleHotkey");
            Check((bool)Get("_toggleHotkeyRegistered")!, "Re-enable failed after disable");
            Call("StopToggleHotkey");
            Check(!(bool)Get("_toggleHotkeyRegistered")!, "Stop left active registration");
            Check(RegisterHotKey(blockerHandle, ConflictId, ModCtrlAltNoRepeat, key),
                "Stop did not free Windows registration");
            Check(UnregisterHotKey(blockerHandle, ConflictId), "Cannot cleanup post-stop probe key");
            Console.WriteLine("REAL_WIN32_HOTKEY_PROBE key=" + keyName + " blocker=real HWND target=real WPF HWND");
        }
        finally
        {
            try { Call("StopToggleHotkey"); } catch { }
            UnregisterHotKey(blockerHandle, ConflictId);
            Set("_ctrl", originalController);
            Set("_mainWin", originalWindow);
            target.Close();
            blocker.Close();
        }
    }
}
