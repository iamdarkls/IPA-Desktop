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
    /// <summary>Добавляет строку двойным левым кликом, оставляя одиночный клик для обычного выделения.</summary>
    public static class RowClickSelection
    {
        /// <summary>Использует системное событие двойного клика WPF и исключает вложенные кнопки.</summary>
        public static void Attach(DataGrid grid, Action<object> add)
        {
            grid.AddHandler(Control.MouseDoubleClickEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
                var source = e.OriginalSource as DependencyObject;
                var row = ItemsControl.ContainerFromElement(grid, source) as DataGridRow;
                if (row == null) return;
                // Двойной клик по кнопке внутри строки не должен добавлять саму строку.
                for (var node = source; node != null && node != row; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node is System.Windows.Controls.Primitives.ButtonBase) return;
                add(row.Item);
            }), true);
        }
    }
}
