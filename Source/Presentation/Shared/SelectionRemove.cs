using Avalonia;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace IpaDesktop;
public sealed class SelectionRemove : Avalonia.AvaloniaObject
{
    public static readonly Avalonia.AttachedProperty<System.Windows.Input.ICommand> CommandProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<SelectionRemove, ItemsControl, System.Windows.Input.ICommand>("Command");
    public static System.Windows.Input.ICommand GetCommand(ItemsControl list) => list.GetValue(CommandProperty);
    public static void SetCommand(ItemsControl list, System.Windows.Input.ICommand value) => list.SetValue(CommandProperty, value);
    static readonly AttachedProperty<bool> AttachedProperty = AvaloniaProperty.RegisterAttached<SelectionRemove, ItemsControl, bool>("Attached");
    static SelectionRemove() => CommandProperty.Changed.AddClassHandler<ItemsControl>((list, _) =>
    {
        if (list.GetValue(AttachedProperty)) return;
        list.SetValue(AttachedProperty, true);
        list.AddHandler(Button.ClickEvent, (_, e) =>
        {
            var control = e.Source as Control;
            var button = control as Button ?? control?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (button?.Tag == null) return;
            var command = GetCommand(list);
            if (command?.CanExecute(button.Tag) == true) { command.Execute(button.Tag); e.Handled = true; }
        }, RoutingStrategies.Bubble, true);
    });
}
