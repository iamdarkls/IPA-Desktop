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
    /// <summary>Импорт, просмотр и удаление локальных IPA с проверкой путей и кэшем метаданных.</summary>
    public sealed class LocalAppRepository
    {
        readonly string folder;
        readonly object gate = new object();
        readonly Dictionary<string, Tuple<long, long, IpaDetails>> metadataCache = new Dictionary<string, Tuple<long, long, IpaDetails>>(StringComparer.OrdinalIgnoreCase);
        public LocalAppRepository(string folder)
        {
            this.folder = Path.GetFullPath(folder);
        }
        /// <summary>Сканирует IPA вне UI-потока, обновляя кэш только для изменившихся файлов.</summary>
        public Task<List<LocalRow>> ListAsync()
        {
            return Task.Run(delegate
            {
                lock (gate)
                {
                var rows = new List<LocalRow>();
                foreach (var path in Directory.GetFiles(folder, "*.ipa"))
                {
                    try
                    {
                        var file = new FileInfo(path);
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        Tuple<long, long, IpaDetails> cached;
                        if (!metadataCache.TryGetValue(path, out cached) || cached.Item1 != file.Length || cached.Item2 != file.LastWriteTimeUtc.Ticks)
                        {
                            cached = Tuple.Create(file.Length, file.LastWriteTimeUtc.Ticks, IpaMetadata.ReadDetails(path));
                            metadataCache[path] = cached;
                        }
                        var metadata = cached.Item3;
                        rows.Add(new LocalRow { Name = file.Name, Size = StorageAnalysis.Bytes(file.Length), Bytes = file.Length, Path = file.FullName, AppleAccount = metadata.Account, AppStoreID = metadata.AppStoreID, BundleID = metadata.BundleID, AppName = metadata.AppName, Version = metadata.Version, Build = metadata.Build, MinimumOS = metadata.MinimumOS, Platforms = metadata.Platforms == null ? null : (string[])metadata.Platforms.Clone() });
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }
                StorageAnalysis.Mark(rows);
                var present = new HashSet<string>(rows.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
                foreach (var missing in metadataCache.Keys.Where(x => !present.Contains(x)).ToArray()) metadataCache.Remove(missing);
                return rows.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
            });
        }
        /// <summary>Копирует выбранные IPA в рабочую папку без перезаписи существующих файлов.</summary>
        public Task ImportAsync(IEnumerable<string> paths, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = Path.Combine(folder, Path.GetFileName(path));
                    if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(target)) throw new IOException("Файл уже существует: " + Path.GetFileName(path));
                    File.Copy(path, target);
                }
            }, ct);
        }
        /// <summary>Разрешает удаление только IPA из рабочей папки; символические ссылки не принимаются.</summary>
        public void Delete(string file)
        {
            var path = Path.GetFullPath(file);
            if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetExtension(path), ".ipa", StringComparison.OrdinalIgnoreCase)) throw new IOException("Файл не относится к папке IPA.");
            if (!File.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Удаление ссылок не поддерживается.");
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
    }
}
