using System;
using System.Collections.Generic;
using System.Linq;

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
