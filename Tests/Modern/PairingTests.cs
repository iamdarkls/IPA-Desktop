using System.Diagnostics;
using System.IO.Compression;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class PairingRunner : IProcessRunner
{
 public Action<string> Log { get; set; } = delegate { };
 public Process Active => null;
 public readonly List<(string Tool,string[] Args)> Calls=new();
 public readonly Queue<(string Command,string Reply)> Pairing=new();
 public bool NetworkBroken, WrongDevice, FailFirstInstall, FailAllInstalls, DisconnectInstall;
 public int Installs;
 public bool WaitForTrust, Trusted;
 public string ID="00008030-0000111ABC000DEF";
 public Task<Result> Run(string exe,string[] args,CancellationToken ct,bool visible,bool publish,string workingDirectory=null)
 {
  ct.ThrowIfCancellationRequested();var name=Path.GetFileName(exe);Calls.Add((name,args.ToArray()));
  string output="SUCCESS";int code=0;
  if(name=="idevicepair.exe")
  {
   var command=args.Last();
   if(WaitForTrust && !Trusted){output=command=="validate"?"Invalid HostID":"Please accept the trust dialog";code=1;return Task.FromResult(new Result{Code=code,Output=output});}
   if(Pairing.Count>0){var step=Pairing.Dequeue();Assert.Equal(step.Command,command);output=step.Reply;code=output=="OK"?0:1;}
  }
  else if(name=="ideviceinfo.exe")
  {
   code=NetworkBroken?1:0;
   output=NetworkBroken?"ERROR: Invalid HostID":"UniqueDeviceID: "+(WrongDevice?"00008030-0000111ABC000ABC":ID)+"\nProductType: iPhone16,2\nProductVersion: 18.0";
  }
  else if(name=="ideviceinstaller.exe")
  {
   Installs++;
   Assert.True(File.Exists(Path.Combine(workingDirectory,args.Last())));
   if(DisconnectInstall){code=1;output="No device found";}
   else if(FailAllInstalls || (FailFirstInstall&&Installs==1)){code=1;output="Could not connect to lockdownd: Invalid HostID";}
  }
  return Task.FromResult(new Result{Code=code,Output=output});
 }
}
public sealed class PairingFixture : IDisposable
{
 readonly TempFolder folder=new();
 public PairingRunner Runner { get; }=new();
 public Engine Engine { get; }
 public DeviceCommandClient Client { get; }
 public DeviceRow Device => new(){ID=Runner.ID,Name="Клиентский iPhone"};
 public PairingFixture()
 {
  var store=new SettingsStore(folder.Path);store.Settings.Current="fixture";store.Save();
  var bins=Path.Combine(folder.Path,"Versions/fixture/windows_amd64");Directory.CreateDirectory(bins);
  foreach(var name in new[]{"idevicepair.exe","ideviceinfo.exe","ideviceinstaller.exe"})File.WriteAllText(Path.Combine(bins,name),"fixture");
  Client=new DeviceCommandClient(Runner,new ToolLocator(store));Engine=new Engine(folder.Path,Runner,new InstalledAppleSupportProbe());
  Engine.Settings.SoundsEnabled=false;Engine.Settings.CheckGuiUpdates=false;
 }
 public LocalRow Ipa(string name="one")
 {
  var path=Path.Combine(folder.Path,name+".ipa");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
  using var writer=new StreamWriter(zip.CreateEntry("Payload/Test.app/Info.plist").Open());
  writer.Write("<plist><dict><key>MinimumOSVersion</key><string>16.0</string><key>CFBundleSupportedPlatforms</key><array><string>iPhoneOS</string></array></dict></plist>");
  return new(){Path=path};
 }
 public void Dispose()=>folder.Dispose();
}
public sealed class PairingTests
{
 [Fact] public async Task PreflightRepairsSelectedDeviceAndWaitsForTrust()
 {
  using var f=new PairingFixture();var r=f.Runner;
  r.Pairing.Enqueue(("validate","ERROR: Invalid HostID"));r.Pairing.Enqueue(("pair","ERROR: Please accept the trust dialog on the screen of device"));r.Pairing.Enqueue(("pair","OK"));r.Pairing.Enqueue(("validate","OK"));
  var states=new List<DeviceConnectionProgress>();var ready=new DeviceReadiness(f.Client,(_,ct)=>Task.CompletedTask);
  await ready.EnsureAsync(f.Device,states.Add,CancellationToken.None);
  Assert.Empty(r.Pairing);Assert.Contains(states,x=>x.WaitingForTrust);Assert.Equal(0,r.Installs);
  Assert.All(r.Calls,c=>{Assert.Contains(r.ID,c.Args);Assert.DoesNotContain("--network",c.Args);});
  Assert.DoesNotContain(r.Calls,c=>c.Args.Contains("unpair"));
 }
 [Fact] public async Task StaleRecordIsRemovedOnlyOnceAndOnlyForChosenUdid()
 {
  using var f=new PairingFixture();var r=f.Runner;
  foreach(var step in new[]{("validate","Invalid HostID"),("pair","Invalid HostID"),("unpair","OK"),("pair","OK"),("validate","OK")})r.Pairing.Enqueue(step);
  await new DeviceReadiness(f.Client).EnsureAsync(f.Device,null,CancellationToken.None);
  Assert.Single(r.Calls,x=>x.Args.Contains("unpair"));Assert.All(r.Calls,c=>Assert.Contains(r.ID,c.Args));
 }
 [Theory] [InlineData("ERROR: Device said that the user denied the trust dialog.",FailureCode.TrustDenied)] [InlineData("No device found",FailureCode.Disconnected)]
 public async Task DenialAndDisconnectionStopWithoutRepairLoop(string message,FailureCode code)
 {
  using var f=new PairingFixture();f.Runner.Pairing.Enqueue(("validate",message));
  var error=await Assert.ThrowsAsync<DeviceConnectionException>(()=>new DeviceReadiness(f.Client).EnsureAsync(f.Device,null,CancellationToken.None));
  Assert.Equal(code,error.Failure.Code);Assert.Single(f.Runner.Calls);
 }
 [Fact] public async Task WifiNeverPairsAnotherTransport()
 {
  using var f=new PairingFixture();f.Runner.NetworkBroken=true;var d=f.Device;d.Network=true;
  var error=await Assert.ThrowsAsync<DeviceConnectionException>(()=>new DeviceReadiness(f.Client).EnsureAsync(d,null,CancellationToken.None));
  Assert.Contains("USB",error.Failure.Remedy);Assert.All(f.Runner.Calls,x=>{Assert.Equal("ideviceinfo.exe",x.Tool);Assert.Contains("--network",x.Args);Assert.Contains(d.ID,x.Args);});
 }
 [Fact] public async Task WrongIdentityStopsBeforeInstaller()
 {
  using var f=new PairingFixture();f.Runner.WrongDevice=true;
  await Assert.ThrowsAsync<DeviceConnectionException>(()=>new DeviceReadiness(f.Client).EnsureAsync(f.Device,null,CancellationToken.None));Assert.Equal(0,f.Runner.Installs);
 }
 [Fact] public async Task CancelWhileWaitingDoesNotInstallAnything()
 {
  using var f=new PairingFixture();using var stop=new CancellationTokenSource();
  f.Runner.Pairing.Enqueue(("validate","Invalid HostID"));f.Runner.Pairing.Enqueue(("pair","Please accept the trust dialog"));
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new DeviceReadiness(f.Client,(_,ct)=>{stop.Cancel();return Task.Delay(10,ct);}).EnsureAsync(f.Device,null,stop.Token));
  Assert.Equal(0,f.Runner.Installs);Assert.Equal(2,f.Runner.Calls.Count);
 }
 [Fact] public async Task BrokenConnectionStopsBatchWithoutMarkingEveryIpaFailed()
 {
  var started=new List<int>();var done=new List<int>();var errors=new List<int>();
  await Assert.ThrowsAsync<DeviceConnectionException>(()=>BatchRunner.Run(new[]{1,2,3,4},(i,ct)=>i==2?Task.FromException(new DeviceConnectionException(ErrorLocalizer.Describe("No device found"))):Task.CompletedTask,(i,n)=>started.Add(i),i=>done.Add(i),(i,e)=>errors.Add(i),null,CancellationToken.None));
  Assert.Equal(new[]{1,2},started);Assert.Equal(new[]{1},done);Assert.Equal(new[]{2},errors);
 }
 [WindowsFact] public async Task HandshakeFailureRepairsAndRetriesOnlyCurrentIpaOnce()
 {
  using var f=new PairingFixture();f.Runner.FailFirstInstall=true;
  var one=f.Ipa();var two=f.Ipa("two");var successes=new List<string>();
  await BatchRunner.Run(new[]{one,two},(row,ct)=>f.Engine.Devices.InstallAsync(f.Device,row,ct),(row,n)=>{},row=>successes.Add(row.Path),(row,e)=>throw e,null,CancellationToken.None);
  Assert.Equal(3,f.Runner.Installs);Assert.Equal(2,successes.Count);Assert.Single(f.Runner.Calls,x=>x.Args.Contains("pair"));
 }
 [WindowsFact] public async Task PersistentHandshakeFailureDoesNotRestartAllIpas()
 {
  using var f=new PairingFixture();f.Runner.FailAllInstalls=true;var errors=new List<string>();
  await Assert.ThrowsAsync<DeviceConnectionException>(()=>BatchRunner.Run(new[]{f.Ipa(),f.Ipa("two")},(row,ct)=>f.Engine.Devices.InstallAsync(f.Device,row,ct),(row,n)=>{},row=>{},(row,e)=>errors.Add(row.Path),null,CancellationToken.None));
  Assert.Equal(2,f.Runner.Installs);Assert.Single(errors);Assert.Single(f.Runner.Calls,x=>x.Args.Contains("pair"));
 }
 [Theory]
 [InlineData("ERROR: Could not connect to lockdownd, error code -21",FailureCode.Pairing)]
 [InlineData("Invalid HostID",FailureCode.Pairing)]
 [InlineData("purchase error: your password has changed",FailureCode.PasswordChanged)]
 [InlineData("invalid response: empty songlist",FailureCode.EmptyCatalog)]
 [InlineData("ApplicationVerificationFailed",FailureCode.Verification)]
 [InlineData("0xe8008015",FailureCode.Signature)]
 [InlineData("No device found",FailureCode.Disconnected)]
 [InlineData("DeviceOSVersionTooLow",FailureCode.Incompatible)]
 public void ErrorsHaveCauseAndAction(string text,FailureCode code)
 {
  var error=ErrorLocalizer.Describe(text);Assert.Equal(code,error.Code);Assert.NotEmpty(error.Cause);Assert.NotEmpty(error.Remedy);Assert.Contains("Что сделать:",error.Description);
 }
}
