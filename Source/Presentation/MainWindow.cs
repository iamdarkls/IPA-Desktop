using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace IpaDesktop;
public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
