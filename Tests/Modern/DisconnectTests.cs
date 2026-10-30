using System.Diagnostics;
using Xunit;
namespace IpaDesktop.Tests;
public sealed class DisconnectTests
{
 static string DotnetHost => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
  (Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet" ? Environment.ProcessPath : "dotnet");

 [Fact] public async Task MissingPhoneCancelsHungInstallerAndDoesNotWaitForNativeExit()
 {
  bool stopped=false;int probes=0;
  async Task<Result> Install(CancellationToken ct){try{await Task.Delay(Timeout.Infinite,ct);return null;}finally{stopped=true;}}
  var error=await Assert.ThrowsAsync<DeviceConnectionException>(()=>DeviceInstallWatchdog.Run(Install,ct=>{probes++;return Task.FromResult(false);},TestContext.Current.CancellationToken,TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
  Assert.Equal(FailureCode.Disconnected,error.Failure.Code);Assert.True(stopped);Assert.Equal(2,probes);
 }
 [Fact] public async Task OneMissedProbeDoesNotAbortHealthyInstallation()
 {
  int probes=0;var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  async Task<Result> Install(CancellationToken ct){await done.Task.WaitAsync(ct);return new Result{Code=0,Output="done"};}
  var result=await DeviceInstallWatchdog.Run(Install,ct=>{probes++;if(probes==3)done.SetResult();return Task.FromResult(probes!=1);},TestContext.Current.CancellationToken,TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken);
  Assert.Equal(0,result.Code);Assert.True(probes>=3);
 }
 [Fact] public async Task HangingPresenceProbeIsBoundedAndUserCancellationStaysCancellation()
 {
  async Task<bool> Probe(CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return true;}
  async Task<Result> Install(CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return null;}
  await Assert.ThrowsAsync<DeviceConnectionException>(()=>DeviceInstallWatchdog.Run(Install,Probe,TestContext.Current.CancellationToken,TimeSpan.FromMilliseconds(1),TimeSpan.FromMilliseconds(10)).WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
  using var stop=new CancellationTokenSource(20);
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>DeviceInstallWatchdog.Run(Install,Probe,stop.Token,TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
 }
 [Theory] [InlineData("disconnect")] [InlineData("silent")] [InlineData("repeat")]
 public async Task NativeHangAndRepeatedProgressReleaseRunnerForNextCommand(string mode)
 {
  using var folder=new TempFolder();var runner=new ProcessRunner(folder.Path);
  var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));var config=AppContext.BaseDirectory.Contains("Release")?"Release":"Debug";
  var fixture=Path.Combine(root,"Tests/LoginFixture/bin",config,"net10.0-windows/LoginFixture.dll");Assert.True(File.Exists(fixture));
  var idle=new DeviceConnectionException(new(FailureCode.Transport,"Нет прогресса","Установщик завис","Проверьте связь"));
  var policy=new ProcessRunPolicy(TimeSpan.FromSeconds(2),DeviceCommandClient.FailureFromOutput,()=>idle);
  var task=runner.RunMonitored(DotnetHost,new[]{fixture,"--process-fixture",mode},TestContext.Current.CancellationToken,false,false,null,policy);
  var limit=Stopwatch.StartNew();while(runner.Active==null&&!task.IsCompleted){Assert.True(limit.Elapsed.TotalSeconds<5);await Task.Delay(10,TestContext.Current.CancellationToken);}
  var pid=runner.Active?.Id;
  var error=await Assert.ThrowsAsync<DeviceConnectionException>(()=>task.WaitAsync(TimeSpan.FromSeconds(8),TestContext.Current.CancellationToken));
  Assert.Equal(mode=="disconnect"?FailureCode.Disconnected:FailureCode.Transport,error.Failure.Code);Assert.Null(runner.Active);
  if(pid.HasValue){try{using var process=Process.GetProcessById(pid.Value);Assert.True(process.HasExited);}catch(ArgumentException){}}
  var again=await runner.Run(DotnetHost,new[]{fixture,"--process-fixture","ok"},TestContext.Current.CancellationToken,false,false);
  Assert.Equal(0,again.Code);Assert.Contains("done",again.Output);
 }
 [Fact] public void InformationalConnectionLogDoesNotStopInstaller()
 {
  Assert.Null(DeviceCommandClient.FailureFromOutput("Connecting to lockdownd"));
  Assert.IsType<DeviceConnectionException>(DeviceCommandClient.FailureFromOutput("ERROR: Could not connect to lockdownd, error code -21"));
 }
}
