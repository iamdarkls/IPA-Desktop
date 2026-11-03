using System;
using System.IO;
namespace IpaDesktop;
/// <summary>Installs the versioned native bundle; user data and account restrictions remain separate.</summary>
public static class BundledTools
{
    public static void Initialize(SettingsStore store)
    {
        if(RuntimePlatform.Windows||store.Root!=RuntimePlatform.DataRoot)return;
        var source=Path.Combine(AppContext.BaseDirectory,"native");
        if(!File.Exists(Path.Combine(source,"ipatool")))throw new IOException("В сборке нет нативных инструментов. Распакуйте полный архив для "+RuntimePlatform.Rid+".");
        var version="bundled-"+AppInfo.Version;
        var target=Path.Combine(store.Root,"Versions",version,RuntimePlatform.ToolFolder);
        if(!Directory.Exists(target))
        {
            var stage=target+"-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
            try { foreach(var path in Directory.GetFiles(source)){var file=Path.Combine(stage,Path.GetFileName(path));File.Copy(path,file);RuntimePlatform.Executable(file);} Directory.Move(stage,target); }
            finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
        if(store.Settings.Current==version&&store.Settings.DeviceCurrent==version&&!store.Settings.AutoUpdate&&!store.Settings.AutoUpdateDeviceTools)return;
        store.Settings.Current=store.Settings.DeviceCurrent=version;
        store.Settings.Version="ipatool-guard 1.2.1.1";store.Settings.DeviceVersion="device-tools 1.0.5 · "+RuntimePlatform.Rid;
        store.Settings.AutoUpdate=store.Settings.AutoUpdateDeviceTools=false;
        store.Save();
    }
}
