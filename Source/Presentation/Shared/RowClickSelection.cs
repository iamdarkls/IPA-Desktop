using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace IpaDesktop;
public static class RowClickSelection
{
    public static void Attach(DataGrid grid, Action<object> add)
    {
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) return;
            var source = e.Source as Control;
            var row = source?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
            if (row == null || source is Button || source?.GetVisualAncestors().TakeWhile(x => x != row).Any(x => x is Button) == true) return;
            add(row.DataContext);
        }, RoutingStrategies.Bubble, true);
    }
}
