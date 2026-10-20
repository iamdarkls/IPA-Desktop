using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Controls;
namespace IpaDesktop
{
    /// <summary>Отображает место на диске и кандидатов на очистку, используя общий снимок локальных IPA.</summary>
    public sealed class StorageController : WindowController
    {
        List<LocalRow> rows = new List<LocalRow>();
        public StorageController(Desktop view, Engine engine) : base(view, engine)
        {
            On("RefreshStorage", view.RefreshFiles);
            On("DeleteStorage", delegate { view.DeleteFiles(C<DataGrid>("StorageFiles").SelectedItems.Cast<LocalRow>().ToList()); });
            On("SelectOld", delegate
            {
                var grid = C<DataGrid>("StorageFiles"); grid.SelectedItems.Clear();
                foreach (var row in rows.Where(x => x.IsOld)) grid.SelectedItems.Add(row);
                ApplyActions();
            });
            C<DataGrid>("StorageFiles").SelectionChanged += delegate { ApplyActions(); };
        }
        /// <summary>Обновляет таблицу и сводку, сохраняя выделение файлов по полному пути.</summary>
        internal void Show(List<LocalRow> available)
        {
            var grid = C<DataGrid>("StorageFiles");
            var selected = new HashSet<string>(grid.SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            rows = available; grid.ItemsSource = rows;
            foreach (var row in rows) if (selected.Contains(row.Path)) grid.SelectedItems.Add(row);
            C<TextBlock>("StorageSummary").Text = "IPA: " + rows.Count + " · занято: " + StorageAnalysis.Bytes(rows.Sum(x => x.Bytes)) + " · старых версий: " + rows.Count(x => x.IsOld) + " (" + StorageAnalysis.Bytes(rows.Where(x => x.IsOld).Sum(x => x.Bytes)) + ")";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("SelectOld").IsEnabled = !busy && rows.Any(x => x.IsOld);
            C<Button>("DeleteStorage").IsEnabled = !busy && C<DataGrid>("StorageFiles").SelectedItems.Count > 0;
        }
    }
}
