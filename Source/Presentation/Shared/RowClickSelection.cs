using Avalonia;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace IpaDesktop;
public sealed class RowClickSelection : Avalonia.AvaloniaObject
{
    public static readonly Avalonia.AttachedProperty<System.Windows.Input.ICommand> CommandProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<RowClickSelection, DataGrid, System.Windows.Input.ICommand>("Command");
    public static System.Windows.Input.ICommand GetCommand(DataGrid grid) => grid.GetValue(CommandProperty);
    public static void SetCommand(DataGrid grid, System.Windows.Input.ICommand value) => grid.SetValue(CommandProperty, value);
    static readonly AttachedProperty<bool> AttachedProperty = AvaloniaProperty.RegisterAttached<RowClickSelection, DataGrid, bool>("Attached");
    static RowClickSelection() => CommandProperty.Changed.AddClassHandler<DataGrid>((grid, _) =>
    {
        if (grid.GetValue(AttachedProperty)) return;
        grid.SetValue(AttachedProperty, true);
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) return;
            var source = e.Source as Control;
            var row = source?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
            if (row == null || source is Button || source?.GetVisualAncestors().TakeWhile(x => x != row).Any(x => x is Button) == true) return;
            var command = GetCommand(grid);
            if (command?.CanExecute(row.DataContext) == true) command.Execute(row.DataContext);
        }, RoutingStrategies.Bubble, true);
    });
}
