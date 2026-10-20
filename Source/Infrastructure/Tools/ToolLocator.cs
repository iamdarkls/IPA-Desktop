using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop
{
    /// <summary>Находит исполняемые файлы в выбранной версии движка или отдельном комплекте устройства.</summary>
    public sealed class ToolLocator
    {
        readonly SettingsStore store;
        readonly object gate = new object();
        readonly Dictionary<string, string> enginePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> devicePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string engineRoot, deviceRoot;
        string Root
        {
            get
            {
                return store.Root;
            }
        }
        State Settings
        {
            get
            {
                return store.Settings;
            }
        }
        public ToolLocator(SettingsStore store)
        {
            this.store = store;
        }
        /// <summary>Возвращает путь выбранной версии инструментов с проверкой допустимости имени.</summary>
        public string VersionRoot(string version)
        {
            if (String.IsNullOrEmpty(version)) throw new Exception("Сначала установите инструменты: кнопка «Проверить обновления».");
            var p = Path.GetFullPath(Path.Combine(Root, "Versions", version));
            var prefix = Path.GetFullPath(Path.Combine(Root, "Versions")) + Path.DirectorySeparatorChar;
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Некорректный путь инструментов.");
            return p;
        }
        public string CurrentRoot { get { return VersionRoot(Settings.Current); } }
        public string DeviceRoot { get { return VersionRoot(Settings.DeviceCurrent ?? Settings.Current); } }
        /// <summary>Находит нужный бинарник в активном комплекте и сообщает об отсутствии.</summary>
        public string Find(string pattern)
        {
            bool engine = pattern.StartsWith("ipatool", StringComparison.OrdinalIgnoreCase);
            var root = engine ? CurrentRoot : DeviceRoot;
            lock (gate)
            {
                var paths = engine ? enginePaths : devicePaths;
                var previousRoot = engine ? engineRoot : deviceRoot;
                // Every update is installed in a new GUID directory. Keep only the
                // active package cache; switching or rolling back invalidates it.
                if (!String.Equals(root, previousRoot, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Clear();
                    if (engine) engineRoot = root; else deviceRoot = root;
                }
                string cached;
                if (paths.TryGetValue(pattern, out cached) && File.Exists(cached)) return cached;
                paths.Remove(pattern);
                // Two matches already prove ambiguity. Do not allocate all matches
                // or traverse the remaining license/source directories needlessly.
                var files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).Take(2).ToArray();
                if (files.Length != 1) throw new Exception("Не найдена однозначная Windows-утилита: " + pattern);
                paths[pattern] = files[0];
                return files[0];
            }
        }
    }
}
