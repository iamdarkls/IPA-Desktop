using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpaDesktop;

public static class AppDialogs
{
    public static Window Create(Window owner, string title, double width, double height) => new Window
    {
        Title = title, Width = width, Height = height, MinWidth = 460, MinHeight = 320,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background,
        Foreground = owner.Foreground, FontFamily = owner.FontFamily, FontSize = owner.FontSize,
        ShowInTaskbar = false, Icon = owner.Icon
    };
    public static Window Text(Window owner, string title, string details)
    {
        var dialog = Create(owner, title, 740, 530);
        var layout = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("*,Auto") };
        var text = new TextBox { Text = details ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        layout.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,14,0,0) };
        var copy = new Button { Content = "Копировать" };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        copy.Click += async delegate
        {
            try { if (dialog.Clipboard != null) await dialog.Clipboard.SetTextAsync(text.Text ?? ""); status.Text = "Скопировано"; }
            catch (Exception) { status.Text = "Выделите текст и нажмите Ctrl+C."; }
        };
        var close = new Button { Content = "Закрыть", IsCancel = true };
        close.Click += delegate { dialog.Close(); };
        actions.Children.Add(copy); actions.Children.Add(close); actions.Children.Add(status);
        Grid.SetRow(actions,1); layout.Children.Add(actions); dialog.Content = layout;
        return dialog;
    }
    public static Task ShowText(Window owner, string title, string details) => Text(owner,title,details).ShowDialog(owner);
    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var dialog = Create(owner,title,560,330);
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Content = "Отмена", IsCancel = true, IsDefault = true };
        var yes = new Button { Content = "Продолжить", Background = Brush.Parse("#3878E5") };
        no.Click += delegate { dialog.Close(false); }; yes.Click += delegate { dialog.Close(true); };
        actions.Children.Add(no); actions.Children.Add(yes); Grid.SetRow(actions,1); layout.Children.Add(actions); dialog.Content=layout;
        return await dialog.ShowDialog<bool>(owner);
    }
    public static Window Ipa(Window owner, LocalRow row) => Text(owner,"Сведения об IPA — " + row.DisplayName,
        row.FileDetails + Environment.NewLine + "App Store ID: " + (row.AppStoreID ?? "не указан") +
        Environment.NewLine + "Bundle ID: " + (row.BundleID ?? "не указан") +
        Environment.NewLine + "Сборка: " + (row.Build ?? "не указана") + Environment.NewLine + "Размер: " + row.Size);
}
