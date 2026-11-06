using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace IpaDesktop;

public sealed record DeviceToolManifest(string Version, string Components)
{
    public string Description => "Комплект " + Version + Environment.NewLine + Components;
    public static DeviceToolManifest Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Манифест инструментов слишком велик.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Неизвестная версия манифеста инструментов.");
        string version = root.GetProperty("version").GetString();
        if (!System.Text.RegularExpressions.Regex.IsMatch(version ?? "", @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("Некорректная версия комплекта.");
        var lines = root.GetProperty("components").EnumerateObject().Select(component => component.Name + " " +
            component.Value.GetProperty("version").GetString() + " · " + component.Value.GetProperty("revision").GetString());
        return new(version, String.Join(Environment.NewLine, lines));
    }
    public static string InstalledDescription(string workspace, State settings)
    {
        var version = settings.DeviceCurrent ?? settings.Current;
        if (String.IsNullOrEmpty(version)) return "Комплект пока не установлен.";
        try
        {
            var root = Path.GetFullPath(Path.Combine(workspace, "Versions"));
            var folder = Path.GetFullPath(Path.Combine(root, version));
            if (!folder.StartsWith(root + Path.DirectorySeparatorChar, RuntimePlatform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return "Некорректный путь комплекта.";
            var manifest = Path.Combine(folder, RuntimePlatform.Windows ? "" : RuntimePlatform.ToolFolder, "device-tools.json");
            return File.Exists(manifest) ? Read(manifest).Description : "У прежнего комплекта нет манифеста компонентов. Обновите инструменты.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException or ArgumentException)
        { return "Манифест комплекта не прочитан. Проверьте или переустановите инструменты."; }
    }
}
