using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using IpaDesktop;
public static class DesktopContract {
 public static object Files(Desktop view){return typeof(Desktop).GetField("files",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);}
 public static Task ScanDevices(Desktop view){var files=Files(view);return (Task)files.GetType().GetMethod("RefreshDevicesAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(files,null);}
 public static void StartMonitoring(Desktop view){var files=Files(view);files.GetType().GetMethod("StartDeviceDetection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(files,null);}
 public static void StopMonitoring(Desktop view){var files=Files(view);files.GetType().GetMethod("StopDeviceDetection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(files,null);}
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
    public string DeviceIds="00008030-0000111ABC000DEF\n00008030-0000111ABC000ABC\n";
    public bool HoldDiscovery,FailDiscovery;
    public int CanceledDiscoveries;
    async Task<Result> Hold(CancellationToken ct){try{await Task.Delay(Timeout.Infinite,ct);return null;}finally{CanceledDiscoveries++;}}
    public CancellationTokenSource CancelInstall;
    public Action<string> Log { get; set; }
    public Process Active { get { return null; } }
    public RecordingProcessRunner() { Log = delegate { }; }
    public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish, string workingDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Calls.Add((string[])args.Clone()); }
        if(Path.GetFileName(exe)=="idevice_id.exe"){
            if(HoldDiscovery)return Hold(ct);
            if(FailDiscovery)return Task.FromResult(new Result{Code=1,Output="discovery unavailable"});
        }
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
            output = DeviceIds;
        else if (Path.GetFileName(exe) == "ideviceinfo.exe")
            output = args.Length == 2 ? "UniqueDeviceID: " + args[1] + "\nProductType: iPhone16,2\nProductVersion: 18.0\n" : "Test iPhone";
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
  if(exe.EndsWith("idevicepair.exe"))return Task.FromResult(new Result{Code=0,Output="SUCCESS"});
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
  foreach(var name in new[]{"idevice_id.exe","ideviceinfo.exe","ideviceinstaller.exe","idevicepair.exe"})File.WriteAllText(Path.Combine(bins,name),"fixture");
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

public sealed class CatalogResponseHandler : HttpMessageHandler {
 public int Calls; public Func<CancellationToken,HttpResponseMessage> Reply;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(Reply(ct));}
}
public sealed class CancelAfterReadStream : MemoryStream {
 readonly CancellationTokenSource stop;
 public CancelAfterReadStream(CancellationTokenSource stop):base(new byte[262144]){this.stop=stop;}
 public override Task<int> ReadAsync(byte[] bytes,int offset,int count,CancellationToken ct){int read=Read(bytes,offset,count);stop.Cancel();return Task.FromResult(read);}
}
public static class ReliabilityContract {
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(string root){
  Directory.CreateDirectory(root);
  var store=new SettingsStore(Path.Combine(root,"catalog"));var locator=new ToolLocator(store);
  var files=Path.Combine(store.Root,"Files");Directory.CreateDirectory(files);
  var path=Path.Combine(files,"AppsList.txt");File.WriteAllText(path,"Cached app: 100001");
  var now=new DateTime(2026,10,23,0,0,0,DateTimeKind.Utc);var handler=new CatalogResponseHandler();
  handler.Reply=ct=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
  using(var http=new HttpClient(handler)){
   var catalog=new AppCatalog(store,locator,http,()=>now);
   Assert(catalog.SearchAsync("app",CancellationToken.None).GetAwaiter().GetResult()[0].Name=="Cached app","HTTP failure discarded cached catalog");
   catalog.SearchAsync("app",CancellationToken.None).GetAwaiter().GetResult();
   Assert(handler.Calls==1,"Catalog retries delayed every search during backoff");
   handler.Reply=ct=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("Updated app: 100001")};
   now=now.AddSeconds(31);
   Assert(catalog.SearchAsync("Updated",CancellationToken.None).GetAwaiter().GetResult().Count==1&&handler.Calls==2,"Catalog did not recover after transient failure");
   now=now.AddMinutes(1);catalog.SearchAsync("app",CancellationToken.None).GetAwaiter().GetResult();
   Assert(handler.Calls==2,"Successful catalog refreshed on every search");
   // Explicit cancellation must propagate and must not consume the next retry.
   var retry=new AppCatalog(store,locator,http,()=>now);
   using(var stop=new CancellationTokenSource()){
    handler.Reply=ct=>{stop.Cancel();ct.ThrowIfCancellationRequested();return null;};
    bool cancelled=false;try{retry.SearchAsync("app",stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled,"User cancellation was treated as a catalog network failure");
   }
   int calls=handler.Calls;
   handler.Reply=ct=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("Recovered app: 100001")};
   Assert(retry.SearchAsync("Recovered",CancellationToken.None).GetAwaiter().GetResult().Count==1&&handler.Calls==calls+1,"Canceled catalog request suppressed next attempt");
  }
  var imports=Path.Combine(root,"imports");Directory.CreateDirectory(imports);var repo=new LocalAppRepository(imports);
  var source=Path.Combine(root,"source.ipa");var data=new byte[262144];data[0]=17;data[data.Length-1]=93;File.WriteAllBytes(source,data);
  repo.ImportAsync(new[]{source},CancellationToken.None).GetAwaiter().GetResult();
  var destination=Path.Combine(imports,"source.ipa");
  var copied=File.ReadAllBytes(destination);Assert(copied.Length==data.Length&&copied[0]==17&&copied[copied.Length-1]==93,"Imported IPA was truncated");
  File.WriteAllText(source,"replacement");bool rejected=false;
  try{AtomicFile.CopyAsync(source,destination,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
  Assert(rejected&&File.ReadAllBytes(destination).Length==data.Length,"Atomic import overwrote an existing IPA");
  Assert(Directory.GetFiles(imports,"*.tmp").Length==0,"Failed import retained staging files");
  using(var stop=new CancellationTokenSource()){
   stop.Cancel();bool cancelled=false;
   try{repo.ImportAsync(new[]{source},stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled,"Pre-canceled import succeeded");
  }
  // Cancel at a deterministic point inside a read, independent of disk speed or file size.
  using(var stop=new CancellationTokenSource())using(var input=new CancelAfterReadStream(stop))using(var output=new MemoryStream()){
   var copy=typeof(AtomicFile).GetMethod("CopyStreamAsync",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   bool cancelled=false;try{((Task)copy.Invoke(null,new object[]{input,output,stop.Token})).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled&&output.Length==0,"Mid-copy cancellation wrote another block or was swallowed");
  }
 }
}
