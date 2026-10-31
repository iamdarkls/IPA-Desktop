using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class UiTests
{
 static T Find<T>(Window window,string name) where T:Control => window.FindControl<T>(name) ?? window.GetLogicalDescendants().OfType<T>().First(x=>x.Name==name);
 static void Click(Window window,string name) => Find<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 static async Task Wait(Func<bool> condition)
 {
  var time=Stopwatch.StartNew();
  while(!condition()){if(time.Elapsed.TotalSeconds>8)throw new TimeoutException("UI did not reach expected state");await Task.Delay(15);}
 }
 static Engine Backend(string root,RecordingProcessRunner runner=null)
 {
  var engine=new Engine(root,runner??new RecordingProcessRunner(),new InstalledAppleSupportProbe());
  engine.Settings.Current="test"; engine.Settings.CheckGuiUpdates=false;engine.Settings.AutoUpdate=false;engine.Settings.AutoUpdateDeviceTools=false;engine.Settings.SoundsEnabled=false;
  var bins=Path.Combine(root,"Versions/test/windows_amd64");Directory.CreateDirectory(bins);
  foreach(var name in new[]{"ipatool.exe","idevice_id.exe","ideviceinfo.exe","ideviceinstaller.exe","idevicepair.exe"})File.WriteAllText(Path.Combine(bins,name),"fixture");
  return engine;
 }
 static bool Busy(Desktop desktop)=>(bool)typeof(Desktop).GetProperty("IsBusy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(desktop);
 [AvaloniaFact] public async Task QueueSetsAndMinimumLayoutRemainUsable()
 {
  using var folder=new TempFolder();var backend=Backend(folder.Path);var desktop=new Desktop(backend,false);var window=desktop.Window;window.Show();
  try
  {
   Assert.IsType<MainWindow>(window);
   Assert.Equal("IPA Desktop · "+AppInfo.Version,window.Title);
   Assert.Equal(AppInfo.Version,Find<TextBlock>(window,"GuiVersionBadge").Text);
   Find<TextBox>(window,"Query").Text="123";Click(window,"Search");await Wait(()=>!Busy(desktop));Click(window,"AddChosen");
   Find<TextBox>(window,"Query").Text="456";Click(window,"Search");await Wait(()=>!Busy(desktop));Click(window,"AddChosen");
   Assert.Equal(2,Find<ItemsControl>(window,"ChosenApps").ItemCount);
   Find<TextBox>(window,"SetName").Text="Тестовый набор";Click(window,"SaveSet");
   Assert.Equal(2,backend.AppSets.Load().Single().Apps.Length);
   Click(window,"ClearChosen");Click(window,"UseSet");await Wait(()=>!Busy(desktop));
   Assert.Equal(2,Find<ItemsControl>(window,"ChosenApps").ItemCount);
   await Task.Delay(50);
   var toolbar=Find<ComboBox>(window,"VersionList");
   foreach(var name in new[]{"Versions","VersionTable","VersionInfo","AddChosen","Download"})
   {
    var button=Find<Button>(window,name);
    Assert.Equal(toolbar.Bounds.Height,button.Bounds.Height);
    Assert.Equal(toolbar.Bounds.Y,button.Bounds.Y);
   }
   var download=Find<Button>(window,"Download");
   Assert.Equal("Скачать (2)",download.Content);
   Assert.Contains(download.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Скачать (2)");
   Assert.Contains(download.GetVisualDescendants().OfType<AppIcon>(),x=>x.Kind==IconKind.Download);
   var tabs=window.GetLogicalDescendants().OfType<TabControl>().Single();
   window.Width=940;window.Height=720;tabs.SelectedIndex=1;await Task.Delay(50);
   Assert.True(Find<DataGrid>(window,"Files").Bounds.Height>=80,"Files table collapsed: " + Find<DataGrid>(window,"Files").Bounds.Height);
   Assert.NotNull(Find<TextBlock>(window,"DeviceDetectionStatus"));
   foreach(var tab in tabs.Items.Cast<TabItem>())
   {
    var origin=tab.TranslatePoint(default,window);
    Assert.NotNull(origin);
    Assert.True(origin.Value.X>=0 && origin.Value.X+tab.Bounds.Width<=window.ClientSize.Width,"Tab header clipped: "+tab.Header);
   }
   foreach(var name in new[]{"FileInfo","AddInstall","InstallFiles","DeleteFiles"})
   {
    var button=Find<Button>(window,name);var origin=button.TranslatePoint(default,window);
    Assert.Equal(Find<Button>(window,"InstallFiles").Bounds.Y,button.Bounds.Y);
    Assert.Equal(Find<Button>(window,"InstallFiles").Bounds.Height,button.Bounds.Height);
    Assert.True(origin.Value.X>=0 && origin.Value.X+button.Bounds.Width<=window.ClientSize.Width,"Action clipped: "+name);
   }
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task ConnectionFailureBlocksRetryUntilSelectedPhoneAnswersAgain()
 {
  using var fixture=new PairingFixture();var row=fixture.Ipa();var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  try
  {
   var devices=Find<ComboBox>(window,"DeviceList");devices.ItemsSource=new[]{fixture.Device};devices.SelectedIndex=0;
   var files=Find<DataGrid>(window,"Files");files.ItemsSource=new[]{row};files.SelectedItem=row;Click(window,"AddInstall");
   Assert.True(Find<Button>(window,"InstallFiles").IsEnabled);
   fixture.Runner.Pairing.Enqueue(("validate","No device found"));Click(window,"CheckConnection");await Wait(()=>!Busy(desktop));
   Assert.False(Find<Button>(window,"InstallFiles").IsEnabled);Assert.True(Find<Button>(window,"CheckConnection").IsEnabled);Assert.Equal(0,fixture.Runner.Installs);
   Assert.True(Find<Border>(window,"ConnectionNotice").IsVisible);Assert.Contains("Соединение",Find<TextBlock>(window,"ConnectionTitle").Text);
   Assert.Contains("Что сделать",Find<TextBlock>(window,"NoticeRemedy").Text);
   await DesktopContract.ScanDevices(desktop);
   Assert.True(Find<Button>(window,"InstallFiles").IsEnabled);
   Assert.Contains("восстановлена",Find<TextBlock>(window,"ConnectionTitle").Text);
   Click(window,"CheckConnection");await Wait(()=>!Busy(desktop));Assert.True(Find<Button>(window,"InstallFiles").IsEnabled, "title="+Find<TextBlock>(window,"ConnectionTitle").Text+" chosen="+Find<ItemsControl>(window,"ChosenFiles").ItemCount+" device="+devices.SelectedItem+" check="+Find<Button>(window,"CheckConnection").IsEnabled);
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task TrustRequestIsVisibleAndAutomaticallyCompletesAfterConfirmation()
 {
  using var fixture=new PairingFixture();fixture.Runner.WaitForTrust=true;var row=fixture.Ipa();var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  try
  {
   var devices=Find<ComboBox>(window,"DeviceList");devices.ItemsSource=new[]{fixture.Device};devices.SelectedIndex=0;
   Click(window,"CheckConnection");await Wait(()=>Find<TextBlock>(window,"ConnectionTitle").Text?.Contains("Подтвердите")==true);
   Assert.True(Busy(desktop));Assert.True(Find<Button>(window,"Cancel").IsEnabled);Assert.Contains("Доверять",Find<TextBlock>(window,"ConnectionMessage").Text);
   window.Width=940;window.Height=720;await Task.Delay(50);
   fixture.Runner.Trusted=true;await Wait(()=>!Busy(desktop));Assert.Contains("готов",Find<TextBlock>(window,"ConnectionTitle").Text);Assert.Equal(0,fixture.Runner.Installs);
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task CanceledTrustWaitKeepsInstallationBlockedUntilConnectionIsChecked()
 {
  using var fixture=new PairingFixture();fixture.Runner.WaitForTrust=true;var row=fixture.Ipa();var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  try
  {
   var devices=Find<ComboBox>(window,"DeviceList");devices.ItemsSource=new[]{fixture.Device};devices.SelectedIndex=0;
   var files=Find<DataGrid>(window,"Files");files.ItemsSource=new[]{row};files.SelectedItem=row;Click(window,"AddInstall");
   Click(window,"CheckConnection");await Wait(()=>Find<TextBlock>(window,"ConnectionTitle").Text?.Contains("Подтвердите")==true);
   Click(window,"Cancel");await Wait(()=>!Busy(desktop));Assert.False(Find<Button>(window,"InstallFiles").IsEnabled);Assert.Contains("отменена",Find<TextBlock>(window,"ConnectionTitle").Text);
   Assert.Equal(0,fixture.Runner.Installs);fixture.Runner.Trusted=true;
   Click(window,"CheckConnection");await Wait(()=>!Busy(desktop));Assert.True(Find<Button>(window,"InstallFiles").IsEnabled, "title="+Find<TextBlock>(window,"ConnectionTitle").Text+" chosen="+Find<ItemsControl>(window,"ChosenFiles").ItemCount+" device="+devices.SelectedItem+" check="+Find<Button>(window,"CheckConnection").IsEnabled);
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task AutomaticDiscoveryPreservesIdentityAndStopsBeforeWork()
 {
  using var folder=new TempFolder();var runner=new RecordingProcessRunner{DeviceIds="00008030-0000111ABC000DEF"};var desktop=new Desktop(Backend(folder.Path,runner),false);var window=desktop.Window;window.Show();
  try
  {
   await DesktopContract.ScanDevices(desktop);var devices=Find<ComboBox>(window,"DeviceList");Assert.Equal(1,devices.ItemCount);Assert.NotNull(devices.SelectedItem);
   var original=devices.SelectedItem;await DesktopContract.ScanDevices(desktop);Assert.Same(original,devices.SelectedItem);
   runner.DeviceIds+="\n00008030-0000111ABC000ABC";await DesktopContract.ScanDevices(desktop);devices.SelectedIndex=1;var chosen=(DeviceRow)devices.SelectedItem;
   await DesktopContract.ScanDevices(desktop);Assert.Equal(chosen.ID,((DeviceRow)devices.SelectedItem).ID);
   runner.DeviceIds="00008030-0000111ABC000DEF";await DesktopContract.ScanDevices(desktop);Assert.Null(devices.SelectedItem);
   runner.DeviceIds="";await DesktopContract.ScanDevices(desktop);Assert.Equal(0,devices.ItemCount);Assert.False(Find<Button>(window,"InstallFiles").IsEnabled);
   runner.DeviceIds="00008030-0000111ABC000DEF";await DesktopContract.ScanDevices(desktop);Assert.NotNull(devices.SelectedItem);
   runner.FailDiscovery=true;await DesktopContract.ScanDevices(desktop);Assert.Equal(0,devices.ItemCount);runner.FailDiscovery=false;await DesktopContract.ScanDevices(desktop);Assert.Equal(1,devices.ItemCount);
   runner.HoldDiscovery=true;var scan=DesktopContract.ScanDevices(desktop);Assert.Same(scan,DesktopContract.ScanDevices(desktop));
   var pending=DesktopContract.BeginWait(desktop);await Wait(()=>scan.IsCompleted);Assert.True(runner.CanceledDiscoveries>0);pending.SetResult(true);await Wait(()=>!Busy(desktop));
   runner.HoldDiscovery=false;
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task ClosingWindowCancelsPendingDiscovery()
 {
  using var folder=new TempFolder();var runner=new RecordingProcessRunner{HoldDiscovery=true};var desktop=new Desktop(Backend(folder.Path,runner),false);var window=desktop.Window;window.Show();
  var scan=DesktopContract.ScanDevices(desktop);window.Close();await Wait(()=>!window.IsVisible);Assert.True(scan.IsCompleted);Assert.True(runner.CanceledDiscoveries>0);
 }
 [AvaloniaFact] public async Task LoginRetriesMasksClearsAndConfirmsTwoFactor()
 {
  using var folder=new TempFolder();var desktop=new Desktop(Backend(folder.Path),false);var owner=desktop.Window;owner.Show();
  try
  {
   var service=new WindowLoginService();using var login=new AppleLoginWindow(owner,service,CancellationToken.None);
   var result=login.Window.ShowDialog<bool>(owner);
   login.EmailBox.Text="fixture@example.com";login.PasswordBox.Text="Fixture-secret";login.SubmitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Assert.Equal("",login.PasswordBox.Text);Assert.NotEqual('\0',login.PasswordBox.PasswordChar);
   await Wait(()=>login.SubmitButton.IsEnabled);Assert.Contains("повтор",login.Status.Text.ToLowerInvariant());
   login.PasswordBox.Text="Fixture-secret";login.SubmitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>login.CodeBox.IsVisible&&login.SubmitButton.IsEnabled);
   login.CodeBox.Text="12";login.SubmitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Contains("шесть",login.Status.Text);
   login.CodeBox.Text="123456";login.SubmitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal("",login.CodeBox.Text);
   Assert.True(await result);Assert.Equal("fixture@example.com",login.SignedInEmail);Assert.Equal("",login.PasswordBox.Text);
  }
  finally{owner.Close();}
 }
 [AvaloniaFact] public async Task VersionMetadataDoesNotLoseMultiSelection()
 {
  using var folder=new TempFolder();var owner=new Desktop(Backend(folder.Path),false).Window;owner.Show();
  try
  {
   var service=new AppVersions(new VersionTool());var rows=await service.ListAsync("123",CancellationToken.None);
   using var dialog=new AppVersionWindow(owner,"Fixture","123",rows,service,CancellationToken.None);var result=dialog.Window.ShowDialog<bool>(owner);
   var grid=dialog.Window.GetLogicalDescendants().OfType<DataGrid>().Single();grid.SelectedItems.Add(rows[0]);grid.SelectedItems.Add(rows[1]);
   var details=dialog.Window.GetLogicalDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Получить сведения"));details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   await Wait(()=>details.IsEnabled);Assert.Equal(2,grid.SelectedItems.Count);Assert.Equal("2.0",rows[0].Version);
   dialog.Window.GetLogicalDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Добавить выбранные в очередь")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Assert.True(await result);Assert.Equal(2,dialog.SelectedIDs.Count);
  }
  finally{owner.Close();}
 }
}
