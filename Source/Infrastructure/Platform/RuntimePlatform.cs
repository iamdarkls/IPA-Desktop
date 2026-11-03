using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
namespace IpaDesktop;

public static class RuntimePlatform
{
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    public static bool Windows => OperatingSystem.IsWindows();
    public static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
    public static string Rid => (Windows?"win":OperatingSystem.IsMacOS()?"osx":"linux")+"-"+Architecture;
    public static string ToolFolder => Windows?"windows_amd64":Rid;
    public static string AssetSuffix => Windows?"Windows":(OperatingSystem.IsMacOS()?"macOS":"Linux")+"-"+Architecture;
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IPA-Desktop");
    public static string ToolName(string name) => Windows?name:name.Replace("*.exe", "").Replace(".exe", "");
    public static void Executable(string file)
    {
        if(!Windows)File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);
    }
    public static void Bootstrap(SettingsStore store)
    {
        if(Windows||store.Root!=DataRoot)return;
        var source=Path.Combine(AppContext.BaseDirectory,"native");
        if(!File.Exists(Path.Combine(source,"ipatool")))throw new IOException("В сборке нет нативных инструментов. Распакуйте полный архив для "+Rid+".");
        var version="bundled-"+AppInfo.Version;
        var target=Path.Combine(store.Root,"Versions",version,ToolFolder);
        if(!Directory.Exists(target))
        {
            var stage=target+"-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
            try { foreach(var path in Directory.GetFiles(source)){var file=Path.Combine(stage,Path.GetFileName(path));File.Copy(path,file);Executable(file);} Directory.Move(stage,target); }
            finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
        store.Settings.Current=store.Settings.DeviceCurrent=version;
        store.Settings.Version="ipatool-guard 1.2.1.1";store.Settings.DeviceVersion="device-tools 1.0.5 · "+Rid;
        store.Settings.AutoUpdate=store.Settings.AutoUpdateDeviceTools=false;
        store.Save();
    }
    public static void Trash(string path)
    {
        if(Windows){Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);return;}
        // gio/Finder use the OS trash API, including cross-volume handling. Never fall back to permanent deletion.
        var info=new ProcessStartInfo(OperatingSystem.IsMacOS()?"/usr/bin/osascript":"gio"){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
        if(OperatingSystem.IsMacOS())
        {
            info.ArgumentList.Add("-e");info.ArgumentList.Add("on run argv\ntell application \"Finder\" to delete (POSIX file (item 1 of argv))\nend run");info.ArgumentList.Add("--");info.ArgumentList.Add(path);
        }
        else {info.ArgumentList.Add("trash");info.ArgumentList.Add("--");info.ArgumentList.Add(path);}
        using var process=Process.Start(info);var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
        if(!process.WaitForExit(15000)){process.Kill(true);throw new IOException("Корзина не ответила. Файл не удалён программой.");}
        if(process.ExitCode!=0)throw new IOException("Не удалось переместить IPA в системную корзину. Проверьте доступ к корзине и наличие gio (Linux).");
    }
}
