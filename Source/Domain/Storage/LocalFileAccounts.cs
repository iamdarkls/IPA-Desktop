using System;
using System.Collections.Generic;
using System.Linq;

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
        static readonly string[] MissingStatuses = { "Не указан", "Не удалось прочитать", "Нет доступа", "Метаданные слишком велики" };

        static string Normalize(string account)
        {
            var value = (account ?? "").Trim();
            // Эти значения возвращает IpaMetadata при отсутствии или ошибке чтения, это не имена аккаунтов.
            foreach (var status in MissingStatuses)
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
