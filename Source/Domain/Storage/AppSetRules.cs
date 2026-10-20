using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
