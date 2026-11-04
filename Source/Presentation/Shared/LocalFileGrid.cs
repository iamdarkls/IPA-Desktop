using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace IpaDesktop;

/// <summary>Rebinds local file tables without losing selection when metadata objects are replaced.</summary>
public static class LocalFileGrid
{
    public static void Show(DataGrid grid, IReadOnlyList<LocalRow> rows)
    {
        var selected = new HashSet<string>(grid.SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        grid.ItemsSource = rows;
        foreach (var row in rows)
            if (selected.Contains(row.Path) && !grid.SelectedItems.Contains(row)) grid.SelectedItems.Add(row);
    }
}
