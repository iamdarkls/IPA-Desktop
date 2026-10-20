using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace IpaDesktop
{
    /// <summary>Таблица версий с ручным ID, выборочной загрузкой метаданных и прерываемыми запросами.</summary>
    public sealed class AppVersionWindow : IDisposable
    {
        public Window Window { get; private set; }
        public List<string> SelectedIDs { get; private set; }
        readonly CancellationTokenSource lifetime;
        CancellationTokenSource operation;
        bool loading, closing;
        readonly DataGrid grid;
        readonly TextBlock status;
        readonly TextBox manual;
        readonly Button details, add, insert, cancel;
        readonly List<AppVersionRow> rows;
        readonly AppVersions service;
        readonly string appId;
        public AppVersionWindow(Window owner, string name, string appId, List<AppVersionRow> rows, AppVersions service, CancellationToken ct)
        {
            this.appId = appId; this.rows = rows; this.service = service;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            SelectedIDs = new List<string>();
            Window = AppDialogs.Create(owner, "Версии — " + name, 920, 610); Window.MinWidth = 750; Window.MinHeight = 470;
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock { Text = "Выберите несколько версий (Ctrl / Shift). Сведения запрашиваются только по кнопке, до 20 версий за раз.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
            layout.Children.Add(heading);
            grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, EnableRowVirtualization = true, EnableColumnVirtualization = false, ItemsSource = rows };
            foreach (var column in new[] { new[] { "ID версии", "ID", "135" }, new[] { "Версия", "Version", "100" }, new[] { "Мин. ОС", "MinimumOS", "90" }, new[] { "Платформы", "Platforms", "135" }, new[] { "Состояние", "State", "280" } })
                grid.Columns.Add(new DataGridTextColumn { Header = column[0], Binding = new Binding(column[1]), Width = new DataGridLength(Int32.Parse(column[2])) });
            Grid.SetRow(grid,1);layout.Children.Add(grid);
            var input = new DockPanel { Margin = new Thickness(0,12,0,0) };
            insert = new Button { Content = "Добавить ID в таблицу", Margin = new Thickness(8,0,0,0) };DockPanel.SetDock(insert,Dock.Right);input.Children.Add(insert);
            manual = new TextBox { ToolTip = "Один или несколько ID версий через запятую или пробел", MinWidth = 150 };input.Children.Add(manual);Grid.SetRow(input,2);layout.Children.Add(input);
            var footer = new StackPanel { Margin = new Thickness(0,10,0,0) };
            status = new TextBlock { Text = "Версий: " + rows.Count + ". Можно ввести известный ID вручную; доступность проверит Apple.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };footer.Children.Add(status);
            var actions = new WrapPanel();details = new Button { Content = "Получить сведения" }; add = new Button { Content = "Добавить выбранные в очередь" };cancel = new Button { Content = "Закрыть" };
            actions.Children.Add(details);actions.Children.Add(add);actions.Children.Add(cancel);footer.Children.Add(actions);Grid.SetRow(footer,3);layout.Children.Add(footer);Window.Content=layout;
            grid.SelectionChanged += delegate { Refresh(); };
            insert.Click += delegate
            {
                try
                {
                    var ids = AppVersions.ParseManual(manual.Text);grid.SelectedItems.Clear();
                    foreach(var id in ids)
                    {
                        var row=rows.FirstOrDefault(x=>x.ID==id);
                        if(row==null){row=new AppVersionRow{ID=id};rows.Insert(0,row);}
                    }
                    grid.Items.Refresh();foreach(var row in rows.Where(x=>ids.Contains(x.ID)))grid.SelectedItems.Add(row);
                    manual.Text="";status.Text="Добавлено ID: "+ids.Count+". Можно запросить сведения или добавить в очередь.";Refresh();
                }
                catch(ArgumentException e){status.Text=e.Message;}
            };
            details.Click += async delegate
            {
                var selected=grid.SelectedItems.Cast<AppVersionRow>().ToList();
                if(selected.Count>20){status.Text="Выберите не более 20 версий для одного запроса сведений.";return;}
                loading=true;operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);Refresh();
                try
                {
                    for(int i=0;i<selected.Count;i++)
                    {
                        operation.Token.ThrowIfCancellationRequested();var row=selected[i];
                        status.Text="Сведения: "+(i+1)+" / "+selected.Count+" · ID "+row.ID;
                        try { await service.ReadAsync(appId,row,operation.Token); }
                        catch(OperationCanceledException){throw;}
                        catch(Exception e){row.State=e.Message.Length>240?e.Message.Substring(0,240):e.Message;}
                        grid.Items.Refresh();
                    }
                    status.Text="Обработано версий: "+selected.Count+". Неизвестные данные не подтверждают совместимость с iPhone.";
                }
                catch(OperationCanceledException){status.Text="Запрос отменён. Полученные сведения сохранены в таблице.";}
                finally{loading=false;operation.Dispose();operation=null;Refresh();if(closing)Window.Close();}
            };
            add.Click+=delegate {SelectedIDs=grid.SelectedItems.Cast<AppVersionRow>().Select(x=>x.ID).ToList();Window.DialogResult=true;};
            cancel.Click+=delegate{if(loading)operation.Cancel();else Window.Close();};
            Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(loading){closing=true;e.Cancel=true;operation.Cancel();}};
            Refresh();
        }
        void Refresh()
        {
            grid.IsEnabled=!loading;manual.IsEnabled=!loading;insert.IsEnabled=!loading;
            details.IsEnabled=!loading&&grid.SelectedItems.Count>0;add.IsEnabled=details.IsEnabled;
            cancel.Content=loading?"Отменить запрос":"Закрыть";
        }
        public void Dispose(){lifetime.Dispose();}
    }
}
