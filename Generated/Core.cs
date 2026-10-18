// Generated from Source/. Edit the source classes, then run Sync-Sources.ps1.
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System;

// Domain/AppInfo.cs
namespace IpaDesktop
{
    /// <summary>Единые сведения о версии и репозитории GUI; версия в XAML сверяется при выпуске.</summary>
    public static class AppInfo
    {
        public const string Version = "0.2.10";
        public const string Repository = "iamdarkls/IPA-Desktop";
        public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    }
}

// Domain/AppleSupportReport.cs
namespace IpaDesktop
{
    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public enum AppleSupportStatus
    {
        Running,
        Stopped,
        ServiceMissing,
        NotDetected,
        Unknown
    }

    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public sealed class AppleSupportSnapshot
    {
        public bool InstallationDetected { get; set; }
        public bool ServiceFound { get; set; }
        public bool ServiceStatusKnown { get; set; }
        public bool ServiceRunning { get; set; }
        public bool ServiceScanCompleted { get; set; }
        public bool RegistryScanCompleted { get; set; }
        public string Details { get; set; }
    }

    /// <summary>Результат проверки драйверов Apple и сведения для объяснения пользователю.</summary>
    public sealed class AppleSupportReport
    {
        public AppleSupportStatus Status { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string HelpUrl
        {
            get
            {
                return Status == AppleSupportStatus.Stopped
                    ? "https://support.apple.com/ru-ru/102347"
                    : "https://support.apple.com/ru-ru/118290";
            }
        }
    }
}

// Domain/AppRow.cs
namespace IpaDesktop
{
    /// <summary>Приложение каталога: отображаемое название и числовой App Store ID; без данных аккаунта.</summary>
    public class AppRow
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
    }
}

// Domain/AppSet.cs
namespace IpaDesktop
{
    /// <summary>Сохранённый набор ID; Files используется только для чтения и миграции прежнего формата.</summary>
    public sealed class AppSet
    {
        public string Name { get; set; }
        public AppRow[] Apps { get; set; }
        // Read-only compatibility with sets saved before 0.1.18.
        public string[] Files { get; set; }
        public string Label { get { return Name + " · " + (Apps == null ? (Files == null ? 0 : Files.Length) : Apps.Length) + " приложений" + (Apps == null ? " · прежний формат" : ""); } }
    }
}

// Domain/AppSetRules.cs
namespace IpaDesktop
{
    /// <summary>Ограничения формата наборов и объединение приложений по App Store ID; без зависимости от WPF.</summary>
    public static class AppSetRules
    {
        /// <summary>Проверяет новый формат ID и прежний формат имён IPA до чтения файлов или записи конфигурации.</summary>
        public static void Validate(AppSet set)
        {
            if (set == null || String.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 64 || set.Name.Any(Char.IsControl)) throw new IOException("Название набора: от 1 до 64 символов.");
            if (set.Apps != null)
            {
                if (set.Apps.Length == 0 || set.Apps.Length > 500) throw new IOException("В наборе должно быть от 1 до 500 приложений.");
                foreach (var app in set.Apps)
                    if (app == null || !ValidId(app.ID) || String.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 320 || app.Name.Any(Char.IsControl)) throw new IOException("Набор содержит некорректный App Store ID или название.");
                if (set.Apps.Select(x => x.ID).Distinct().Count() != set.Apps.Length) throw new IOException("App Store ID в наборе повторяются.");
            }
            else
            {
                if (set.Files == null || set.Files.Length == 0 || set.Files.Length > 500) throw new IOException("Пустой набор.");
                foreach (var name in set.Files)
                    if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(Char.IsControl) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !String.Equals(Path.GetExtension(name), ".ipa", StringComparison.OrdinalIgnoreCase)) throw new IOException("Набор содержит некорректное имя IPA.");
            }
        }
        /// <summary>Проверяет положительный числовой ID без знака, пробелов и управляющих символов.</summary>
        public static bool ValidId(string id)
        {
            ulong value;
            return !String.IsNullOrEmpty(id) && id.Length <= 20 && id.All(c => c >= '0' && c <= '9') && UInt64.TryParse(id, out value) && value > 0;
        }
        /// <summary>Копирует входные данные, проверяет значения и сохраняет порядок первого появления ID.</summary>
        public static AppRow[] UniqueApps(IEnumerable<AppRow> apps)
        {
            if (apps == null) throw new IOException("Список приложений не задан.");
            var result = new List<AppRow>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var app in apps)
            {
                if (app == null || !ValidId(app.ID) || String.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 320 || app.Name.Any(Char.IsControl)) throw new IOException("Некорректный App Store ID или название приложения.");
                if (ids.Add(app.ID)) result.Add(new AppRow { ID = app.ID, Name = app.Name });
            }
            return result.ToArray();
        }
    }
}

// Domain/AppVersionRow.cs
namespace IpaDesktop
{
    /// <summary>Строка таблицы версий; неизвестные поля не подменяются данными другой версии.</summary>
    public sealed class AppVersionRow
    {
        public string ID { get; set; }
        public string Version { get; set; }
        public string MinimumOS { get; set; }
        public string Platforms { get; set; }
        public string State { get; set; }
        public AppVersionRow() { Version = "—"; MinimumOS = "—"; Platforms = "—"; State = "Не запрошено"; }
    }
}

// Domain/Asset.cs
namespace IpaDesktop
{
    /// <summary>Описание архива GitHub Releases; имена полей сохраняют контракт JSON API.</summary>
    public class Asset
    {
        public string name
        {
            get;
            set;
        }
        public string browser_download_url
        {
            get;
            set;
        }
        public string digest
        {
            get;
            set;
        }
        public long size
        {
            get;
            set;
        }
    }
}

// Domain/DeviceRow.cs
namespace IpaDesktop
{
    /// <summary>Обнаруженный iPhone: подпись для интерфейса и реальный UDID для команд установки.</summary>
    public class DeviceRow
    {
        public string ID
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public bool Network { get; set; }
        public string Connection { get { return Network ? "Wi-Fi" : "USB"; } }
        public string DisplayLabel { get { return Name + " · " + Connection; } }
        public override string ToString()
        {
            return Name;
        }
    }
}

// Domain/DownloadItem.cs
namespace IpaDesktop
{
    /// <summary>Снимок запроса загрузки с необязательным ID версии; null означает последнюю доступную.</summary>
    public class DownloadItem
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
        public string VersionID
        {
            get;
            set;
        }
        public string Key { get { return ID + "@" + (VersionID ?? "latest"); } }
        public string Label
        {
            get
            {
                return Name + (VersionID == null ? " · последняя" : " · версия " + VersionID);
            }
        }
    }
}

// Domain/DownloadSelection.cs
namespace IpaDesktop
{
    /// <summary>Очередь загрузок, уникальная по паре App Store ID и ID версии; снимки отделены от внутреннего состояния.</summary>
    public class DownloadSelection
    {
        readonly List<DownloadItem> items = new List<DownloadItem>();
        public int Count
        {
            get
            {
                return items.Count;
            }
        }
        public void Add(AppRow app, string version)
        {
            if (app == null || String.IsNullOrEmpty(app.ID) || !app.ID.All(Char.IsDigit)) throw new ArgumentException("Некорректный App Store ID.");
            if (version != null && (version.Length == 0 || !version.All(Char.IsDigit))) throw new ArgumentException("Некорректная версия.");
            int index = items.FindIndex(x => x.ID == app.ID);
            items.RemoveAll(x => x.ID == app.ID);
            var replacement = new DownloadItem { Name = app.Name, ID = app.ID, VersionID = version };
            if (index < 0) items.Add(replacement); else items.Insert(Math.Min(index, items.Count), replacement);
        }
        /// <summary>Добавляет ещё одну версию того же приложения, не заменяя соседние элементы.</summary>
        public void AddVariant(AppRow app, string version)
        {
            if (app == null || !AppSetStore.ValidId(app.ID) || (version != null && !AppSetStore.ValidId(version))) throw new ArgumentException("Некорректный ID приложения или версии.");
            var item = items.FirstOrDefault(x => x.ID == app.ID && x.VersionID == version);
            if (item == null) items.Add(new DownloadItem { ID = app.ID, Name = app.Name, VersionID = version });
            else item.Name = app.Name;
        }
        public void RemoveKey(string key) { items.RemoveAll(x => x.Key == key); }
        /// <summary>Дополняет очередь набором, сохраняя уже выбранную пользователем версию приложения.</summary>
        public int AddMissing(IEnumerable<AppRow> apps)
        {
            var incoming = AppSetRules.UniqueApps(apps);
            int added = 0;
            foreach (var app in incoming)
            {
                if (items.Any(x => x.ID == app.ID)) continue;
                Add(app, null); added++;
            }
            return added;
        }
        /// <summary>Возвращает копии элементов, чтобы текущая операция не зависела от дальнейших изменений выбора.</summary>
        public List<DownloadItem> Snapshot()
        {
            return items.Select(x => new DownloadItem
            {
                Name = x.Name, ID = x.ID, VersionID = x.VersionID
            }).ToList();
        }
        public void Remove(string id)
        {
            items.RemoveAll(x => x.ID == id);
        }
        public void Clear()
        {
            items.Clear();
        }
    }
}

// Domain/FileTransferMeter.cs
namespace IpaDesktop
{
    /// <summary>Оценка записи по монотонным отсчётам размера одного файла, без вывода объёма из округлённых процентов.</summary>
    public sealed class FileTransferMeter
    {
        readonly Queue<Tuple<double, long>> samples = new Queue<Tuple<double, long>>();
        public void Reset() { samples.Clear(); }
        public FileTransferSample Sample(long bytes, double seconds)
        {
            if (bytes < 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0) throw new ArgumentException("Некорректное измерение передачи.");
            if (samples.Count > 0 && (seconds <= LastTime || bytes < LastBytes)) Reset();
            samples.Enqueue(Tuple.Create(seconds, bytes)); LastTime = seconds; LastBytes = bytes;
            while (samples.Count > 8 || (samples.Count > 1 && seconds - samples.Peek().Item1 > 3.5)) samples.Dequeue();
            var first = samples.Peek(); double elapsed = seconds - first.Item1;
            return new FileTransferSample { Bytes = bytes, BytesPerSecond = elapsed >= 0.5 ? (double?)(bytes - first.Item2) / elapsed : null };
        }
        double LastTime; long LastBytes;
    }
    public sealed class FileTransferSample
    {
        public long Bytes { get; set; }
        public double? BytesPerSecond { get; set; }
        public string Message
        {
            get { return "FILE_TRANSFER:" + Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (BytesPerSecond.HasValue ? BytesPerSecond.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "?"); }
        }
    }
}

// Domain/InstallationSelection.cs
namespace IpaDesktop
{
    /// <summary>Очередь установки, уникальная по пути IPA; сведения аккаунта принадлежат самому файлу.</summary>
    public class InstallationSelection
    {
        readonly List<LocalRow> items = new List<LocalRow>();
        static LocalRow Copy(LocalRow row)
        {
            return new LocalRow
            {
                AppStoreID = row.AppStoreID, Name = row.Name, Path = row.Path, Size = row.Size, AppleAccount = row.AppleAccount, Bytes = row.Bytes, BundleID = row.BundleID, AppName = row.AppName, Version = row.Version, Build = row.Build, MinimumOS = row.MinimumOS, Platforms = row.Platforms == null ? null : (string[])row.Platforms.Clone(), IsOld = row.IsOld, StorageStatus = row.StorageStatus
            };
        }
        public int Count
        {
            get
            {
                return items.Count;
            }
        }
        public void Add(LocalRow row)
        {
            if (row == null || String.IsNullOrWhiteSpace(row.Path)) throw new ArgumentException("Файл не выбран.");
            if (!items.Any(x => String.Equals(x.Path, row.Path, StringComparison.OrdinalIgnoreCase))) items.Add(Copy(row));
        }
        /// <summary>Возвращает копии элементов, чтобы текущая операция не зависела от дальнейших изменений выбора.</summary>
        public List<LocalRow> Snapshot()
        {
            return items.Select(Copy).ToList();
        }
        public void Remove(string path)
        {
            items.RemoveAll(x => String.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        public void Clear()
        {
            items.Clear();
        }
        /// <summary>Сверяет выбранные пути с новым снимком диска, удаляя исчезнувшие файлы из очереди.</summary>
        public void Reconcile(IEnumerable<LocalRow> files)
        {
            var available = files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                LocalRow row;
                if (!available.TryGetValue(items[i].Path, out row)) items.RemoveAt(i);
                else items[i] = Copy(row);
            }
        }
    }
}

// Domain/IpaDetails.cs
namespace IpaDesktop
{
    /// <summary>Метаданные IPA, прочитанные из архива; отсутствующие поля остаются неизвестными.</summary>
    public sealed class IpaDetails
    {
        public string AppStoreID { get; set; }
        public string Account { get; set; }
        public string BundleID { get; set; }
        public string AppName { get; set; }
        public string Version { get; set; }
        public string MinimumOS { get; set; }
        public string[] Platforms { get; set; }
        public string Build { get; set; }
    }
}

// Domain/LocalFileAccounts.cs
namespace IpaDesktop
{
    /// <summary>Вариант фильтра локальных IPA. Null означает все аккаунты, пустая строка — неизвестный аккаунт.</summary>
    public sealed class LocalFileAccount
    {
        public string Key { get; set; }
        public string Label { get; set; }
        // Общий шаблон ComboBox отображает SelectionBoxItem через ToString, включая выбранный элемент.
        public override string ToString() { return Label; }
    }

    /// <summary>Группирует только аккаунты из метаданных IPA, без подстановки текущей сессии ipatool.</summary>
    public static class LocalFileAccounts
    {
        static string Normalize(string account)
        {
            var value = (account ?? "").Trim();
            // Эти значения возвращает IpaMetadata при отсутствии или ошибке чтения, это не имена аккаунтов.
            foreach (var status in new[] { "Не указан", "Не удалось прочитать", "Нет доступа", "Метаданные слишком велики" })
                if (String.Equals(value, status, StringComparison.OrdinalIgnoreCase)) return "";
            return value;
        }

        public static bool Matches(LocalRow row, string key)
        {
            return key == null || String.Equals(Normalize(row.AppleAccount), key, StringComparison.OrdinalIgnoreCase);
        }

        public static List<LocalFileAccount> Choices(IEnumerable<LocalRow> files)
        {
            var rows = files.ToList();
            var choices = new List<LocalFileAccount> { new LocalFileAccount { Key = null, Label = "Все аккаунты (" + rows.Count + ")" } };
            foreach (var group in rows.GroupBy(x => Normalize(x.AppleAccount), StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key.Length == 0).ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                choices.Add(new LocalFileAccount { Key = group.Key, Label = (group.Key.Length == 0 ? "Без данных об аккаунте" : group.Key) + " (" + group.Count() + ")" });
            }
            return choices;
        }
    }
}

// Domain/LocalRow.cs
namespace IpaDesktop
{
    /// <summary>Строка локального IPA: путь, размер, версия, аккаунт и признак старой версии.</summary>
    public class LocalRow
    {
        /// <summary>Название из IPA; при отсутствии метаданных убирает только наш точный служебный суффикс.</summary>
        public string DisplayName
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(AppName)) return AppName;
                string title = System.IO.Path.GetFileNameWithoutExtension(Name ?? System.IO.Path.GetFileName(Path ?? ""));
                var match = Regex.Match(title, @"^(?<title>.+)-[0-9]{1,20}-[0-9a-fA-F]{32}$");
                return match.Success ? match.Groups["title"].Value : title;
            }
        }
        /// <summary>Полные сведения доступны по наведению, включая настоящее имя файла и путь.</summary>
        public string FileDetails
        {
            get
            {
                return DisplayName + Environment.NewLine + "Версия: " + (String.IsNullOrWhiteSpace(Version) ? "не указана" : Version) +
                    Environment.NewLine + "Apple Account: " + (String.IsNullOrWhiteSpace(AppleAccount) ? "не указан" : AppleAccount) +
                    Environment.NewLine + "Минимальная ОС: " + (MinimumOS ?? "не указана") +
                    Environment.NewLine + "Платформы: " + (Platforms == null || Platforms.Length == 0 ? "не указаны" : String.Join(", ", Platforms)) +
                    Environment.NewLine + "Файл: " + Name + Environment.NewLine + Path;
            }
        }
        public string MinimumOS { get; set; }
        public string[] Platforms { get; set; }
        public long Bytes { get; set; }
        public string AppStoreID { get; set; }
        public string BundleID { get; set; }
        public string AppName { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }
        public bool IsOld { get; set; }
        public string StorageStatus { get; set; }
        public string AppleAccount
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public string Size
        {
            get;
            set;
        }
        public string Path
        {
            get;
            set;
        }
    }
}

// Domain/OperationReport.cs
namespace IpaDesktop
{
    /// <summary>Итог одной очереди с полными ошибками; не зависит от журнала и текущей вкладки.</summary>
    public sealed class OperationReport
    {
        public string Kind { get; set; }
        public int Total { get; set; }
        public bool Canceled { get; set; }
        public readonly List<OperationOutcome> Items = new List<OperationOutcome>();
        public int Failed { get { return Items.Count(x => !x.Success); } }
        public int Succeeded { get { return Items.Count(x => x.Success); } }
        public string Summary
        {
            get
            {
                return Kind + (Canceled ? " отменено" : Failed > 0 ? ": есть ошибки" : ": завершено") +
                    " · готово " + Succeeded + "/" + Total + (Failed > 0 ? " · ошибок " + Failed : "");
            }
        }
        public string Details
        {
            get
            {
                var text = new StringBuilder(Summary);
                if (Canceled) text.AppendLine().Append("Не запущено или прервано: ").Append(Math.Max(0, Total - Items.Count));
                foreach (var item in Items)
                {
                    text.AppendLine().AppendLine().Append(item.Success ? "ГОТОВО: " : "ОШИБКА: ").Append(item.Name);
                    if (!item.Success) text.AppendLine().Append(item.Error);
                }
                return text.ToString();
            }
        }
        public void Add(string name, Exception error)
        {
            string message = error == null ? null : error.Message;
            if (message != null && message.Length > 16000) message = message.Substring(0, 16000);
            Items.Add(new OperationOutcome { Name = name, Success = error == null, Error = message });
        }
    }

    /// <summary>Результат отдельного приложения без потери исходного сообщения утилиты.</summary>
    public sealed class OperationOutcome
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public string ShortError
        {
            get
            {
                if (String.IsNullOrWhiteSpace(Error)) return "";
                if (Error.IndexOf("zip_open", StringComparison.OrdinalIgnoreCase) >= 0) return "Не удалось открыть IPA";
                if (Error.IndexOf("ApplicationVerificationFailed", StringComparison.OrdinalIgnoreCase) >= 0) return "iPhone отклонил проверку приложения";
                var line = Error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(x => !x.StartsWith("Код ошибки", StringComparison.OrdinalIgnoreCase)) ?? Error;
                return line.Length > 120 ? line.Substring(0, 120) + "…" : line;
            }
        }
    }
}

// Domain/QueueProgress.cs
namespace IpaDesktop
{
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class TransferUpdate
    {
        public bool IsMeasurement { get; set; }
        public double Percent { get; set; }
        public string Metrics { get; set; }
        public static TransferUpdate Parse(string line, bool installing)
        {
            if (line == null) return null;
            var sample = Regex.Match(line, @"^FILE_TRANSFER:(\d+):(\?|\d+(?:\.\d+)?)$");
            if (sample.Success)
            {
                long bytes; double rate;
                if (!Int64.TryParse(sample.Groups[1].Value, out bytes)) return null;
                string speed = "измерение скорости…";
                if (sample.Groups[2].Value != "?")
                {
                    if (!Double.TryParse(sample.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate) || Double.IsInfinity(rate)) return null;
                    speed = "≈ " + (rate / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " МБ/с";
                }
                return new TransferUpdate { IsMeasurement = true, Metrics = "записано " + StorageAnalysis.Bytes(bytes) + " · " + speed };
            }
            var match = Regex.Match(line, @"^PROGRESS:(\d{1,3})\s*$");
            if (!match.Success && installing) match = Regex.Match(line, @"\((\d{1,3})%\)\s*$");
            if (match.Success)
            {
                int percent;
                if (!Int32.TryParse(match.Groups[1].Value, out percent) || percent > 100) return null;
                return new TransferUpdate { Percent = percent };
            }
            match = Regex.Match(line, @"^Downloading:\s*(\d{1,3})%.*\((\d+(?:\.\d+)?\s*(?:MB|GB)/\d+(?:\.\d+)?\s*(?:MB|GB),\s*\d+(?:\.\d+)?\s*MB/s)\)");
            if (!match.Success) return null;
            var value = Double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return value <= 100 ? new TransferUpdate { Percent = value, Metrics = match.Groups[2].Value } : null;
        }
    }
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class QueueProgress
    {
        readonly object gate = new object();
        readonly Stopwatch elapsed = new Stopwatch();
        string kind, name, metrics, fileMetrics, summary;
        int index, count;
        double? percent;
        public void Reset() { lock (gate) { kind = null; summary = null; elapsed.Reset(); } }
        public void Begin(string operation, int position, int total, string app)
        {
            lock (gate) { kind = operation; index = position; count = total; name = app; metrics = null; fileMetrics = null; summary = null; percent = null; elapsed.Restart(); }
        }
        /// <summary>Разбирает реальный вывод CLI под блокировкой; неподдерживаемые метрики не выдумываются.</summary>
        public bool Feed(string line)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return false;
                var update = TransferUpdate.Parse(line, kind == "Установка");
                if (update == null) return false;
                if (update.IsMeasurement) { if (kind != "Скачивание") return false; fileMetrics = update.Metrics; }
                else { percent = update.Percent; metrics = update.Metrics; }
                return true;
            }
        }
        public void Complete(int success, int failures)
        {
            lock (gate) { summary = "Готово: " + success + " · ошибок: " + failures + " · всего: " + count; percent = 100; index = count; elapsed.Stop(); }
        }
        /// <summary>Фиксирует отмену или ошибку и прекращает приём поздних сообщений прогресса.</summary>
        public void Stop(string reason)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return;
                elapsed.Stop(); summary = reason + " · " + index + "/" + count + ": " + name;
            }
        }
        public bool Active { get { lock (gate) return kind != null; } }
        public double Value { get { lock (gate) return count > 0 ? 100.0 * (index - 1 + (percent ?? 0) / 100.0) / count : 0; } }
        public bool Indeterminate { get { lock (gate) return !percent.HasValue; } }
        string MetricsText()
        {
            return kind == "Скачивание" && summary == null ? metrics ?? fileMetrics ?? "объём и скорость недоступны" : "";
        }
        string Describe(bool includeMetrics)
        {
            if (kind == null) return "";
            if (summary != null) return summary;
            string text = kind + " " + index + "/" + count + ": " + name + " · осталось после текущего: " + Math.Max(0, count - index) + " · " +
                (percent.HasValue ? percent.Value.ToString("0") + "%" : "выполняется") + " · " + elapsed.Elapsed.ToString(@"hh\:mm\:ss");
            if (kind == "Скачивание" && percent.HasValue && percent.Value >= 100) text += " · проверка и подготовка IPA";
            string transfer = MetricsText();
            return includeMetrics && transfer.Length > 0 ? text + " · " + transfer : text;
        }
        public string TransferText { get { lock (gate) return MetricsText(); } }
        public string StatusText { get { lock (gate) return Describe(false); } }
        public string Text { get { lock (gate) return Describe(true); } }
    }
}

// Domain/Release.cs
namespace IpaDesktop
{
    /// <summary>Ответ GitHub Releases с тегом и архивами; используется при проверке доступных обновлений.</summary>
    public class Release
    {
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public string tag_name
        {
            get;
            set;
        }
        public string html_url
        {
            get;
            set;
        }
        public string body
        {
            get;
            set;
        }
        public Asset[] assets
        {
            get;
            set;
        }
    }
}

// Domain/Result.cs
namespace IpaDesktop
{
    /// <summary>Код завершения, стандартный вывод и ошибки одной команды внешней утилиты.</summary>
    public class Result
    {
        public int Code
        {
            get;
            set;
        }
        public string Output
        {
            get;
            set;
        }
    }
}

// Domain/SearchSession.cs
namespace IpaDesktop
{
    /// <summary>Ревизия поискового запроса: устаревшие асинхронные ответы не возвращаются в выдачу.</summary>
    public class SearchSession
    {
        int revision;
        string query = "";
        /// <summary>Повышает ревизию запроса, чтобы игнорировать ответы прежнего поиска.</summary>
        public int Change(string value)
        {
            query = (value ?? "").Trim();
            return++revision;
        }
        public int Revision
        {
            get
            {
                return revision;
            }
        }
        /// <summary>Проверяет, относится ли асинхронный ответ к текущему запросу.</summary>
        public bool IsCurrent(int request)
        {
            return request == revision && query.Length > 0;
        }
    }
}

// Domain/State.cs
namespace IpaDesktop
{
    /// <summary>Сохраняемые настройки и независимые указатели текущих и прежних версий инструментов.</summary>
    public class State
    {
        public State()
        {
            AutoUpdate = true;
            SoundsEnabled = true;
            AutoUpdateDeviceTools = true;
            CheckGuiUpdates = true;
        }
        public bool SoundsEnabled { get; set; }
        public State Copy() { return (State)MemberwiseClone(); }
        public bool AutoUpdateDeviceTools { get; set; }
        public string PreviousDeviceCurrent { get; set; }
        public string PreviousDeviceVersion { get; set; }
        public string Source { get; set; }
        public string PreviousSource { get; set; }
        public string DeviceSource { get; set; }
        public string PreviousDeviceSource { get; set; }
        public string DeviceCurrent { get; set; }
        public string DeviceVersion { get; set; }
        public string Current
        {
            get;
            set;
        }
        public string Previous
        {
            get;
            set;
        }
        public string Version
        {
            get;
            set;
        }
        public string PreviousVersion
        {
            get;
            set;
        }
        public bool AutoUpdate
        {
            get;
            set;
        }
        public bool CheckGuiUpdates
        {
            get;
            set;
        }
    }
}

// Infrastructure/AtomicFile.cs
namespace IpaDesktop
{
    /// <summary>Записывает конфигурацию целиком, затем атомарно заменяет прежний файл.</summary>
    public static class AtomicFile
    {
        /// <summary>Уникальный временный файл исключает пересечение записей; ошибка не удаляет оригинал.</summary>
        public static void WriteUtf8(string path, string contents)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, contents, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                // Очистка не должна скрыть первоначальную ошибку записи.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

// Infrastructure/BufferedJournal.cs
namespace IpaDesktop
{
    /// <summary>Ограниченный буфер сообщений между фоновыми процессами и таймером интерфейса.</summary>
    public class BufferedJournal
    {
        readonly Queue<string> pending = new Queue<string> ();
        readonly object gate = new object();
        int skipped;
        public void Add(string text)
        {
            if (text == null) return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text.Length > 2000 ? text.Substring(0, 2000) + " …" : text) + Environment.NewLine;
            lock (gate)
            {
                if (pending.Count >= 500)
                {
                    pending.Dequeue();
                    skipped++;
                }
                pending.Enqueue(line);
            }
        }
        /// <summary>Извлекает накопленные сообщения для пакетного обновления интерфейса.</summary>
        public string Drain()
        {
            lock (gate)
            {
                var result = new StringBuilder();
                if (skipped > 0)
                {
                    result.AppendLine("Слишком частый вывод: пропущено строк " + skipped + ".");
                    skipped = 0;
                }
                while (pending.Count > 0 && result.Length < 6000) result.Append(pending.Dequeue());
                return result.ToString();
            }
        }
    }
}

// Infrastructure/ConPtyLoginProcess.cs
namespace IpaDesktop
{
    /// <summary>ConPTY Windows 10 1809+: скрытый TTY для штатного ipatool, без shell и секретных аргументов.</summary>
    public sealed class ConPtyLoginFactory : ILoginProcessFactory
    {
        public ILoginProcess Start(string executable, string workingDirectory)
        {
            return new ConPtyLoginProcess(executable, workingDirectory);
        }
    }
    public sealed class ConPtyLoginProcess : ILoginProcess
    {
        readonly Queue<string> output = new Queue<string>();
        readonly object gate = new object();
        readonly SemaphoreSlim available = new SemaphoreSlim(0);
        FileStream input, reader;
        IntPtr console, process;
        Task readTask;
        bool ended, overflow;
        int disposed;

        public ConPtyLoginProcess(string executable, string workingDirectory)
        {
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
            IntPtr attributes = IntPtr.Zero;
            bool initialized = false;
            try
            {
                if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
                try
                {
                    int hr = CreatePseudoConsole(new Coord { X = 1200, Y = 60 }, inputRead, outputWrite, 0, out console);
                    if (hr != 0) throw new Win32Exception(hr);
                }
                catch (EntryPointNotFoundException) { throw new NotSupportedException("Вход в окне требует Windows 10 версии 1809 или новее. Обновите Windows."); }
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) throw new Win32Exception();
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
                initialized = true;
                if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020016), console, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                var start = new StartupEx(); start.Info.Size = Marshal.SizeOf(typeof(StartupEx)); start.Attributes = attributes;
                // При запуске из перенаправленного host (включая CI) Windows иначе копирует его pipe handles.
                // Явные нулевые std handles + STARTF_USESTDHANDLES дают ConPTY назначить собственный TTY.
                start.Info.Flags = 0x00000100;
                ProcessInfo info;
                // Только фиксированная команда. Адрес, пароль и код передаются позже в терминальный канал.
                if (!CreateProcess(executable, new StringBuilder(ProcessRunner.Quote(executable) + " auth login --format json"), IntPtr.Zero, IntPtr.Zero,
                    false, 0x00080000, IntPtr.Zero, workingDirectory, ref start, out info)) throw new Win32Exception();
                process = info.Process; CloseHandle(info.Thread);
                input = new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write, 4096, false); inputWrite = IntPtr.Zero;
                reader = new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read, 4096, false); outputRead = IntPtr.Zero;
                readTask = Task.Run((Action)ReadOutput);
            }
            catch { Dispose(); throw; }
            finally
            {
                if (initialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                foreach (var handle in new[] { inputRead, inputWrite, outputRead, outputWrite }) if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }
        // Чтение не останавливается при отмене: ClosePseudoConsole требует дренировать выходной канал.
        void ReadOutput()
        {
            try
            {
                using (var text = new StreamReader(reader, Encoding.UTF8, false, 1024, true))
                {
                    var chars = new char[1024]; int count;
                    while ((count = text.Read(chars, 0, chars.Length)) > 0)
                    {
                        lock (gate)
                        {
                            if (disposed != 0 || overflow) continue;
                            if (output.Count >= 128) { overflow = true; output.Clear(); }
                            else output.Enqueue(new string(chars, 0, count));
                            available.Release();
                        }
                        Array.Clear(chars, 0, chars.Length);
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { lock (gate) { ended = true; available.Release(); } }
        }
        public async Task<string> ReadAsync(CancellationToken ct)
        {
            var exitDrain = new System.Diagnostics.Stopwatch();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (overflow) throw new IOException("Превышен безопасный размер ответа входа.");
                    if (output.Count > 0) return output.Dequeue();
                    if (ended) return null;
                }
                if (WaitForSingleObject(process, 0) == 0)
                {
                    if (!exitDrain.IsRunning) exitDrain.Start();
                    if (exitDrain.ElapsedMilliseconds >= 700) return null;
                }
                await available.WaitAsync(100, ct).ConfigureAwait(false);
            }
        }
        /// <summary>Короткоживущие массивы очищаются; plaintext-строка пароля и аргумент -p не создаются.</summary>
        public Task SendAsync(SecureString value, CancellationToken ct)
        {
            return SendValueAsync(value, true, ct);
        }
        /// <summary>Ввод без Enter для проверки TTY до сетевой авторизации; реальные учётные данные так не отправляются.</summary>
        public Task SendCharactersAsync(SecureString value, CancellationToken ct)
        {
            return SendValueAsync(value, false, ct);
        }
        Task SendValueAsync(SecureString value, bool enter, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                ct.ThrowIfCancellationRequested();
                IntPtr pointer = IntPtr.Zero; char[] chars = null; byte[] bytes = null;
                try
                {
                    if (value == null || value.Length == 0 || value.Length > 1024) throw new ArgumentException("Проверьте введённые данные.");
                    pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
                    chars = new char[value.Length + (enter ? 1 : 0)];
                    for (int i = 0; i < value.Length; i++)
                    {
                        chars[i] = (char)Marshal.ReadInt16(pointer, i * 2);
                        if (Char.IsControl(chars[i])) throw new ArgumentException("Ввод не должен содержать управляющие символы или переносы строк.");
                    }
                    if (enter) chars[value.Length] = '\r'; bytes = Encoding.UTF8.GetBytes(chars);
                    ct.ThrowIfCancellationRequested(); input.Write(bytes, 0, bytes.Length); input.Flush();
                }
                finally
                {
                    if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
                    if (chars != null) Array.Clear(chars, 0, chars.Length);
                    if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                }
            }, ct);
        }
        public int ExitCode
        {
            get { uint code; if (!GetExitCodeProcess(process, out code) || code == 259) throw new IOException("Процесс входа не завершён."); return (int)code; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (process != IntPtr.Zero)
            {
                if (WaitForSingleObject(process, 0) != 0) { TerminateProcess(process, 1); WaitForSingleObject(process, 2000); }
                CloseHandle(process); process = IntPtr.Zero;
            }
            if (input != null) input.Dispose();
            lock (gate) { output.Clear(); }
            var pc = console; console = IntPtr.Zero;
            // Сбои платформы не должны навечно блокировать окно. Читатель продолжает дренировать pipe.
            var close = Task.Run(delegate { if (pc != IntPtr.Zero) ClosePseudoConsole(pc); if (reader != null) reader.Dispose(); });
            close.Wait(3000);
            if (readTask != null) readTask.Wait(1000);
        }
        [StructLayout(LayoutKind.Sequential)] struct Coord { public short X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Startup
        {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public short Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct StartupEx { public Startup Info; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr security, int size);
        [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
        [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr console);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr result);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupEx start, out ProcessInfo process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle, uint code);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    }
}

// Infrastructure/ILoginProcess.cs
namespace IpaDesktop
{
    /// <summary>Изолированный интерактивный канал авторизации: вывод никогда не поступает в общий ProcessRunner.</summary>
    public interface ILoginProcess : IDisposable
    {
        Task<string> ReadAsync(CancellationToken ct);
        Task SendAsync(SecureString value, CancellationToken ct);
        int ExitCode { get; }
    }
    public interface ILoginProcessFactory
    {
        ILoginProcess Start(string executable, string workingDirectory);
    }
}

// Infrastructure/InstallationLayout.cs
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

// Infrastructure/InstallationPackage.cs
namespace IpaDesktop
{
    /// <summary>Даёт старому CLI ASCII-имя IPA, сохраняя Unicode-каталог через рабочую директорию процесса.</summary>
    public sealed class InstallationPackage : IDisposable
    {
        readonly string directory;
        public string WorkingDirectory { get; private set; }
        public string RelativePath { get; private set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateHardLink(string newName, string existingName, IntPtr securityAttributes);

        InstallationPackage(string source)
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(source));
            string name = ".install-" + Guid.NewGuid().ToString("N");
            directory = Path.Combine(WorkingDirectory, name);
            RelativePath = Path.Combine(name, "package.ipa");
        }

        /// <summary>Предпочитает жёсткую ссылку без копирования больших IPA; на других ФС копирует с отменой.</summary>
        public static Task<InstallationPackage> CreateAsync(string source, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                ct.ThrowIfCancellationRequested();
                var package = new InstallationPackage(source);
                try
                {
                    Directory.CreateDirectory(package.directory);
                    string target = Path.Combine(package.WorkingDirectory, package.RelativePath);
                    if (!CreateHardLink(target, Path.GetFullPath(source), IntPtr.Zero))
                    {
                        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[1024 * 1024];
                            int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, count);
                            }
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    return package;
                }
                catch { package.Dispose(); throw; }
            }, ct);
        }

        /// <summary>Удаляет только временное имя: исходный IPA остаётся после успеха, ошибки или отмены.</summary>
        public void Dispose()
        {
            try
            {
                File.Delete(Path.Combine(WorkingDirectory, RelativePath));
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

// Infrastructure/IProcessRunner.cs
namespace IpaDesktop
{
    /// <summary>Подменяемый контракт запуска процессов, журналирования и отмены для Windows и тестов.</summary>
    public interface IProcessRunner
    {
        Action<string> Log
        {
            get;
            set;
        }
        Process Active
        {
            get;
        }
        Task<Result> Run(string executable, string[] arguments, CancellationToken cancellation, bool visible, bool publish, string workingDirectory = null);
    }
}

// Infrastructure/JsonCodec.cs
namespace IpaDesktop
{
    /// <summary>Единая обёртка JSON-сериализации для совместимости с .NET Framework без внешних пакетов.</summary>
    public static class JsonCodec
    {
        static JavaScriptSerializer Serializer
        {
            get
            {
                return new JavaScriptSerializer
                {
                    MaxJsonLength = 16000000
                };
            }
        }
        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }
        public static T Deserialize<T> (string value)
        {
            return Serializer.Deserialize<T> (value);
        }
        public static object DeserializeObject(string value)
        {
            return Serializer.DeserializeObject(value);
        }
    }
}

// Infrastructure/NetworkClient.cs
namespace IpaDesktop
{
    /// <summary>Создаёт HTTP-клиенты с TLS и идентификатором приложения для запросов к источникам обновлений.</summary>
    public static class NetworkClient
    {
        public static readonly HttpClient Http = CreateHttp();
        /// <summary>Создаёт клиент с заданным временем ожидания и User-Agent текущей версии приложения.</summary>
        static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromMinutes(15);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/" + AppInfo.Version);
            return h;
        }
    }
}

// Infrastructure/ProcessRunner.cs
namespace IpaDesktop
{
    /// <summary>Запускает CLI вне UI-потока, ограничивает вывод и обеспечивает прерываемое ожидание процесса.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        readonly string Downloads;
        public Action<string> Log
        {
            get;
            set;
        }
        public Process Active
        {
            get;
            private set;
        }
        public ProcessRunner(string workingDirectory)
        {
            Downloads = workingDirectory;
            Log = delegate
            {
            };
        }
        // Windows CommandLineToArgvW-compatible quoting; never invoke cmd.exe.
        /// <summary>Экранирует один аргумент по правилам Windows, включая кавычки и завершающие обратные слеши.</summary>
        public static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            int n = 0;
            foreach (char c in s)
            {
                if (c == '\\')
                {
                    n++;
                    continue;
                }
                if (c == '\"')
                {
                    b.Append('\\', n * 2 + 1);
                    b.Append(c);
                } else
                {
                    b.Append('\\', n);
                    b.Append(c);
                }
                n = 0;
            }
            b.Append('\\', n * 2);
            return b.Append('"').ToString();
        }
        /// <summary>Запускает процесс в фоне; отмена завершает дочерний процесс, а ожидание и чтение вывода ограничены.</summary>
        public Task<Result> Run(string exe, string[] args, CancellationToken token, bool visible, bool publish, string workingDirectory = null)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                var si = new ProcessStartInfo(exe, String.Join(" ", args.Select(Quote)))
                {
                    WorkingDirectory = workingDirectory ?? Downloads, UseShellExecute = false, CreateNoWindow = !visible
                };
                si.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + Environment.GetEnvironmentVariable("PATH");
                if (!visible)
                {
                    si.RedirectStandardInput = true;
                    si.RedirectStandardOutput = true;
                    si.RedirectStandardError = true;
                    si.StandardOutputEncoding = Encoding.UTF8;
                    si.StandardErrorEncoding = Encoding.UTF8;
                }
                using (var p = new Process
                {
                    StartInfo = si
                })
                {
                    var output = new StringBuilder();
                    object gate = new object();
                    bool stdoutDone = visible, stderrDone = visible;
                    long lastOutput = Stopwatch.GetTimestamp();
                    string lastProgressMessage = null;
                    DataReceivedEventHandler stdout = delegate(object sender, DataReceivedEventArgs e)
                    {
                        lock (gate)
                        {
                            if (e.Data == null)
                            {
                                stdoutDone = true;
                                return;
                            }
                            lastOutput = Stopwatch.GetTimestamp();
                            if (e.Data.StartsWith("PROGRESS:", StringComparison.Ordinal))
                            {
                                if (e.Data == lastProgressMessage) return;
                                lastProgressMessage = e.Data;
                            }
                            int count = Math.Min(e.Data.Length, 15000000 - output.Length);
                            if (count > 0)
                            {
                                output.Append(e.Data, 0, count);
                                if (output.Length<15000000) output.AppendLine();
                            }
                        }
                        if (publish) Log(e.Data);
                    };
                    DataReceivedEventHandler stderr = delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null)
                        {
                            lock (gate)
                            {
                                stderrDone = true;
                            }
                            return;
                        }
                        stdout(sender, e);
                    };
                    if (!visible)
                    {
                        p.OutputDataReceived += stdout;
                        p.ErrorDataReceived += stderr;
                    }
                    var elapsed = Stopwatch.StartNew();
                    long nextNotice = 30;
                    try
                    {
                        p.Start();
                        Active = p;
                        if (!visible)
                        {
                            p.StandardInput.Close();
                            p.BeginOutputReadLine();
                            p.BeginErrorReadLine();
                        }
                        while (!p.WaitForExit(200))
                        {
                            if (token.IsCancellationRequested)
                            {
                                try
                                {
                                    p.Kill();
                                } catch (InvalidOperationException)
                                {
                                }
                                if (!p.WaitForExit(5000)) throw new IOException("Не удалось остановить процесс. Завершите его через Диспетчер задач.");
                                token.ThrowIfCancellationRequested();
                            }
                            if (publish && elapsed.Elapsed.TotalSeconds>= nextNotice)
                            {
                                long last;
                                lock (gate)
                                {
                                    last = lastOutput;
                                }
                                Log("Процесс работает " + (int) elapsed.Elapsed.TotalSeconds + " с; без нового вывода " + (int)((Stopwatch.GetTimestamp() - last) / (double) Stopwatch.Frequency) + " с. При необходимости нажмите «Отмена».");
                                nextNotice += 30;
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        // A child may keep a redirected pipe open after its parent exits.
                        // Never use an unbounded WaitForExit() to drain those pipes.
                        var drain = Stopwatch.StartNew();
                        while (drain.ElapsedMilliseconds < 2000)
                        {
                            lock (gate)
                            {
                                if (stdoutDone && stderrDone) break;
                            }
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(20);
                        }
                        lock (gate)
                        {
                            if (!stdoutDone || !stderrDone) Log("Процесс завершился, но поток вывода не закрылся. Ожидание потока прекращено.");
                            return new Result
                            {
                                Code = p.ExitCode, Output = output.ToString()
                            };
                        }
                    } finally
                    {
                        Active = null;
                    }
                }
            }, token);
        }
    }
}

// Infrastructure/SettingsStore.cs
namespace IpaDesktop
{
    /// <summary>Загружает state.json и сохраняет его атомарно; используется сервисами установки и отката.</summary>
    public sealed class SettingsStore
    {
        public string Root
        {
            get;
            private set;
        }
        public State Settings
        {
            get;
            set;
        }
        public SettingsStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "state.json");
            Settings = File.Exists(path) ? JsonCodec.Deserialize<State> (File.ReadAllText(path)) : new State();
            if (Settings == null) throw new IOException("Не удалось прочитать настройки.");
        }
        /// <summary>Атомарно сохраняет текущие настройки без частичного изменения существующего файла.</summary>
        public void Save()
        {
            AtomicFile.WriteUtf8(Path.Combine(Root, "state.json"), JsonCodec.Serialize(Settings));
        }
    }
}

// Infrastructure/ToolLocator.cs
namespace IpaDesktop
{
    /// <summary>Находит исполняемые файлы в выбранной версии движка или отдельном комплекте устройства.</summary>
    public sealed class ToolLocator
    {
        readonly SettingsStore store;
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
            var root = pattern.StartsWith("ipatool", StringComparison.OrdinalIgnoreCase) ? CurrentRoot : DeviceRoot;
            var files = Directory.GetFiles(root, pattern, SearchOption.AllDirectories).Where(x => x.Replace('\\', '/').Contains("/windows_amd64/")).ToArray();
            if (files.Length != 1) throw new Exception("Не найдена однозначная Windows-утилита: " + pattern);
            return files[0];
        }
    }
}

// Infrastructure/WindowsAppleSupportProbe.cs
namespace IpaDesktop
{
    /// <summary>Только читает службы и реестр Windows; не устанавливает драйверы и не меняет службы.</summary>
    public interface IAppleSupportProbe
    {
        AppleSupportSnapshot Read();
    }

    /// <summary>Только читает службы и реестр Windows; не устанавливает драйверы и не меняет службы.</summary>
    public sealed class WindowsAppleSupportProbe : IAppleSupportProbe
    {
        public AppleSupportSnapshot Read()
        {
            var result = new AppleSupportSnapshot();
            var details = new List<string>();
            ServiceController[] services = null;
            try
            {
                services = ServiceController.GetServices();
                result.ServiceScanCompleted = true;
                foreach (var service in services)
                {
                    if (!service.ServiceName.Equals("AppleMobileDeviceService", StringComparison.OrdinalIgnoreCase)
                        && !service.ServiceName.Equals("Apple Mobile Device Service", StringComparison.OrdinalIgnoreCase)
                        && !service.DisplayName.StartsWith("Apple Mobile Device", StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.ServiceFound = true;
                    details.Add("Служба: " + service.ServiceName);
                    result.ServiceRunning = service.Status == ServiceControllerStatus.Running;
                    result.ServiceStatusKnown = true;
                    details.Add("Состояние службы: " + service.Status);
                    break;
                }
            }
            catch (InvalidOperationException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (Win32Exception error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            catch (SecurityException error)
            {
                result.ServiceScanCompleted = false;
                details.Add("Проверка службы: " + error.Message);
            }
            finally
            {
                if (services != null)
                    foreach (var service in services) service.Dispose();
            }

            bool registryComplete = true;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var apple = root.OpenSubKey(@"SOFTWARE\Apple Inc.\Apple Mobile Device Support"))
                        {
                            if (apple != null)
                            {
                                result.InstallationDetected = true;
                                details.Add("Регистрация компонентов Apple: " + view);
                            }
                        }
                        using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var keyName in uninstall.GetSubKeyNames())
                            {
                                using (var product = uninstall.OpenSubKey(keyName))
                                {
                                    if (product == null) continue;
                                    string name = Convert.ToString(product.GetValue("DisplayName"));
                                    if (!name.Equals("Apple Mobile Device Support", StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    result.InstallationDetected = true;
                                    details.Add("Установленный компонент: " + name + " " + Convert.ToString(product.GetValue("DisplayVersion")));
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (SecurityException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
                catch (System.IO.IOException error)
                {
                    registryComplete = false;
                    details.Add("Проверка реестра: " + error.Message);
                }
            }
            result.RegistryScanCompleted = registryComplete;
            result.Details = String.Join(Environment.NewLine, details);
            return result;
        }
    }
}

// Services/AppCatalog.cs
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

// Services/AppCompatibility.cs
namespace IpaDesktop
{
    /// <summary>Блокирует только подтверждённую несовместимость; неизвестные сведения не подменяет догадками.</summary>
    public static class AppCompatibility
    {
        static Version Parse(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"^\d+(\.\d+){0,3}$")) return null;
            var parts = value.Split('.').ToList(); while (parts.Count < 4) parts.Add("0");
            Version result; return Version.TryParse(String.Join(".", parts), out result) ? result : null;
        }
        public static string BlockReason(IpaDetails app, string ios)
        {
            if (app.Platforms != null && app.Platforms.Length > 0 && !app.Platforms.Any(x => String.Equals(x, "iPhoneOS", StringComparison.OrdinalIgnoreCase)))
                return "Пакет предназначен для другой платформы: " + String.Join(", ", app.Platforms) + ". Для iPhone нужен iPhoneOS.";
            var minimum = Parse(app.MinimumOS); var current = Parse(ios);
            if (minimum != null && current != null && minimum > current)
                return "Приложению требуется iOS " + app.MinimumOS + ", на iPhone установлена " + ios + ". Выберите более старую версию приложения.";
            return null;
        }
    }
}

// Services/AppleLoginService.cs
namespace IpaDesktop
{
    public interface IAppleLoginService
    {
        Task<string> LoginAsync(string identifier, SecureString password, Func<CancellationToken, Task<SecureString>> requestCode, CancellationToken ct);
    }
    /// <summary>Только заранее определённые сообщения могут выйти из приватного канала авторизации.</summary>
    public sealed class AppleLoginException : Exception
    {
        public AppleLoginException(string message) : base(message) { }
    }
    public enum AppleLoginPrompt { None, Identifier, Password, Code }
    /// <summary>Ограниченный протокол интерактивных запросов; исходный вывод не отдаётся UI или журналу.</summary>
    public sealed class AppleLoginProtocol
    {
        string buffer = "";
        bool identifierSent, passwordSent, codeSent;
        public AppleLoginPrompt Append(string chunk)
        {
            if (buffer.Length + chunk.Length > 65536) throw new AppleLoginException("Неожиданный ответ ipatool. Обновите движок и повторите вход.");
            buffer += chunk;
            string text = Text();
            // ConPTY может повторно прислать всю строку при перерисовке, включая старые запросы.
            // Реагируем только на следующий этап; пароль/код повторно не отправляем.
            if (!identifierSent && text.Contains("Enter email:"))
            {
                identifierSent = true; buffer = ""; return AppleLoginPrompt.Identifier;
            }
            if (!passwordSent && text.Contains("Enter password:"))
            {
                if (!identifierSent) throw Changed();
                passwordSent = true; buffer = ""; return AppleLoginPrompt.Password;
            }
            if (!codeSent && text.Contains("Enter 2FA code:"))
            {
                if (!passwordSent) throw Changed();
                codeSent = true; buffer = ""; return AppleLoginPrompt.Code;
            }
            // Отбрасываем обработанный префикс перерисовки, сохраняя хвост и частичные ANSI-последовательности.
            // Иначе многократное эхо старого запроса раздувало бы ограниченный буфер.
            int consumed = 0;
            foreach (var prompt in new[] { "Enter email:", "Enter password:", "Enter 2FA code:" })
            {
                int index = text.LastIndexOf(prompt, StringComparison.Ordinal);
                if (index >= 0) consumed = Math.Max(consumed, index + prompt.Length);
            }
            if (consumed > 0) buffer = text.Substring(consumed);
            return AppleLoginPrompt.None;
        }
        static AppleLoginException Changed() { return new AppleLoginException("Не удалось распознать последовательность запросов входа. Закройте окно и повторите попытку. Если ошибка повторяется, проверьте обновления программы."); }
        string Text()
        {
            var text = Regex.Replace(buffer, "\u001b\\][^\u0007]*(?:\u0007|\u001b\\\\)", "");
            return Regex.Replace(text, "\u001b\\[[0-?]*[ -/]*[@-~]", "").Replace("\r", "");
        }
        public string Complete(int exitCode)
        {
            var text = Text();
            try
            {
                if (text.Contains("could not save account")) throw new AppleLoginException("Движок не смог сохранить сессию. Проверьте доступ к папке .ipatool в профиле Windows и повторите вход.");
                if (exitCode == 0 && passwordSent)
                {
                    try
                    {
                        var data = ToolResponse.Object(text);
                        var email = Convert.ToString(ToolResponse.Get(data, "email"));
                        if (Object.Equals(ToolResponse.Get(data, "success"), true) && !String.IsNullOrWhiteSpace(email)) return email;
                    }
                    catch (AppleLoginException) { throw; }
                    catch (Exception) { }
                }
                var lower = text.ToLowerInvariant();
                if (lower.Contains("locked") || lower.Contains("disabled")) throw new AppleLoginException("Apple сообщает об ограничении аккаунта. Проверьте его состояние на сайте Apple.");
                if (lower.Contains("resolve") || lower.Contains("connect") || lower.Contains("timeout") || lower.Contains("ssl") || lower.Contains("tls"))
                    throw new AppleLoginException("Не удалось связаться с Apple. Проверьте подключение, дату Windows и повторите вход позже.");
                if (lower.Contains("terms") || lower.Contains("agreement")) throw new AppleLoginException("Apple требует принять условия аккаунта. Откройте App Store на устройстве и завершите настройку аккаунта.");
                throw new AppleLoginException(codeSent ? "Apple не подтвердила вход с этим кодом. Проверьте пароль и повторите вход со свежим кодом." : "Вход не подтверждён. Проверьте Apple Account и обычный пароль аккаунта, затем повторите попытку.");
            }
            finally { Clear(); }
        }
        public void Clear() { buffer = ""; }
    }
    /// <summary>Авторизует штатным ipatool и затем отдельно проверяет сохранённую сессию. Без CLI-паролей и сырых ошибок.</summary>
    public sealed class AppleLoginService : IAppleLoginService
    {
        readonly ILoginProcessFactory factory;
        readonly Func<string> executable;
        readonly string directory;
        readonly IAppToolClient tools;
        public AppleLoginService(ILoginProcessFactory factory, Func<string> executable, string directory, IAppToolClient tools)
        { this.factory = factory; this.executable = executable; this.directory = directory; this.tools = tools; }
        public Task<string> LoginAsync(string identifier, SecureString password, Func<CancellationToken, Task<SecureString>> requestCode, CancellationToken ct)
        {
            return Task.Run(async delegate
            {
                if (String.IsNullOrWhiteSpace(identifier) || identifier.Length > 320 || password == null || password.Length == 0 || password.Length > 1024)
                    throw new AppleLoginException("Введите Apple Account и пароль.");
                foreach (char c in identifier) if (Char.IsControl(c)) throw new AppleLoginException("Apple Account не должен содержать переносов строк.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var token = timeout.Token; var protocol = new AppleLoginProtocol(); string email = null;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        using (var session = factory.Start(executable(), directory))
                        {
                            string chunk;
                            while ((chunk = await session.ReadAsync(token).ConfigureAwait(false)) != null)
                            {
                                var prompt = protocol.Append(chunk);
                                if (prompt == AppleLoginPrompt.Identifier)
                                {
                                    using (var value = new SecureString())
                                    {
                                        foreach (char c in identifier.Trim()) value.AppendChar(c);
                                        value.MakeReadOnly(); await session.SendAsync(value, token).ConfigureAwait(false);
                                    }
                                }
                                else if (prompt == AppleLoginPrompt.Password) await session.SendAsync(password, token).ConfigureAwait(false);
                                else if (prompt == AppleLoginPrompt.Code)
                                {
                                    using (var code = await requestCode(token).ConfigureAwait(false))
                                    {
                                        if (code == null) throw new OperationCanceledException(token);
                                        await session.SendAsync(code, token).ConfigureAwait(false);
                                    }
                                }
                            }
                            email = protocol.Complete(session.ExitCode);
                        }
                        // Не принимаем старую сессию за результат нового входа, даже если CLI напечатал success.
                        var info = await tools.RunAsync(new[] { "auth", "info" }, token, false).ConfigureAwait(false);
                        var saved = ToolResponse.Object(info.Output);
                        if (info.Code != 0 || !Object.Equals(ToolResponse.Get(saved, "success"), true) ||
                            !String.Equals(email, Convert.ToString(ToolResponse.Get(saved, "email")), StringComparison.OrdinalIgnoreCase))
                            throw new AppleLoginException("Новая сессия не подтверждена. Повторите вход; текущий аккаунт можно проверить основной кнопкой.");
                        return email;
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                        throw new AppleLoginException("Время входа истекло. Повторите попытку и подготовьте доверенное устройство для получения кода.");
                    }
                    catch (AppleLoginException) { throw; }
                    catch (NotSupportedException) { throw new AppleLoginException("Вход в окне требует Windows 10 версии 1809 или новее."); }
                    catch (Exception) { throw new AppleLoginException("Не удалось завершить вход. Проверьте установку ipatool и повторите попытку. Подробности авторизации не записываются в журнал."); }
                    finally { protocol.Clear(); }
                }
            }, ct);
        }
    }
}

// Services/AppleSupportService.cs
namespace IpaDesktop
{
    /// <summary>Преобразует снимок служб и реестра в понятный статус готовности компонентов Apple.</summary>
    public sealed class AppleSupportService
    {
        readonly IAppleSupportProbe probe;

        public AppleSupportService(IAppleSupportProbe probe)
        {
            if (probe == null) throw new ArgumentNullException("probe");
            this.probe = probe;
        }

        /// <summary>Выполняет чтение системного состояния вне UI-потока и возвращает оценку компонентов Apple.</summary>
        public Task<AppleSupportReport> CheckAsync(CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                cancellation.ThrowIfCancellationRequested();
                var snapshot = probe.Read();
                cancellation.ThrowIfCancellationRequested();
                return Evaluate(snapshot);
            }, cancellation);
        }

        /// <summary>Различает отсутствие драйверов, остановленную службу и невозможность достоверной проверки.</summary>
        public static AppleSupportReport Evaluate(AppleSupportSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            AppleSupportStatus status;
            string message;
            string guidance;
            if (snapshot.ServiceFound && snapshot.ServiceStatusKnown)
            {
                status = snapshot.ServiceRunning ? AppleSupportStatus.Running : AppleSupportStatus.Stopped;
                message = snapshot.ServiceRunning ? "Служба Apple запущена" : "Служба Apple остановлена";
                guidance = snapshot.ServiceRunning
                    ? "Служба Apple Mobile Device работает. Если iPhone не найден, проверьте USB, разблокировку и доверие."
                    : "Откройте services.msc, найдите Apple Mobile Device Service и запустите или перезапустите её. Для изменения состояния службы могут потребоваться права администратора.";
            }
            else if (snapshot.ServiceFound || !snapshot.ServiceScanCompleted || !snapshot.RegistryScanCompleted)
            {
                status = AppleSupportStatus.Unknown;
                message = "Статус компонентов Apple неизвестен";
                guidance = "Не удалось полностью проверить компоненты Apple. Подробности — ниже. Поиск iPhone можно выполнить вручную.";
            }
            else if (snapshot.InstallationDetected)
            {
                status = AppleSupportStatus.ServiceMissing;
                message = "Компоненты Apple найдены, служба отсутствует";
                guidance = "Найдена регистрация Apple Mobile Device Support, но служба отсутствует. Проверьте или восстановите установку ПО Apple, затем повторите проверку.";
            }
            else
            {
                status = AppleSupportStatus.NotDetected;
                message = "Apple Mobile Device Support не обнаружен";
                guidance = "Служба и регистрация Apple Mobile Device Support не найдены. Установите ПО для устройств с официального сайта Apple или Microsoft Store, затем повторите проверку.";
            }
            return new AppleSupportReport
            {
                Status = status,
                Message = message,
                Details = guidance + (String.IsNullOrWhiteSpace(snapshot.Details) ? "" : Environment.NewLine + snapshot.Details)
            };
        }
    }
}

// Services/ApplicationDownloads.cs
namespace IpaDesktop
{
    /// <summary>Загружает IPA под текущей сессией ipatool в уникальный файл, исключая перезапись чужой копии.</summary>
    public sealed class ApplicationDownloads
    {
        readonly IAppToolClient tools;
        readonly string folder;
        public Action<string> Transfer { get; set; }
        public ApplicationDownloads(IAppToolClient tools, string folder)
        {
            this.tools = tools;
            this.folder = folder;
            Transfer = delegate { };
        }
        /// <summary>Запускает скачивание с получением лицензии и отдельным назначением для каждого запроса.</summary>
        public async Task DownloadAsync(DownloadItem item, CancellationToken ct)
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
            if (item.VersionID != null) arguments.AddRange(new[]
            {
                "--external-version-id", item.VersionID
            });
            using (var monitor = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var observing = ObserveAsync(destination + ".tmp", monitor.Token);
                // C# 5 не поддерживает await в finally: сохраняем исходную ошибку и дожидаемся наблюдателя перед её повторным выбросом.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
                try { ToolResponse.Ensure(await tools.RunAsync(arguments.ToArray(), ct, true)); }
                catch (Exception error) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                monitor.Cancel();
                try { await observing.ConfigureAwait(false); } catch (OperationCanceledException) { }
                if (failure != null) failure.Throw();
            }
        }
        /// <summary>Раз в секунду читает только размер нашего временного IPA; не сканирует папку и не читает тело файла.</summary>
        async Task ObserveAsync(string path, CancellationToken ct)
        {
            var meter = new FileTransferMeter(); var time = Stopwatch.StartNew();
            while (true)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists) Transfer(meter.Sample(file.Length, time.Elapsed.TotalSeconds).Message);
                    else meter.Reset();
                }
                catch (IOException) { meter.Reset(); }
                catch (UnauthorizedAccessException) { meter.Reset(); }
            }
        }
    }
}

// Services/AppSetStore.cs
namespace IpaDesktop
{
    /// <summary>Хранит наборы отдельно от настроек; проверяет формат, объединяет ID и мигрирует прежние записи.</summary>
    public sealed class AppSetStore
    {
        readonly string file, folder;
        public AppSetStore(string root, string downloads)
        {
            file = Path.Combine(root, "AppSets.json");
            folder = Path.GetFullPath(downloads);
        }
        /// <summary>Читает ограниченный файл наборов и проверяет все записи до передачи в интерфейс.</summary>
        public List<AppSet> Load()
        {
            if (!File.Exists(file)) return new List<AppSet>();
            if (new FileInfo(file).Length > 1000000) throw new IOException("Файл наборов слишком велик.");
            var sets = JsonCodec.Deserialize<List<AppSet>>(File.ReadAllText(file, Encoding.UTF8));
            if (sets == null || sets.Count > 100) throw new IOException("Некорректный файл наборов.");
            foreach (var set in sets) AppSetRules.Validate(set);
            if (sets.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new IOException("Имена наборов повторяются.");
            return sets.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        /// <summary>Проверяет ограничения конфигурации и атомарно заменяет файл наборов.</summary>
        void Write(List<AppSet> sets)
        {
            if (sets.Count > 100) throw new IOException("Можно сохранить не более 100 наборов.");
            var text = JsonCodec.Serialize(sets);
            if (Encoding.UTF8.GetByteCount(text) > 1000000) throw new IOException("Файл наборов слишком велик.");
            AtomicFile.WriteUtf8(file, text);
        }
        /// <summary>Проверяет положительный числовой ID без знака, пробелов и управляющих символов.</summary>
        public static bool ValidId(string id) { return AppSetRules.ValidId(id); }
        /// <summary>Заменяет содержимое именованного набора после проверки; интерфейс дополнения использует AddApps.</summary>
        public void SaveApps(string name, IEnumerable<AppRow> apps)
        {
            var set = new AppSet { Name = (name ?? "").Trim(), Apps = AppSetRules.UniqueApps(apps) };
            AppSetRules.Validate(set);
            var sets = Load();
            sets.RemoveAll(x => String.Equals(x.Name, set.Name, StringComparison.OrdinalIgnoreCase));
            sets.Add(set);
            Write(sets);
        }
        /// <summary>Атомарно меняет имя и состав; конфликт имени не перезаписывает другой набор.</summary>
        public void Update(string originalName, string name, IEnumerable<AppRow> apps)
        {
            var updated = new AppSet { Name = (name ?? "").Trim(), Apps = AppSetRules.UniqueApps(apps) };
            AppSetRules.Validate(updated);
            var sets = Load();
            var original = sets.FirstOrDefault(x => String.Equals(x.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (original == null) throw new IOException("Набор уже удалён. Обновите список.");
            if (sets.Any(x => !Object.ReferenceEquals(x, original) && String.Equals(x.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Набор с таким именем уже существует.");
            sets.Remove(original); sets.Add(updated); Write(sets);
        }
        /// <summary>Дополняет именованный набор без повторов по ID, сохраняя ранее добавленные приложения.</summary>
        public void AddApps(string name, IEnumerable<AppRow> apps)
        {
            name = (name ?? "").Trim();
            var existing = Load().FirstOrDefault(x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var combined = existing == null ? new List<AppRow>() : ResolveApps(existing).ToList();
            combined.AddRange(apps);
            SaveApps(name, combined);
        }
        /// <summary>Совместимый API извлечения ID из IPA; в интерфейсе создание наборов выполняется из поиска.</summary>
        public void Save(string name, IEnumerable<LocalRow> rows)
        {
            SaveApps(name, rows.Select(row => FromFile(Path.GetFileName(row.Path), row.Path)).ToArray());
        }
        /// <summary>Проверяет принадлежность IPA папке загрузок и читает его ID, не угадывая по имени файла.</summary>
        AppRow FromFile(string name, string path)
        {
            path = Path.GetFullPath(path);
            if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("IPA отсутствует в папке приложения: " + name + ". Создайте набор из результатов поиска.");
            var details = IpaMetadata.ReadDetails(path);
            if (!ValidId(details.AppStoreID)) throw new IOException("В IPA нет App Store ID: " + name + ". Создайте набор из результатов поиска.");
            return new AppRow { ID = details.AppStoreID, Name = details.AppName ?? ("Приложение " + details.AppStoreID) };
        }
        /// <summary>Возвращает копии ID; для прежнего формата извлекает их только из метаданных исходных IPA.</summary>
        public AppRow[] ResolveApps(AppSet set)
        {
            AppSetRules.Validate(set);
            if (set.Apps != null) return set.Apps.Select(x => new AppRow { ID = x.ID, Name = x.Name }).ToArray();
            // Convert only from IPA metadata, never infer IDs from filenames or reuse their account.
            return set.Files.Select(name => FromFile(name, Path.Combine(folder, name))).GroupBy(x => x.ID).Select(g => g.First()).ToArray();
        }
        /// <summary>Объединяет наборы по ID. Миграция записывается один раз, только после проверки всех наборов.</summary>
        public AppRow[] ResolveMany(IEnumerable<AppSet> selected)
        {
            var requested = selected.ToArray();
            if (requested.Length == 0) throw new IOException("Выберите хотя бы один набор.");
            var resolved = requested.Select(x => new AppSet { Name = x.Name, Apps = ResolveApps(x) }).ToArray();
            var apps = AppSetRules.UniqueApps(resolved.SelectMany(x => x.Apps));
            if (requested.Any(x => x.Apps == null))
            {
                var saved = Load();
                foreach (var converted in resolved)
                {
                    saved.RemoveAll(x => String.Equals(x.Name, converted.Name, StringComparison.OrdinalIgnoreCase));
                    saved.Add(converted);
                }
                Write(saved);
            }
            return apps;
        }
        /// <summary>Удаляет именованный набор, не затрагивая загруженные IPA.</summary>
        public void Delete(string name)
        {
            DeleteMany(new[] { name });
        }
        /// <summary>Удаляет только конфигурацию выбранных наборов одной атомарной записью.</summary>
        public void DeleteMany(IEnumerable<string> names)
        {
            var selected = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var sets = Load();
            sets.RemoveAll(x => selected.Contains(x.Name));
            Write(sets);
        }
    }
}

// Services/AppToolClient.cs
namespace IpaDesktop
{
    /// <summary>Адаптер ipatool: находит бинарник, добавляет JSON-формат и делегирует запуск процессному слою.</summary>
    public sealed class AppToolClient : IAppToolClient
    {
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        public AppToolClient(IProcessRunner runner, ToolLocator locator)
        {
            this.runner = runner;
            this.locator = locator;
        }
        /// <summary>Добавляет параметры протокола CLI и выполняет команду через подменяемый процессный адаптер.</summary>
        public Task<Result> RunAsync(string[] args, CancellationToken ct, bool publish)
        {
            return Task.Run(async delegate
            {
                return await runner.Run(locator.Find("ipatool*.exe"), args.Concat(new[]
                {
                    "--format", "json"
                }).ToArray(), ct, false, publish);
            }, ct);
        }
    }
}

// Services/AppVersions.cs
namespace IpaDesktop
{
    /// <summary>Получает список быстро, а дорогие сведения — только для выбранных пользователем версий.</summary>
    public sealed class AppVersions
    {
        readonly IAppToolClient tool;
        public AppVersions(IAppToolClient tool) { this.tool = tool; }
        public static List<string> ParseManual(string text)
        {
            var ids = (text ?? "").Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            if (ids.Count == 0 || ids.Count > 50 || ids.Any(x => !AppSetStore.ValidId(x))) throw new ArgumentException("Введите от 1 до 50 числовых ID версий через запятую или пробел (до 20 цифр каждый).");
            return ids;
        }
        public async Task<List<AppVersionRow>> ListAsync(string appId, CancellationToken ct)
        {
            if (!AppSetStore.ValidId(appId)) throw new ArgumentException("Некорректный App Store ID.");
            var result = await tool.RunAsync(new[] { "list-versions", "-i", appId }, ct, false);
            ToolResponse.Ensure(result);
            return ToolResponse.VersionIds(result.Output).Distinct().Reverse().Select(x => new AppVersionRow { ID = x }).ToList();
        }
        public async Task ReadAsync(string appId, AppVersionRow row, CancellationToken ct)
        {
            if (!AppSetStore.ValidId(appId) || !AppSetStore.ValidId(row.ID)) throw new ArgumentException("Некорректный ID.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var result = await tool.RunAsync(new[] { "get-version-metadata", "-i", appId, "--external-version-id", row.ID }, timeout.Token, false);
                    ToolResponse.Ensure(result); var data = ToolResponse.Object(result.Output);
                    row.Version = ToolResponse.Get(data, "displayVersion") as string ?? "не указана";
                    row.MinimumOS = ToolResponse.Get(data, "minimumOSVersion") as string ?? "не указана";
                    var platforms = ToolResponse.Get(data, "supportedPlatforms") as object[];
                    row.Platforms = platforms == null ? "не указаны" : String.Join(", ", platforms.OfType<string>().Where(x => !String.IsNullOrWhiteSpace(x)).Distinct());
                    if (String.IsNullOrWhiteSpace(row.Platforms)) row.Platforms = "не указаны";
                    row.State = "Готово";
                }
                catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw new System.IO.IOException("Сведения не получены за 30 секунд. Можно повторить."); }
            }
        }
    }
}

// Services/BatchRunner.cs
namespace IpaDesktop
{
    /// <summary>Итог пакета: успешные операции и элементы с ошибками; отмена не считается ошибкой элемента.</summary>
    public sealed class BatchResult<T>
    {
        public int Succeeded { get; internal set; }
        public List<T> Failed { get; private set; }
        public BatchResult() { Failed = new List<T>(); }
    }
    /// <summary>Последовательно выполняет пакет, продолжая работу после ошибки отдельного приложения.</summary>
    public static class BatchRunner
    {
        /// <summary>Обработчики вызываются в исходном контексте UI. Долгая операция обязана сама быть асинхронной.</summary>
        public static async Task<BatchResult<T>> Run<T>(IList<T> items, Func<T, CancellationToken, Task> operation,
            Action<T, int> started, Action<T> succeeded, Action<T, Exception> failed, Action refreshed, CancellationToken ct)
        {
            var result = new BatchResult<T>();
            for (int index = 0; index < items.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[index];
                started(item, index + 1);
                Exception error = null;
                try { await operation(item, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { error = e; }
                // Ошибки отображения не выдаются за ошибки скачивания или установки.
                if (error == null) { succeeded(item); result.Succeeded++; }
                else { result.Failed.Add(item); failed(item, error); }
                if (refreshed != null) refreshed();
            }
            return result;
        }
    }
}

// Services/CompletionSound.cs
namespace IpaDesktop
{
    /// <summary>Один системный сигнал после полностью успешной очереди; звук не влияет на результат операции.</summary>
    public sealed class CompletionSound
    {
        readonly Action play;
        public CompletionSound() : this(delegate { SystemSounds.Asterisk.Play(); }) { }
        public CompletionSound(Action play) { this.play = play; }

        /// <summary>Пустые, частично неудачные и отключённые уведомления остаются без звука.</summary>
        public void Notify(bool enabled, int succeeded, int failed)
        {
            if (!enabled || succeeded <= 0 || failed != 0) return;
            try { play(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
}

// Services/DeviceCommandClient.cs
namespace IpaDesktop
{
    /// <summary>Единая обвязка CLI: транспорт, ожидание ответа, идентификация устройства и запуск без скрытых повторов.</summary>
    public sealed class DeviceCommandClient
    {
        readonly IProcessRunner runner; readonly ToolLocator locator;
        public DeviceCommandClient(IProcessRunner runner, ToolLocator locator) { this.runner = runner; this.locator = locator; }
        public Task<Result> ListAsync(bool network, CancellationToken ct) { return Probe(locator.Find("idevice_id*.exe"), new[] { network ? "-n" : "-l" }, ct); }
        public Task<Result> NameAsync(DeviceRow device, CancellationToken ct, int seconds = 5) { return Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", device.ID, "-k", "DeviceName"), ct, seconds); }
        public async Task InstallAsync(DeviceRow device, InstallationPackage package, CancellationToken ct)
        {
            ToolResponse.Ensure(await runner.Run(locator.Find("ideviceinstaller*.exe"), Arguments(device, "--udid", device.ID, "install", package.RelativePath), ct, false, true, package.WorkingDirectory));
        }
        public static string[] Arguments(DeviceRow device, params string[] args)
        {
            if (device == null || !System.Text.RegularExpressions.Regex.IsMatch(device.ID ?? "", "^[0-9A-Fa-f-]{20,}$")) throw new IOException("Некорректный идентификатор iPhone.");
            return device.Network ? new[] { "--network" }.Concat(args).ToArray() : args;
        }
        async Task<Result> Probe(string exe, string[] args, CancellationToken ct, int seconds = 15)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                try { var result = await runner.Run(exe, args, timeout.Token, false, false); ToolResponse.Ensure(result); return result; }
                catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new IOException("iPhone не ответил за " + seconds + " секунд. Разблокируйте телефон; проверьте USB либо Wi-Fi и доверие компьютеру.");
                }
            }
        }
        public async Task<Dictionary<string, string>> ReadDeviceAsync(DeviceRow device, CancellationToken ct)
        {
            var result = await Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", device.ID), ct);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in result.Output.Split('\n'))
            {
                int split = line.IndexOf(':');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            string actual;
            if (values.TryGetValue("UniqueDeviceID", out actual) && !String.Equals(actual, device.ID, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ответил другой iPhone. Повторите поиск устройств.");
            if (device.Network && (!values.TryGetValue("ProductType", out actual) || !actual.StartsWith("iPhone", StringComparison.Ordinal)))
                throw new IOException("Не удалось подтвердить связь с выбранным iPhone по Wi-Fi. Разблокируйте телефон и проверьте сетевую синхронизацию.");
            return values;
        }
    }
}

// Services/DeviceDiscovery.cs
namespace IpaDesktop
{
    /// <summary>Обнаружение USB и Wi-Fi сохраняет независимые результаты и не выполняет параллельные операции с одним runner.</summary>
    public sealed class DeviceDiscovery
    {
        readonly DeviceCommandClient client;
        public Action<string> Log { get; set; }
        public DeviceDiscovery(DeviceCommandClient client) { this.client = client; Log = delegate { }; }
        public Task<List<DeviceRow>> FindAsync(CancellationToken ct) { return FindAsync(false, ct); }
        public async Task<List<DeviceRow>> FindAsync(bool includeWifi, CancellationToken ct)
        {
            var devices = new List<DeviceRow>(); bool usbFailed = false;
            try { devices = await FindTransport(false, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { if (!includeWifi) throw; usbFailed = true; Log("Поиск USB: " + e.Message); }
            if (includeWifi)
            {
                try { devices.AddRange(await FindTransport(true, ct)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { if (usbFailed) throw new IOException("Не удалось проверить USB и Wi-Fi. Проверьте службу Apple и соединение.\n" + e.Message, e); Log("Поиск Wi-Fi: " + e.Message + ". USB-устройства сохранены. Проверьте сетевую синхронизацию в Apple Devices/iTunes."); }
            }
            // Сначала получаем все UDID, затем имена. Не позволяем множеству спящих Wi-Fi-устройств задержать поиск на минуты.
            var names = Stopwatch.StartNew(); bool omitted = false;
            foreach (var device in devices)
            {
                ct.ThrowIfCancellationRequested();
                int remaining = (int)Math.Floor(15 - names.Elapsed.TotalSeconds);
                if (remaining < 1) { omitted = true; continue; }
                try
                {
                    var info = await client.NameAsync(device, ct, Math.Min(5, remaining));
                    string value = info.Output.Trim();
                    if (value.StartsWith("DeviceName:", StringComparison.Ordinal)) value = value.Substring(11).Trim();
                    if (value.Length > 0 && value.Length <= 200 && !value.Any(Char.IsControl)) device.Name = value;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Log("Имя iPhone (" + device.Connection + ") не получено: " + e.Message); }
            }
            if (omitted) Log("Для части устройств показан короткий идентификатор: общий лимит получения имён 15 секунд. Поиск устройств завершён.");
            foreach (var group in devices.GroupBy(x => x.Name + "|" + x.Connection).Where(g => g.Count() > 1))
                foreach (var device in group) device.Name += " · " + device.ID.Substring(Math.Max(0, device.ID.Length - 6));
            return devices;
        }
        async Task<List<DeviceRow>> FindTransport(bool network, CancellationToken ct)
        {
            var result = await client.ListAsync(network, ct);
            var ids = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[0-9A-Fa-f-]{20,}$")).Distinct().ToList();
            var devices = new List<DeviceRow>();
            foreach (var id in ids) devices.Add(new DeviceRow { ID = id, Network = network, Name = "iPhone · " + id.Substring(Math.Max(0, id.Length - 6)) });
            return devices;
        }
    }
}

// Services/DeviceInstallation.cs
namespace IpaDesktop
{
    /// <summary>Проверяет устройство и исходный IPA до копирования; затем повторно проверяет фактически отправляемый пакет.</summary>
    public sealed class DeviceInstallation
    {
        readonly DeviceCommandClient client;
        public Action<string> Log { get; set; }
        public DeviceInstallation(DeviceCommandClient client) { this.client = client; Log = delegate { }; }
        public async Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct)
        {
            if (!File.Exists(file.Path)) throw new IOException("IPA-файл больше не существует.");
            try
            {
                var source = await Task.Run(() => IpaMetadata.ReadDetails(file.Path), ct);
                string platformError = AppCompatibility.BlockReason(source, null);
                if (platformError != null) throw new IOException(platformError);
                var info = await client.ReadDeviceAsync(device, ct);
                string ios; info.TryGetValue("ProductVersion", out ios);
                string reason = AppCompatibility.BlockReason(source, ios);
                if (reason != null) throw new IOException(reason);
                using (var package = await InstallationPackage.CreateAsync(file.Path, ct))
                {
                    var details = await Task.Run(() => IpaMetadata.ReadDetails(Path.Combine(package.WorkingDirectory, package.RelativePath)), ct);
                    reason = AppCompatibility.BlockReason(details, ios);
                    if (reason != null) throw new IOException(reason);
                    if (String.IsNullOrEmpty(details.MinimumOS) || details.Platforms == null || details.Platforms.Length == 0 || String.IsNullOrEmpty(ios))
                        Log("Совместимость " + file.DisplayName + ": часть метаданных недоступна; окончательную проверку выполнит iPhone.");
                    ct.ThrowIfCancellationRequested();
                    await client.InstallAsync(device, package, ct);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!device.Network) throw;
                throw new IOException("Установка по Wi-Fi не завершена. Проверьте связь и состояние приложения на iPhone. Разблокируйте телефон, проверьте общую сеть и сетевую синхронизацию; при необходимости выберите USB и повторите только ошибки.\n" + e.Message, e);
            }
        }
    }
}

// Services/DeviceService.cs
namespace IpaDesktop
{
    /// <summary>Фасад инструментов iPhone. Обнаружение, команды и установка разделены и доступны для независимых проверок.</summary>
    public sealed class DeviceService
    {
        readonly DeviceCommandClient client; readonly DeviceDiscovery discovery; readonly DeviceInstallation installation;
        Action<string> log;
        public Action<string> Log
        {
            get { return log; }
            set { log = value ?? delegate { }; discovery.Log = log; installation.Log = log; }
        }
        public DeviceService(IProcessRunner runner, ToolLocator locator)
        {
            client = new DeviceCommandClient(runner, locator); discovery = new DeviceDiscovery(client); installation = new DeviceInstallation(client); Log = delegate { };
        }
        public Task<List<DeviceRow>> FindAsync(CancellationToken ct) { return discovery.FindAsync(ct); }
        public Task<List<DeviceRow>> FindAsync(bool includeWifi, CancellationToken ct) { return discovery.FindAsync(includeWifi, ct); }
        public Task<Dictionary<string, string>> ReadDeviceAsync(DeviceRow device, CancellationToken ct) { return client.ReadDeviceAsync(device, ct); }
        public Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct) { return installation.InstallAsync(device, file, ct); }
        public static string[] Arguments(DeviceRow device, params string[] args) { return DeviceCommandClient.Arguments(device, args); }
        public async Task<string> DetailsAsync(DeviceRow device, CancellationToken ct)
        {
            var values = await client.ReadDeviceAsync(device, ct);
            string model, version;
            if (!values.TryGetValue("ProductType", out model)) model = "не удалось прочитать";
            if (!values.TryGetValue("ProductVersion", out version)) version = "не удалось прочитать";
            return "Имя: " + device.Name + Environment.NewLine + "Подключение: " + device.Connection + Environment.NewLine +
                "Модель (ProductType): " + model + Environment.NewLine + "iOS: " + version + Environment.NewLine + "UDID: " + device.ID;
        }
    }
}

// Services/DiagnosticReport.cs
namespace IpaDesktop
{
    /// <summary>Создаёт диагностический отчёт и удаляет известные секреты, почту и идентификаторы до записи.</summary>
    public static class DiagnosticReport
    {
        static string Replace(string text, string pattern, string replacement)
        {
            return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        /// <summary>Очищает текст по структурным шаблонам и известным приватным значениям до экспорта.</summary>
        public static string Redact(string text, IEnumerable<string> privateValues)
        {
            text = text ?? "";
            if (text.Length > 1000000) text = text.Substring(text.Length - 1000000);
            foreach (var value in (privateValues ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x) && x.Length >= 3).Distinct().OrderByDescending(x => x.Length)) text = Replace(text, Regex.Escape(value), "[СКРЫТО]");
            text = Replace(text, @"(?m)^.*(?:Authorization|Cookie|Set-Cookie|X-Apple-[A-Za-z-]+)\s*[:=].*$", "[СКРЫТ ЗАГОЛОВОК]");
            text = Replace(text, @"(""(?:password[^""\r\n]*|token|access[_-]?token|refresh[_-]?token|auth[_-]?code|verification[_-]?code|passphrase|keychain-passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid|cookie|authorization)""\s*:\s*)(?:""(?:\\.|[^""\\])*""|[^,}\r\n]+)", "$1\"[СКРЫТО]\"");
            text = Replace(text, @"(<key>\s*(?:password[^<]*|token|access[_-]?token|refresh[_-]?token|auth[_-]?code|passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid|cookie)\s*</key>\s*<(?:string|data|integer)>)[\s\S]*?(</(?:string|data|integer)>)", "$1[СКРЫТО]$2");
            text = Replace(text, @"(?<!\w)(--(?:password|auth-code|keychain-passphrase|email|token|udid)|-p|-e|-u)(?:=|\s+)(?:""(?:\\.|[^""\\])*""|'[^']*'|\S+)", "$1 [СКРЫТО]");
            text = Replace(text, @"\b(password(?:Token)?|access[_-]?token|refresh[_-]?token|token|auth[_ -]?code|verification[_ -]?code|passphrase|keychain-passphrase|secret|kbsync|dsid|directoryServicesID|hardwareID|udid|guid)\s*[:=]\s*[^\r\n]+", "$1=[СКРЫТО]");
            text = Replace(text, @"[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.[\p{L}]{2,}", "[ПОЧТА]");
            text = Replace(text, @"\b[A-Z]:\\Users\\[^\\\r\n""<>]+", @"C:\Users\[ПОЛЬЗОВАТЕЛЬ]");
            text = Replace(text, @"/(?:Users|home)/[^/\s""']+", "/home/[ПОЛЬЗОВАТЕЛЬ]");
            text = Replace(text, @"\b[0-9a-f]{8}-[0-9a-f]{16}\b|\b[0-9a-f]{40}\b|\b(?:[0-9a-f]{2}:){5}[0-9a-f]{2}\b", "[ИДЕНТИФИКАТОР]");
            text = Replace(text, @"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", "[ТОКЕН]");
            return text;
        }
        /// <summary>Формирует сводку версий и журнал, затем очищает весь отчёт от чувствительных данных.</summary>
        public static string Create(string journal, string engineVersion, string toolsVersion, IEnumerable<string> privateValues)
        {
            var header = "IPA Desktop " + GuiUpdater.CurrentVersion + Environment.NewLine + "Windows: " + Environment.OSVersion.Version + Environment.NewLine + "ipatool: " + engineVersion + Environment.NewLine + "Device tools: " + toolsVersion + Environment.NewLine + "Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Environment.NewLine;
            return Redact(header + journal, privateValues);
        }
    }
}

// Services/Engine.cs
namespace IpaDesktop
{
    /// <summary>Фасад и точка сборки сервисов; сохраняет совместимый API для portable-сборок и проверок.</summary>
    public sealed class Engine
    {
        readonly SettingsStore settings;
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        readonly IAppToolClient tools;
        readonly AppCatalog catalog;
        readonly EngineUpdater updater;
        public string Root
        {
            get
            {
                return settings.Root;
            }
        }
        public string Downloads
        {
            get;
            private set;
        }
        public AppVersions AppVersions { get; private set; }
        public IAppleLoginService AppleLogin { get; set; }
        public State Settings
        {
            get
            {
                return settings.Settings;
            }
            set
            {
                settings.Settings = value;
            }
        }
        public Process Active
        {
            get
            {
                return runner.Active;
            }
        }
        public AppSetStore AppSets { get; private set; }
        public AppleSupportService AppleSupport { get; private set; }
        public DeviceService Devices
        {
            get;
            private set;
        }
        public LocalAppRepository LocalFiles
        {
            get;
            private set;
        }
        public ApplicationDownloads ApplicationDownloads
        {
            get;
            private set;
        }
        public Action<string> Log
        {
            get
            {
                return runner.Log;
            }
            set
            {
                runner.Log = value;
                catalog.Log = value;
                updater.Log = value;
                Devices.Log = value;
                ApplicationDownloads.Transfer = value;
            }
        }
        public Action<double> Progress
        {
            get
            {
                return updater.Progress;
            }
            set
            {
                updater.Progress = value;
            }
        }
        public Engine(string root) : this(root, null)
        {
        }
        public Engine(string root, IProcessRunner processRunner) : this(root, processRunner, new WindowsAppleSupportProbe())
        {
        }
        public Engine(string root, IProcessRunner processRunner, IAppleSupportProbe appleSupportProbe)
        {
            settings = new SettingsStore(root);
            Downloads = Path.Combine(root, "Apps");
            Directory.CreateDirectory(Downloads);
            runner = processRunner ?? new ProcessRunner(Downloads);
            locator = new ToolLocator(settings);
            tools = new AppToolClient(runner, locator);
            AppleLogin = new AppleLoginService(new ConPtyLoginFactory(), delegate { return locator.Find("ipatool*.exe"); }, Downloads, tools);
            AppVersions = new AppVersions(tools);
            catalog = new AppCatalog(settings, locator);
            updater = new EngineUpdater(settings, runner);
            AppleSupport = new AppleSupportService(appleSupportProbe);
            Devices = new DeviceService(runner, locator);
            LocalFiles = new LocalAppRepository(Downloads);
            AppSets = new AppSetStore(root, Downloads);
            ApplicationDownloads = new ApplicationDownloads(tools, Downloads);
        }
        public void Save()
        {
            settings.Save();
        }
        public string CurrentRoot
        {
            get
            {
                return locator.CurrentRoot;
            }
        }
        public string Binary(string pattern)
        {
            return locator.Find(pattern);
        }
        public Task<Result> Run(string exe, string[] args, CancellationToken ct, bool visible, bool publish)
        {
            return runner.Run(exe, args, ct, visible, publish);
        }
        public Task<Result> Tool(string[] args, CancellationToken ct, bool publish)
        {
            return tools.RunAsync(args, ct, publish);
        }
        public Task<List<AppRow>> CatalogSearch(string query, CancellationToken ct)
        {
            return catalog.SearchAsync(query, ct);
        }
        public Task<Release> Check(CancellationToken ct)
        {
            return updater.Check(ct);
        }
        public Task Install(Release release, CancellationToken ct)
        {
            return updater.Install(release, ct);
        }
        /// <summary>Получает опубликованные релизы комплекта устройства независимо от последнего GUI-релиза.</summary>
        public Task<Release> CheckDeviceTools(CancellationToken ct) { return updater.CheckDeviceTools(ct); }
        /// <summary>Сопоставляет источник, тег и наличие бинарников, а не только номер версии.</summary>
        public bool DeviceToolsCurrent(Release release) { return updater.DeviceToolsCurrent(release); }
        /// <summary>Устанавливает комплект в новую папку, проверяет CLI и сохраняет настройки после успеха.</summary>
        public Task InstallDeviceTools(Release release, CancellationToken ct) { return updater.InstallDeviceTools(release, ct); }
        /// <summary>Меняет местами текущий и предыдущий комплект устройства, сохраняя состояние ipatool.</summary>
        public void RollbackDeviceTools() { updater.RollbackDeviceTools(); }
        /// <summary>Возвращает предыдущую версию движка и сохраняет независимые пользовательские настройки.</summary>
        public void Rollback()
        {
            updater.Rollback();
        }
        // Compatibility API for portable builds and existing verification scripts.
        /// <summary>Экранирует один аргумент по правилам Windows, включая кавычки и завершающие обратные слеши.</summary>
        public static string Quote(string text)
        {
            return ProcessRunner.Quote(text);
        }
        public static void Ensure(Result result)
        {
            ToolResponse.Ensure(result);
        }
        public static Dictionary<string, object> Object(string output)
        {
            return ToolResponse.Object(output);
        }
        public static object Get(Dictionary<string, object> data, string key)
        {
            return ToolResponse.Get(data, key);
        }
        /// <summary>Извлекает доступные идентификаторы версий без подстановки неподтверждённых значений.</summary>
        public static List<string> VersionIds(string output)
        {
            return ToolResponse.VersionIds(output);
        }
        /// <summary>Извлекает приложения из JSON-вывода ipatool.</summary>
        public static List<AppRow> ParseApps(string output)
        {
            return ToolResponse.ParseApps(output);
        }
        /// <summary>Разбирает строки каталога и отбрасывает некорректные записи.</summary>
        public static List<AppRow> ParseCatalog(string text)
        {
            return ToolResponse.ParseCatalog(text);
        }
        /// <summary>Фильтрует объединённые каталоги по пользовательскому запросу.</summary>
        public static List<AppRow> MatchCatalog(string query, List<AppRow> custom, List<AppRow> catalog)
        {
            return ToolResponse.MatchCatalog(query, custom, catalog);
        }
        /// <summary>Объединяет источники по App Store ID, исключая повторяющиеся приложения.</summary>
        public static List<AppRow> MergeApps(IEnumerable<AppRow> local, IEnumerable<AppRow> online)
        {
            return ToolResponse.MergeApps(local, online);
        }
        public static void Extract(string zip, string destination)
        {
            EngineUpdater.Extract(zip, destination);
        }
    }
}

// Services/EngineUpdater.cs
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

// Services/IAppToolClient.cs
namespace IpaDesktop
{
    /// <summary>Подменяемый контракт команд ipatool; тесты выполняются без Apple Account и сетевого входа.</summary>
    public interface IAppToolClient
    {
        /// <summary>Добавляет параметры протокола CLI и выполняет команду через подменяемый процессный адаптер.</summary>
        Task<Result> RunAsync(string[] arguments, CancellationToken cancellation, bool publish);
    }
}

// Services/LocalAppRepository.cs
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

// Services/StorageAnalysis.cs
namespace IpaDesktop
{
    /// <summary>Считает размеры и отмечает старые числовые версии только внутри одного приложения и аккаунта.</summary>
    public static class StorageAnalysis
    {
        /// <summary>Форматирует размер в удобные единицы без изменения исходного числа байт.</summary>
        public static string Bytes(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("N2") + " ГБ";
            return (bytes / 1048576.0).ToString("N1") + " МБ";
        }
        static Version Parse(string text)
        {
            Version value;
            if (!Version.TryParse(text, out value)) return null;
            return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        }
        /// <summary>Помечает старые версии только при известных bundle ID, аккаунте и числовой версии.</summary>
        public static void Mark(IEnumerable<LocalRow> rows)
        {
            var list = rows.ToList();
            foreach (var row in list) { row.IsOld = false; row.StorageStatus = "Версия не определена"; }
            var known = list.Where(x => !String.IsNullOrWhiteSpace(x.BundleID) && x.AppleAccount != null && x.AppleAccount.Contains("@") && Parse(x.Version) != null);
            foreach (var group in known.GroupBy(x => x.BundleID.ToLowerInvariant() + "\n" + x.AppleAccount.ToLowerInvariant()))
            {
                var newest = group.Select(x => Parse(x.Version)).Max();
                foreach (var row in group)
                {
                    row.IsOld = Parse(row.Version).CompareTo(newest) < 0;
                    row.StorageStatus = row.IsOld ? "Старая версия" : "Самая новая в папке";
                }
            }
        }
    }
}

// Services/ToolResponse.cs
namespace IpaDesktop
{
    /// <summary>Разбирает ответы CLI и каталоги, проверяет ошибки и объединяет приложения по ID.</summary>
    public static class ToolResponse
    {
        public static void Ensure(Result r)
        {
            if (r.Code != 0) throw new Exception("Код ошибки " + r.Code + "\n" + (r.Output.Length > 8000 ? r.Output.Substring(r.Output.Length - 8000) : r.Output));
        }
        public static Dictionary<string, object> Object(string output)
        {
            // Structured output is sometimes preceded by a human-readable diagnostic.
            foreach (var line in output.Split('\n').Reverse())
            {
                try
                {
                    var d = JsonCodec.DeserializeObject(line.Trim()) as Dictionary<string, object>;
                    if (d != null) return d;
                } catch (ArgumentException)
                {
                }
            }
            try
            {
                return JsonCodec.DeserializeObject(output.Trim()) as Dictionary<string, object> ?? new Dictionary<string, object>();
            } catch (Exception)
            {
                throw new Exception("Формат ответа движка изменился. См. журнал или выполните откат.");
            }
        }
        /// <summary>Необязательные поля ipatool 1.1.9/1.2.0: отсутствие данных не означает совместимость с любой ОС.</summary>
        public static string VersionMetadataText(Dictionary<string, object> data)
        {
            Func<string, string> value = key =>
            {
                var text = Get(data, key) as string;
                return String.IsNullOrWhiteSpace(text) ? "не указана" : text.Trim();
            };
            var raw = Get(data, "supportedPlatforms") as object[];
            var platforms = (raw ?? new object[0]).OfType<string>().Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Select(x =>
                {
                    if (String.Equals(x, "iPhoneOS", StringComparison.OrdinalIgnoreCase)) return "iOS (iPhoneOS)";
                    if (String.Equals(x, "MacOSX", StringComparison.OrdinalIgnoreCase) || String.Equals(x, "macOS", StringComparison.OrdinalIgnoreCase)) return "macOS";
                    return x;
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return "Версия: " + value("displayVersion") + Environment.NewLine +
                "Дата выпуска: " + value("releaseDate") + Environment.NewLine +
                "Минимальная версия ОС: " + value("minimumOSVersion") + Environment.NewLine +
                "Платформы: " + (platforms.Length == 0 ? "не указаны" : String.Join(", ", platforms));
        }
        public static object Get(Dictionary<string, object> d, string key)
        {
            return d.Where(k => String.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase)).Select(k => k.Value).FirstOrDefault();
        }
        /// <summary>Извлекает доступные идентификаторы версий без подстановки неподтверждённых значений.</summary>
        public static List<string> VersionIds(string text)
        {
            var values = Get(Object(text), "externalVersionIdentifiers") as object[];
            if (values == null) throw new Exception("Формат списка версий не поддерживается.");
            return values.Select(Convert.ToString).Where(x => x.Length > 0 && x.All(Char.IsDigit)).ToList();
        }
        /// <summary>Извлекает приложения из JSON-вывода ipatool.</summary>
        public static List<AppRow> ParseApps(string text)
        {
            var d = Object(text);
            var values = Get(d, "apps") as object[];
            if (values == null) throw new Exception("В ответе отсутствует список apps. Проверьте совместимость движка.");
            return values.OfType<Dictionary<string, object>>().Select(x => new AppRow
            {
                Name = Convert.ToString(Get(x, "name")), ID = Convert.ToString(Get(x, "id"))
            }).Where(x => x.ID.All(Char.IsDigit) && x.ID.Length > 0).ToList();
        }
        /// <summary>Разбирает строки каталога и отбрасывает некорректные записи.</summary>
        public static List<AppRow> ParseCatalog(string text)
        {
            var rows = new List<AppRow>();
            foreach (var line in text.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'), @"^(.*):\s*(\d+)(?:[ \t]+(\d{1,2}\.\d{1,2}\.\d{4}))?\s*$");
                if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(line, @"^(.*?):\s*(\d+)");
                if (m.Success) rows.Add(new AppRow
                {
                    Name = m.Groups[1].Value.Trim(), ID = m.Groups[2].Value
                });
            }
            return rows.GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
        /// <summary>Фильтрует объединённые каталоги по пользовательскому запросу.</summary>
        public static List<AppRow> MatchCatalog(string query, List<AppRow> custom, List<AppRow> catalog)
        {
            var ids = new HashSet<string>(custom.Select(x => x.ID));
            return custom.Concat(catalog.Where(x => !ids.Contains(x.ID))).Where(x => x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
        /// <summary>Объединяет источники по App Store ID, исключая повторяющиеся приложения.</summary>
        public static List<AppRow> MergeApps(IEnumerable<AppRow> local, IEnumerable<AppRow> online)
        {
            return local.Concat(online).GroupBy(x => x.ID).Select(x => x.First()).ToList();
        }
    }
}
