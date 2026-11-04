using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
namespace IpaDesktop
{
    /// <summary>Таблица версий с ручным ID, выборочной загрузкой метаданных и прерываемыми запросами.</summary>
    public sealed class AppVersionWindow : IDisposable
    {
        public Window Window { get; private set; }
        public IReadOnlyList<string> SelectedIDs => Model.SelectedIDs;
        public AppVersionViewModel Model { get; }
        public AppVersionWindow(Window owner, string name, string appId, List<AppVersionRow> rows, AppVersions service, CancellationToken ct)
        {
            Model = new(appId, rows, service, ct);
            Window = AppDialogs.Create(owner, "Версии — " + name, 920, 610); Window.MinWidth = 750; Window.MinHeight = 470;
            Window.DataContext = Model;
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock { Text = "Выберите несколько версий (Ctrl / Shift). Сведения запрашиваются только по кнопке, до 20 версий за раз.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
            layout.Children.Add(heading);
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, ItemsSource = Model.Versions.Items };
            foreach (var column in new[] { new[] { "ID версии", "ID", "135" }, new[] { "Версия", "Version", "100" }, new[] { "Мин. ОС", "MinimumOS", "90" }, new[] { "Платформы", "Platforms", "135" }, new[] { "Состояние", "State", "280" } })
                grid.Columns.Add(new DataGridTextColumn { Header = column[0], Binding = new Binding(column[1]), Width = new DataGridLength(Int32.Parse(column[2])) });
            Grid.SetRow(grid,1);layout.Children.Add(grid);
            var input = new DockPanel { Margin = new Thickness(0,12,0,0) };
            var insert = AppDialogs.Action("Добавить ID в таблицу", IconKind.Plus); insert.Margin = new Thickness(8,0,0,0);DockPanel.SetDock(insert,Dock.Right);input.Children.Add(insert);
            var manual = new TextBox { PlaceholderText = "ID версий через запятую или пробел", MinWidth = 150 };input.Children.Add(manual);Grid.SetRow(input,2);layout.Children.Add(input);
            var footer = new StackPanel { Margin = new Thickness(0,10,0,0) };
            var status = new TextBlock { Text = "Версий: " + rows.Count + ". Можно ввести известный ID вручную; доступность проверит Apple.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };footer.Children.Add(status);
            var actions = new WrapPanel();var details = AppDialogs.Action("Получить сведения", IconKind.Info); var add = AppDialogs.Action("Добавить выбранные в очередь", IconKind.Plus, primary: true);var cancel = AppDialogs.Action("Закрыть", IconKind.Close);
            actions.Children.Add(details);actions.Children.Add(add);actions.Children.Add(cancel);footer.Children.Add(actions);Grid.SetRow(footer,3);layout.Children.Add(footer);Window.Content=layout;
            SelectionBinding.SetItems(grid, Model.Versions.SelectedItems);
            ViewBindings.Bind(grid, Control.IsEnabledProperty, nameof(Model.IsEnabled));
            ViewBindings.Bind(manual, TextBox.TextProperty, nameof(Model.Manual), true);
            ViewBindings.Bind(manual, Control.IsEnabledProperty, nameof(Model.IsEnabled));
            ViewBindings.Bind(status, TextBlock.TextProperty, nameof(Model.Status));
            ViewBindings.Bind(cancel, ContentControl.ContentProperty, nameof(Model.CancelLabel));
            insert.Command = Model.InsertCommand; details.Command = Model.DetailsCommand; add.Command = Model.AddCommand; cancel.Command = Model.CancelCommand;
            Model.CloseRequested += result => { if (result == true) Window.Close(true); else Window.Close(); };
            Window.Closing += (_, e) => { if (!Model.RequestClose()) e.Cancel = true; };
        }
        public void Dispose() => Model.Dispose();
    }
}
