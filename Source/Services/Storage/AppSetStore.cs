using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
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
