using System;
using System.Collections.Generic;
using System.Linq;

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
