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
