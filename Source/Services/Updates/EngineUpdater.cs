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
    /// <summary>Проверяет, устанавливает и откатывает ipatool и отдельный комплект libimobiledevice.</summary>
    public sealed class EngineUpdater
    {
        public const string SourceRepository = "Sorvigolova/ipatool";
        public const string DeviceRepository = AppInfo.Repository;
        public const string DeviceTagPrefix = "device-tools-v";
        public const string WindowsAssetName = "windows_amd64_ipatool-cpp.zip";
        static readonly string[] DeviceFiles = { "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "libimobiledevice-1.0.dll", "libplist-2.0.dll" };
        /// <summary>Выбирает единственный ожидаемый Windows-архив и проверяет его источник.</summary>
        public static Asset SelectAsset(Release release)
        {
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name == WindowsAssetName).ToArray();
            if (matches.Length != 1) throw new IOException("В релизе ipatool ожидается один Windows x64 ZIP: " + WindowsAssetName);
            ValidateAsset(matches[0], SourceRepository);
            return matches[0];
        }
        /// <summary>Проверяет допустимый репозиторий и параметры архива перед загрузкой.</summary>
        static void ValidateAsset(Asset asset, string repository)
        {
            if (asset.browser_download_url == null || !asset.browser_download_url.StartsWith("https://github.com/" + repository + "/releases/download/", StringComparison.Ordinal)) throw new IOException("Неожиданный источник архива.");
            if (asset.size <= 0 || asset.size > 1000000000) throw new IOException("Некорректный размер архива.");
        }
        /// <summary>Выбирает архив инструментов устройства из релиза с допустимым тегом и источником.</summary>
        public static Asset SelectDeviceAsset(Release release)
        {
            if (release.tag_name == null || !System.Text.RegularExpressions.Regex.IsMatch(release.tag_name, @"^device-tools-v\d+\.\d+\.\d+$")) throw new IOException("Некорректный релиз нашего комплекта для iPhone.");
            var expected = "IPA-DeviceTools-" + release.tag_name.Substring(DeviceTagPrefix.Length) + "-Windows-x64.zip";
            var matches = (release.assets ?? new Asset[0]).Where(a => a.name == expected).ToArray();
            if (matches.Length != 1) throw new IOException("Не найден однозначный Windows x64 архив нашего комплекта.");
            ValidateAsset(matches[0], DeviceRepository);
            if (!matches[0].browser_download_url.StartsWith("https://github.com/" + DeviceRepository + "/releases/download/" + release.tag_name + "/", StringComparison.Ordinal)) throw new IOException("Архив принадлежит другому релизу.");
            if (matches[0].digest == null || !System.Text.RegularExpressions.Regex.IsMatch(matches[0].digest, @"^sha256:[0-9a-fA-F]{64}$")) throw new IOException("Для комплекта требуется SHA-256 от GitHub.");
            return matches[0];
        }
        /// <summary>Проверяет наличие обязательных EXE и DLL в указанном комплекте.</summary>
        public static bool HasDeviceTools(string root)
        {
            if (!Directory.Exists(root)) return false;
            return new[] { "idevice_id.exe", "ideviceinfo.exe", "ideviceinstaller.exe", "libimobiledevice-1.0*.dll", "libplist-2.0*.dll" }.All(file => Directory.GetFiles(root, file, SearchOption.AllDirectories).Count(x => x.Replace('\\', '/').Contains("/windows_amd64/")) == 1);
        }
        /// <summary>Отклоняет неполный комплект инструментов установки до переключения настроек.</summary>
        public static void CheckDevicePackage(string root)
        {
            if (!HasDeviceTools(root) || !Directory.Exists(Path.Combine(root, "LICENSES")) || !File.Exists(Path.Combine(root, "sources.json"))) throw new IOException("Неполный комплект: нужны утилиты, DLL, лицензии и исходные ревизии.");
            var names = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            if (names.Length != 3 || DeviceFiles.Take(3).Any(name => !names.Contains(name))) throw new IOException("Комплект содержит неожиданные программы.");
        }

        readonly SettingsStore store;
        readonly IProcessRunner runner;
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
        State Settings
        {
            get
            {
                return store.Settings;
            }
            set
            {
                store.Settings = value;
            }
        }
        void Save()
        {
            store.Save();
        }
        public Action<string> Log
        {
            get;
            set;
        }
        public Action<double> Progress
        {
            get;
            set;
        }
        public EngineUpdater(SettingsStore store, IProcessRunner runner)
        {
            this.store = store;
            this.runner = runner;
            Log = delegate
            {
            };
            Progress = delegate
            {
            };
        }
        public async Task<Release> Check(CancellationToken ct)
        {
            using (var response = await Http.GetAsync("https://api.github.com/repos/Sorvigolova/ipatool/releases/latest", ct))
            {
                response.EnsureSuccessStatusCode();
                return JsonCodec.Deserialize<Release> (await response.Content.ReadAsStringAsync());
            }
        }
        /// <summary>Распаковывает архив с проверкой выходных путей, исключая запись за пределами целевой папки.</summary>
        public static void Extract(string zip, string dest)
        {
            Directory.CreateDirectory(dest);
            var prefix = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
            using (var z = ZipFile.OpenRead(zip))
            {
                if (z.Entries.Count > 30000 || z.Entries.Sum(x => x.Length) > 2000000000L) throw new IOException("Архив превышает допустимый размер.");
                foreach (var e in z.Entries)
                {
                    var p = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                    if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасный путь в ZIP.");
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(p);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    e.ExtractToFile(p, false);
                }
            }
        }
        /// <summary>Проверяет новую версию в отдельной папке и меняет настройки только после успешной установки.</summary>
        public async Task Install(Release rel, CancellationToken ct)
        {
            var asset = SelectAsset(rel);
            var name = Guid.NewGuid().ToString("N");
            var staging = Path.Combine(Root, "Versions", name);
            var zip = Path.Combine(Root, name + ".zip");
            var supportName = Settings.DeviceCurrent ?? Settings.Current;
            var locator = new ToolLocator(store);
            var needSupport = String.IsNullOrEmpty(supportName) || !HasDeviceTools(locator.VersionRoot(supportName));
            var newSupport = "support-" + name;
            var supportStage = Path.Combine(Root, "Versions", newSupport);
            var supportZip = Path.Combine(Root, "support-" + name + ".zip");
            var supportVersion = Settings.DeviceVersion ?? Settings.Version;
            var supportSource = Settings.DeviceSource;
            bool committed = false;
            try
            {
                Log("Загрузка релиза " + rel.tag_name);
                await Download(asset, zip, ct);
                if (needSupport)
                {
                    Log("Получение нашего Windows-комплекта для iPhone…");
                    var supportRelease = await CheckDeviceTools(ct);
                    await Download(SelectDeviceAsset(supportRelease), supportZip, ct);
                    await Task.Run(delegate
                    {
                        Extract(supportZip, supportStage);
                        CheckDevicePackage(supportStage);
                    }, ct);
                    await ValidateDeviceTools(supportStage, ct);
                    supportName = newSupport;
                    supportVersion = supportRelease.tag_name;
                    supportSource = DeviceRepository;
                }
                ct.ThrowIfCancellationRequested();
                Log("Распаковка и проверка команд…");
                await Task.Run(delegate
                {
                    Extract(zip, Path.Combine(staging, "windows_amd64"));
                }, ct);
                var bins = Directory.GetFiles(staging, "ipatool*.exe", SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
                if (bins.Length != 1) throw new IOException("В архиве нет ожидаемого Windows-движка.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var test = await runner.Run(bins[0], new string[0], timeout.Token, false, false);
                    ToolResponse.Ensure(test);
                    foreach (var command in new[]
                    {
                        "auth", "search", "download", "list-versions", "get-version-metadata", "--format", "--external-version-id"
                    }) if (!test.Output.Contains(command)) throw new IOException("Релиз несовместим с адаптером: нет " + command);
                }
                ct.ThrowIfCancellationRequested();
                var previous = Settings;
                Settings = previous.Copy();
                Settings.Source = SourceRepository;
                Settings.PreviousSource = previous.Source;
                Settings.DeviceCurrent = supportName;
                Settings.DeviceVersion = supportVersion;
                Settings.DeviceSource = supportSource;
                Settings.Current = name;
                Settings.Version = rel.tag_name;
                Settings.Previous = previous.Current;
                Settings.PreviousVersion = previous.Version;
                try
                {
                    Save();
                    committed = true;
                } catch
                {
                    Settings = previous;
                    throw;
                }
                Log("Установлен движок " + rel.tag_name + ". Предыдущая версия сохранена.");
            } finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (File.Exists(supportZip)) File.Delete(supportZip);
                if (!committed && Directory.Exists(staging)) Directory.Delete(staging, true);
                if (!committed && Directory.Exists(supportStage)) Directory.Delete(supportStage, true);
            }
        }
        /// <summary>Скачивает архив с контролем размера, SHA-256 и отменой запроса.</summary>
        async Task Download(Asset asset, string zip, CancellationToken ct)
        {
            using (var response = await Http.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync()) using (var output = File.Create(zip))
                {
                    var buf = new byte[131072];
                    int n;
                    long total = 0;
                    while ((n = await input.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        total += n;
                        if (total > asset.size) throw new IOException("Размер архива не совпадает.");
                        await output.WriteAsync(buf, 0, n, ct);
                        Progress(100.0 * total / asset.size);
                    }
                    if (total != asset.size) throw new IOException("Архив загружен не полностью.");
                }
            }
            if (!String.IsNullOrEmpty(asset.digest))
            {
                if (!asset.digest.StartsWith("sha256:")) throw new IOException("Неизвестный формат контрольной суммы.");
                using (var sha = SHA256.Create()) using (var f = File.OpenRead(zip))
                {
                    var digest = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (!String.Equals("sha256:" + digest, asset.digest, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма не совпадает.");
                }
            } else Log("GitHub не предоставил SHA-256; проверены HTTPS-источник и размер архива.");
        }
        /// <summary>Получает опубликованные релизы комплекта устройства независимо от последнего GUI-релиза.</summary>
        public async Task<Release> CheckDeviceTools(CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                // Tool releases are separate from GUI releases and never become GUI latest.
                for (int page = 1; page <= 10; page++)
                {
                    using (var response = await Http.GetAsync("https://api.github.com/repos/" + DeviceRepository + "/releases?per_page=100&page=" + page, timeout.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        var releases = JsonCodec.Deserialize<Release[]>(await response.Content.ReadAsStringAsync());
                        var found = releases.FirstOrDefault(r => !r.draft && !r.prerelease && r.tag_name != null && r.tag_name.StartsWith(DeviceTagPrefix, StringComparison.Ordinal));
                        if (found != null) { SelectDeviceAsset(found); return found; }
                        if (releases.Length < 100) break;
                    }
                }
                throw new IOException("Наш опубликованный Windows-комплект для iPhone пока не найден.");
            }
        }
        /// <summary>Сопоставляет источник, тег и наличие бинарников, а не только номер версии.</summary>
        public bool DeviceToolsCurrent(Release release)
        {
            var current = Settings.DeviceCurrent ?? Settings.Current;
            var version = Settings.DeviceVersion ?? (Settings.Source == null ? Settings.Version : null);
            return Settings.DeviceSource == DeviceRepository && !String.IsNullOrEmpty(current) && release.tag_name == version && HasDeviceTools(new ToolLocator(store).VersionRoot(current));
        }
        /// <summary>Проверяет запуск обязательных утилит до активации установленного комплекта.</summary>
        async Task ValidateDeviceTools(string root, CancellationToken ct)
        {
            if (!HasDeviceTools(root)) throw new IOException("Неполный комплект инструментов для iPhone.");
            foreach (var name in DeviceFiles.Take(3))
            {
                var file = Directory.GetFiles(root, name, SearchOption.AllDirectories).Single();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var result = await runner.Run(file, new[] { "--version" }, timeout.Token, false, false);
                    ToolResponse.Ensure(result);
                    if (String.IsNullOrWhiteSpace(result.Output)) throw new IOException("Инструмент не сообщил версию: " + name);
                }
            }
        }
        /// <summary>Устанавливает комплект в новую папку, проверяет CLI и сохраняет настройки после успеха.</summary>
        public async Task InstallDeviceTools(Release release, CancellationToken ct)
        {
            var asset = SelectDeviceAsset(release);
            var name = "support-" + Guid.NewGuid().ToString("N");
            var stage = Path.Combine(Root, "Versions", name);
            var zip = Path.Combine(Root, name + ".zip");
            bool committed = false;
            try
            {
                Log("Обновление нашего комплекта для iPhone " + release.tag_name);
                await Download(asset, zip, ct);
                await Task.Run(delegate
                {
                    Extract(zip, stage);
                    CheckDevicePackage(stage);
                }, ct);
                await ValidateDeviceTools(stage, ct);
                ct.ThrowIfCancellationRequested();
                var old = Settings;
                Settings = old.Copy();
                Settings.PreviousDeviceCurrent = old.DeviceCurrent ?? (old.Source == null ? old.Current : null);
                Settings.PreviousDeviceVersion = old.DeviceVersion ?? (old.Source == null ? old.Version : null);
                Settings.PreviousDeviceSource = old.DeviceSource;
                Settings.DeviceCurrent = name;
                Settings.DeviceVersion = release.tag_name;
                Settings.DeviceSource = DeviceRepository;
                try { Save(); committed = true; }
                catch { Settings = old; throw; }
                Log("Инструменты для iPhone обновлены. Предыдущий комплект сохранён.");
            }
            finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (!committed && Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        }
        /// <summary>Меняет местами текущий и предыдущий комплект устройства, сохраняя состояние ipatool.</summary>
        public void RollbackDeviceTools()
        {
            var old = Settings;
            if (String.IsNullOrEmpty(old.PreviousDeviceCurrent) || !HasDeviceTools(new ToolLocator(store).VersionRoot(old.PreviousDeviceCurrent))) throw new IOException("Предыдущего комплекта инструментов для iPhone пока нет.");
            Settings = old.Copy();
            Settings.DeviceCurrent = old.PreviousDeviceCurrent;
            Settings.DeviceVersion = old.PreviousDeviceVersion;
            Settings.DeviceSource = old.PreviousDeviceSource;
            Settings.PreviousDeviceSource = old.DeviceSource;
            Settings.PreviousDeviceCurrent = old.DeviceCurrent ?? old.Current;
            Settings.PreviousDeviceVersion = old.DeviceVersion ?? old.Version;
            Settings.AutoUpdateDeviceTools = false;
            try { Save(); }
            catch { Settings = old; throw; }
            Log("Комплект для iPhone восстановлен. Его автообновление отключено.");
        }
        /// <summary>Возвращает предыдущую версию движка и сохраняет независимые пользовательские настройки.</summary>
        public void Rollback()
        {
            if (String.IsNullOrEmpty(Settings.Previous) || !Directory.Exists(Path.Combine(Root, "Versions", Settings.Previous))) throw new Exception("Предыдущей версии пока нет.");
            var old = Settings;
            Settings = old.Copy();
            Settings.Source = old.PreviousSource;
            Settings.PreviousSource = old.Source;
            Settings.DeviceCurrent = old.DeviceCurrent ?? old.Current;
            Settings.DeviceVersion = old.DeviceVersion ?? old.Version;
            Settings.Current = old.Previous;
            Settings.Version = old.PreviousVersion;
            Settings.Previous = old.Current;
            Settings.PreviousVersion = old.Version;
            Settings.AutoUpdate = false;
            try
            {
                Save();
            } catch
            {
                Settings = old;
                throw;
            }
            Log("Откат выполнен. Автообновление отключено, чтобы не вернуть проблемный релиз.");
        }
    }
}
