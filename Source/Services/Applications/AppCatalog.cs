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

namespace IpaDesktop
{
    /// <summary>Ищет по кэшируемому каталогу IPA_Downloader; при сетевой ошибке использует локальную копию.</summary>
    public sealed class AppCatalog : IAppCatalog
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
        DateTime nextCatalogAttemptUtc;
        readonly HttpClient http;
        readonly Func<DateTime> utcNow;
        readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
        readonly SemaphoreSlim searchGate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, Tuple<long, long, List<AppRow>>> parsedFiles =
            new Dictionary<string, Tuple<long, long, List<AppRow>>>(StringComparer.OrdinalIgnoreCase);
        public Action<string> Log
        {
            get;
            set;
        }
        public AppCatalog(SettingsStore store, ToolLocator locator) : this(store, locator, Http, delegate { return DateTime.UtcNow; })
        {
        }
        public AppCatalog(SettingsStore store, ToolLocator locator, HttpClient http, Func<DateTime> utcNow)
        {
            this.store = store;
            this.locator = locator;
            this.http = http;
            this.utcNow = utcNow;
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
                var length = file.Length;
                var modified = file.LastWriteTimeUtc.Ticks;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                cached = Tuple.Create(length, modified, ToolResponse.ParseCatalog(reader.ReadToEnd()));
                parsedFiles[path] = cached;
            }
            return cached.Item3;
        }
        string CatalogFolder => Path.Combine(Root, "Files");
        string CatalogPath => Path.Combine(CatalogFolder, "AppsList.txt");

        public Task RefreshAsync(CancellationToken ct) => Task.Run(() => RefreshCore(ct, true), ct);

        async Task RefreshCore(CancellationToken ct, bool wait)
        {
            if (wait) await refreshGate.WaitAsync(ct);
            else if (!await refreshGate.WaitAsync(0, ct)) return;
            try
            {
                Directory.CreateDirectory(CatalogFolder);
                var cache = CatalogPath;
                if (!File.Exists(cache) && !String.IsNullOrEmpty(store.Settings.Current) && Directory.Exists(locator.CurrentRoot))
                {
                    var bundled = Directory.GetFiles(locator.CurrentRoot, "AppsList.txt", SearchOption.AllDirectories).FirstOrDefault();
                    if (bundled != null) await AtomicFile.CopyAsync(bundled, cache, ct);
                }
                if (!catalogChecked && utcNow() >= nextCatalogAttemptUtc)
                {
                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(5));
                            using (var response = await http.GetAsync("https://raw.githubusercontent.com/kda2495/IPA_Downloader/refs/heads/main/Files/AppsList.txt", timeout.Token))
                            {
                                response.EnsureSuccessStatusCode();
                                var content = await response.Content.ReadAsStringAsync(timeout.Token);
                                if (content.Length > 2000000 || ToolResponse.ParseCatalog(content).Count == 0) throw new IOException("Неожиданный формат каталога.");
                                ct.ThrowIfCancellationRequested();
                                AtomicFile.WriteUtf8(cache, content);
                                catalogChecked = true;
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
                    // Ошибка не отключает обновления до перезапуска; короткая пауза не задерживает каждый поиск.
                    if (!catalogChecked) nextCatalogAttemptUtc = utcNow().AddSeconds(30);
                }
            }
            finally { refreshGate.Release(); }
        }

        async Task<List<AppRow>> SearchCore(string query, CancellationToken ct)
        {
            // If startup is already fetching a fresh copy, searches can use the saved one.
            var cache = CatalogPath;
            await RefreshCore(ct, !File.Exists(cache));
            var customPath = Path.Combine(CatalogFolder, "AppsListCustom.txt");
            var custom = ReadCatalog(customPath);
            var catalog = ReadCatalog(cache);
            ct.ThrowIfCancellationRequested();
            // В таблицу уходят копии только совпавших строк: UI не может испортить общий кэш.
            return ToolResponse.MatchCatalog(query, custom, catalog)
                .Select(x => new AppRow { ID = x.ID, Name = x.Name }).ToList();
        }
    }
}
