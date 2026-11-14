using System;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpaDesktop;

public static class AppDialogs
{
    public static Button Action(string text, IconKind icon, bool primary = false)
    {
        var button = new Button { Content = text };
        ActionIcon.SetKind(button, icon);
        if (primary) button.Classes.Add("primary");
        return button;
    }

    public static Window Create(Window owner, string title, double width, double height) => new Window
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = 460,
        MinHeight = 320,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = owner.Background,
        Foreground = owner.Foreground,
        FontFamily = owner.FontFamily,
        FontSize = owner.FontSize,
        ShowInTaskbar = false,
        Icon = owner.Icon
    };
    public static Window Text(Window owner, string title, string details)
    {
        var dialog = Create(owner, title, 740, 530);
        var layout = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("*,Auto") };
        var text = new TextBox { Text = details ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        layout.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var copy = Action("Копировать", IconKind.Layers);
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var model = new TextDialogViewModel(details, value => dialog.Clipboard?.SetTextAsync(value) ?? Task.CompletedTask);
        dialog.DataContext = model;
        ViewBindings.Bind(status, TextBlock.TextProperty, nameof(model.Status));
        copy.Command = model.CopyCommand;
        var close = Action("Закрыть", IconKind.Close); close.IsCancel = true; close.Command = model.CloseCommand;
        model.CloseRequested += () => dialog.Close();
        actions.Children.Add(copy); actions.Children.Add(close); actions.Children.Add(status);
        Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
        return dialog;
    }
    public static Task ShowText(Window owner, string title, string details) => Text(owner, title, details).ShowDialog(owner);
    public static async Task<bool> Confirm(Window owner, string title, string message, CancellationToken cancellation = default)
    {
        var dialog = Create(owner, title, 560, 330);
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var no = Action("Отмена", IconKind.Close); no.IsCancel = true; no.IsDefault = true;
        var yes = Action("Продолжить", IconKind.Check, primary: true);
        var model = new ConfirmationViewModel(message); dialog.DataContext = model;
        no.Command = model.CancelCommand; yes.Command = model.AcceptCommand;
        model.CloseRequested += result => dialog.Close(result);
        actions.Children.Add(no); actions.Children.Add(yes); Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
        var pending = dialog.ShowDialog<bool>(owner);
        using var registration = cancellation.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await pending;
    }
    public static async Task<InstallAccountChoice> ChooseInstallAccount(Window owner, string message, bool canPrepare, CancellationToken cancellation)
    {
        var dialog = Create(owner, "Владелец IPA", 660, 370);
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Action("Отмена", IconKind.Close); cancel.IsCancel = true; cancel.IsDefault = true;
        cancel.Click += (_, _) => dialog.Close(InstallAccountChoice.Cancel);
        var original = Action("Оставить исходные IPA", IconKind.Folder);
        original.Click += (_, _) => dialog.Close(InstallAccountChoice.InstallOriginal);
        actions.Children.Add(cancel); actions.Children.Add(original);
        if (canPrepare)
        {
            var prepare = Action("Подготовить для текущего аккаунта", IconKind.Download, primary: true);
            prepare.Click += (_, _) => dialog.Close(InstallAccountChoice.PrepareForCurrent);
            actions.Children.Add(prepare);
        }
        Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
        var pending = dialog.ShowDialog<InstallAccountChoice>(owner);
        using var registration = cancellation.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(InstallAccountChoice.Cancel)));
        return await pending;
    }
    public static Window Ipa(Window owner, LocalRow row) => Text(owner, "Сведения об IPA — " + row.DisplayName,
        row.FileDetails + Environment.NewLine + "App Store ID: " + (row.AppStoreID ?? "не указан") +
        Environment.NewLine + "Bundle ID: " + (row.BundleID ?? "не указан") +
        Environment.NewLine + "Сборка: " + (row.Build ?? "не указана") + Environment.NewLine + "Размер: " + row.Size);
}
