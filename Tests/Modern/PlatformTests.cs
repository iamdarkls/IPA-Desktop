using System.Diagnostics;
using System.IO.Compression;
using Xunit;
namespace IpaDesktop.Tests;
public sealed class PlatformTests
{
 public static bool Unix=>!OperatingSystem.IsWindows();
 [Fact(SkipUnless=nameof(Unix),Skip="Unix platform integration")] public void UnixToolLocatorDoesNotSelectWindowsOrForeignArchitecture()
 {
  using var folder=new TempFolder();var store=new SettingsStore(folder.Path);store.Settings.Current="test";
  var native=Path.Combine(folder.Path,"Versions/test",RuntimePlatform.ToolFolder);var other=Path.Combine(folder.Path,"Versions/test/windows_amd64");Directory.CreateDirectory(native);Directory.CreateDirectory(other);
  File.WriteAllText(Path.Combine(native,"ipatool"),"fixture");File.WriteAllText(Path.Combine(other,"ipatool.exe"),"foreign");
  Assert.Equal(Path.Combine(native,"ipatool"),new ToolLocator(store).Find("ipatool*.exe"));
 }
 [Fact(SkipUnless=nameof(Unix),Skip="Unix platform integration")] public void UnixUpdateRejectsIncompletePackageAndRestoresExecutableModes()
 {
  using var folder=new TempFolder();var zip=Path.Combine(folder.Path,"package.zip");var package=OperatingSystem.IsMacOS()?"IPA-Desktop.app/Contents/MacOS/":"IPA-Desktop/";
  using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))foreach(var name in new[]{"IPA-Desktop","native/ipatool","native/ipa-pty","native/idevice_id","native/ideviceinfo","native/ideviceinstaller","native/idevicepair","Scripts/Update-App.sh"})
   {using var writer=new StreamWriter(archive.CreateEntry(package+name).Open());writer.Write("fixture");}
  var stage=Path.Combine(folder.Path,"stage");UnixGuiUpdate.Extract(zip,stage);
  Assert.True((File.GetUnixFileMode(Path.Combine(stage,package,"IPA-Desktop"))&UnixFileMode.UserExecute)!=0);
  var bad=Path.Combine(folder.Path,"bad.zip");using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry(package+"IPA-Desktop").Open());writer.Write("fixture");}
  Assert.Throws<InvalidDataException>(()=>UnixGuiUpdate.Extract(bad,Path.Combine(folder.Path,"bad")));
 }
 [Fact(SkipUnless=nameof(IsLinux),Skip="Linux updater integration")] public async Task UnixUpdaterWaitsAndSwapsOnlyTheInstallation()
 {
  using var folder=new TempFolder();var stage=Path.Combine(folder.Path,"new app");var target=Path.Combine(folder.Path,"old app");Directory.CreateDirectory(stage);Directory.CreateDirectory(target);
  File.WriteAllText(Path.Combine(target,"version"),"old");File.WriteAllText(Path.Combine(stage,"version"),"new");
  var marker=Path.Combine(folder.Path,"started");File.WriteAllText(Path.Combine(stage,"IPA-Desktop"),"#!/bin/sh\ntouch '"+marker.Replace("'","'\\''")+"'\n");RuntimePlatform.Executable(Path.Combine(stage,"IPA-Desktop"));
  var data=Path.Combine(folder.Path,"data.ipa");File.WriteAllText(data,"preserved");
  var script=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Scripts/Update-App.sh"));
  var info=new ProcessStartInfo("/bin/sh"){UseShellExecute=false};foreach(var arg in new[]{script,stage,target,"99999999"})info.ArgumentList.Add(arg);
  using var process=Process.Start(info);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await process.WaitForExitAsync(timeout.Token);Assert.Equal(0,process.ExitCode);
  Assert.Equal("new",File.ReadAllText(Path.Combine(target,"version")));Assert.Equal("preserved",File.ReadAllText(data));
  Assert.Equal("old",File.ReadAllText(Path.Combine(Directory.GetDirectories(folder.Path,"old app.backup-*").Single(),"version")));
  for(int i=0;i<50&&!File.Exists(marker);i++)await Task.Delay(20);Assert.True(File.Exists(marker));
 }
 public static bool IsLinux=>OperatingSystem.IsLinux();
}
