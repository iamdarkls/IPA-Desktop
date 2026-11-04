using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace IpaDesktop;

public sealed class NetworkSettingsWindow : Window
{
    public NetworkSettingsWindow(Window owner, NetworkSettingsViewModel model)
    {
        DataContext=model;
        Title="Сеть и прокси";Width=650;Height=440;MinWidth=540;MinHeight=380;ShowInTaskbar=false;Icon=owner.Icon;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Margin=new Thickness(24),Spacing=12};
        body.Children.Add(new TextBlock{Text="Подключение движка ipatool",FontSize=22});
        var mode=new ComboBox{ItemsSource=new[]{"Системный прокси / окружение","Напрямую","Указать прокси"},SelectedIndex=model.Mode};
        var address=new TextBox{Text=model.Address,PlaceholderText="http://127.0.0.1:8080 или socks5://127.0.0.1:1080"};
        body.Children.Add(mode);body.Children.Add(address);
        body.Children.Add(new TextBlock{Text="Поддерживаются HTTP, HTTPS и SOCKS5 без сохранения логина и пароля прокси. Маршрут применяется к входу и командам ipatool. Сертификаты TLS проверяются, защитные паузы аккаунта сохраняются.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12});
        var status=new TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap,MaxHeight=130};body.Children.Add(status);
        var buttons=new WrapPanel();var save=AppDialogs.Action("Сохранить",IconKind.Save,true);var test=AppDialogs.Action("Проверить сеть",IconKind.Link);buttons.Children.Add(save);buttons.Children.Add(test);body.Children.Add(buttons);Content=new ScrollViewer{Content=body};
        ViewBindings.Bind(mode,ComboBox.SelectedIndexProperty,nameof(model.Mode),true);
        ViewBindings.Bind(mode,Control.IsEnabledProperty,nameof(model.IsEnabled));
        ViewBindings.Bind(address,TextBox.TextProperty,nameof(model.Address),true);
        ViewBindings.Bind(address,Control.IsEnabledProperty,nameof(model.AddressEnabled));
        ViewBindings.Bind(status,TextBlock.TextProperty,nameof(model.Status));
        save.Command=model.SaveCommand;test.Command=model.TestCommand;
        Closed+=(_,_)=>model.Dispose();
    }
}
