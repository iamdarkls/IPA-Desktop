using System;
using System.Linq;
using System.Collections.Generic;
namespace IpaDesktop
{
    /// <summary>Отображает место на диске и кандидатов на очистку, используя общий снимок локальных IPA.</summary>
    public sealed partial class StorageViewModel : FeatureViewModel
    {
        List<LocalRow> rows = new List<LocalRow>();
        public StorageViewModel(DesktopViewModel desktop, Engine engine) : base(desktop, engine) { }
        internal void Initialize()
        {
            RefreshStorageCommand = Command(desktop.RefreshFiles);
            DeleteStorageCommand = AsyncCommand(() => desktop.DeleteFiles(StorageFiles.SelectedItems.ToList()));
            SelectOldCommand = Command(delegate
            {
                var grid = StorageFiles; grid.SelectedItems.Clear();
                foreach (var row in rows.Where(x => x.IsOld)) grid.SelectedItems.Add(row);
                ApplyActions();
            });
            StorageFiles.SelectionChanged += delegate { ApplyActions(); };
        }
        /// <summary>Обновляет таблицу и сводку, сохраняя выделение файлов по полному пути.</summary>
        internal void Show(List<LocalRow> available)
        {
            var grid = StorageFiles;
            rows = available;
            grid.ReplacePreservingSelection(rows, x => x.Path);
            StorageSummary = "IPA: " + rows.Count + " · занято: " + StorageAnalysis.Bytes(rows.Sum(x => x.Bytes)) + " · старых версий: " + rows.Count(x => x.IsOld) + " (" + StorageAnalysis.Bytes(rows.Where(x => x.IsOld).Sum(x => x.Bytes)) + ")";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            SelectOldEnabled = !busy && rows.Any(x => x.IsOld);
            DeleteStorageEnabled = !busy && StorageFiles.SelectedItems.Count > 0;
        }
    }
}
