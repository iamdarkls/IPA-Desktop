using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace IpaDesktop
{
    /// <summary>Редактирует независимую копию набора; отмена не меняет файл конфигурации.</summary>
    public static class AppSetEditor
    {
        public static Window Create(Window owner, string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save)
        {
            var draft = new ObservableCollection<AppRow>(apps.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
            var dialog = AppDialogs.Create(owner, "Редактирование набора", 680, 580);
            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBox { Text = name, MaxLength = 64, Margin = new Thickness(0, 0, 0, 12) };
            title.Name = "DraftName"; grid.Children.Add(title);
            var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemsSource = draft, DisplayMemberPath = "Name" };
            list.Style = (Style)owner.FindResource("SetList"); list.Name = "DraftApps";
            Grid.SetRow(list, 1); grid.Children.Add(list);
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var hint = new TextBlock { Text = "Клик — отметить приложение. Новые приложения добавляются в набор через поиск.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            status.Foreground = System.Windows.Media.Brushes.Salmon;
            var buttons = new WrapPanel();
            var remove = new Button { Content = "Убрать отмеченные" }; remove.Name = "DraftRemove";
            remove.Click += delegate { foreach (var item in list.SelectedItems.Cast<AppRow>().ToArray()) draft.Remove(item); };
            var apply = new Button { Content = "Сохранить" }; apply.Name = "DraftSave";
            apply.Click += delegate
            {
                try
                {
                    save(title.Text.Trim(), draft.ToArray());
                    dialog.DialogResult = true;
                }
                catch (Exception e) { status.Text = e.Message; }
            };
            var cancel = new Button { Content = "Отмена", IsCancel = true };
            cancel.Click += delegate { dialog.Close(); };
            buttons.Children.Add(remove); buttons.Children.Add(apply); buttons.Children.Add(cancel);
            bottom.Children.Add(hint); bottom.Children.Add(status); bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            dialog.Content = grid;
            return dialog;
        }
    }
}
