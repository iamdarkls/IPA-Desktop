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
 [Fact] public void LoginParsesStructuredResultAfterTerminalOutput()
 {
  var protocol=new AppleLoginProtocol();
  protocol.Append("Enter email: ");protocol.Append("Enter password: ");protocol.Append("Enter 2FA code: ");
  protocol.Append("\rEnter 2FA code: ******\r\n{\"success\":true,\"email\":\"fixture@example.com\"}\r\n");
  Assert.Equal("fixture@example.com",protocol.Complete(0));
  Assert.Equal(true,ToolResponse.Get(ToolResponse.Object("diagnostic\n{\"success\":true}\n"),"success"));
 }
 [Fact] public void CatalogRecoveryAndAtomicImportContracts()
 { using var folder = new TempFolder(); ReliabilityContract.Run(folder.Path); }
 sealed class SlowCatalog : HttpMessageHandler
 {
  public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public readonly TaskCompletionSource<string> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public int Calls;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
  {
   Interlocked.Increment(ref Calls);
   Started.TrySetResult();
   return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(await Reply.Task.WaitAsync(ct)) };
  }
 }
 [Fact] public async Task CatalogSearchUsesSavedCopyDuringStartupDownload()
 {
  using var folder = new TempFolder();
  var store = new SettingsStore(folder.Path);
  var files = Directory.CreateDirectory(Path.Combine(store.Root, "Files"));
  File.WriteAllText(Path.Combine(files.FullName, "AppsList.txt"), "Saved app: 100001");
  using var handler = new SlowCatalog();
  using var http = new HttpClient(handler);
  var catalog = new AppCatalog(store, new ToolLocator(store), http, () => DateTime.UtcNow);
  var ct = TestContext.Current.CancellationToken;
  var refresh = catalog.RefreshAsync(ct);
  try
  {
   await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
   var saved = await catalog.SearchAsync("app", ct).WaitAsync(TimeSpan.FromSeconds(2), ct);
   Assert.Equal("Saved app", Assert.Single(saved).Name);
  }
  finally { handler.Reply.TrySetResult("New app: 100001"); await refresh; }
  Assert.Equal("New app", Assert.Single(await catalog.SearchAsync("app", ct)).Name);
  Assert.Equal(1, handler.Calls);
 }
 [Fact] public async Task IpaCacheSeparatesSameSizeCaseDistinctFiles()
 {
  using var folder = new TempFolder();
  var first = Path.Combine(folder.Path, "App.ipa");
  var second = Path.Combine(folder.Path, "app.ipa");
  static void WriteIpa(string path, string name)
  {
   using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
   using var writer = new StreamWriter(zip.CreateEntry("iTunesMetadata.plist", CompressionLevel.NoCompression).Open());
   writer.Write($"<plist><dict><key>itemName</key><string>{name}</string></dict></plist>");
  }
  WriteIpa(first, "First");
  if (File.Exists(second)) return; // This volume treats the names as the same path.
  WriteIpa(second, "Other");
  var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
  File.SetLastWriteTimeUtc(first, timestamp);
  File.SetLastWriteTimeUtc(second, timestamp);
  Assert.Equal(new FileInfo(first).Length, new FileInfo(second).Length);
  var local = new LocalAppRepository(folder.Path);
  for (int scan = 0; scan < 2; scan++)
  {
   var rows = await local.ListAsync(TestContext.Current.CancellationToken);
   Assert.Equal("First", Assert.Single(rows, x => x.Path == first).AppName);
   Assert.Equal("Other", Assert.Single(rows, x => x.Path == second).AppName);
  }
 }
 [Fact] public async Task DownloadProgressAndCancellationContracts()
 { using var folder = new TempFolder(); await DownloadMeterContract.RunAsync(folder.Path); }
 [Fact] public void DeviceTransportAndInstallationContracts()
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
 [Fact] public void PrivateTerminalLoginContract()
 {
  using var folder = new TempFolder();
  var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
  var configuration = AppContext.BaseDirectory.Contains("Release")?"Release":"Debug";
  var fixture = System.IO.Path.Combine(root,"Tests/LoginFixture/bin",configuration,"net10.0/"+RuntimePlatform.ToolName("LoginFixture.exe"));
  Assert.True(File.Exists(fixture),fixture); LoginContract.Run(fixture,folder.Path);
 }

 [Fact] public void ModernUpdaterValidatesSourceAndCompleteManifest()
 {
  using var folder=new TempFolder();
  var release=new Release{tag_name="v0.3.1",assets=new[]{new Asset{name="IPA-Desktop-0.3.1-"+RuntimePlatform.AssetSuffix+".zip",size=10,digest="sha256:"+new string('a',64),browser_download_url=AppInfo.ReleasesUrl+"/download/v0.3.1/IPA-Desktop-0.3.1-"+RuntimePlatform.AssetSuffix+".zip"}}};
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
}
