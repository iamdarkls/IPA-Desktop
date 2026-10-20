using System;
using System.IO;
using System.Text.RegularExpressions;

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
