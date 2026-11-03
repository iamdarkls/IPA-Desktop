using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace IpaDesktop;

public sealed class NetworkSettingsWindow : Window
{
    public NetworkSettingsWindow(Window owner,Desktop desktop,Engine engine)
    {
        Title="Сеть и прокси";Width=650;Height=440;MinWidth=540;MinHeight=380;ShowInTaskbar=false;Icon=owner.Icon;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Margin=new Thickness(24),Spacing=12};
        body.Children.Add(new TextBlock{Text="Подключение движка ipatool",FontSize=22});
        var mode=new ComboBox{ItemsSource=new[]{"Системный прокси / окружение","Напрямую","Указать прокси"},SelectedIndex=engine.Settings.ProxyMode=="Custom"?2:engine.Settings.ProxyMode=="Direct"?1:0};
        var address=new TextBox{Text=engine.Settings.ProxyUrl??"",PlaceholderText="http://127.0.0.1:8080 или socks5://127.0.0.1:1080"};
        address.IsEnabled=mode.SelectedIndex==2;mode.SelectionChanged+=(_,_)=>address.IsEnabled=mode.SelectedIndex==2;
        body.Children.Add(mode);body.Children.Add(address);
        body.Children.Add(new TextBlock{Text="Поддерживаются HTTP, HTTPS и SOCKS5 без сохранения логина и пароля прокси. Маршрут применяется к входу и командам ipatool. Сертификаты TLS проверяются, защитные паузы аккаунта сохраняются.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12});
        var status=new TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxHeight=130};body.Children.Add(status);
        var buttons=new WrapPanel();var save=AppDialogs.Action("Сохранить",IconKind.Save,true);var test=AppDialogs.Action("Проверить сеть",IconKind.Link);buttons.Children.Add(save);buttons.Children.Add(test);body.Children.Add(buttons);Content=new ScrollViewer{Content=body};
        void Save()
        {
            if(desktop.Model.IsBusy)throw new InvalidOperationException("Дождитесь окончания операции.");
            var selected=mode.SelectedIndex==2?"Custom":mode.SelectedIndex==1?"Direct":"System";
            if(selected=="Custom")ProxyConfiguration.Validate(address.Text);
            var previous=engine.Settings.Copy();
            try { engine.Settings.ProxyMode=selected;engine.Settings.ProxyUrl=selected=="Custom"?address.Text.Trim():null;engine.Save(); }
            catch { engine.Settings=previous;throw; }
            status.Text="Настройки сохранены. Защитные ограничения аккаунта не сброшены.";
        }
        save.Click+=(_,_)=>{try{Save();}catch(Exception error){status.Text=error.Message;}};
        test.Click+=async(_,_)=>
        {
            try
            {
                Save();test.IsEnabled=save.IsEnabled=mode.IsEnabled=address.IsEnabled=false;
                using var stop=new System.Threading.CancellationTokenSource();
                void Cancel(object s,EventArgs e)=>stop.Cancel();Closed+=Cancel;
                try { status.Text=await engine.Network.DiagnoseAsync(stop.Token); }
                finally { Closed-=Cancel; }
            }
            catch(OperationCanceledException){status.Text="Проверка отменена.";}
            catch(Exception error){status.Text=error.Message;}
            finally{test.IsEnabled=save.IsEnabled=mode.IsEnabled=true;address.IsEnabled=mode.SelectedIndex==2;}
        };
    }
}
