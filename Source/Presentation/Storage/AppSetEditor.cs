using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace IpaDesktop
{
    /// <summary>Редактирует независимую копию набора; отмена не меняет файл конфигурации.</summary>
    public static class AppSetEditor
    {
        public static Window Create(Window owner, string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save)
        {
            var model = new AppSetEditorViewModel(name, apps, save);
            var dialog = AppDialogs.Create(owner, "Редактирование набора", 680, 580);
            dialog.DataContext = model;
            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBox { Text = name, MaxLength = 64, Margin = new Thickness(0, 0, 0, 12) };
            title.Name = "DraftName"; grid.Children.Add(title);
            var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemsSource = model.Apps.Items, DisplayMemberBinding = new Avalonia.Data.Binding("Name") };
            list.Name = "DraftApps";
            Grid.SetRow(list, 1); grid.Children.Add(list);
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var hint = new TextBlock { Text = "Клик — отметить приложение. Новые приложения добавляются в набор через поиск.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            status.Foreground = Avalonia.Media.Brushes.Salmon;
            var buttons = new WrapPanel();
            var remove = AppDialogs.Action("Убрать отмеченные", IconKind.Trash); remove.Name = "DraftRemove";
            remove.Command = model.RemoveCommand;
            var apply = AppDialogs.Action("Сохранить", IconKind.Save, primary: true); apply.Name = "DraftSave";
            apply.Command = model.SaveCommand;
            var cancel = AppDialogs.Action("Отмена", IconKind.Close); cancel.IsCancel = true; cancel.Command = model.CancelCommand;
            SelectionBinding.SetItems(list, model.Apps.SelectedItems);
            ViewBindings.Bind(title, TextBox.TextProperty, nameof(model.Name), true);
            ViewBindings.Bind(status, TextBlock.TextProperty, nameof(model.Status));
            model.CloseRequested += result => dialog.Close(result);
            buttons.Children.Add(remove); buttons.Children.Add(apply); buttons.Children.Add(cancel);
            bottom.Children.Add(hint); bottom.Children.Add(status); bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            dialog.Content = grid;
            return dialog;
        }
    }
}
