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
        var query=new TextBox{PlaceholderText="Поиск по названию, App Store ID или Bundle ID"};ViewBindings.Bind(query,TextBox.TextProperty,nameof(model.Query),true);layout.Children.Add(query);
        var toolbar=new WrapPanel();
        var period=new ComboBox{ItemsSource=new[]{"Последние 90 дней","Выбранный год","Вся история"},SelectedIndex=0,Width=180,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        var year=new ComboBox{ItemsSource=Enumerable.Range(2008,DateTime.Now.Year-2007).Reverse().ToArray(),SelectedIndex=0,Width=100,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        ViewBindings.Bind(period,ComboBox.SelectedIndexProperty,nameof(model.Period),true);
        ViewBindings.Bind(period,Control.IsEnabledProperty,nameof(model.IsEnabled));
        year.ItemsSource=model.Years;ViewBindings.Bind(year,ComboBox.SelectedItemProperty,nameof(model.Year),true);
        ViewBindings.Bind(year,Control.IsVisibleProperty,nameof(model.YearVisible));
        ViewBindings.Bind(year,Control.IsEnabledProperty,nameof(model.IsEnabled));
        var refresh=AppDialogs.Action("Загрузить список",IconKind.Refresh);refresh.Command=model.ReloadCommand;
        var add=AppDialogs.Action("В очередь скачивания",IconKind.Plus,primary:true);add.Command=model.AddCommand;
        toolbar.Children.Add(period);toolbar.Children.Add(year);toolbar.Children.Add(refresh);toolbar.Children.Add(add);Grid.SetRow(toolbar,1);layout.Children.Add(toolbar);
        Apps=new DataGrid{Name="LibraryApps",SelectionMode=DataGridSelectionMode.Extended};
        var columns=purchases
            ? new[]{new[]{"Приложение","Name","2*"},new[]{"App Store ID","ID","130"},new[]{"Вид","Status","170"},new[]{"Дата покупки","Date","140"}}
            : new[]{new[]{"Приложение","Name","2*"},new[]{"App Store ID","ID","130"},new[]{"Bundle ID","BundleID","*"},new[]{"Версия","Version","105"},new[]{"Сборка","Build","105"},new[]{"Состояние","Status","170"}};
        foreach(var c in columns)
            Apps.Columns.Add(new DataGridTextColumn{Header=c[0],Binding=new Binding(c[1]),Width=c[2]=="*"?new DataGridLength(1,DataGridLengthUnitType.Star):c[2]=="2*"?new DataGridLength(2,DataGridLengthUnitType.Star):new DataGridLength(Int32.Parse(c[2]))});
        ViewBindings.Bind(Apps,DataGrid.ItemsSourceProperty,"Apps.Items");SelectionBinding.SetItems(Apps,model.Apps.SelectedItems);Grid.SetRow(Apps,2);layout.Children.Add(Apps);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};Grid.SetRow(status,3);layout.Children.Add(status);Content=layout;
        ViewBindings.Bind(status,TextBlock.TextProperty,nameof(model.Status));
        Closed+=(_,_)=>model.Dispose();
    }
    public void SetRows(List<LibraryApp> rows,string message) => Model.SetRows(rows,message);
}
