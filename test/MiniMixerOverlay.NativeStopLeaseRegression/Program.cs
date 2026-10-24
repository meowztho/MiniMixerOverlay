using System.Diagnostics;
using System.Reflection;
using System.IO;
const BindingFlags flags=BindingFlags.Static | BindingFlags.NonPublic;
string prefix=@"Local\MiniMixerOverlayStop_";
if(args.Contains("--child")){
    int hostPid=int.Parse(args[1]);string resultFile=args[2];
    Console.WriteLine(Environment.ProcessId);Console.Out.Flush();
    try{using var host=Process.GetProcessById(hostPid);host.WaitForExit(8000);}catch(ArgumentException){}
    using var late=new EventWaitHandle(false,EventResetMode.ManualReset,prefix+Environment.ProcessId,out bool createdNew);
    bool signalled=late.WaitOne(150);
    File.WriteAllText(resultFile,$"SURVIVED_HOST_EXIT={!createdNew && signalled}");
    return !createdNew && signalled ? 0 : 1;
}
if(args.Contains("--middle")){
    string file=args[1];
    using var target=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){
        ArgumentList={"--child",Environment.ProcessId.ToString(),file},
        UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true
    })!;
    uint pid=uint.Parse((await target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)))!);
    var loader=typeof(MiniMixerOverlay.App.Program).Assembly.GetType("MiniMixerOverlay.App.NativeGameHookLoader")!;
    var ensure=loader.GetMethod("EnsureDurableStopEvent",flags)!;
    bool duplicated=(bool)ensure.Invoke(null,new object[]{pid,target.StartTime.ToUniversalTime(),target.Handle})!;
    loader.GetMethod("StopAllStartedAttempts",flags)!.Invoke(null,null);
    using var evt=EventWaitHandle.OpenExisting(prefix+pid);
    bool signalledBefore=evt.WaitOne(0);
    Console.WriteLine($"DUPLICATED_TARGET_HANDLE={duplicated} STOP_BEFORE_HOST_EXIT={signalledBefore}");
    Console.Out.Flush();
    return duplicated && signalledBefore ? 0 : 2;
}
string resultPath=Path.Combine(Path.GetTempPath(),"MiniMixerOwnedStopLease_"+Guid.NewGuid().ToString("N")+".txt");
try{
    using var middle=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){
        ArgumentList={"--middle",resultPath},UseShellExecute=false,
        CreateNoWindow=true,RedirectStandardOutput=true
    })!;
    string output=await middle.StandardOutput.ReadToEndAsync();
    Console.Write(output);
    if(!middle.WaitForExit(8000)||middle.ExitCode!=0)return 3;
    for(int i=0;i<100&&!File.Exists(resultPath);i++)await Task.Delay(30);
    if(!File.Exists(resultPath))return 4;
    string result=File.ReadAllText(resultPath);Console.WriteLine(result);
    if(result!="SURVIVED_HOST_EXIT=True")return 5;
    Console.WriteLine("PASS OWNED_DURABLE_STOP_EVENT_HANDOFF host_exit=1 late_create=1 no_game=1");
    return 0;
}finally{if(File.Exists(resultPath))File.Delete(resultPath);}
