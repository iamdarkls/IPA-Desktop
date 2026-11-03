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
 static void Click(Window window,string name)
 {
  var button=Find<Button>(window,name);
  if(button.Command != null) button.Command.Execute(button.CommandParameter);
  else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 }
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
   foreach(var name in new[]{"Versions","MoreSearch","AddChosen","Download"})
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
   foreach(var name in new[]{"AddInstall","InstallFiles","DeleteFiles","DeviceApps","CheckConnection"})
   {
    var button=Find<Button>(window,name);var origin=button.TranslatePoint(default,window);
    if(name is "AddInstall" or "InstallFiles" or "DeleteFiles")
    {
     var install=Find<Button>(window,"InstallFiles");
     Assert.Equal(install.TranslatePoint(default,window).Value.Y,button.TranslatePoint(default,window).Value.Y);
     Assert.Equal(install.Bounds.Height,button.Bounds.Height);
    }
    Assert.True(button.IsEffectivelyVisible,"Frequent action hidden: "+name);
    Assert.True(origin.Value.X>=0 && origin.Value.X+button.Bounds.Width<=window.ClientSize.Width,"Action clipped: "+name);
   }
   desktop.Model.Queue.Start("Скачивание",Enumerable.Range(1,1234).Select(i=>new QueueSeed(i+"@latest","App "+i,"Последняя")),null,"Fixture",_=>{});
   await Task.Delay(50);
   var count=window.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="QueuePendingCount");
   Assert.Equal("1234",count.Text);
   var queueTab=Find<TabItem>(window,"QueueTab");var countOrigin=count.TranslatePoint(default,queueTab).Value;
   Assert.True(countOrigin.X>=0&&countOrigin.X+count.Bounds.Width<=queueTab.Bounds.Width,"Queue count clipped");
   Assert.True(count.Bounds.Width>=count.DesiredSize.Width,"Queue count width truncated");
   foreach(var tab in tabs.Items.Cast<TabItem>())
   {
    var origin=tab.TranslatePoint(default,window).Value;
    Assert.True(origin.X+tab.Bounds.Width<=window.ClientSize.Width,"Tab clipped after count update");
   }
   desktop.Model.Queue.Begin(1);desktop.Model.Queue.Result(null);await Task.Delay(30);Assert.Equal("1233",count.Text);
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task JournalReopensWithHistoryAndMoreMenuKeepsActionsAvailable()
 {
  using var folder=new TempFolder();var desktop=new Desktop(Backend(folder.Path),false);var window=desktop.Window;window.Show();
  try
  {
   Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TabItem>(),x=>Equals(x.Header,"Журнал"));
   desktop.Model.AppendJournal("before-open\n");Click(window,"ShowJournal");await Task.Delay(50);
   var journal=Assert.IsType<JournalWindow>(window.OwnedWindows.Single());
   Assert.Contains("before-open",journal.FindControl<TextBox>("JournalText").Text);
   desktop.Model.AppendJournal("while-open\n");await Task.Delay(30);
   Assert.Contains("while-open",journal.FindControl<TextBox>("JournalText").Text);journal.Close();
   desktop.Model.AppendJournal("while-closed\n");Click(window,"ShowJournal");await Task.Delay(30);
   journal=Assert.IsType<JournalWindow>(window.OwnedWindows.Single());Assert.Contains("while-closed",journal.FindControl<TextBox>("JournalText").Text);journal.Close();
   var more=Find<Button>(window,"MoreAccount");more.Flyout.ShowAt(more);await Task.Delay(30);
   Assert.True(Find<Button>(window,"AccountInfo").IsEffectivelyVisible);Click(window,"AccountInfo");await Wait(()=>!Busy(desktop));
   Assert.Contains("test@example.com",Find<TextBlock>(window,"AccountLabel").Text);
   Click(window,"CheckAppleSupport");await Wait(()=>!Busy(desktop));
   Assert.Equal("Служба Apple запущена",desktop.Model.Device.SupportMessage);
   Find<TabItem>(window,"FilesTab").IsSelected=true;var deviceMore=Find<Button>(window,"MoreDevice");deviceMore.Flyout.ShowAt(deviceMore);await Task.Delay(30);
   Assert.Equal(desktop.Model.Device.SupportMessage,Find<TextBlock>(window,"AppleSupportStatus").Text);deviceMore.Flyout.Hide();
  }
  finally{window.Close();}
 }
 [AvaloniaFact] public async Task RestartedDownloadQueueWaitsForOriginalAccountAndRestoresItsResumeAction()
 {
  using var folder=new TempFolder();var runner=new RecordingProcessRunner();var engine=Backend(folder.Path,runner);
  var first=new Desktop(engine,false);first.Window.Show();Click(first.Window,"AccountInfo");await Wait(()=>!Busy(first));
  first.Model.Queue.Start("Скачивание",new[]{new QueueSeed("123@latest","Restored app","Последняя")},null,"Account",_=>throw new Exception("Old callback must not survive"),accountKey:first.Model.AccountKey);
  first.Model.Queue.Begin(1);first.Window.Close();
  var second=new Desktop(engine,false);var window=second.Window;window.Show();
  try
  {
   Assert.Equal(QueueItemState.Interrupted,Assert.Single(second.Model.Queue.Items).State);Assert.False(second.Model.Queue.ResumeCommand.CanExecute(null));
   runner.SessionEmail="other@example.com";Click(window,"AccountInfo");await Wait(()=>!Busy(second));Assert.False(second.Model.Queue.ResumeCommand.CanExecute(null));
   runner.SessionEmail="test@example.com";Click(window,"AccountInfo");await Wait(()=>!Busy(second));Assert.True(second.Model.Queue.ResumeCommand.CanExecute(null));
   second.Model.Queue.ResumeCommand.Execute(null);await Wait(()=>!Busy(second));Assert.Equal(QueueItemState.Succeeded,second.Model.Queue.Items.Single().State);
   Assert.Contains(runner.Calls,x=>x.Length>0&&x[0]=="download");
  }
  finally{window.Close();}
 }
 static void AddAccountMetadata(LocalRow row)
 {
  using var zip=System.IO.Compression.ZipFile.Open(row.Path,System.IO.Compression.ZipArchiveMode.Update);
  using var writer=new StreamWriter(zip.CreateEntry("iTunesMetadata.plist").Open());
  writer.Write("<plist><dict><key>itemId</key><integer>123</integer><key>appleId</key><string>fixture@example.com</string><key>softwareVersionBundleId</key><string>com.fixture</string><key>bundleShortVersionString</key><string>1.0</string></dict></plist>");
 }
 [AvaloniaFact] public async Task DeviceLibraryAndResultVerificationRequireExplicitConfirmation()
 {
  using var fixture=new PairingFixture();var row=fixture.Ipa("Library");AddAccountMetadata(row);
  fixture.Runner.InventoryXml="<plist><array><dict><key>CFBundleIdentifier</key><string>com.fixture</string><key>CFBundleDisplayName</key><string>Library fixture</string><key>CFBundleShortVersionString</key><string>1.0</string><key>iTunesMetadata</key><dict><key>itemId</key><integer>123</integer></dict></dict></array></plist>";
  var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  try
  {
   var phones=Find<ComboBox>(window,"DeviceList");phones.ItemsSource=new[]{fixture.Device};phones.SelectedIndex=0;
   Click(window,"CheckConnection");await Wait(()=>!Busy(desktop));
   Click(window,"DeviceApps");await Wait(()=>!Busy(desktop));
   var browser=Assert.IsType<AppBrowserWindow>(window.OwnedWindows.Single());Assert.Single(browser.Apps.ItemsSource.Cast<LibraryApp>());browser.Close();
   var file=new FileInfo(row.Path);desktop.Model.Queue.Start("Установка",new[]{new QueueSeed(row.Path,"Library","1.0"){FileSize=file.Length,ModifiedTicks=file.LastWriteTimeUtc.Ticks}},fixture.Device.ID+"|False","Phone",_=>{});
   desktop.Model.Queue.Begin(1);desktop.Model.Queue.Stop("Обрыв",false);Assert.True(desktop.Model.Queue.VerifyCommand.CanExecute(null));desktop.Model.Queue.VerifyCommand.Execute(null);
   await Wait(()=>window.OwnedWindows.Any(x=>x.Title=="Версия найдена на iPhone"));Assert.Equal(QueueItemState.Uncertain,desktop.Model.Queue.Items.Single().State);
   var confirm=window.OwnedWindows.Single(x=>x.Title=="Версия найдена на iPhone");confirm.GetLogicalDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Продолжить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   await Wait(()=>!Busy(desktop));Assert.Equal(QueueItemState.Succeeded,desktop.Model.Queue.Items.Single().State);
  }
  finally{window.Close();}
 }
 [AvaloniaFact(SkipUnless=nameof(IsWindows),Skip="Windows set installation integration test")] public async Task SetWizardReusesOnlyMatchingLocalIpaAndConfirmsBeforeInstalling()
 {
  using var fixture=new PairingFixture();var row=fixture.Ipa("Set");AddAccountMetadata(row);
  var binaries=Path.Combine(fixture.Engine.Root,"Versions/fixture/windows_amd64");File.WriteAllText(Path.Combine(binaries,"ipatool.exe"),"fixture");
  fixture.Engine.AppSets.AddApps("Local set",new[]{new AppRow{ID="123",Name="Set"}});
  var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  async Task Confirm(string title)
  {
   await Wait(()=>window.OwnedWindows.Any(x=>x.Title==title));var dialog=window.OwnedWindows.Single(x=>x.Title==title);
   dialog.GetLogicalDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Продолжить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  }
  try
  {
   var phones=Find<ComboBox>(window,"DeviceList");phones.ItemsSource=new[]{fixture.Device};phones.SelectedIndex=0;
   Click(window,"InstallSet");await Confirm("Подготовка установки набора");await Wait(()=>window.OwnedWindows.Any(x=>x.Title=="Установка"));Assert.Equal(0,fixture.Runner.Installs);
   await Confirm("Установка");await Wait(()=>!Busy(desktop)&&fixture.Runner.Installs==1);Assert.All(desktop.Model.Queue.Items,x=>Assert.Equal(QueueItemState.Succeeded,x.State));
  }
  finally{window.Close();}
 }
 public static bool IsWindows => OperatingSystem.IsWindows();
 [AvaloniaFact(SkipUnless=nameof(IsWindows), Skip="Windows installer integration test")] public async Task InstallationQueueRecoversSamePhoneAndSkipsCompletedIpa()
 {
  using var fixture=new PairingFixture();var rows=new[]{fixture.Ipa("One"),fixture.Ipa("Two"),fixture.Ipa("Three")};
  fixture.Runner.DisconnectAt=2;var desktop=new Desktop(fixture.Engine,false);var window=desktop.Window;window.Show();
  async Task ConfirmInstall(int expectedInstalls)
  {
   await Wait(()=>window.OwnedWindows.Any(x=>x.Title=="Установка"));
   var dialog=window.OwnedWindows.Single(x=>x.Title=="Установка");
   bool started=false,finished=false;
   void Changed(object sender,System.ComponentModel.PropertyChangedEventArgs args)
   {
    if(args.PropertyName!=nameof(DesktopViewModel.IsBusy))return;
    if(desktop.Model.IsBusy)started=true;else if(started)finished=true;
   }
   desktop.Model.PropertyChanged+=Changed;
   try
   {
    dialog.GetLogicalDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Продолжить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Wait(()=>finished);
   }
   finally{desktop.Model.PropertyChanged-=Changed;}
   var diagnostic="installs="+fixture.Runner.Installs+" rows="+desktop.Model.Queue.Items.Count+" status="+desktop.Model.Status+"\n"+
    String.Join("\n",desktop.Model.Queue.Items.Select(x=>x.Name+": "+x.Status+" "+x.Detail))+"\n"+ToolTip.GetTip(Find<TextBlock>(window,"NoticeText"));
   Assert.True(fixture.Runner.Installs==expectedInstalls,diagnostic);
   Assert.True(desktop.Model.Queue.Items.Count==3,diagnostic);
  }
  try
  {
   Find<TabItem>(window,"FilesTab").IsSelected=true;
   var files=Find<DataGrid>(window,"Files");await Wait(()=>files.ItemsSource?.Cast<LocalRow>().Count()==3);
   var devices=Find<ComboBox>(window,"DeviceList");devices.ItemsSource=new[]{fixture.Device};devices.SelectedIndex=0;
   foreach(var row in files.ItemsSource.Cast<LocalRow>().ToArray())files.SelectedItems.Add(row);Click(window,"AddInstall");
   Assert.Equal(3,Find<ItemsControl>(window,"ChosenFiles").ItemCount);
   Click(window,"InstallFiles");await ConfirmInstall(2);Assert.Equal(2,fixture.Runner.Installs);
   Assert.Equal(new[]{QueueItemState.Succeeded,QueueItemState.Uncertain,QueueItemState.Paused},desktop.Model.Queue.Items.Select(x=>x.State));
   Assert.Equal(PhoneStatus.Lost,desktop.Model.Device.Status);Assert.False(desktop.Model.Queue.ResumeCommand.CanExecute(null));
   fixture.Runner.DisconnectAt=0;await DesktopContract.ScanDevices(desktop);
   Assert.Equal(PhoneStatus.Ready,desktop.Model.Device.Status);Assert.True(desktop.Model.Queue.ResumeCommand.CanExecute(null));
   var original=(DeviceRow)devices.SelectedItem;
   devices.ItemsSource=new[]{new DeviceRow{ID="different",Name="Другой iPhone"}};devices.SelectedIndex=0;
   Assert.False(desktop.Model.Queue.ResumeCommand.CanExecute(null));
   devices.ItemsSource=new[]{original};devices.SelectedIndex=0;Assert.True(desktop.Model.Queue.ResumeCommand.CanExecute(null));
   Click(window,"ResumeQueue");await ConfirmInstall(4);Assert.Equal(4,fixture.Runner.Installs);
   Assert.All(desktop.Model.Queue.Items,x=>Assert.Equal(QueueItemState.Succeeded,x.State));
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
   Assert.False(Find<Button>(window,"InstallFiles").IsEnabled);Assert.True(Find<Button>(window,"CheckConnection").IsEnabled);Assert.Equal(0,fixture.Runner.Installs);Assert.Equal(PhoneStatus.Lost,desktop.Model.Device.Status);
   Assert.True(Find<Border>(window,"ConnectionNotice").IsVisible);Assert.Contains("Соединение",Find<TextBlock>(window,"ConnectionTitle").Text);
   Assert.Contains("Что сделать",Find<TextBlock>(window,"NoticeRemedy").Text);
   await DesktopContract.ScanDevices(desktop);
   Assert.True(Find<Button>(window,"InstallFiles").IsEnabled);
   Assert.Contains("восстановлена",Find<TextBlock>(window,"ConnectionTitle").Text);Assert.Equal(PhoneStatus.Ready,desktop.Model.Device.Status);
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
   Assert.True(Busy(desktop));Assert.Equal(PhoneStatus.WaitingForTrust,desktop.Model.Device.Status);Assert.True(Find<Button>(window,"Cancel").IsEnabled);Assert.Contains("Доверять",Find<TextBlock>(window,"ConnectionMessage").Text);
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
