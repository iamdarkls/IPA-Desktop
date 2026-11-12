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
    /// <summary>Загружает IPA под текущей сессией ipatool в уникальный файл, исключая перезапись чужой копии.</summary>
    public sealed class ApplicationDownloads : IApplicationDownloads
    {
        readonly IAppToolClient tools;
        readonly string folder;
        readonly TimeProvider clock;
        readonly string rawCache;
        public Action<string> Transfer { get; set; }
        public ApplicationDownloads(IAppToolClient tools, string folder, TimeProvider clock = null, string rawCache = null)
        {
            this.tools = tools;
            this.folder = folder;
            this.clock=clock??TimeProvider.System;
            this.rawCache = rawCache;
            Transfer = delegate { };
        }
        /// <summary>Запускает скачивание с получением лицензии и отдельным назначением для каждого запроса.</summary>
        public Task DownloadAsync(DownloadItem item, CancellationToken ct) => DownloadFileAsync(item,ct);
        public async Task<string> DownloadFileAsync(DownloadItem item, CancellationToken ct)
        {
            if (!AppSetStore.ValidId(item.ID)) throw new IOException("Некорректный App Store ID.");
            Directory.CreateDirectory(folder);
            // A new destination for every request: never reuse another account's IPA.
            string title = new string((item.Name ?? "App").Where(c => !Char.IsControl(c) && Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).Take(48).ToArray()).Trim().TrimEnd('.');
            if (title.Length == 0) title = "App";
            string destination = Path.Combine(folder, title + "-" + item.ID + "-" + Guid.NewGuid().ToString("N") + ".ipa");
            var arguments = new List<string>
            {
                "download", "-i", item.ID, "--purchase", "-o", destination
            };
            if (!String.IsNullOrEmpty(rawCache)) arguments.AddRange(new[] { "--ipa-desktop-cache", rawCache });
            if (item.VersionID != null) arguments.AddRange(new[]
            {
                "--external-version-id", item.VersionID
            });
            using (var monitor = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var observing = ObserveAsync(destination + ".tmp", monitor.Token);
                try { ToolResponse.Ensure(await tools.RunAsync(arguments.ToArray(), ct, true)); }
                finally
                {
                    monitor.Cancel();
                    try { await observing.ConfigureAwait(false); } catch (OperationCanceledException) when(monitor.IsCancellationRequested) { }
                }
            }
            return destination;
        }
        /// <summary>Раз в секунду читает только размер нашего временного IPA; не сканирует папку и не читает тело файла.</summary>
        async Task ObserveAsync(string path, CancellationToken ct)
        {
            var meter = new FileTransferMeter(); var start = clock.GetTimestamp();
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1),clock, ct).ConfigureAwait(false);
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists) Transfer(meter.Sample(file.Length, clock.GetElapsedTime(start).TotalSeconds).Message);
                    else meter.Reset();
                }
                catch (IOException) { meter.Reset(); }
                catch (UnauthorizedAccessException) { meter.Reset(); }
            }
        }
    }
}
