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
    /// <summary>Ищет по кэшируемому каталогу IPA_Downloader; при сетевой ошибке использует локальную копию.</summary>
    public sealed class AppCatalog
    {
        readonly SettingsStore store;
        readonly ToolLocator locator;
        static HttpClient Http
        {
            get
            {
                return NetworkClient.Http;
            }
        }
        string Root
        {
            get
            {
                return store.Root;
            }
        }
        bool catalogChecked;
        readonly SemaphoreSlim searchGate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, Tuple<long, long, List<AppRow>>> parsedFiles =
            new Dictionary<string, Tuple<long, long, List<AppRow>>>(StringComparer.OrdinalIgnoreCase);
        public Action<string> Log
        {
            get;
            set;
        }
        public AppCatalog(SettingsStore store, ToolLocator locator)
        {
            this.store = store;
            this.locator = locator;
            Log = delegate
            {
            };
        }
        /// <summary>Ищет по каталогам асинхронно и учитывает токен отмены.</summary>
        public Task<List<AppRow>> SearchAsync(string query, CancellationToken ct)
        {
            // Чтение и разбор файлов не выполняются в UI-потоке даже на повторном поиске без HTTP.
            return Task.Run(async delegate
            {
                await searchGate.WaitAsync(ct);
                try { return await SearchCore(query, ct); }
                finally { searchGate.Release(); }
            }, ct);
        }
        /// <summary>Переиспользует разобранный каталог до изменения размера или времени записи файла.</summary>
        List<AppRow> ReadCatalog(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) { parsedFiles.Remove(path); return new List<AppRow>(); }
            Tuple<long, long, List<AppRow>> cached;
            if (!parsedFiles.TryGetValue(path, out cached) || cached.Item1 != file.Length || cached.Item2 != file.LastWriteTimeUtc.Ticks)
            {
                cached = Tuple.Create(file.Length, file.LastWriteTimeUtc.Ticks,
                    ToolResponse.ParseCatalog(File.ReadAllText(path, Encoding.UTF8)));
                parsedFiles[path] = cached;
            }
            return cached.Item3;
        }
        async Task<List<AppRow>> SearchCore(string query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var folder = Path.Combine(Root, "Files");
            Directory.CreateDirectory(folder);
            var cache = Path.Combine(folder, "AppsList.txt");
            if (!File.Exists(cache) && !String.IsNullOrEmpty(store.Settings.Current) && Directory.Exists(locator.CurrentRoot))
            {
                var bundled = Directory.GetFiles(locator.CurrentRoot, "AppsList.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (bundled != null) File.Copy(bundled, cache);
            }
            if (!catalogChecked)
            {
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        using (var response = await Http.GetAsync("https://raw.githubusercontent.com/kda2495/IPA_Downloader/refs/heads/main/Files/AppsList.txt", timeout.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            var content = await response.Content.ReadAsStringAsync();
                            if (content.Length > 2000000 || ToolResponse.ParseCatalog(content).Count == 0) throw new IOException("Неожиданный формат каталога.");
                            AtomicFile.WriteUtf8(cache, content);
                            Log("Каталог приложений обновлён из исходного репозитория.");
                        }
                    }
                } catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    Log("Каталог: таймаут обновления, используется сохранённая копия.");
                } catch (Exception e)
                {
                    Log("Каталог: используется сохранённая копия. " + e.Message);
                }
                catalogChecked = true;
            }
            var customPath = Path.Combine(folder, "AppsListCustom.txt");
            var custom = ReadCatalog(customPath);
            var catalog = ReadCatalog(cache);
            ct.ThrowIfCancellationRequested();
            // В таблицу уходят копии только совпавших строк: UI не может испортить общий кэш.
            return ToolResponse.MatchCatalog(query, custom, catalog)
                .Select(x => new AppRow { ID = x.ID, Name = x.Name }).ToList();
        }
    }
}
