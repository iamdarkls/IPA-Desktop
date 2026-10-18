$projectRoot=Split-Path $PSScriptRoot -Parent
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'IPA-Desktop.exe') -Destination (Join-Path $projectRoot 'IPA-Desktop-test.dll') -Force
Add-Type -ReferencedAssemblies @((Join-Path $projectRoot 'IPA-Desktop-test.dll'),'System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IpaDesktop;
public static class DesktopContract {
 public static TaskCompletionSource<bool> BeginWait(Desktop view) {
  var pending = new TaskCompletionSource<bool>();
  Func<CancellationToken, Task> action = delegate(CancellationToken ct) { return pending.Task; };
  typeof(Desktop).GetMethod("Work", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
   .Invoke(view, new object[]{"Test busy controls", action});
  return pending;
 }

 public static void Error(Desktop view, string message) {
  typeof(Desktop).GetMethod("Error", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
   .Invoke(view, new object[]{new Exception(message)});
 }
}
public sealed class InstalledAppleSupportProbe : IAppleSupportProbe
{
    public AppleSupportSnapshot Read()
    {
        return new AppleSupportSnapshot
        {
            ServiceFound = true,
            ServiceStatusKnown = true,
            ServiceRunning = true,
            ServiceScanCompleted = true,
            RegistryScanCompleted = true
        };
    }
}
public sealed class RecordingProcessRunner : IProcessRunner
{
    readonly object gate = new object();
    public readonly List<string[]> Calls = new List<string[]>();
    public string LastPackage;
    public string LastWorkingDirectory;
    public bool FailInstall;
    public bool FailDownload;
    public CancellationTokenSource CancelInstall;
    public Action<string> Log { get; set; }
    public Process Active { get { return null; } }
    public RecordingProcessRunner() { Log = delegate { }; }
    public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish, string workingDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Calls.Add((string[])args.Clone()); }
        if (args.Length == 4 && args[2] == "install")
        {
            LastWorkingDirectory = workingDirectory;
            if (String.IsNullOrEmpty(workingDirectory) || Path.IsPathRooted(args[3])) throw new Exception("Expected relative IPA path with explicit working directory");
            foreach (char c in args[3]) if (c > 127) throw new Exception("Non-ASCII install argument");
            LastPackage = Path.Combine(workingDirectory, args[3]);
            if (!File.Exists(LastPackage) || File.ReadAllText(LastPackage) != "fixture") throw new Exception("Staged IPA content is unavailable");
            if (CancelInstall != null) { CancelInstall.Cancel(); ct.ThrowIfCancellationRequested(); }
            if (FailInstall) return Task.FromResult(new Result { Code = 1, Output = "fixture install failure" });
        }
        if (FailDownload && args.Length > 0 && args[0] == "download")
            return Task.FromResult(new Result { Code = 1, Output = "ERROR: fixture download failure" });
        string output = "{\"success\":true}";
        if (Path.GetFileName(exe) == "idevice_id.exe")
            output = "00008030-0000111ABC000DEF\n00008030-0000111ABC000ABC\n";
        else if (Path.GetFileName(exe) == "ideviceinfo.exe")
            output = args.Length == 2 ? "ProductType: iPhone16,2\nProductVersion: 18.0\n" : "Test iPhone";
        else if (args.Length >= 2 && args[0] == "auth" && args[1] == "info")
            output = "{\"success\":true,\"email\":\"test@example.com\"}";
        return Task.FromResult(new Result { Code = 0, Output = output });
    }
}
public sealed class WifiRunner : IProcessRunner {
 public Action<string> Log{get;set;} public Process Active{get{return null;}}
 public int Installs,NetworkInstalls;public bool NetworkFailure,UsbFailure,InstallFailure,WrongIdentity;
 public string Ios="17.0";
 public Task<Result> Run(string exe,string[] args,CancellationToken ct,bool visible,bool publish,string workingDirectory=null){
  ct.ThrowIfCancellationRequested();bool net=Array.IndexOf(args,"--network")>=0||Array.IndexOf(args,"-n")>=0;
  if(exe.EndsWith("idevice_id.exe")){
   if(net&&NetworkFailure||!net&&UsbFailure)return Task.FromResult(new Result{Code=1,Output="discovery failed"});
   return Task.FromResult(new Result{Code=0,Output="00008030-0000111ABC000DEF\ninvalid\n00008030-0000111ABC000DEF"});
  }
  if(exe.EndsWith("ideviceinfo.exe")){
   if(net&&NetworkFailure)return Task.FromResult(new Result{Code=1,Output="network unreachable"});
   return Task.FromResult(new Result{Code=0,Output=Array.IndexOf(args,"-k")>=0?"Test iPhone":"UniqueDeviceID: "+(WrongIdentity?"00008030-0000111ABC000ABC":"00008030-0000111ABC000DEF")+"\nProductType: iPhone16,2\nProductVersion: "+Ios});
  }
  if(exe.EndsWith("ideviceinstaller.exe")){
   Installs++;if(net)NetworkInstalls++;
   int offset=net?1:0;if(args[offset]!="--udid"||args[offset+1]!="00008030-0000111ABC000DEF"||args[offset+2]!="install")throw new Exception("Transport/UDID arguments lost");
   if(!File.Exists(Path.Combine(workingDirectory,args[offset+3])))throw new Exception("Missing staged package");
   return Task.FromResult(new Result{Code=InstallFailure?1:0,Output=InstallFailure?"connection lost":"OK"});
  }
  throw new Exception("Unexpected command");
 }
}
public sealed class VersionTool : IAppToolClient {
 public readonly List<string[]> Calls=new List<string[]>();
 public Task<Result> RunAsync(string[] args,CancellationToken ct,bool publish){
  ct.ThrowIfCancellationRequested();Calls.Add(args);if(publish)throw new Exception("Version query published raw data");
  return Task.FromResult(new Result{Code=0,Output=args[0]=="list-versions"?"{\"externalVersionIdentifiers\":[\"100\",\"200\",\"200\"]}":"{\"displayVersion\":\"2.0\",\"minimumOSVersion\":\"16.0\",\"supportedPlatforms\":[\"iPhoneOS\"]}"});
 }
}
public static class WifiVersionContract {
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action,string message){try{action();}catch(Exception){return;}throw new Exception(message);}
 static void Ipa(string path,string minimum,string platform,bool binary){
  using(var z=System.IO.Compression.ZipFile.Open(path,System.IO.Compression.ZipArchiveMode.Create)){
   using(var stream=z.CreateEntry("Payload/Test.app/Info.plist").Open()){
    byte[] bytes=binary?Convert.FromBase64String("YnBsaXN0MDDSAQIDBV8QGkNGQnVuZGxlU3VwcG9ydGVkUGxhdGZvcm1zXxAQTWluaW11bU9TVmVyc2lvbqEEWGlQaG9uZU9TVDE2LjAIDSo9P0gAAAAAAAABAQAAAAAAAAAGAAAAAAAAAAAAAAAAAAAATQ=="):System.Text.Encoding.UTF8.GetBytes("<plist><dict><key>MinimumOSVersion</key><string>"+minimum+"</string><key>CFBundleSupportedPlatforms</key><array><string>"+platform+"</string></array></dict></plist>");
    stream.Write(bytes,0,bytes.Length);
   }
  }
 }
 public static void Run(string root){
  var ct=CancellationToken.None;Directory.CreateDirectory(root);
  var runner=new WifiRunner();var engine=new Engine(root,runner);engine.Settings.Current="test";
  string bins=Path.Combine(root,"Versions/test/windows_amd64");Directory.CreateDirectory(bins);
  foreach(var name in new[]{"idevice_id.exe","ideviceinfo.exe","ideviceinstaller.exe"})File.WriteAllText(Path.Combine(bins,name),"fixture");
  var devices=engine.Devices.FindAsync(true,ct).GetAwaiter().GetResult();
  Assert(devices.Count==2&&!devices[0].Network&&devices[1].Network&&devices[1].DisplayLabel.Contains("Wi-Fi"),"Discovery merged transports or duplicated UDIDs");
  runner.UsbFailure=true;Assert(engine.Devices.FindAsync(true,ct).GetAwaiter().GetResult().Count==1,"USB failure blocked Wi-Fi");runner.UsbFailure=false;
  runner.NetworkFailure=true;Assert(engine.Devices.FindAsync(true,ct).GetAwaiter().GetResult().Count==1,"Wi-Fi failure lost USB");runner.NetworkFailure=false;
  string good=Path.Combine(root,"good.ipa"),bad=Path.Combine(root,"bad.ipa"),mac=Path.Combine(root,"mac.ipa"),binary=Path.Combine(root,"binary.ipa");
  Ipa(good,"16.0","iPhoneOS",false);Ipa(bad,"18.0","iPhoneOS",false);Ipa(mac,"15.0","MacOSX",false);Ipa(binary,"","",true);
  var binaryInfo=IpaMetadata.ReadDetails(binary);Assert(binaryInfo.MinimumOS=="16.0"&&binaryInfo.Platforms.Length==1&&binaryInfo.Platforms[0]=="iPhoneOS","Binary plist minimum OS/platform lost");
  Assert(AppCompatibility.BlockReason(new IpaDetails{MinimumOS="17.0.0",Platforms=new[]{"iPhoneOS"}},"17.0")==null,"Equal normalized OS blocked");
  Assert(AppCompatibility.BlockReason(new IpaDetails(),null)==null,"Unknown compatibility fabricated");
  engine.Devices.InstallAsync(devices[1],new LocalRow{Path=good},ct).GetAwaiter().GetResult();Assert(runner.NetworkInstalls==1,"Wi-Fi install did not use network");
  Reject(()=>engine.Devices.InstallAsync(devices[1],new LocalRow{Path=bad},ct).GetAwaiter().GetResult(),"Installed unsupported iOS");
  Reject(()=>engine.Devices.InstallAsync(devices[0],new LocalRow{Path=mac},ct).GetAwaiter().GetResult(),"Installed macOS package");
  Assert(runner.Installs==1,"Blocked compatibility reached installer");
  runner.WrongIdentity=true;Reject(()=>engine.Devices.InstallAsync(devices[1],new LocalRow{Path=good},ct).GetAwaiter().GetResult(),"Wrong device accepted");runner.WrongIdentity=false;
  runner.NetworkFailure=true;Reject(()=>engine.Devices.InstallAsync(devices[1],new LocalRow{Path=good},ct).GetAwaiter().GetResult(),"Offline device installed");runner.NetworkFailure=false;
  Assert(runner.Installs==1,"Offline Wi-Fi fell back to USB");
  runner.InstallFailure=true;Reject(()=>engine.Devices.InstallAsync(devices[1],new LocalRow{Path=good},ct).GetAwaiter().GetResult(),"Failed install succeeded");Assert(runner.Installs==2&&runner.NetworkInstalls==2,"Wi-Fi failure retried automatically");runner.InstallFailure=false;
  using(var stop=new CancellationTokenSource()){stop.Cancel();Reject(()=>engine.Devices.InstallAsync(devices[1],new LocalRow{Path=good},stop.Token).GetAwaiter().GetResult(),"Cancelled install succeeded");}Assert(runner.Installs==2,"Cancellation started installer");
  var queue=new DownloadSelection();var app=new AppRow{ID="123",Name="Test"};queue.AddVariant(app,"100");queue.AddVariant(app,"200");queue.AddVariant(app,"100");queue.AddVariant(app,null);
  Assert(queue.Count==3,"Multiple versions collapsed or duplicates added");var snapshot=queue.Snapshot();queue.RemoveKey(snapshot[0].Key);Assert(queue.Count==2&&queue.Snapshot()[0].VersionID=="200","Removal deleted adjacent version");
  Assert(AppVersions.ParseManual("100, 200 100").Count==2,"Manual ID dedup failed");Reject(()=>AppVersions.ParseManual("100 --debug"),"Unsafe manual ID accepted");
  var tool=new VersionTool();var versions=new AppVersions(tool);var rows=versions.ListAsync("123",ct).GetAwaiter().GetResult();Assert(rows.Count==2&&rows[0].ID=="200"&&tool.Calls.Count==1,"Version list queried metadata eagerly");
  versions.ReadAsync("123",rows[0],ct).GetAwaiter().GetResult();Assert(rows[0].Version=="2.0"&&rows[0].MinimumOS=="16.0"&&rows[0].Platforms=="iPhoneOS","Version table metadata missing");
 }
}

public sealed class GrowingDownloadTool : IAppToolClient {
 public async Task<Result> RunAsync(string[] args,CancellationToken ct,bool publish){
  string path=args[Array.IndexOf(args,"-o")+1]+".tmp";
  try{
   using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite)){
    for(int i=1;i<=3;i++){file.SetLength(i*1048576);file.Flush();await Task.Delay(1150,ct).ConfigureAwait(false);}
   }
   return new Result{Code=0,Output="{\"success\":true}"};
  }finally{File.Delete(path);}
 }
}
public static class DownloadMeterContract {
 static void Assert(bool ok,string text){if(!ok)throw new Exception(text);}
 public static void Run(string folder){
  Directory.CreateDirectory(folder);
  var meter=new FileTransferMeter();
  Assert(!meter.Sample(1048576,0).BytesPerSecond.HasValue,"Speed invented from first sample");
  var update=meter.Sample(3145728,2);Assert(update.BytesPerSecond==1048576,"Transfer rate calculation is wrong");
  meter.Sample(3145728,3);meter.Sample(3145728,4);meter.Sample(3145728,5);Assert(meter.Sample(3145728,6).BytesPerSecond==0,"Stalled transfer kept stale speed");
  Assert(!meter.Sample(1024,7).BytesPerSecond.HasValue,"Counter restart produced fake speed");
  var progress=new QueueProgress();progress.Begin("Скачивание",1,2,"Test");
  Assert(progress.Feed(update.Message),"Measured transfer was not accepted");progress.Feed("PROGRESS:50");
  Assert(progress.Text.Contains("≈")&&progress.Text.Contains("МБ/с")&&Math.Abs(progress.Value-25)<0.01,"Percentage erased measured metrics or changed queue progress");
  progress.Begin("Установка",1,1,"Test");Assert(!progress.Feed(update.Message),"File-download measurement leaked into install progress");
  var downloads=new ApplicationDownloads(new GrowingDownloadTool(),folder);var messages=new List<string>();
  downloads.Transfer=delegate(string text){lock(messages){messages.Add(text);}};
  downloads.DownloadAsync(new DownloadItem{ID="123",Name="Test"},CancellationToken.None).GetAwaiter().GetResult();
  lock(messages){Assert(messages.Count>=2&&messages.Exists(x=>x.StartsWith("FILE_TRANSFER:")&&!x.EndsWith(":?")),"Actual growing temporary file did not report speed");}
  int count;lock(messages){count=messages.Count;}
  using(var stop=new CancellationTokenSource()){
   stop.CancelAfter(1250);bool cancelled=false;
   try{downloads.DownloadAsync(new DownloadItem{ID="123",Name="Canceled"},stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled,"Download cancellation was swallowed");
  }
  lock(messages){count=messages.Count;}Thread.Sleep(1100);lock(messages){Assert(messages.Count==count,"Transfer monitor continued after completion or cancellation");}
  Assert(Directory.GetFiles(folder,"*.tmp").Length==0,"Test download retained temporary files");
 }
}

'@
# Реальные схемы get-version-metadata: 1.1.8 без новых полей, 1.1.9 с minimumOSVersion, 1.2.0 с supportedPlatforms.
$old=[IpaDesktop.ToolResponse]::VersionMetadataText([IpaDesktop.ToolResponse]::Object('{"displayVersion":"2.3","releaseDate":"2026-10-15","success":true}'))
if(!$old.Contains('Версия: 2.3') -or !$old.Contains('Минимальная версия ОС: не указана') -or !$old.Contains('Платформы: не указаны')){throw 'Legacy metadata lost version/date or fabricated compatibility'}
$v119=[IpaDesktop.ToolResponse]::VersionMetadataText([IpaDesktop.ToolResponse]::Object('{"displayVersion":"2.3","releaseDate":"2026-10-15","minimumOSVersion":"16.0","success":true}'))
if(!$v119.Contains('Минимальная версия ОС: 16.0') -or !$v119.Contains('Платформы: не указаны')){throw 'ipatool 1.1.9 minimum OS metadata failed'}
$v120=[IpaDesktop.ToolResponse]::VersionMetadataText([IpaDesktop.ToolResponse]::Object('{"displayVersion":"2.3","minimumOSVersion":"17.0","supportedPlatforms":["iPhoneOS","iPhoneOS","AppleTVOS"],"futureField":true}'))
if(!$v120.Contains('Платформы: iOS (iPhoneOS), AppleTVOS') -or !$v120.Contains('Минимальная версия ОС: 17.0')){throw 'ipatool 1.2.0 platform metadata failed'}
$mac=[IpaDesktop.ToolResponse]::VersionMetadataText([IpaDesktop.ToolResponse]::Object('{"supportedPlatforms":["macOS"],"minimumOSVersion":"15.7"}'))
if(!$mac.Contains('Платформы: macOS') -or $mac.Contains('iPhoneOS')){throw 'macOS app presented as iOS compatible'}
$missing=[IpaDesktop.ToolResponse]::VersionMetadataText([IpaDesktop.ToolResponse]::Object('{"minimumOSVersion":null,"supportedPlatforms":[null,12,{},""]}'))
if(!$missing.Contains('Минимальная версия ОС: не указана') -or !$missing.Contains('Платформы: не указаны')){throw 'Unavailable optional metadata fabricated values'}
Write-Host 'PASS: ipatool 1.1.8/1.1.9/1.2.0 metadata, minimum OS, multiple platforms, missing fields and macOS distinction'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-oop-'+[Guid]::NewGuid().ToString('N'))
$window=$null
$originalContext=[Threading.SynchronizationContext]::Current
try {
 [DownloadMeterContract]::Run((Join-Path $temp 'download-meter'))
 Write-Host 'PASS: measured file growth, rate window, stalled transfer, counter reset, percentage preservation, actual temporary-file sampling and monitor cleanup on success/cancel'
 [WifiVersionContract]::Run((Join-Path $temp 'wifi-contract'))
 Write-Host 'PASS: USB/Wi-Fi discovery isolation, partial discovery failure, network flags, connection identity, actual staged IPA compatibility, XML/binary minimum OS, platform rejection, cancellation, no silent fallback/retry, multiple versions and manual IDs'
 $runner=New-Object RecordingProcessRunner
 $supportProbe=New-Object InstalledAppleSupportProbe
 $engine=New-Object IpaDesktop.Engine($temp,$runner,$supportProbe)
 $engine.Settings.Current='test'
 $engine.Settings.SoundsEnabled=$false;$engine.Settings.CheckGuiUpdates=$false;$engine.Settings.AutoUpdate=$false;$engine.Settings.AutoUpdateDeviceTools=$false
 $bins=Join-Path $temp 'Versions/test/windows_amd64'
 New-Item -ItemType Directory -Path $bins -Force|Out-Null
 foreach($name in @('ipatool.exe','idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')){[IO.File]::WriteAllText((Join-Path $bins $name),'fixture')}
 $item=New-Object IpaDesktop.DownloadItem;$item.ID='123';$item.Name='Example';$item.VersionID='456'
 $engine.ApplicationDownloads.DownloadAsync($item,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 $call=$runner.Calls[$runner.Calls.Count-1]
 if(($call[0..4] -join ' ') -ne 'download -i 123 --purchase -o' -or ($call[6..9] -join ' ') -ne '--external-version-id 456 --format json' -or [IO.Path]::GetDirectoryName($call[5]) -ne $engine.Downloads -or [IO.Path]::GetExtension($call[5]) -ne '.ipa'){throw 'Download service changed CLI arguments'}
 $firstDestination=$call[5]
 $engine.ApplicationDownloads.DownloadAsync($item,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 if($runner.Calls[$runner.Calls.Count-1][5] -eq $firstDestination){throw 'Separate account downloads can overwrite the same IPA'}
 $devices=$engine.Devices.FindAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if($devices.Count -ne 2 -or $devices[0].Name -eq $devices[1].Name -or !$devices[0].Name.StartsWith('Test iPhone')){throw 'Device service lost names or duplicate-name disambiguation'}
 foreach($name in @('MAX.ipa','Telegram.ipa')){[IO.File]::WriteAllText((Join-Path $engine.Downloads $name),'fixture')}
 $files=$engine.LocalFiles.ListAsync().GetAwaiter().GetResult()
 if($files.Count -ne 2){throw 'Local repository did not list IPA files'}
 $engine.Devices.InstallAsync($devices[0],$files[0],[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
 $call=$runner.Calls[$runner.Calls.Count-1]
 if($call[0] -ne '--udid' -or $call[1] -ne $devices[0].ID -or [IO.File]::Exists($runner.LastPackage) -or [IO.Directory]::Exists([IO.Path]::GetDirectoryName($runner.LastPackage))){throw 'Installation lost UDID or retained temporary package'}
 # Exercise Unicode file names and parent folders, independent of 8.3 aliases.
 $unicodeDir=Join-Path $temp 'Пользователь Кириллица'
 [IO.Directory]::CreateDirectory($unicodeDir)|Out-Null
 $unicodeFile=Join-Path $unicodeDir 'АЗС Лукойл Макс.ipa'
 [IO.File]::WriteAllText($unicodeFile,'fixture')
 $unicodeRow=New-Object IpaDesktop.LocalRow;$unicodeRow.Path=$unicodeFile
 foreach($mode in @('success','failure','cancel')) {
  $runner.FailInstall=($mode -eq 'failure');$cancel=New-Object Threading.CancellationTokenSource
  $runner.CancelInstall=$null;if($mode -eq 'cancel'){$runner.CancelInstall=$cancel}
  $rejected=$false
  try {try{$engine.Devices.InstallAsync($devices[0],$unicodeRow,$cancel.Token).GetAwaiter().GetResult()|Out-Null}catch{$rejected=$true}}
  finally {$runner.CancelInstall=$null;$cancel.Dispose()}
  if(($mode -eq 'success') -eq $rejected){throw ('Unexpected install outcome: '+$mode)}
  if([IO.File]::ReadAllText($unicodeFile) -ne 'fixture' -or @(Get-ChildItem -LiteralPath $unicodeDir -Directory -Filter '.install-*').Count -ne 0){throw ('Installation damaged source or leaked temporary files: '+$mode)}
 }
 $runner.FailInstall=$false
 # Run a real child process from a Unicode working directory.
 $probe=Join-Path $temp 'install-path-probe.exe'
 Add-Type -OutputAssembly $probe -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
public static class InstallPathProbe {
 public static int Main(string[] args) {
  if(args.Length!=1 || Path.IsPathRooted(args[0]))return 2;
  foreach(char c in args[0])if(c>127)return 3;
  return File.ReadAllText(args[0])=="fixture" ? 0 : 4;
 }
}
'@
 $package=[IpaDesktop.InstallationPackage]::CreateAsync($unicodeFile,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 try {
  $realRunner=New-Object IpaDesktop.ProcessRunner($temp)
  $probeResult=$realRunner.Run($probe,[string[]]@($package.RelativePath),[Threading.CancellationToken]::None,$false,$false,$package.WorkingDirectory).GetAwaiter().GetResult()
  if($probeResult.Code -ne 0){throw 'Real Windows child process could not read Unicode-directory IPA through ASCII relative path'}
 }finally{$package.Dispose()}
 Write-Host 'PASS: real child process Unicode working directory, Cyrillic IPA, ASCII arguments, success/failure/cancel cleanup and original preservation'
 $engine.Save()
 [IO.Directory]::CreateDirectory((Join-Path $temp 'Versions/old'))|Out-Null
 $engine.Settings.Previous='old';$engine.Settings.PreviousVersion='0';$engine.Rollback()
 $restored=New-Object IpaDesktop.Engine($temp)
 if($restored.Settings.SoundsEnabled -or $restored.Settings.CheckGuiUpdates -or $restored.Settings.AutoUpdate -or $restored.Settings.AutoUpdateDeviceTools){throw 'Settings/rollback lost user update preferences'}
 $engine.Settings.Current='test';$engine.Save()
 $snapshot=New-Object IpaDesktop.AppleSupportSnapshot
 $snapshot.ServiceScanCompleted=$true;$snapshot.RegistryScanCompleted=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::NotDetected){throw 'Absent components were reported installed'}
 $snapshot.InstallationDetected=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::ServiceMissing){throw 'Registered components without a service were reported ready'}
 $snapshot.ServiceFound=$true;$snapshot.ServiceStatusKnown=$true
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Stopped){throw 'Stopped service was reported ready'}
 $snapshot.ServiceRunning=$true;$snapshot.InstallationDetected=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Running){throw 'Store-style service without standalone installation record was missed'}
 $snapshot.ServiceStatusKnown=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Unknown){throw 'Unreadable service status was reported stopped'}
 $snapshot.ServiceFound=$false;$snapshot.RegistryScanCompleted=$false
 if([IpaDesktop.AppleSupportService]::Evaluate($snapshot).Status -ne [IpaDesktop.AppleSupportStatus]::Unknown){throw 'Registry access failure was reported absent'}
 $probe=New-Object IpaDesktop.WindowsAppleSupportProbe
 $actual=$probe.Read()
 if(!$actual){throw 'Read-only Windows probe did not return a report'}
 $report=[IpaDesktop.AppleSupportService]::Evaluate($actual)
 Write-Host ('PASS: read-only Windows probe; actual status: '+$report.Status)
 Write-Host 'PASS: absent, stopped, missing service, running without MSI record and unknown inspection states'
 Write-Host 'PASS: injectable process runner; download arguments; device names/UDIDs; local repository; update preferences'
 [Threading.SynchronizationContext]::SetSynchronizationContext((New-Object Windows.Threading.DispatcherSynchronizationContext([Windows.Threading.Dispatcher]::CurrentDispatcher)))
 $hostFolder=Join-Path $temp 'host'
 New-Item -ItemType Directory -Path $hostFolder|Out-Null
 Copy-Item -LiteralPath (Join-Path $projectRoot 'MainWindow.xaml') -Destination $hostFolder
 $desktop=New-Object IpaDesktop.Desktop($engine,$false,$hostFolder)
 $window=$desktop.Window;$window.Show()
 function Pump {
  $window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$window.UpdateLayout()
 }
 function WaitIdle {
  $clock=[Diagnostics.Stopwatch]::StartNew()
  do {Pump;[Threading.Thread]::Sleep(30)}while($window.FindName('Cancel').IsEnabled -and $clock.Elapsed.TotalSeconds -lt 10)
  if($window.FindName('Cancel').IsEnabled){throw 'Controller operation did not finish'}
  Pump
 }
 function Click($name) {$window.FindName($name).RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))}
 function FindTabs($node) {
  if($node -is [Windows.Controls.TabControl]){return $node}
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($node)){
   if($child -is [Windows.DependencyObject]){$found=FindTabs $child;if($found){return $found}}
  }
 }
 function FindRemoveButton($node) {
  if($node -is [Windows.Controls.Button] -and $node.Tag){return $node}
  for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){
   $found=FindRemoveButton ([Windows.Media.VisualTreeHelper]::GetChild($node,$i));if($found){return $found}
  }
 }
 function ClickRow($grid,$index=0,$doubleClick=$true) {
  $grid.SelectedItems.Clear();$grid.SelectedIndex=$index
  $grid.ScrollIntoView($grid.Items[$index]);Pump
  $row=$grid.ItemContainerGenerator.ContainerFromIndex($index)
  if(!$row){throw 'Controller row was not generated'}
  foreach($event in @([Windows.UIElement]::PreviewMouseLeftButtonDownEvent,[Windows.UIElement]::PreviewMouseLeftButtonUpEvent)){
   $mouse=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
   $mouse.RoutedEvent=$event;$mouse.Source=$row;$grid.RaiseEvent($mouse)
  }
  if($doubleClick) {
   $double=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
   $double.RoutedEvent=[Windows.Controls.Control]::MouseDoubleClickEvent;$double.Source=$row;$grid.RaiseEvent($double)
  }
  Pump
 }
 Pump
 $tabs=FindTabs $window
 $window.FindName('Query').Text='123';Click 'Search';WaitIdle
 if($window.FindName('Apps').Items.Count -ne 1){throw 'Direct-ID search changed'}
 ClickRow ($window.FindName('Apps')) 0 $false
 if($window.FindName('ChosenApps').Items.Count -ne 0){throw 'Search single click added a row'}
 ClickRow ($window.FindName('Apps'))
 if($window.FindName('ChosenApps').Items.Count -ne 1){throw 'Search click selection failed'}
 $window.FindName('Query').Text='';Pump
 if($window.FindName('Apps').Items.Count -ne 0 -or $window.FindName('ChosenApps').Items.Count -ne 1){throw 'Search clear discarded selection or kept suggestions'}
 $remove=FindRemoveButton ($window.FindName('ChosenApps'))
 $remove.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$remove)));Pump
 if($window.FindName('ChosenApps').Items.Count -ne 0){throw 'Search remove button failed'}
 $tabs.SelectedIndex=1;Pump
 $clock=[Diagnostics.Stopwatch]::StartNew()
 while($window.FindName('Files').Items.Count -eq 0 -and $clock.Elapsed.TotalSeconds -lt 5){Pump;[Threading.Thread]::Sleep(30)}
 ClickRow ($window.FindName('Files')) 0 $false
 if($window.FindName('ChosenFiles').Items.Count -ne 0){throw 'File single click added a row'}
 ClickRow ($window.FindName('Files'))
 if($window.FindName('ChosenFiles').Items.Count -ne 1){throw 'IPA click selection failed'}
 $remove=FindRemoveButton ($window.FindName('ChosenFiles'))
 $remove.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$remove)));Pump
 if($window.FindName('ChosenFiles').Items.Count -ne 0){throw 'IPA remove button failed'}
 # Two independent double clicks build the action queue even though only one table row is selected.
 ClickRow ($window.FindName('Files')) 0
 ClickRow ($window.FindName('Files')) 1
 if($window.FindName('ChosenFiles').Items.Count -ne 2 -or $window.FindName('Files').SelectedItems.Count -ne 1 -or !$window.FindName('DeleteFiles').IsEnabled -or !$window.FindName('DeleteFiles').Content.Contains('(2)')){throw 'Delete action still depends on Ctrl/table multi-selection'}
 $deletePoint=$window.FindName('DeleteFiles').TranslatePoint((New-Object Windows.Point(0,0)),$window)
 $chosenPoint=$window.FindName('ChosenFiles').TranslatePoint((New-Object Windows.Point(0,0)),$window)
 if($deletePoint.Y -le $chosenPoint.Y){throw 'Delete action was not placed below selected IPA'}
 Click 'ClearInstall';Pump
 if($window.FindName('DeleteFiles').IsEnabled){throw 'Delete enabled with an empty action queue'}
 # Real IPA metadata drives the actual account dropdown and bulk action, without CLI calls.
 function NewAccountIpa($name,$account) {
  $path=Join-Path $engine.Downloads $name
  $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
  try {
   $entry=$zip.CreateEntry('iTunesMetadata.plist');$stream=$entry.Open()
   try {
    $bytes=[Text.Encoding]::UTF8.GetBytes('<plist version="1.0"><dict><key>appleId</key><string>'+$account+'</string><key>itemName</key><string>Same app</string></dict></plist>')
    $stream.Write($bytes,0,$bytes.Length)
   }finally{$stream.Dispose()}
  }finally{$zip.Dispose()}
  return $path
 }
 function RefreshAndWait {
  $before=$window.FindName('FileAccount').SelectedItem
  Click 'RefreshFiles'
  $clock=[Diagnostics.Stopwatch]::StartNew()
  do {Pump;[Threading.Thread]::Sleep(20)}while([object]::ReferenceEquals($before,$window.FindName('FileAccount').SelectedItem) -and $clock.Elapsed.TotalSeconds -lt 5)
  if([object]::ReferenceEquals($before,$window.FindName('FileAccount').SelectedItem)){throw 'File account refresh did not complete'}
 }
 $aliceA=NewAccountIpa 'account-a-1.ipa' 'alice@example.com'
 $aliceB=NewAccountIpa 'account-a-2.ipa' ' ALICE@example.com '
 $bob=NewAccountIpa 'account-b.ipa' 'bob@example.com'
 $callsBefore=$runner.Calls.Count
 RefreshAndWait
 $accounts=$window.FindName('FileAccount');$fileGrid=$window.FindName('Files')
 if($accounts.Items.Count -ne 4 -or $fileGrid.Items.Count -ne 5){throw 'Account groups lost unknown/case-insensitive metadata'}
 $accounts.SelectedItem=@($accounts.Items|Where-Object Key -eq 'alice@example.com')[0];Pump
 if($fileGrid.Items.Count -ne 2 -or !$window.FindName('FilesSummary').Text.Contains('2 из 5')){throw 'Account filter did not limit visible IPA'}
 Click 'SelectAllFiles';Pump
 if($window.FindName('ChosenFiles').Items.Count -ne 2 -or $window.FindName('SelectAllFiles').IsEnabled){throw 'Bulk add missed rows or allowed duplicate selection'}
 $accounts.SelectedItem=@($accounts.Items|Where-Object Key -eq 'bob@example.com')[0];Pump
 if($fileGrid.Items.Count -ne 1 -or $window.FindName('ChosenFiles').Items.Count -ne 2 -or !$window.FindName('InstallCount').Text.Contains('вне фильтра: 2')){throw 'Changing account lost or hid the existing queue without warning'}
 Click 'SelectAllFiles';Pump
 if($window.FindName('ChosenFiles').Items.Count -ne 3){throw 'Bulk add included filtered-out IPA or dropped previous selection'}
 # Check actual WPF text in the custom ComboBox template, not just its selected model.
 function VisualText($node) {
  if($node -is [Windows.Controls.TextBlock]){$node.Text}
  for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++) {
   VisualText ([Windows.Media.VisualTreeHelper]::GetChild($node,$i))
  }
 }
 $selection=$accounts.Template.FindName('SelectionContent',$accounts)
 $rendered=@(VisualText $selection) -join ' '
 if(!$rendered.Contains('bob@example.com') -or $rendered.Contains('IpaDesktop.LocalFileAccount')){throw 'Selected account label is not visible in the ComboBox template'}
 # Capture the live controller state, including selected account and hidden chosen IPA.
 $visual=$window.Content
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$visual.ActualWidth,[int]$visual.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=New-Object IO.MemoryStream
 $encoder.Save($stream);Write-Host ('IPA_ACCOUNT_PREVIEW:'+ [Convert]::ToBase64String($stream.ToArray()));$stream.Dispose()

 $accounts.SelectedItem=@($accounts.Items|Where-Object {$null -ne $_.Key -and $_.Key.Length -eq 0})[0];Pump
 if($fileGrid.Items.Count -ne 2 -or !$window.FindName('InstallCount').Text.Contains('вне фильтра: 3')){throw 'Unknown metadata filter used current signed-in account'}
 Click 'SelectAllFiles';Pump
 if($window.FindName('ChosenFiles').Items.Count -ne 5){throw 'Unknown-account files were omitted from bulk selection'}
 Click 'ClearInstall';Pump
 if(!$window.FindName('SelectAllFiles').IsEnabled){throw 'Clearing selection did not restore bulk action'}
 $accounts.SelectedItem=@($accounts.Items|Where-Object Key -eq 'alice@example.com')[0];Pump
 Click 'SelectAllFiles';Pump
 RefreshAndWait
 if($fileGrid.Items.Count -ne 2 -or $window.FindName('ChosenFiles').Items.Count -ne 2 -or $accounts.SelectedItem.Key -ne 'alice@example.com'){throw 'Refresh lost account filter or queued paths'}
 [IO.File]::Delete($aliceA);RefreshAndWait
 if($fileGrid.Items.Count -ne 1 -or $window.FindName('ChosenFiles').Items.Count -ne 1){throw 'Deleted IPA remained in the action queue'}
 [IO.File]::Delete($aliceB);RefreshAndWait
 if($fileGrid.Items.Count -ne 3 -or $null -ne $accounts.SelectedItem.Key -or $window.FindName('ChosenFiles').Items.Count -ne 0){throw 'Removed account did not fall back to all files safely'}
 [IO.File]::Delete($bob);RefreshAndWait
 if($runner.Calls.Count -ne $callsBefore){throw 'File filtering or bulk selection ran a device/auth command'}
 $pending=[DesktopContract]::BeginWait($desktop);Pump
 if($accounts.IsEnabled -or $window.FindName('SelectAllFiles').IsEnabled){throw 'Account controls stayed enabled during an operation'}
 $pending.SetResult($true);WaitIdle
 if(!$accounts.IsEnabled -or !$window.FindName('SelectAllFiles').IsEnabled){throw 'Account controls were not restored after an operation'}
 Write-Host 'PASS: real IPA account filtering, unknown metadata, normalized grouping, visible-only bulk selection, hidden queue counts, deduplication, refresh/deletion reconciliation and busy-state guards'
 # Use the actual checkbox event and reload the persisted settings.
 $window.FindName('SoundsEnabled').IsChecked=$true;Click 'SoundsEnabled';Pump
 $soundReload=New-Object IpaDesktop.Engine($temp)
 if(!$soundReload.Settings.SoundsEnabled){throw 'Sound checkbox did not persist enabled preference'}
 $window.FindName('SoundsEnabled').IsChecked=$false;Click 'SoundsEnabled';Pump
 $soundReload=New-Object IpaDesktop.Engine($temp)
 if($soundReload.Settings.SoundsEnabled){throw 'Sound checkbox did not persist disabled preference'}
 Write-Host 'PASS: double-click IPA action queue, bottom delete placement, empty-queue guard and sound checkbox persistence'
 if($window.FindName('Devices').Background.Color.ToString() -ne '#FF16704C'){throw 'Initial device-search color changed'}
 Click 'CheckAppleSupport';WaitIdle
 if(!$window.FindName('AppleSupportStatus').Text.Contains('Apple') -or $window.FindName('AppleSupportStatus').Foreground.Color.ToString() -ne '#FF81D4AD'){throw 'Apple support check did not render the running state'}
 Click 'Devices';WaitIdle
 if($window.FindName('DeviceList').Items.Count -ne 2 -or !$window.FindName('DeviceList').SelectedItem.Name.StartsWith('Test iPhone')){throw 'Named device discovery failed'}
 Click 'AccountInfo';WaitIdle
 if($window.FindName('Login').IsEnabled -or !$window.FindName('Logout').IsEnabled){throw 'Signed-in button state failed'}
 # Retry uses failure snapshots, preserving a different version already selected for the same app.
 $tabs.SelectedIndex=0;Pump
 $window.FindName('Query').Text='333';Click 'Search';WaitIdle
 $searchController=$desktop.GetType().GetField('search',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($desktop)
 $searchController.GetType().GetField('versionApp',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($searchController,'333')
 $window.FindName('VersionList').Items.Add('777')|Out-Null;$window.FindName('VersionList').SelectedIndex=1
 Click 'AddChosen';Pump
 $failures=$searchController.GetType().GetField('failedDownloads',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($searchController)
 $retryRow=New-Object IpaDesktop.AppRow;$retryRow.ID='333';$retryRow.Name='Retry fixture';$failures.Add($retryRow,'456')
 $desktop.GetType().GetMethod('ApplyActions',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 if(!$window.FindName('RetryDownloads').IsEnabled){throw 'Retry button did not reflect stored errors'}
 $callsBefore=$runner.Calls.Count;Click 'RetryDownloads';WaitIdle
 $downloads=New-Object 'Collections.Generic.List[object]'
 for($i=$callsBefore;$i -lt $runner.Calls.Count;$i++){if($runner.Calls[$i][0] -eq 'download'){$downloads.Add($runner.Calls[$i])}}
 if($downloads.Count -ne 1 -or ($downloads[0] -join ' ') -notmatch '--external-version-id 456'){throw 'Retry did not use the failed version snapshot'}
 if($window.FindName('ChosenApps').Items.Count -ne 1 -or $window.FindName('ChosenApps').Items[0].VersionID -ne '777'){throw 'Retry discarded a separately selected version'}
 if($window.FindName('RetryDownloads').IsEnabled -or !$window.FindName('OperationDetail').Text.Contains('1')){throw 'Retry result/progress was not rendered'}
 # Save and restore a named set through the actual UI controls.
 $tabs.SelectedIndex=1;Pump
 $window.FindName('Files').SelectedItems.Clear()
 foreach($local in $window.FindName('Files').Items){$window.FindName('Files').SelectedItems.Add($local)}
 Click 'AddInstall';Pump
 $expected=$window.FindName('ChosenFiles').Items.Count
 $tabs.SelectedIndex=4;Pump
 $window.FindName('SetName').Text='Base fixture';Click 'SaveSet';Pump
 if($window.FindName('AppSets').Items.Count -ne 1){throw 'Set save was not connected'}
 Click 'ClearInstall';Click 'ClearChosen';Pump
 Click 'UseSet';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 1 -or $window.FindName('ChosenApps').Items[0].ID -ne '333' -or $window.FindName('ChosenApps').Items[0].VersionID -ne $null -or $tabs.SelectedIndex -ne 0){throw 'Set did not add account-independent IDs with latest version to download queue'}
 if($window.FindName('ChosenFiles').Items.Count -ne 0){throw 'ID set reused IPA from another account'}
 if(!$window.FindName('StorageSummary').Text.Contains('2') -or $window.FindName('StorageFiles').Items.Count -ne 2){throw 'Storage view was not populated'}
 if(!$window.FindName('ExportDiagnostics')){throw 'Diagnostic export control is missing'}
 if($window.FindName('SaveFileSet')){throw 'Removed IPA set action is still present'}
 # Inline set creation and additive updates through the editable WPF template.
 Click 'ClearChosen';$tabs.SelectedIndex=0;Pump
 $window.FindName('Query').Text='444';Click 'Search';WaitIdle;Click 'AddChosen';Pump
 $picker=$window.FindName('SearchSet');$picker.ApplyTemplate()|Out-Null
 $editor=$picker.Template.FindName('PART_EditableTextBox',$picker)
 if(!$editor -or $editor.Visibility -ne 'Visible'){throw 'Set picker has no visible editable input'}
 $editor.Text='Inline fixture';Pump
 if($picker.Text -ne 'Inline fixture'){throw 'Typing a new set name did not update the picker'}
 Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline fixture')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 1 -or $saved[0].Apps[0].ID -ne '444'){throw 'Inline creation failed'}
 if($window.FindName('AppSets').Items.Count -ne 2){throw 'Inline set did not refresh the library tab'}
 $window.FindName('Query').Text='555';Click 'Search';WaitIdle;Click 'AddChosen';Pump
 $picker.SelectedItem='Inline fixture';Click 'AddToSet';Pump;Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline fixture')
 if($saved[0].Apps.Count -ne 2){throw 'Add to set replaced entries or duplicated IDs'}
 Click 'Download';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 0 -or !$window.FindName('AddToSet').IsEnabled -or !$window.FindName('SaveSet').IsEnabled){throw 'Set saving became unavailable after successful download'}
 $tabs.SelectedIndex=4;Pump;$window.FindName('SetName').Text='After download';Click 'SaveSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'After download')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 2){throw 'Legacy save button lost the completed download selection'}
 $tabs.SelectedIndex=0;Pump;$picker.Text='Inline after download';Click 'AddToSet';Pump
 $saved=@($engine.AppSets.Load()|Where-Object Name -eq 'Inline after download')
 if($saved.Count -ne 1 -or $saved[0].Apps.Count -ne 2){throw 'Inline save lost the completed download selection'}
 Click 'ClearChosen';Pump
 if($window.FindName('AddToSet').IsEnabled -or $window.FindName('SaveSet').IsEnabled){throw 'Explicit clear retained stale set selection'}
 # Multiple sets can be checked together; overlapping IDs preserve manually selected versions.
 $a=New-Object IpaDesktop.AppRow;$a.ID='444';$a.Name='Four'
 $b=New-Object IpaDesktop.AppRow;$b.ID='555';$b.Name='Five'
 $c=New-Object IpaDesktop.AppRow;$c.ID='666';$c.Name='Six'
 $engine.AppSets.SaveApps('Overlap A',[IpaDesktop.AppRow[]]@($a,$b))
 $engine.AppSets.SaveApps('Overlap B',[IpaDesktop.AppRow[]]@($b,$c))
 $desktop.GetType().GetMethod('RefreshAppSets',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 $window.FindName('Query').Text='555';Click 'Search';WaitIdle
 $searchController.GetType().GetField('versionApp',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($searchController,'555')
 $window.FindName('VersionList').Items.Add('888')|Out-Null;$window.FindName('VersionList').SelectedIndex=1
 Click 'AddChosen';Pump
 $list=$window.FindName('AppSets');$list.SelectedItems.Clear()
 foreach($set in $list.Items){if($set.Name -like 'Overlap *'){$list.SelectedItems.Add($set)|Out-Null}}
 $tabs.SelectedIndex=4;Pump
 if($list.SelectedItems.Count -ne 2 -or !$window.FindName('SetPreview').Text.Contains('3')){throw 'Multiple-set selection/preview failed'}
 Click 'UseSet';WaitIdle
 $queue=@($window.FindName('ChosenApps').Items)
 if($queue.Count -ne 3 -or ($queue|Where-Object ID -eq '555').VersionID -ne '888'){throw 'Multiple sets duplicated IDs or reset selected version'}
 $tabs.SelectedIndex=4;Pump;Click 'UseSet';WaitIdle
 if($window.FindName('ChosenApps').Items.Count -ne 3){throw 'Adding multiple sets twice duplicated the queue'}
 $reopened=New-Object IpaDesktop.Engine($temp)
 if(@($reopened.AppSets.Load()|Where-Object Name -like 'Overlap *').Count -ne 2){throw 'Multiple sets did not survive reopening'}
 $tabs.SelectedIndex=4;Pump;Click 'DeleteSet';Pump
 if(@($engine.AppSets.Load()|Where-Object Name -like 'Overlap *').Count -ne 0 -or $window.FindName('StorageFiles').Items.Count -ne 2){throw 'Batch set deletion lost files or retained selected sets'}
 # Persistent errors survive tab switches and dismissal preserves the full report.
 Click 'ClearChosen';$tabs.SelectedIndex=0;Pump
 $window.FindName('Query').Text='999';Click 'Search';WaitIdle;Click 'AddChosen';Pump
 $runner.FailDownload=$true;Click 'Download';WaitIdle
 if($window.FindName('OperationNotice').Visibility -ne 'Visible' -or !$window.FindName('NoticeText').Text.Contains('999') -or !$window.FindName('NoticeText').Text.Contains('fixture download failure') -or !$window.FindName('NoticeRetry').IsEnabled){throw 'Failed download did not produce an actionable visible error'}
 $tabs.SelectedIndex=4;Pump
 if($window.FindName('OperationNotice').Visibility -ne 'Visible'){throw 'Error disappeared on tab switch'}
 $savedReport=$desktop.GetType().GetField('lastReport',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($desktop)
 if($savedReport.Failed -ne 1 -or !$savedReport.Details.Contains('fixture download failure')){throw 'Report lost per-app CLI error'}
 Click 'DismissNotice';Pump
 if($window.FindName('OperationNotice').Visibility -ne 'Collapsed' -or !$window.FindName('ShowLastReport').IsEnabled){throw 'Dismissing notice lost last report'}
 # Root error handling is nonmodal and visible even outside the journal.
 [DesktopContract]::Error($desktop,'generic fixture error')
 if(!$window.FindName('NoticeText').Text.Contains('generic fixture error') -or $window.FindName('NoticeRetry').Visibility -ne 'Collapsed'){throw 'Generic error was hidden or advertised an invalid retry'}
 # Run a new failed batch, then retry through the banner itself.
 $tabs.SelectedIndex=0;Click 'Download';WaitIdle
 $runner.FailDownload=$false;Click 'NoticeRetry';WaitIdle
 $report=$desktop.GetType().GetField('lastReport',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($desktop)
 if($report.Failed -ne 0 -or $report.Succeeded -ne 1 -or $window.FindName('NoticeRetry').Visibility -ne 'Collapsed'){throw 'Banner retry did not replace the failed result with success'}
 # Dedicated transfer metric remains visible independently of a long app title.
 $meterQueue=$desktop.GetType().GetField('queueProgress',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($desktop)
 $meterQueue.Begin('Скачивание',1,2,('Длинное название приложения '*20))
 $meterQueue.Feed('FILE_TRANSFER:2097152:1048576')|Out-Null
 $meterQueue.Feed('PROGRESS:50')|Out-Null
 $desktop.GetType().GetMethod('RenderQueue',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 Pump
 if(!$window.FindName('TransferDetail').Text.Contains('≈') -or $window.FindName('TransferDetail').ActualWidth -lt 100){throw 'Download speed is hidden by long app title'}
 $meterQueue.Reset();$desktop.GetType().GetMethod('RenderQueue',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($desktop,@())|Out-Null
 Write-Host 'PASS: dedicated download speed remains visible independently of long app names'
 # Exercise actual version-table controls, multiple manual IDs, metadata button and modal result.
 $versionTool=New-Object VersionTool
 $versionService=New-Object IpaDesktop.AppVersions($versionTool)
 $versionRows=$versionService.ListAsync('123',[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 $script:versionDialog=New-Object IpaDesktop.AppVersionWindow($window,'Fixture','123',$versionRows,$versionService,[Threading.CancellationToken]::None)
 $script:versionUiError=$null
 $window.Dispatcher.BeginInvoke([Action]{
  try{
   $dialog=$script:versionDialog;$type=$dialog.GetType();$flags=[Reflection.BindingFlags]'Instance,NonPublic'
   $manual=$type.GetField('manual',$flags).GetValue($dialog);$manual.Text='300, 100'
   $insert=$type.GetField('insert',$flags).GetValue($dialog);$insert.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$insert)))
   $grid=$type.GetField('grid',$flags).GetValue($dialog)
   if($grid.SelectedItems.Count -ne 2 -or $grid.Items.Count -ne 3){throw 'Manual IDs failed in actual dialog'}
   $details=$type.GetField('details',$flags).GetValue($dialog);$details.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$details)))
   if(@($grid.SelectedItems|Where-Object MinimumOS -eq '16.0').Count -ne 2){throw 'Selected metadata not shown in actual table'}
   $dialog.Window.Width=750;$dialog.Window.Height=470;$dialog.Window.UpdateLayout()
   if($grid.ActualHeight -lt 100 -or $grid.Columns[2].ActualWidth -lt 70){throw 'Version table collapsed at minimum size'}
   $target=$dialog.Window.Content
   $bmp=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$target.ActualWidth,[int]$target.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
   $bmp.Render($target);$png=New-Object Windows.Media.Imaging.PngBitmapEncoder;$png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
   $memory=New-Object IO.MemoryStream;try{$png.Save($memory);Write-Host ('IPA_VERSION_TABLE_PREVIEW:'+ [Convert]::ToBase64String($memory.ToArray()))}finally{$memory.Dispose()}
   $add=$type.GetField('add',$flags).GetValue($dialog);$add.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent,$add)))
  }catch{$script:versionUiError=$_;$script:versionDialog.Window.Close()}
 })|Out-Null
 try{
  if($script:versionDialog.Window.ShowDialog() -ne $true -or $script:versionUiError -or $script:versionDialog.SelectedIDs.Count -ne 2){throw ('Version dialog failed: '+$script:versionUiError)}
 }finally{$script:versionDialog.Dispose()}
 Write-Host 'PASS: actual WPF version table, manual IDs, selected metadata, multiple selection, minimum layout and modal queue result'
 # Dialog factories render the same IPA details and shared theme without extra file reads.
 $ipaDialog=[IpaDesktop.AppDialogs]::Ipa($window,$window.FindName('Files').Items[0])
 $ipaDialog.Show();$ipaDialog.UpdateLayout()
 if($ipaDialog.Content.Children[0].Text -notmatch 'App Store ID:'){throw 'IPA details dialog is incomplete'}
 $ipaDialog.Close()
 $deviceText=$engine.Devices.DetailsAsync($devices[0],[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if(!$deviceText.Contains('iPhone16,2') -or !$deviceText.Contains('18.0') -or !$deviceText.Contains($devices[0].ID)){throw 'Device details lost model/iOS/UDID'}
 function FindNamed($node,$name) {
  if($node -is [Windows.FrameworkElement] -and $node.Name -eq $name){return $node}
  foreach($child in [Windows.LogicalTreeHelper]::GetChildren($node)){
   if($child -is [Windows.DependencyObject]){$found=FindNamed $child $name;if($found){return $found}}
  }
 }
 # Inline settings retain persistence without a modal window.
 $window.FindName('SettingsTab').IsSelected=$true;Pump
 if(!$window.FindName('SettingsContent').IsVisible -or $window.OwnedWindows.Count -ne 0){throw 'Settings must remain in the main window'}
 $window.FindName('SoundsEnabled').IsChecked=$true;Click 'SoundsEnabled';Pump
 $checkSettings=New-Object IpaDesktop.Engine($temp)
 if(!$checkSettings.Settings.SoundsEnabled){throw 'Inline sound setting did not persist'}
 $engine.Settings.SoundsEnabled=$false;$engine.Save()
 # Cancel an edited draft; then save a rename and removal through the actual editor buttons.
 $editA=New-Object IpaDesktop.AppRow;$editA.ID='700';$editA.Name='Editor A'
 $editB=New-Object IpaDesktop.AppRow;$editB.ID='701';$editB.Name='Editor B'
 $engine.AppSets.SaveApps('Editor fixture',[IpaDesktop.AppRow[]]@($editA,$editB))
 $script:editorSave=[Action[string,Collections.Generic.IEnumerable[IpaDesktop.AppRow]]]{param($name,$apps) $engine.AppSets.Update('Editor fixture',$name,$apps)}
 $script:editor=[IpaDesktop.AppSetEditor]::Create($window,'Editor fixture',[IpaDesktop.AppRow[]]@($editA,$editB),$script:editorSave)
 $window.Dispatcher.BeginInvoke([Action]{(FindNamed $script:editor 'DraftName').Text='Canceled rename';$script:editor.Close()})|Out-Null
 $script:editor.ShowDialog()|Out-Null
 if(@($engine.AppSets.Load()|Where-Object Name -eq 'Editor fixture').Count -ne 1){throw 'Cancel changed saved set'}
 $script:editor=[IpaDesktop.AppSetEditor]::Create($window,'Editor fixture',[IpaDesktop.AppRow[]]@($editA,$editB),$script:editorSave)
 $window.Dispatcher.BeginInvoke([Action]{
  (FindNamed $script:editor 'DraftName').Text='Renamed editor'
  $draftList=FindNamed $script:editor 'DraftApps';$draftList.SelectedIndex=0
  (FindNamed $script:editor 'DraftRemove').RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
  (FindNamed $script:editor 'DraftSave').RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
 })|Out-Null
 $saved=$script:editor.ShowDialog()
 $edited=@($engine.AppSets.Load()|Where-Object Name -eq 'Renamed editor')
 if(!$saved -or $edited.Count -ne 1 -or $edited[0].Apps.Count -ne 1 -or $edited[0].Apps[0].ID -ne '701'){throw 'Editor did not save atomic rename and removal'}
 Write-Host 'PASS: visible per-app errors, tab persistence, dismissal, banner retry, IPA/device dialogs, inline settings persistence and set editor save/cancel'
 Write-Host 'PASS: simultaneous sets, overlapping IDs, version preservation, repeated additions, persistence and independent deletion'
 Write-Host 'PASS: editable inline set picker, additive deduplication, cross-tab refresh, both save buttons after completed download and explicit clear'
 Write-Host 'PASS: retry snapshot/version isolation, queue progress, saved set UI and storage UI'
 Write-Host 'PASS: real controller composition; search/clear/selection/removal; IPA selection/removal; device button/names; account state'
}finally{
 if($window){$window.Close()}
 [Threading.SynchronizationContext]::SetSynchronizationContext($originalContext)
 if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}
}
