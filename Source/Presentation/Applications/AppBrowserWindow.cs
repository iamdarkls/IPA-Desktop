using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpaDesktop;

/// <summary>Read-only account/device library; rows remain separate from the download queue until explicitly added.</summary>
public sealed class AppBrowserWindow : Window
{
    public AppBrowserViewModel Model { get; }
    public DataGrid Apps { get; }
    public AppBrowserWindow(Window owner, DesktopViewModel model, string title, bool purchases, Func<bool> contextValid)
        : this(owner, new AppBrowserViewModel(model, title, purchases, contextValid)) { }
    public AppBrowserWindow(Window owner, AppBrowserViewModel model)
    {
        Model = model; DataContext = model; var purchases = model.Purchases; var title = model.Title;
        Title=title;Icon=owner.Icon;Width=1000;Height=650;MinWidth=760;MinHeight=420;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var layout=new Grid{Margin=new Thickness(20),RowDefinitions=new("Auto,Auto,*,Auto"),RowSpacing=10};
        var query=new TextBox{PlaceholderText="Поиск по названию, App Store ID или Bundle ID"};ViewBindings.Bind(query,TextBox.TextProperty,nameof(AppBrowserViewModel.Query), (AppBrowserViewModel vm) => vm.Query, (vm, value) => vm.Query = value);layout.Children.Add(query);
        var toolbar=new WrapPanel();
        var period=new ComboBox{ItemsSource=new[]{"Последние 90 дней","Выбранный год","Вся история"},SelectedIndex=0,Width=180,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        var year=new ComboBox{ItemsSource=Enumerable.Range(2008,DateTime.Now.Year-2007).Reverse().ToArray(),SelectedIndex=0,Width=100,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        ViewBindings.Bind(period,ComboBox.SelectedIndexProperty,nameof(AppBrowserViewModel.Period), (AppBrowserViewModel vm) => vm.Period, (vm, value) => vm.Period = value);
        ViewBindings.Bind(period,Control.IsEnabledProperty,nameof(AppBrowserViewModel.IsEnabled), (AppBrowserViewModel vm) => vm.IsEnabled);
        year.ItemsSource=model.Years;ViewBindings.Bind(year,ComboBox.SelectedItemProperty,nameof(AppBrowserViewModel.Year), (AppBrowserViewModel vm) => vm.Year, (vm, value) => vm.Year = value);
        ViewBindings.Bind(year,Control.IsVisibleProperty,nameof(AppBrowserViewModel.YearVisible), (AppBrowserViewModel vm) => vm.YearVisible);
        ViewBindings.Bind(year,Control.IsEnabledProperty,nameof(AppBrowserViewModel.IsEnabled), (AppBrowserViewModel vm) => vm.IsEnabled);
        var refresh=AppDialogs.Action("Загрузить список",IconKind.Refresh);refresh.Command=model.ReloadCommand;
        var add=AppDialogs.Action("В очередь скачивания",IconKind.Plus,primary:true);add.Command=model.AddCommand;
        toolbar.Children.Add(period);toolbar.Children.Add(year);toolbar.Children.Add(refresh);toolbar.Children.Add(add);Grid.SetRow(toolbar,1);layout.Children.Add(toolbar);
        Apps=new DataGrid{Name="LibraryApps",SelectionMode=DataGridSelectionMode.Extended};
        Apps.Columns.Add(new DataGridTextColumn { Header = "Приложение", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Name), (LibraryApp row) => row.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Apps.Columns.Add(new DataGridTextColumn { Header = "App Store ID", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.ID), (LibraryApp row) => row.ID)), Width = new DataGridLength(130) });
        if (purchases)
        {
            Apps.Columns.Add(new DataGridTextColumn { Header = "Вид", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Status), (LibraryApp row) => row.Status)), Width = new DataGridLength(170) });
            Apps.Columns.Add(new DataGridTextColumn { Header = "Дата покупки", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Date), (LibraryApp row) => row.Date)), Width = new DataGridLength(140) });
        }
        else
        {
            Apps.Columns.Add(new DataGridTextColumn { Header = "Bundle ID", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.BundleID), (LibraryApp row) => row.BundleID)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Apps.Columns.Add(new DataGridTextColumn { Header = "Версия", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Version), (LibraryApp row) => row.Version)), Width = new DataGridLength(105) });
            Apps.Columns.Add(new DataGridTextColumn { Header = "Сборка", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Build), (LibraryApp row) => row.Build)), Width = new DataGridLength(105) });
            Apps.Columns.Add(new DataGridTextColumn { Header = "Состояние", Binding = ViewBindings.Create(ViewBindings.Property(nameof(LibraryApp.Status), (LibraryApp row) => row.Status)), Width = new DataGridLength(170) });
        }
        Apps.Bind(DataGrid.ItemsSourceProperty, ViewBindings.Create(
            ViewBindings.Property(nameof(AppBrowserViewModel.Apps), (AppBrowserViewModel vm) => vm.Apps),
            ViewBindings.Property(nameof(SelectionViewModel<LibraryApp>.Items), (SelectionViewModel<LibraryApp> selection) => selection.Items)));
        SelectionBinding.SetItems(Apps,model.Apps.SelectedItems);Grid.SetRow(Apps,2);layout.Children.Add(Apps);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};Grid.SetRow(status,3);layout.Children.Add(status);Content=layout;
        ViewBindings.Bind(status,TextBlock.TextProperty,nameof(AppBrowserViewModel.Status), (AppBrowserViewModel vm) => vm.Status);
        Closed+=(_,_)=>model.Dispose();
    }
    public void SetRows(List<LibraryApp> rows,string message) => Model.SetRows(rows,message);
}
