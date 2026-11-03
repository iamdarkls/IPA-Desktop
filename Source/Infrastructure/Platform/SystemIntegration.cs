using System;
using System.IO;
using System.Diagnostics;
namespace IpaDesktop;
/// <summary>Desktop integration shared by controllers; never permanently deletes a failed trash operation.</summary>
public static class SystemIntegration
{
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
    public static void Trash(string path)
    {
        if(RuntimePlatform.Windows){Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);return;}
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
