using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
namespace IpaDesktop;
public static class UnixGuiUpdate
{
    public static string Extract(string archive,string stage)
    {
        if (OperatingSystem.IsMacOS()) ExtractMacBundle(archive, stage);
        else EngineUpdater.Extract(archive, stage);
        var package=Path.Combine(stage,OperatingSystem.IsMacOS()?"IPA-Desktop.app":"IPA-Desktop");
        var binaries=OperatingSystem.IsMacOS()?Path.Combine(package,"Contents","MacOS"):package;
        var required=new[]{"IPA-Desktop","ipatool","ipa-pty","idevice_id","ideviceinfo","ideviceinstaller","idevicepair"};
        foreach(var name in required)
        {
            var file=name=="IPA-Desktop"?Path.Combine(binaries,name):Path.Combine(binaries,"native",name);
            if(!File.Exists(file)||new FileInfo(file).Length==0)throw new InvalidDataException("Неполный пакет для "+RuntimePlatform.Rid+": "+name);
            if (!OperatingSystem.IsMacOS()) RuntimePlatform.Executable(file);
        }
        if(!File.Exists(Path.Combine(binaries,"Scripts","Update-App.sh")))throw new InvalidDataException("В пакете нет помощника обновления.");
        return package;
    }
    private static void ExtractMacBundle(string archive, string stage)
    {
        using (var zip = ZipFile.OpenRead(archive))
        {
            if (zip.Entries.Count > 30_000 || zip.Entries.Sum(entry => entry.Length) > 2_000_000_000L)
                throw new InvalidDataException("Архив превышает допустимый размер.");

            foreach (var entry in zip.Entries)
            {
                var parts = entry.FullName.TrimEnd('/').Split('/');
                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((parts[0] is not ("IPA-Desktop.app" or "__MACOSX")) ||
                    entry.FullName.Contains('\\') || parts.Any(part => part is "" or "." or "..") ||
                    unixType is not (0 or 0x4000 or 0x8000))
                    throw new InvalidDataException("Архив macOS содержит небезопасный путь или тип файла.");
            }
        }

        Directory.CreateDirectory(stage);
        var start = new ProcessStartInfo("/usr/bin/ditto") { UseShellExecute = false };
        foreach (var argument in new[] { "-x", "-k", archive, stage }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить распаковку IPA Desktop.");
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            process.WaitForExit();
            throw new IOException("Время распаковки IPA Desktop истекло.");
        }
        if (process.ExitCode != 0)
            throw new IOException("Не удалось распаковать приложение IPA Desktop для macOS.");
    }

    internal static string InstallationTarget(string baseDirectory, bool macOS)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (!macOS) return path;

        var binaries = new DirectoryInfo(path);
        var contents = binaries.Parent;
        var bundle = contents?.Parent;
        if (binaries.Name != "MacOS" || contents?.Name != "Contents" ||
            bundle == null || !bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Не найдена папка IPA Desktop. Запустите распакованный IPA-Desktop.app из «Программ».");

        if (path.Contains("/AppTranslocation/", StringComparison.Ordinal))
            throw new IOException("macOS запустила IPA Desktop из временной папки. Закройте приложение, перенесите IPA-Desktop.app в «Программы» через Finder и откройте его оттуда.");

        return bundle.FullName;
    }

    internal static void CheckInstallation() => WritableInstallation();

    private static string WritableInstallation()
    {
        var target = InstallationTarget(AppContext.BaseDirectory, OperatingSystem.IsMacOS());
        var parent = Directory.GetParent(target)?.FullName;
        if (parent == null) throw new IOException("Не найдена папка установки IPA Desktop.");

        try
        {
            var probe = Path.Combine(parent, ".ipa-update-" + Guid.NewGuid().ToString("N"));
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            var instruction = OperatingSystem.IsMacOS()
                ? "Перенесите IPA-Desktop.app в доступную вам папку «Программы» или замените приложение вручную."
                : "Перенесите IPA Desktop в доступную вам папку или замените приложение вручную.";
            throw new IOException("Не удалось записать файлы рядом с IPA Desktop. " + instruction, error);
        }
        return target;
    }

    public static void Launch(string stage)
    {
        var target = WritableInstallation();
        var source = Path.Combine(AppContext.BaseDirectory, "Scripts", "Update-App.sh");
        var helper = Path.Combine(Path.GetTempPath(), "ipa-update-" + Guid.NewGuid().ToString("N") + ".sh");
        File.Copy(source, helper);
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var arg in new[] { helper, stage, target, Environment.ProcessId.ToString() })
            info.ArgumentList.Add(arg);
        _ = Process.Start(info) ?? throw new IOException("Не удалось запустить обновление IPA Desktop.");
    }
}
