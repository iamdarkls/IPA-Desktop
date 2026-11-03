using System.Text;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class LibraryAndRecoveryTests
{
 [Fact] public void InventoryReadsMetadataAndPlaceholdersAndNeverConfirmsIncompleteOrUnknownApps()
 {
  var metadata=Convert.ToBase64String(Encoding.UTF8.GetBytes("<plist><dict><key>itemId</key><integer>123</integer></dict></plist>"));
  var rows=DeviceApplications.Parse("note\n<plist><array><dict><key>CFBundleIdentifier</key><string>com.fixture</string><key>CFBundleShortVersionString</key><string>1.2</string><key>CFBundleVersion</key><string>7</string><key>IsPlaceholder</key><true/><key>iTunesMetadata</key><data>"+metadata+"</data></dict></array></plist>");
  var row=Assert.Single(rows);Assert.Equal("123",row.ID);Assert.True(row.Placeholder);
  var expected=new IpaDetails{BundleID=row.BundleID,Version="1.2",Build="7"};Assert.NotNull(DeviceApplications.Compare(expected,row));
  row.Placeholder=false;Assert.Null(DeviceApplications.Compare(expected,row));
  row.Build="8";Assert.NotNull(DeviceApplications.Compare(expected,row));
  Assert.NotNull(DeviceApplications.Compare(new IpaDetails(),row));
  Assert.ThrowsAny<Exception>(()=>DeviceApplications.Parse("not XML"));
 }
 [Fact] public void PurchasesKeepAppIdsButDoNotTreatBooksAsApps()
 {
  var rows=PurchaseHistory.Parse("{\"apps\":[{\"id\":123,\"name\":\"App\",\"kind\":\"iOS App\",\"purchaseDate\":\"2026-01-01\"},{\"id\":124,\"name\":\"Book\",\"kind\":\"Book\"}]}");
  Assert.True(rows.Single(x=>x.ID=="123").Downloadable);Assert.False(rows.Single(x=>x.ID=="124").Downloadable);
 }
 [Fact] public void QueueRoundTripPreservesIdentityAndConvertsRunningToUncertainWithoutResuming()
 {
  using var root=new TempFolder();var store=new QueueArchiveStore(root.Path);var queue=new OperationQueueViewModel();
  queue.Start("Установка",new[]{new QueueSeed("one.ipa","One","1.0"){BundleID="com.one",FileSize=42,ModifiedTicks=7},new QueueSeed("two.ipa","Two","2.0")},"phone|False","Phone",_=>{},null,"account-hash");
  queue.Begin(1);queue.Result(null);queue.Begin(2);queue.Measure(35,"transfer");store.Save(queue);
  bool resumed=false;var restored=new OperationQueueViewModel();restored.Restore(store.Load(),(_,_)=>resumed=true);
  Assert.False(resumed);Assert.Equal(QueueItemState.Succeeded,restored.Items[0].State);Assert.Equal(42,restored.Items[0].Seed.FileSize);
  Assert.Equal(QueueItemState.Uncertain,restored.Items[1].State);Assert.Equal(35,restored.Items[1].Progress);Assert.Equal("account-hash",restored.Items[1].Batch.AccountKey);
  Assert.False(restored.ResumeCommand.CanExecute(null));
  File.WriteAllText(Path.Combine(root.Path,"operation-queue.json"),"broken");Assert.ThrowsAny<Exception>(()=>store.Load());Assert.Equal("broken",File.ReadAllText(Path.Combine(root.Path,"operation-queue.json")));
 }
 [Fact] public void ProxySettingsCreateChildEnvironmentWithoutChangingParentAndRejectStoredCredentials()
 {
  var state=new State{ProxyMode="Custom",ProxyUrl="socks5://127.0.0.1:1080"};var proxy=new ProxyConfiguration(()=>state);
  var before=Environment.GetEnvironmentVariable("HTTPS_PROXY");var changes=proxy.EnvironmentChanges();Assert.Contains("127.0.0.1:1080",changes["HTTPS_PROXY"]);
  var block=ProxyConfiguration.EnvironmentBlock(changes);Assert.True(block.EndsWith("\0\0"),"Environment block must end with two NULs");Assert.True(before==Environment.GetEnvironmentVariable("HTTPS_PROXY"),"Parent environment changed");
  state.ProxyMode="Direct";Assert.Null(proxy.EnvironmentChanges()["HTTPS_PROXY"]);Assert.Equal("*",proxy.EnvironmentChanges()["NO_PROXY"]);
  Assert.Throws<ArgumentException>(()=>ProxyConfiguration.Validate("http://user:secret@localhost:8080"));
  Assert.Throws<ArgumentException>(()=>ProxyConfiguration.Validate("file:///tmp/proxy"));
 }
 [Fact] public void SetPlanUsesOnlyCurrentAccountAndPrefersHighestKnownLocalVersion()
 {
  var apps=new[]{new AppRow{ID="123",Name="One"},new AppRow{ID="456",Name="Two"}};
  var rows=new[]{new LocalRow{AppStoreID="123",AppleAccount="other@example.com",Version="9.0",Path="foreign"},new LocalRow{AppStoreID="123",AppleAccount="me@example.com",Version="1.0",Path="old"},new LocalRow{AppStoreID="123",AppleAccount="me@example.com",Version="2.0",Path="new"},new LocalRow{AppStoreID="456",AppleAccount="Не указан",Path="unknown"}};
  var plan=AppSetInstallationPlan.Build(apps,rows,"me@example.com");Assert.Equal("new",Assert.Single(plan.Local).Path);Assert.Equal("456",Assert.Single(plan.Missing).ID);
 }
}
