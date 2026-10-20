using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Обрабатывает крестики в шаблонах выбранных элементов по стабильному ID или пути.</summary>
    public static class SelectionRemove
    {
        /// <summary>Находит крестик во вложенном WPF-шаблоне и передаёт идентификатор удаляемого элемента.</summary>
        public static void Attach(ItemsControl list, Action<string> remove)
        {
            list.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                // WPF can retarget Source to an item container; OriginalSource identifies the button.
                var current = e.OriginalSource as DependencyObject;
                while (current != null && !(current is Button))
                {
                    current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
                }
                var button = current as Button;
                if (button == null || button.Tag == null) return;
                remove(Convert.ToString(button.Tag));
                e.Handled = true;
            }), true);
        }
    }
}
