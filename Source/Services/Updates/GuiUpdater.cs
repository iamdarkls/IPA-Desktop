using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace IpaDesktop
{
    /// <summary>Проверяет источник, размер и SHA-256 GUI-сборки; готовит обновление с восстановлением при ошибке.</summary>
    public static class GuiUpdater
    {
        public const string CurrentVersion = AppInfo.Version;
        public static readonly string[] PackageFiles = PackageManifest.Files;
        /// <summary>Разбирает числовой тег версии для корректного сравнения обновлений.</summary>
        public static Version ParseVersion(string tag)
        {
            Version v;
            if (tag == null || !System.Text.RegularExpressions.Regex.IsMatch(tag, "^v?[0-9]+\\.[0-9]+\\.[0-9]+$") || !Version.TryParse(tag.TrimStart('v'), out v)) throw new InvalidDataException("Некорректная версия релиза.");
            return v;
        }
        static HttpClient Client()
        {
            var h = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/" + CurrentVersion);
            return h;
        }
        public static async Task<Release> Check(CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (var h = Client()) using (var response = await h.GetAsync("https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest", timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    var r = JsonCodec.Deserialize<Release>(await response.Content.ReadAsStringAsync(timeout.Token));
                    return ParseVersion(r.tag_name) > new Version(CurrentVersion) ? r : null;
                }
            }
        }
        /// <summary>Выбирает архив текущей платформы и проверяет его источник.</summary>
        public static Asset SelectAsset(Release release)
        {
            var version = ParseVersion(release.tag_name).ToString();
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name == "IPA-Desktop-" + version + "-" + RuntimePlatform.AssetSuffix + ".zip").ToArray();
            if (matches.Length != 1) throw new InvalidDataException("В релизе нет единственного ZIP для "+RuntimePlatform.Rid+".");
            var asset = matches[0];
            string expected = AppInfo.ReleasesUrl + "/download/" + release.tag_name + "/" + asset.name;
            if (asset.browser_download_url != expected || asset.size <= 0 || asset.size > 128 * 1024 * 1024 || !ReleaseArchiveDownload.HasSha256(asset.digest)) throw new InvalidDataException("Не удалось проверить источник, размер или SHA-256 обновления.");
            return asset;
        }
        /// <summary>Извлекает только проверенные файлы манифеста с ограничением размера и проверкой структуры.</summary>
        public static void Extract(string zip, string stage)
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                long total = 0;
                var entries = archive.Entries.ToLookup(e => e.FullName.Replace('\\', '/'), StringComparer.Ordinal);
                var selected = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                foreach (var name in PackageFiles)
                {
                    var matches = entries[name].ToArray();
                    if (matches.Length != 1 || matches[0].Length == 0 || matches[0].Length > 128 * 1024 * 1024) throw new InvalidDataException("Неполный пакет обновления: " + name);
                    selected.Add(name, matches[0]);
                    total += matches[0].Length;
                    if (total > 256 * 1024 * 1024) throw new InvalidDataException("Пакет слишком велик.");
                }
                Directory.CreateDirectory(stage);
                foreach (var name in PackageFiles)
                {
                    string destination = Path.Combine(stage, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    selected[name].ExtractToFile(destination);
                }
            }
        }
        /// <summary>Загружает и проверяет обновление перед запуском внешнего установочного скрипта.</summary>
        public static async Task<string> Prepare(Release release, CancellationToken ct, Action<double> progress)
        {
            var asset = SelectAsset(release);
            if (!RuntimePlatform.Windows) UnixGuiUpdate.CheckInstallation();
            string root = Path.Combine(RuntimePlatform.DataRoot, "GuiUpdates");
            Directory.CreateDirectory(root);
            foreach (var dir in Directory.GetDirectories(root, "update-*")) try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays( - 7)) Directory.Delete(dir, true);
            } catch (IOException)
            {
            } catch (UnauthorizedAccessException)
            {
            }
            string work = Path.Combine(root, "update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "package.zip"), stage = Path.Combine(work, "stage");
            try
            {
                try
                {
                    using var http = Client();
                    await ReleaseArchiveDownload.SaveAsync(http, asset, zip, ct, progress);
                }
                catch (HttpRequestException error)
                {
                    var status = error.StatusCode is { } code ? " (HTTP " + (int)code + ")" : "";
                    throw new IOException("Не удалось скачать архив обновления с GitHub" + status +
                        ". Попробуйте скачать его с GitHub Releases через браузер.", error);
                }

                var package = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!RuntimePlatform.Windows) return UnixGuiUpdate.Extract(zip, stage);
                    Extract(zip, stage);
                    return stage;
                }, ct);
                File.Delete(zip);
                return package;
            } catch
            {
                try
                {
                    Directory.Delete(work, true);
                } catch
                {
                }
                throw;
            }
        }
        /// <summary>Экранирует строковый аргумент PowerShell без интерполяции пользовательского содержимого.</summary>
        static string QuotePS(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
        /// <summary>Запускает обновляющий скрипт, который дождётся закрытия приложения и заменит файлы.</summary>
        public static void Launch(string stage)
        {
            if(!RuntimePlatform.Windows){UnixGuiUpdate.Launch(stage);return;}
            string target = AppDomain.CurrentDomain.BaseDirectory;
            string probe = Path.Combine(target, ".update-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            string helper = Path.Combine(Path.GetDirectoryName(stage), "Apply.ps1");
            File.Copy(Path.Combine(target, "Scripts", "Update-App.ps1"), helper);
            string command = "& " + QuotePS(helper) + " -Stage " + QuotePS(stage) + " -Target " + QuotePS(target) + " -ParentId " + Environment.ProcessId;
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false, CreateNoWindow = true
            });
        }
    }
}
