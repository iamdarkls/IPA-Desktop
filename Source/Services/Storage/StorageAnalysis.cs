using System;
using System.Linq;
using System.Collections.Generic;
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
            // Parse each version once, then reuse it for grouping, max and comparison.
            var known = list.Where(x => !String.IsNullOrWhiteSpace(x.BundleID) && x.AppleAccount != null && x.AppleAccount.Contains("@"))
                .Select(x => Tuple.Create(x, Parse(x.Version))).Where(x => x.Item2 != null);
            foreach (var group in known.GroupBy(x => x.Item1.BundleID.ToLowerInvariant() + "\n" + x.Item1.AppleAccount.ToLowerInvariant()))
            {
                var newest = group.Select(x => x.Item2).Max();
                foreach (var entry in group)
                {
                    var row = entry.Item1;
                    row.IsOld = entry.Item2.CompareTo(newest) < 0;
                    row.StorageStatus = row.IsOld ? "Старая версия" : "Самая новая в папке";
                }
            }
        }
    }
}
