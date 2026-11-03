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
    readonly DesktopViewModel ownerModel;
    readonly Func<bool> contextValid;
    readonly TextBox query;
    readonly TextBlock status;
    readonly Button refresh,add;
    readonly ComboBox period,year;
    readonly CancellationTokenSource lifetime=new();
    List<LibraryApp> rows=new();
    public DataGrid Apps { get; }
    public Action ReloadRequested { get; set; }
    public Action<IReadOnlyList<LibraryApp>> AddRequested { get; set; }
    public CancellationToken Token => lifetime.Token;
    public bool AllYears => period.SelectedIndex==2;
    public int? SelectedYear => period.SelectedIndex==1?(int?)year.SelectedItem:null;
    public AppBrowserWindow(Window owner, DesktopViewModel model, string title, bool purchases, Func<bool> contextValid)
    {
        ownerModel=model;this.contextValid=contextValid;Title=title;Icon=owner.Icon;Width=1000;Height=650;MinWidth=760;MinHeight=420;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var layout=new Grid{Margin=new Thickness(20),RowDefinitions=new("Auto,Auto,*,Auto"),RowSpacing=10};
        query=new TextBox{PlaceholderText="Поиск по названию, App Store ID или Bundle ID"};query.TextChanged+=(_,_)=>Filter();layout.Children.Add(query);
        var toolbar=new WrapPanel();
        period=new ComboBox{ItemsSource=new[]{"Последние 90 дней","Выбранный год","Вся история"},SelectedIndex=0,Width=180,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        year=new ComboBox{ItemsSource=Enumerable.Range(2008,DateTime.Now.Year-2007).Reverse().ToArray(),SelectedIndex=0,Width=100,IsVisible=purchases,Margin=new Thickness(0,0,8,8)};
        year.IsVisible=purchases&&period.SelectedIndex==1;
        period.SelectionChanged+=(_,_)=>year.IsVisible=purchases&&period.SelectedIndex==1;
        refresh=AppDialogs.Action("Загрузить список",IconKind.Refresh);refresh.Click+=(_,_)=>ReloadRequested?.Invoke();
        add=AppDialogs.Action("В очередь скачивания",IconKind.Plus,primary:true);add.Click+=(_,_)=>AddRequested?.Invoke(Apps.SelectedItems.Cast<LibraryApp>().ToArray());
        toolbar.Children.Add(period);toolbar.Children.Add(year);toolbar.Children.Add(refresh);toolbar.Children.Add(add);Grid.SetRow(toolbar,1);layout.Children.Add(toolbar);
        Apps=new DataGrid{Name="LibraryApps",SelectionMode=DataGridSelectionMode.Extended};
        var columns=purchases
            ? new[]{new[]{"Приложение","Name","2*"},new[]{"App Store ID","ID","130"},new[]{"Вид","Status","170"},new[]{"Дата покупки","Date","140"}}
            : new[]{new[]{"Приложение","Name","2*"},new[]{"App Store ID","ID","130"},new[]{"Bundle ID","BundleID","*"},new[]{"Версия","Version","105"},new[]{"Сборка","Build","105"},new[]{"Состояние","Status","170"}};
        foreach(var c in columns)
            Apps.Columns.Add(new DataGridTextColumn{Header=c[0],Binding=new Binding(c[1]),Width=c[2]=="*"?new DataGridLength(1,DataGridLengthUnitType.Star):c[2]=="2*"?new DataGridLength(2,DataGridLengthUnitType.Star):new DataGridLength(Int32.Parse(c[2]))});
        Apps.SelectionChanged+=(_,_)=>RefreshControls();Grid.SetRow(Apps,2);layout.Children.Add(Apps);
        status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};Grid.SetRow(status,3);layout.Children.Add(status);Content=layout;
        model.PropertyChanged+=ModelChanged;model.Device.PropertyChanged+=ModelChanged;
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();model.PropertyChanged-=ModelChanged;model.Device.PropertyChanged-=ModelChanged;};RefreshControls();
    }
    void ModelChanged(object sender,System.ComponentModel.PropertyChangedEventArgs args)
    {
        if(!contextValid()) { rows.Clear();Apps.ItemsSource=Array.Empty<LibraryApp>();status.Text="Аккаунт или выбранный iPhone изменился. Откройте список заново."; }
        RefreshControls();
    }
    void RefreshControls()
    {
        refresh.IsEnabled=period.IsEnabled=year.IsEnabled=!ownerModel.IsBusy&&contextValid();
        add.IsEnabled=!ownerModel.IsBusy&&contextValid()&&Apps.SelectedItems.Count>0;
    }
    public void SetRows(List<LibraryApp> value,string message) { rows=value;status.Text=message;Filter(); }
    void Filter()
    {
        var text=query.Text?.Trim()??"";
        Apps.ItemsSource=rows.Where(x=>(x.Name??"").Contains(text,StringComparison.OrdinalIgnoreCase)||(x.ID??"").Contains(text,StringComparison.OrdinalIgnoreCase)||(x.BundleID??"").Contains(text,StringComparison.OrdinalIgnoreCase)).ToArray();
        RefreshControls();
    }
}
