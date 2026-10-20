using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IpaDesktop
{
    /// <summary>Восстанавливает вложенные файлы из ресурсов и переносит старую структуру portable-сборки.</summary>
    public static class InstallationLayout
    {
        static readonly Dictionary<string, string> LegacyFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Assets/App.ico", "App.ico" },
            { "Assets/App.svg", "App.svg" },
            { "Scripts/Update-App.ps1", "Update-App.ps1" },
            { "Scripts/Sync-Sources.ps1", "Sync-Sources.ps1" },
            { "Docs/ARCHITECTURE.md", "ARCHITECTURE.md" },
            { "Generated/Core.cs", "Core.cs" },
            { "Generated/App.cs", "App.cs" },
            { "Generated/IpaMetadata.cs", "IpaMetadata.cs" },
            { "Generated/WindowTheme.cs", "WindowTheme.cs" },
            { "Generated/GuiUpdater.cs", "GuiUpdater.cs" },
            { "Tests/Verify.ps1", "Verify.ps1" },
            { "Tests/Verify-IpaMetadata.ps1", "Verify-IpaMetadata.ps1" },
            { "Tests/Verify-Updater.ps1", "Verify-Updater.ps1" },
            { "Tests/Verify-Design.ps1", "Verify-Design.ps1" },
            { "Tests/Verify-Services.ps1", "Verify-Services.ps1" }
        };

        /// <summary>Восстанавливает отсутствующие файлы и удаляет только неизменённые устаревшие копии.</summary>
        public static void Ensure(string installationDirectory)
        {
            string root = Path.GetFullPath(installationDirectory);
            var assembly = typeof(InstallationLayout).Assembly;
            foreach (var relative in GuiUpdater.PackageFiles.Where(path => path.Contains("/")))
            {
                using (var resource = assembly.GetManifestResourceStream("IPA.Layout." + relative.Replace('/', '.')))
                {
                    if (resource == null) throw new InvalidDataException("Отсутствует ресурс структуры программы: " + relative);
                    byte[] data;
                    using (var buffer = new MemoryStream())
                    {
                        resource.CopyTo(buffer);
                        data = buffer.ToArray();
                    }

                    string destination = Path.Combine(root, relative);
                    string legacyName;
                    string legacy = LegacyFiles.TryGetValue(relative, out legacyName) ? Path.Combine(root, legacyName) : null;
                    bool migrate = legacy != null && File.Exists(legacy) && new FileInfo(legacy).Length == data.Length && File.ReadAllBytes(legacy).SequenceEqual(data);
                    if (!File.Exists(destination) || migrate)
                    {
                        if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Файл программы является ссылкой: " + relative);
                        string folder = Path.GetDirectoryName(destination);
                        for (string parent = folder; parent != null && parent.StartsWith(root, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                        {
                            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                                throw new IOException("Папка программы содержит ссылку: " + relative);
                            if (parent.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
                        }
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(destination, data);
                    }

                    // Remove only known migration copies matching this binary's resources.
                    // User-modified files and IPA downloads are never removed here.
                    if (migrate) File.Delete(legacy);
                }
            }
        }
    }
}
