using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
namespace IpaDesktop;
public static class UnixGuiUpdate
{
    public static string Extract(string archive,string stage)
    {
        EngineUpdater.Extract(archive,stage);
        var package=Path.Combine(stage,OperatingSystem.IsMacOS()?"IPA-Desktop.app":"IPA-Desktop");
        var binaries=OperatingSystem.IsMacOS()?Path.Combine(package,"Contents","MacOS"):package;
        var required=new[]{"IPA-Desktop","ipatool","ipa-pty","idevice_id","ideviceinfo","ideviceinstaller","idevicepair"};
        foreach(var name in required)
        {
            var file=name=="IPA-Desktop"?Path.Combine(binaries,name):Path.Combine(binaries,"native",name);
            if(!File.Exists(file)||new FileInfo(file).Length==0)throw new InvalidDataException("Неполный пакет для "+RuntimePlatform.Rid+": "+name);
            RuntimePlatform.Executable(file);
        }
        if(!File.Exists(Path.Combine(binaries,"Scripts","Update-App.sh")))throw new InvalidDataException("В пакете нет помощника обновления.");
        return package;
    }
    public static void Launch(string stage)
    {
        var basePath=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var target=OperatingSystem.IsMacOS()?Directory.GetParent(basePath).Parent.FullName:basePath;
        var parent=Directory.GetParent(target)?.FullName??throw new IOException("Не найдена папка установки.");
        var probe=Path.Combine(parent,".ipa-update-"+Guid.NewGuid().ToString("N"));
        try{File.WriteAllText(probe,"");File.Delete(probe);}catch(UnauthorizedAccessException){throw new IOException("Нет прав на обновление этой папки. Установите приложение в папку пользователя или замените его вручную; данные сохранятся.");}
        var source=Path.Combine(AppContext.BaseDirectory,"Scripts","Update-App.sh");var helper=Path.Combine(Path.GetTempPath(),"ipa-update-"+Guid.NewGuid().ToString("N")+".sh");File.Copy(source,helper);
        var info=new ProcessStartInfo("/bin/sh"){UseShellExecute=false};foreach(var arg in new[]{helper,stage,target,Environment.ProcessId.ToString()})info.ArgumentList.Add(arg);
        _=Process.Start(info)??throw new IOException("Не удалось запустить обновление.");
    }
}
