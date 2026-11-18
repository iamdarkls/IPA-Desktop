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
        public AppVersionWindow(Window owner, string name, string appId, List<AppVersionRow> rows, IAppVersions service, CancellationToken ct, BackgroundTasks ownerTasks = null)
        {
            Model = new(appId, rows, service, ct, ownerTasks);
            Window = AppDialogs.Create(owner, "Версии — " + name, 920, 610); Window.MinWidth = 750; Window.MinHeight = 470;
            Window.DataContext = Model;
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock { Text = "Выберите несколько версий (Ctrl / Shift). Сведения запрашиваются только по кнопке, до 20 версий за раз.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            layout.Children.Add(heading);
            var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, ItemsSource = Model.Versions.Items };
            grid.Columns.Add(new DataGridTextColumn { Header = "ID версии", Binding = ViewBindings.Create(ViewBindings.Property(nameof(AppVersionRow.ID), (AppVersionRow row) => row.ID)), Width = new DataGridLength(135) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Версия", Binding = ViewBindings.Create(ViewBindings.Property(nameof(AppVersionRow.Version), (AppVersionRow row) => row.Version)), Width = new DataGridLength(100) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Мин. ОС", Binding = ViewBindings.Create(ViewBindings.Property(nameof(AppVersionRow.MinimumOS), (AppVersionRow row) => row.MinimumOS)), Width = new DataGridLength(90) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Платформы", Binding = ViewBindings.Create(ViewBindings.Property(nameof(AppVersionRow.Platforms), (AppVersionRow row) => row.Platforms)), Width = new DataGridLength(135) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Состояние", Binding = ViewBindings.Create(ViewBindings.Property(nameof(AppVersionRow.State), (AppVersionRow row) => row.State)), Width = new DataGridLength(280) });
            Grid.SetRow(grid, 1); layout.Children.Add(grid);
            var input = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var insert = AppDialogs.Action("Добавить ID в таблицу", IconKind.Plus); insert.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(insert, Dock.Right); input.Children.Add(insert);
            var manual = new TextBox { PlaceholderText = "ID версий через запятую или пробел", MinWidth = 150 }; input.Children.Add(manual); Grid.SetRow(input, 2); layout.Children.Add(input);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var status = new TextBlock { Text = "Версий: " + rows.Count + ". Можно ввести известный ID вручную; доступность проверит Apple.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; footer.Children.Add(status);
            var actions = new WrapPanel(); var details = AppDialogs.Action("Получить сведения", IconKind.Info); var add = AppDialogs.Action("Добавить выбранные в очередь", IconKind.Plus, primary: true); var cancel = AppDialogs.Action("Закрыть", IconKind.Close);
            actions.Children.Add(details); actions.Children.Add(add); actions.Children.Add(cancel); footer.Children.Add(actions); Grid.SetRow(footer, 3); layout.Children.Add(footer); Window.Content = layout;
            SelectionBinding.SetItems(grid, Model.Versions.SelectedItems);
            ViewBindings.Bind(grid, Control.IsEnabledProperty, nameof(AppVersionViewModel.IsEnabled), (AppVersionViewModel vm) => vm.IsEnabled);
            ViewBindings.Bind(manual, TextBox.TextProperty, nameof(AppVersionViewModel.Manual), (AppVersionViewModel vm) => vm.Manual, (vm, value) => vm.Manual = value);
            ViewBindings.Bind(manual, Control.IsEnabledProperty, nameof(AppVersionViewModel.IsEnabled), (AppVersionViewModel vm) => vm.IsEnabled);
            ViewBindings.Bind(status, TextBlock.TextProperty, nameof(AppVersionViewModel.Status), (AppVersionViewModel vm) => vm.Status);
            ViewBindings.Bind(cancel, ContentControl.ContentProperty, nameof(AppVersionViewModel.CancelLabel), (AppVersionViewModel vm) => vm.CancelLabel);
            insert.Command = Model.InsertCommand; details.Command = Model.DetailsCommand; add.Command = Model.AddCommand; cancel.Command = Model.CancelCommand;
            Model.CloseRequested += result => { if (result == true) Window.Close(true); else Window.Close(); };
            Window.Closing += (_, e) => { if (!Model.RequestClose()) e.Cancel = true; };
        }
        public void Dispose() => Model.Dispose();
    }
}
