using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace IpaDesktop;
public static class SelectionRemove
{
    public static void Attach(ItemsControl list, Action<string> remove)
    {
        list.AddHandler(Button.ClickEvent, (_, e) =>
        {
            var control = e.Source as Control;
            var button = control as Button ?? control?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (button?.Tag == null) return;
            remove(Convert.ToString(button.Tag)); e.Handled = true;
        }, RoutingStrategies.Bubble, true);
    }
}
