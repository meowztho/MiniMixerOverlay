// Safe cross-process smoke: fake WPF overlay + independently compiled native client.
// Never calls MiniMixer.Program.Main, GameHookBridge, injector or real audio APIs.
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

static class Test
{
    static void Check(bool success, string reason)
    {
        if (!success) throw new InvalidOperationException(reason);
    }
    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    static int Value(string result, string name)
    {
        var match = Regex.Match(result, $@"(?:^|\s){Regex.Escape(name)}=(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }
    [STAThread]
    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var exe = args.FirstOrDefault(a => a.EndsWith(".exe",
            StringComparison.OrdinalIgnoreCase)) ??
            Path.Combine(root, ".tmp", "NativeCandidate", "native_candidate.exe");
        var testLayeredRendering = args.Contains("--show");
        var sphereWireInput = args.Contains("--readonly-wpf-ipc-sphere-native-wire");
        var sphereNativeInputWorker = args.Contains("--readonly-wpf-ipc-sphere-native-worker");
        var sphereMixed = args.Contains("--readonly-wpf-ipc-sphere-mixed");
        var sphereInputGate = args.Contains("--readonly-wpf-ipc-sphere-input-gate");
        var sphereMainHidden = args.Contains("--readonly-wpf-ipc-sphere-main-hidden");
        var sphereMode = sphereWireInput || sphereNativeInputWorker || sphereMixed || sphereMainHidden || sphereInputGate ||
                         args.Contains("--readonly-wpf-ipc-sphere");
        var standaloneIpc = sphereMode || args.Contains("--readonly-wpf-ipc-client");
        var inputNoCapability = args.Contains("--input-probe-no-capability");
        var inputNoForward = args.Contains("--input-probe-no-forward") ||
                             args.Contains("--gpu-d3d12-overlay-input-no-forward");
        var frameMailbox = args.Contains("--gpu-d3d12-overlay-mailbox");
        var pbgraOverlay = frameMailbox || args.Contains("--gpu-d3d12-overlay") ||
                           args.Contains("--gpu-d3d12-overlay-input") ||
                           args.Contains("--gpu-d3d12-overlay-input-no-forward");
        var inputGpuD3d11 = args.Contains("--gpu-hook-input") ||
                            args.Contains("--gpu-hook-input-warp");
        var inputGpuD3d12 = args.Contains("--gpu-d3d12-hook-input") ||
                            args.Contains("--gpu-d3d12-hook-input-warp");
        var inputProbe = args.Contains("--input-probe") || inputNoForward ||
                         inputNoCapability ||
                         args.Contains("--gpu-d3d12-overlay-input") ||
                         inputGpuD3d11 || inputGpuD3d12;
        var gpuD3d12 = pbgraOverlay || args.Contains("--gpu-d3d12") || args.Contains("--gpu-d3d12-warp") ||
                       args.Contains("--gpu-d3d12-hook") || args.Contains("--gpu-d3d12-hook-warp") ||
                       inputGpuD3d12;
        var gpuHook = args.Contains("--gpu-hook") || args.Contains("--gpu-hook-warp") ||
                      args.Contains("--gpu-d3d12-hook") || args.Contains("--gpu-d3d12-hook-warp") ||
                      inputGpuD3d11 || inputGpuD3d12;
        var gpu = args.Contains("--gpu") || args.Contains("--gpu-warp") || gpuHook || gpuD3d12;
        var gpuWarp = args.Contains("--gpu-warp") || args.Contains("--gpu-hook-warp") ||
                      args.Contains("--gpu-d3d12-warp") || args.Contains("--gpu-d3d12-hook-warp") ||
                      args.Contains("--gpu-hook-input-warp") ||
                      args.Contains("--gpu-d3d12-hook-input-warp");
        Check(!(gpu && testLayeredRendering), "GPU and layered tests are exclusive");
        Check(!(inputProbe && testLayeredRendering), "Input and layered modes are exclusive");
        Check(!(inputProbe && gpu && !gpuHook && !pbgraOverlay),
              "Combined GPU input requires an owned Present or owned Pbgra overlay");
        Check(File.Exists(exe), "Build candidate first: " + exe);
        var hostName = "minimixer-test-" + Guid.NewGuid().ToString("N");
        var visual = new Border
        {
            Width = 200, Height = 95,
            Background = new SolidColorBrush(Color.FromArgb(220, 15, 45, 65)),
            Child = new TextBlock
            {
                Text = "Native candidate smoke",
                Foreground = Brushes.White,
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var fakeSphere = sphereMode ? new Window
        {
            Width = 52, Height = 52, Left = 420, Top = 210,
            ShowInTaskbar = false, ShowActivated = false,
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent,
            Content = new Border {
                Width = 50, Height = 50,
                CornerRadius = new CornerRadius(25),
                Background = new SolidColorBrush(Color.FromArgb(235, 24, 200, 69))
            }
        } : null;
        var fakeWindow = new Window
        {
            Width = 220, Height = 115, Left = 150, Top = 180,
            ShowInTaskbar = false, ShowActivated = false, Content = visual
        };
        var native = new WindowInteropHelper(fakeWindow);
        native.EnsureHandle();
        var source = HwndSource.FromHwnd(native.Handle)!;
        int wpfMoves = 0, wpfDown = 0, wpfUp = 0, wpfKey = 0;
        HwndSourceHook inputHook = (IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (inputProbe)
            {
                if (msg == 0x0200) wpfMoves++;
                if (msg == 0x0201) wpfDown++;
                if (msg == 0x0202) wpfUp++;
                if (msg == 0x0100) wpfKey++;
            }
            return IntPtr.Zero; // Observe only. Let normal WPF handle the event.
        };
        if (inputProbe) source.AddHook(inputHook);

        var type = typeof(MiniMixerOverlay.App.Program).Assembly.GetType(
            "MiniMixerOverlay.App.GameHookOverlayRuntime")!;
        var runtime = Activator.CreateInstance(type, new object?[] {
            fakeWindow, null, hostName
        })!;
        var clients = (System.Collections.IDictionary)type.GetField(
            "_clients", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
        var processes = new List<Process>();
        var outputs = new List<Task<string>>();
        var errors = new List<Task<string>>();
        try
        {
            fakeWindow.Show();
            if (fakeSphere != null)
            {
                fakeSphere.Show();
                type.GetMethod("SetAuxiliaryWindow")!.Invoke(runtime, new object?[] { fakeSphere });
            }
            Check((bool)type.GetMethod("Start")!.Invoke(runtime, null)!,
                "Isolated MiniMixer IPC host failed to start");
            for (int i = 0; i < 2; ++i)
            {
                var chosenExe = sphereMixed && i == 1
                    ? Path.Combine(root, ".tmp", "NativeCandidate", "native_candidate.exe")
                    : exe;
                var start = new ProcessStartInfo(chosenExe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("--host=" + hostName);
                start.ArgumentList.Add("--duration-ms=7000");
                if (sphereWireInput)
                    start.ArgumentList.Add(i == 0 ? "--owned-sphere-input-wire" :
                                           "--sphere-native-input-worker");
                else if (sphereNativeInputWorker)
                    start.ArgumentList.Add("--sphere-native-input-worker");
                if (i == 0)
                {
                    if (!standaloneIpc) start.ArgumentList.Add("--stall-ms=500");
                    if (testLayeredRendering) start.ArgumentList.Add("--show");
                    if (inputProbe) start.ArgumentList.Add("--input-probe");
                    if (gpu) start.ArgumentList.Add(frameMailbox ? "--gpu-d3d12-overlay-mailbox" :
                        pbgraOverlay ? "--gpu-d3d12-overlay" : gpuD3d12
                        ? (gpuHook ? (gpuWarp ? "--gpu-d3d12-hook-warp" : "--gpu-d3d12-hook")
                                   : (gpuWarp ? "--gpu-d3d12-warp" : "--gpu-d3d12"))
                        : gpuHook ? (gpuWarp ? "--gpu-hook-warp" : "--gpu-hook")
                        : (gpuWarp ? "--gpu-warp" : "--gpu"));
                }
                var p = Process.Start(start)!;
                processes.Add(p);
                outputs.Add(p.StandardOutput.ReadToEndAsync());
                errors.Add(p.StandardError.ReadToEndAsync());
            }
            var watch = Stopwatch.StartNew();
            var changed = false;
            var inputEnabled = false;
            var sphereMoved = false;
            var sphereHidden = false;
            var sphereShownAgain = false;
            var mainHidden = false;
            var mainShownAgain = false;
            var mixedCapabilityVerified = false;
            var nativeInputCapabilityVerified = false;
            var auxInputVerified = false;
            var auxMoves = 0;
            var auxDown = 0;
            var auxUp = 0;
            HwndSourceHook? auxInputHook = null;
            if ((sphereInputGate || sphereWireInput) && fakeSphere != null)
            {
                var hwnd = new WindowInteropHelper(fakeSphere).Handle;
                var src = HwndSource.FromHwnd(hwnd)!;
                auxInputHook = (IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
                {
                    if (msg == 0x0200) auxMoves++;
                    if (msg == 0x0201) auxDown++;
                    if (msg == 0x0202) auxUp++;
                    return IntPtr.Zero;
                };
                src.AddHook(auxInputHook);
            }
            while (processes.Any(x => !x.HasExited) &&
                   watch.ElapsedMilliseconds < 15000)
            {
                Pump(40);
                if (sphereInputGate && !auxInputVerified &&
                    watch.ElapsedMilliseconds > 1750 && clients.Count == 2)
                {
                    var send = type.GetMethod("HandleOverlayIpcMessage",
                        BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var pack = type.GetMethod("PackOverlayIpc",
                        BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var pid = clients.Keys.Cast<uint>().First();
                    var hwnd = new WindowInteropHelper(fakeSphere!).Handle;
                    var id = unchecked((uint)hwnd.ToInt64());
                    var payload = (uint winId, uint msg, uint wp, uint lp) =>
                        (byte[])pack.Invoke(runtime, new object[] {
                            "experimental.aux.input",
                            $"{{\"windowId\":{winId},\"msg\":{msg},\"wparam\":{wp},\"lparam\":{lp}}}"
                        })!;
                    void TrySend(uint windowId, uint msg, uint wparam = 0, uint lparam = 25u | (25u << 16))
                    {
                        send.Invoke(runtime, new object[] { pid, payload(windowId, msg, wparam, lparam) });
                        Pump(70);
                    }
                    // Client capability off and host gate off must reject.
                    TrySend(id, 0x0200);
                    Check(auxMoves == 0, "Aux input accepted without consent");
                    var peer = clients[pid]!;
                    peer.GetType().GetField("SupportsAuxiliaryInput")!.SetValue(peer, true);
                    clients[pid] = peer;
                    TrySend(id, 0x0200);
                    Check(auxMoves == 0, "Aux input ignored disabled WPF input gate");
                    type.GetProperty("ForwardGameInputToWindow")!.SetValue(runtime, true);
                    TrySend(id + 1, 0x0200);
                    TrySend(id, 0x0100); // Never keyboard
                    TrySend(id, 0x0200, 0, 60000); // Wrong coords
                    Check(auxMoves == 0 && auxDown == 0,
                          "Aux input accepted forged HWND/keyboard/outside coords");
                    TrySend(id, 0x0200);
                    TrySend(id, 0x0201, 1);
                    TrySend(id, 0x0202);
                    Check(auxMoves == 1 && auxDown == 1 && auxUp == 1 &&
                          wpfMoves == 0 && wpfDown == 0,
                          $"Aux input relay wrong: moves={auxMoves},down={auxDown},up={auxUp}");
                    type.GetProperty("ForwardGameInputToWindow")!.SetValue(runtime, false);
                    TrySend(id, 0x0200);
                    Check(auxMoves == 1, "Aux input bypassed host shutdown gate");
                    auxInputVerified = true;
                    Console.WriteLine("PASS AUX_SPHERE_INPUT_GATES capability/session/host/HWND/type/coordinates; received move=1 down=1 up=1; no main/keyboard spill");
                }
                if (sphereWireInput && !nativeInputCapabilityVerified &&
                    watch.ElapsedMilliseconds > 800 && clients.Count == 2)
                {
                    foreach (object peer in clients.Values)
                        Check((bool)peer.GetType().GetField("SupportsAuxiliaryInput")!
                            .GetValue(peer)!, "Native IPC input wire capability missing");
                    type.GetProperty("ForwardGameInputToWindow")!.SetValue(runtime, true);
                    nativeInputCapabilityVerified = true;
                }
                if (sphereNativeInputWorker && !nativeInputCapabilityVerified &&
                    watch.ElapsedMilliseconds > 1700 && clients.Count == 2)
                {
                    foreach (object peer in clients.Values)
                    {
                        var field = peer.GetType().GetField("SupportsAuxiliaryInput")!;
                        Check((bool)field.GetValue(peer)!,
                              "Opt-in native worker did not advertise auxiliary input capability");
                    }
                    nativeInputCapabilityVerified = true;
                }
                if (sphereMixed && !mixedCapabilityVerified &&
                    watch.ElapsedMilliseconds > 1400 && clients.Count == 2)
                {
                    var auxCount = 0;
                    foreach (object client in clients.Values)
                    {
                        var field = client.GetType().GetField("SupportsAuxiliarySurface")!;
                        if ((bool)field.GetValue(client)!) auxCount++;
                    }
                    Check(auxCount == 1, "Legacy peer unexpectedly received auxiliary capability");
                    mixedCapabilityVerified = true;
                }
                if (sphereMainHidden && !mainHidden && watch.ElapsedMilliseconds > 2600)
                {
                    fakeWindow.Hide();
                    mainHidden = true;
                }
                if (sphereMainHidden && !mainShownAgain && watch.ElapsedMilliseconds > 6000)
                {
                    fakeWindow.Show();
                    mainShownAgain = true;
                }
                if (fakeSphere != null && !sphereMoved && watch.ElapsedMilliseconds > 2000)
                {
                    fakeSphere.Left += 65;
                    fakeSphere.Top += 24;
                    sphereMoved = true;
                }
                if (fakeSphere != null && !sphereHidden && watch.ElapsedMilliseconds > 4000)
                {
                    fakeSphere.Hide();
                    sphereHidden = true;
                }
                if (fakeSphere != null && !sphereShownAgain && watch.ElapsedMilliseconds > 5000)
                {
                    fakeSphere.Show();
                    sphereShownAgain = true;
                }
                if (inputProbe && !inputEnabled &&
                    watch.ElapsedMilliseconds > 150 && clients.Count == 2)
                {
                    // Only this isolated WPF test instance explicitly accepts
                    // native input; the product default is unchanged.
                    type.GetProperty("ForwardGameInputToWindow")!.SetValue(runtime, !inputNoForward);
                    type.GetProperty("ForwardNativeMainInput")!.SetValue(runtime, !inputNoForward);
                    if (inputNoCapability)
                    {
                        foreach (uint pid in clients.Keys.Cast<uint>().ToArray())
                        {
                            var peer = clients[pid]!;
                            peer.GetType().GetField("SupportsNativeMainInput")!.SetValue(peer, false);
                            clients[pid] = peer;
                        }
                    }
                    type.GetMethod("SendInputIntercept")!.Invoke(runtime, new object[] { true });
                    inputEnabled = true;
                }
                if ((gpu || standaloneIpc) && !changed && watch.ElapsedMilliseconds > 1800)
                {
                    // Change both the WPF capture dimensions and actual pixels.
                    // Test must not merely draw the same cached dummy frame.
                    fakeWindow.Width = 280;
                    fakeWindow.Height = 175;
                    visual.Width = 260;
                    visual.Height = 155;
                    visual.Background = new SolidColorBrush(
                        Color.FromArgb(215, 115, 22, 72));
                    changed = true;
                }
            }
            if (sphereInputGate)
                Check(auxInputVerified, "Auxiliary input contract test never executed");
            if (sphereMainHidden)
                Check(mainHidden && mainShownAgain,
                      "Main WPF surface hidden/reopened stimulus was not executed");
            if (sphereWireInput)
                Check(nativeInputCapabilityVerified && auxMoves == 1 &&
                      auxDown == 1 && auxUp == 1,
                      $"Native IPC input wire events: move={auxMoves} down={auxDown} up={auxUp}");
            if (sphereNativeInputWorker)
                Check(nativeInputCapabilityVerified,
                      "Native opt-in input session was never verified");
            if (sphereMixed)
                Check(mixedCapabilityVerified, "Mixed auxiliary/legacy IPC capabilities not verified");
            if (sphereMode)
                Check(sphereMoved && sphereHidden && sphereShownAgain,
                      "Auxiliary WPF sphere stimulus incomplete");
            if (gpu || standaloneIpc)
                Check(changed, "WPF source resize/content stimulus not executed");
            if (inputProbe) Check(inputEnabled, "Input probe consent was not activated");
            foreach (var p in processes)
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
            for (int i = 0; i < processes.Count; ++i)
            {
                var result = outputs[i].GetAwaiter().GetResult().Trim();
                Console.WriteLine($"NATIVE_CLIENT_{i} EXIT={processes[i].ExitCode} {result}");
                var stderr = errors[i].GetAwaiter().GetResult().Trim();
                if (stderr.Length > 0) Console.WriteLine($"STDERR_{i}: {stderr}");
                Check(processes[i].ExitCode == 0, "Native client exited nonzero: " + i);
                Check(Value(result, "ACK") >= 1 &&
                      Value(result, "INIT") >= 1 &&
                      Value(result, "COPIED") >= 10,
                      "Native IPC/mutex/frame cycle failed: " + result);
                Check(Value(result, "REJECTED") == 0,
                      "Native IPC rejected malformed data or GPU operation: " + result);
                Check(Value(result, "FRAME_MISSES") >= 0 &&
                      Value(result, "FRAME_MISSES") <= 3,
                      "Excessive transient frame capture misses: " + result);
                if (i == 0)
                {
                    if (!standaloneIpc)
                        Check(Value(result, "STALLED") == 1 &&
                              Value(result, "INIT") >= 2,
                              "Native IPC did not resume after controlled stall");
                    if (sphereMainHidden)
                        Check(Value(result, "AUX_FRAMES") > Value(result, "COPIED") + 15,
                              "Sphere did not keep publishing while WPF mixer was hidden: " + result);
                    if (sphereMode)
                        Check(Value(result, "AUX_FRAMES") >= 20 &&
                              Value(result, "AUX_READ") >= 15 &&
                              Value(result, "AUX_MOVED") >= 1 &&
                              Value(result, "AUX_HIDDEN") >= 1 &&
                              Value(result, "AUX_ALPHA") >= 10,
                              "Auxiliary WPF sphere capture/transition failed: " + result);
                    if (sphereWireInput)
                        Check(Value(result, "INPUT_WORKER") == 1 &&
                              Value(result, "INPUT_EVENTS") == 0 &&
                              (i != 0 || (Value(result, "OWNED_WIRE_INPUT") == 1 &&
                                          Value(result, "OWNED_WIRE_OK") == 1)),
                              "Native synthetic IPC wire sample failed: " + result);
                    if (sphereNativeInputWorker)
                        Check(Value(result, "INPUT_WORKER") == 1 &&
                              Value(result, "INPUT_EVENTS") == 0 &&
                              Value(result, "INPUT_SENT") == 0,
                              "Native input worker acted without validated foreground game HWND: " + result);
                    if (standaloneIpc)
                        Check(Value(result, "READONLY_SNAPSHOT") == 1 &&
                              Value(result, "READ_FRAMES") >= 10 &&
                              Value(result, "FRAME_VARIANTS") >= 1,
                              "Read-only WPF IPC worker missed snapshots or resize: " + result);
                    if (testLayeredRendering)
                        Check(Value(result, "DISPLAYED") >= 10,
                            "Layered Win32 compositor did not display frames");
                    if (inputProbe)
                        Check(Value(result, "INPUT_DONE") == 1 &&
                              Value(result, "INPUT_FORWARDED") == 4 &&
                              Value(result, "INPUT_PASSTHROUGH") >= 2 &&
                              Value(result, "INPUT_COMMANDS") >= 3 &&
                              Value(result, "INPUT_INTERCEPT") == 1 &&
                              Value(result, "INPUT_ID_VALID") == 1 &&
                              Value(result, "INPUT_MASK_UPDATES") == Value(result, "COPIED") &&
                              (!gpu || Value(result, "INPUT_RECT_UPDATES") >= 2),
                              "Native local alpha hit-test/drag/input relay failed: " + result);
                    if (gpu)
                    {
                        Check(Value(result, "GPU_FRAMES") >= 10 &&
                              Value(result, "GPU_MATCHED") == Value(result, "GPU_FRAMES") &&
                              Value(result, "GPU_PRESENTS") == Value(result, "GPU_FRAMES") &&
                              Value(result, "GPU_RESIZES") >= 1 &&
                              Value(result, "GPU_VARIANTS") >= 1 &&
                              Value(result, "GPU_ERRORS") == 0,
                              "Actual host IPC pixels were not rendered/read back on GPU: " + result);
                        Check(result.Contains("GPU_DRIVER=" + (gpuWarp ? "WARP" : "HARDWARE")),
                              "GPU driver mismatch: " + result);
                        Check(result.Contains("GPU_API=" + (gpuD3d12 ? "D3D12" : "D3D11")),
                              "GPU API mismatch: " + result);
                        if (inputProbe)
                            Check(Value(result, "INPUT_GPU_SAME_HWND") == 1,
                                  "Input and GPU must operate on the same owned HWND: " + result);
                        if (pbgraOverlay)
                            Check(Value(result, "GPU_OVERLAY_COMPOSITE") == 1 &&
                                  (!frameMailbox || Value(result, "GPU_FRAME_MAILBOX") == 1) &&
                                  Value(result, "GPU_RESIZES") >= 1 &&
                                  Value(result, "GPU_MATCHED") == Value(result, "GPU_FRAMES"),
                                  "Pbgra32 WPF IPC did not composite into actual 10-bit D3D12 backbuffer: " + result);
                        if (gpuD3d12 && !pbgraOverlay)
                            Check(Value(result, "GPU_PIPELINE_BUILDS") == 1 &&
                                  Value(result, "GPU_TEXTURE_BUILDS") == 2,
                                  "D3D12 GPU caching across IPC frames and resize failed: " + result);
                        if (gpuHook)
                            Check(Value(result, "GPU_HOOK_CALLBACKS") == Value(result, "GPU_FRAMES") &&
                                  Value(result, "GPU_HOOK_ATTACHES") >= 2 &&
                                  Value(result, "GPU_HOOK_ATTACHES") == Value(result, "GPU_HOOK_DETACHES"),
                                  "Owned Present lifecycle did not cover real IPC frames/resize/cleanup: " + result);
                    }
                }
                else
                    Check(Value(result, "INIT") == 1,
                          "Healthy client unexpectedly required reconnect");
            }
            Pump(100); // Deliver clients' PID close and posted WPF messages.
            if (inputProbe)
            {
                Check((inputNoForward || inputNoCapability)
                    ? (wpfMoves == 0 && wpfDown == 0 && wpfUp == 0 && wpfKey == 0)
                    : (wpfMoves >= 2 && wpfDown == 1 && wpfUp == 1 && wpfKey == 0),
                    $"WPF forwarding gate mismatch: noForward={inputNoForward} move={wpfMoves} down={wpfDown} up={wpfUp} key={wpfKey}");
                Console.WriteLine($"PASS INPUT_WPF_EVENTS moves={wpfMoves} down={wpfDown} up={wpfUp} key={wpfKey}");
            }
            Check(clients.Count == 0, "Host kept disconnected native clients");
            Console.WriteLine("PASS: 2 independent Win32 clients, SharedMemory frames, stalled-client recovery, cleanup");
            return 0;
        }
        finally
        {
            foreach (var p in processes)
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
                p.Dispose();
            }
            type.GetMethod("Stop")!.Invoke(runtime, null);
            if (inputProbe) source.RemoveHook(inputHook);
            Check(clients.Count == 0, "Stop leaked native client registration");
            fakeWindow.Close();
        }
    }
}
