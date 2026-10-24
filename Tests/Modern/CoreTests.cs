using System.IO.Compression;
using System.Text.Json;
using Xunit;
namespace IpaDesktop.Tests;
public sealed class CoreTests
{
 [Fact] public void ExistingSettingsKeepNamesDefaultsAndPreferences()
 {
  using var folder = new TempFolder();
  File.WriteAllText(System.IO.Path.Combine(folder.Path,"state.json"), "{\"Current\":\"old\",\"Version\":\"1.2.0\",\"AutoUpdate\":false,\"SoundsEnabled\":false,\"DeviceCurrent\":\"devices\",\"CheckGuiUpdates\":false}");
  var store = new SettingsStore(folder.Path);
  Assert.Equal("old",store.Settings.Current); Assert.False(store.Settings.AutoUpdate); Assert.True(store.Settings.AutoUpdateDeviceTools);
  store.Save(); var again = new SettingsStore(folder.Path);
  Assert.Equal("devices",again.Settings.DeviceCurrent); Assert.False(again.Settings.SoundsEnabled); Assert.False(again.Settings.CheckGuiUpdates);
 }
 [Fact] public void UntypedToolJsonKeepsDictionariesArraysAndBooleans()
 {
  var result = ToolResponse.Object("{\"success\":true,\"externalVersionIdentifiers\":[123,\"456\"],\"email\":\"fixture@example.com\",\"metadata\":{\"a\":1}}");
  Assert.Equal(true,ToolResponse.Get(result,"success")); Assert.IsType<object[]>(ToolResponse.Get(result,"externalVersionIdentifiers"));
  Assert.IsType<Dictionary<string,object>>(ToolResponse.Get(result,"metadata"));
  Assert.Equal(new[]{"123","456"},Engine.VersionIds(JsonCodec.Serialize(result)));
 }
 [Fact] public void CatalogRecoveryAndAtomicImportContracts()
 { using var folder = new TempFolder(); ReliabilityContract.Run(folder.Path); }
 [Fact] public void DownloadProgressAndCancellationContracts()
 { using var folder = new TempFolder(); DownloadMeterContract.Run(folder.Path); }
 [WindowsFact] public void DeviceTransportAndInstallationContracts()
 { using var folder = new TempFolder(); WifiVersionContract.Run(folder.Path); }
 [Fact] public void MetadataFixturesRemainCompatible()
 {
  using var folder = new TempFolder();
  using var fixtures = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"metadata-fixtures.json")));
  foreach(var fixture in fixtures.RootElement.EnumerateArray())
  {
   var path = System.IO.Path.Combine(folder.Path,fixture.GetProperty("name").GetString()+".ipa");
   File.WriteAllBytes(path,Convert.FromBase64String(fixture.GetProperty("data").GetString()));
   Assert.Equal(fixture.GetProperty("expected").GetString(),IpaMetadata.ReadAccount(path));
  }
 }
 [Fact] public async Task BatchContinuesErrorsAndStopsCancellation()
 {
  var succeeded = new List<int>(); var failed = new List<int>();
  var result = await BatchRunner.Run(new[]{1,2,3},(int item,CancellationToken ct)=>item==2?Task.FromException(new IOException("fixture")):Task.CompletedTask,(_,_)=>{},i=>succeeded.Add(i),(i,_)=>failed.Add(i),null,CancellationToken.None);
  Assert.Equal(new[]{1,3},succeeded); Assert.Equal(new[]{2},failed); Assert.Equal(2,result.Succeeded);
 }
 [WindowsFact] public void PrivateConPtyLoginContract()
 {
  using var folder = new TempFolder();
  var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
  var configuration = AppContext.BaseDirectory.Contains("Release")?"Release":"Debug";
  var fixture = System.IO.Path.Combine(root,"Tests/LoginFixture/bin",configuration,"net10.0-windows/LoginFixture.exe");
  Assert.True(File.Exists(fixture),fixture); LoginContract.Run(fixture,folder.Path);
 }

 [Fact] public void ModernUpdaterValidatesSourceAndCompleteManifest()
 {
  using var folder=new TempFolder();
  var release=new Release{tag_name="v0.3.1",assets=new[]{new Asset{name="IPA-Desktop-0.3.1-Windows.zip",size=10,digest="sha256:"+new string('a',64),browser_download_url=AppInfo.ReleasesUrl+"/download/v0.3.1/IPA-Desktop-0.3.1-Windows.zip"}}};
  Assert.NotNull(GuiUpdater.SelectAsset(release));release.assets[0].browser_download_url="https://example.com/fixture.zip";
  Assert.Throws<InvalidDataException>(()=>GuiUpdater.SelectAsset(release));
  var zip=Path.Combine(folder.Path,"package.zip");
  using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var name in GuiUpdater.PackageFiles){using var writer=new StreamWriter(archive.CreateEntry(name).Open());writer.Write("fixture");}
  var stage=Path.Combine(folder.Path,"stage");GuiUpdater.Extract(zip,stage);
  Assert.All(GuiUpdater.PackageFiles,name=>Assert.True(File.Exists(Path.Combine(stage,name))));
  var bad=Path.Combine(folder.Path,"bad.zip");using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("IPA-Desktop.exe").Open());writer.Write("fixture");}
  Assert.Throws<InvalidDataException>(()=>GuiUpdater.Extract(bad,Path.Combine(folder.Path,"bad")));
 }
 [Fact] public void IndependentToolRollbackKeepsModernSettingsCompatible()
 {
  using var folder=new TempFolder();var engine=new Engine(folder.Path);
  foreach(var version in new[]{"current","previous","devices"})Directory.CreateDirectory(Path.Combine(folder.Path,"Versions",version));
  engine.Settings.Current="current";engine.Settings.Previous="previous";engine.Settings.DeviceCurrent="devices";engine.Settings.Source="Sorvigolova/ipatool";engine.Settings.AutoUpdateDeviceTools=false;engine.Save();
  engine.Rollback();var reloaded=new Engine(folder.Path);
  Assert.Equal("previous",reloaded.Settings.Current);Assert.Equal("devices",reloaded.Settings.DeviceCurrent);Assert.False(reloaded.Settings.AutoUpdate);Assert.False(reloaded.Settings.AutoUpdateDeviceTools);
 }
 [WindowsFact] public async Task ActualWindowsToolchainStillWorksOnModernRuntime()
 {
  using var folder=new TempFolder();using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(3));
  var engine=new Engine(folder.Path);var release=await engine.Check(stop.Token);await engine.Install(release,stop.Token);
  foreach(var name in new[]{"idevice_id.exe","ideviceinfo.exe","ideviceinstaller.exe"})
  {
   var help=await engine.Run(engine.Binary(name),new[]{"--help"},stop.Token,false,false);
   Assert.Equal(0,help.Code);Assert.Contains("--network",help.Output);
  }
  using var process=new ConPtyLoginFactory().Start(engine.Binary("ipatool*.exe"),engine.Downloads);var protocol=new AppleLoginProtocol();bool password=false;
  while(!password)
  {
   var chunk=await process.ReadAsync(stop.Token);Assert.NotNull(chunk);
   var prompt=protocol.Append(chunk);
   if(prompt==AppleLoginPrompt.Identifier){using var identifier=LoginContract.Secret("fixture@example.com");await process.SendAsync(identifier,stop.Token);}
   if(prompt==AppleLoginPrompt.Password)password=true;
  }
  Assert.True(password); // Stop before submitting any Apple password or network login.
 }
}
