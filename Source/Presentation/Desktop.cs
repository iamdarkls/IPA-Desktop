using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IpaDesktop;

/// <summary>Composition root: connects the view to application ViewModels and platform UI services.</summary>
public sealed class Desktop
{
    public Window Window { get; }
    public DesktopViewModel Model { get; }
    internal bool IsBusy => Model.IsBusy;
    public Desktop() : this(null, true) { }
    public Desktop(Engine backend, bool checkAtStartup) : this(backend, checkAtStartup, AppDomain.CurrentDomain.BaseDirectory) { }
    public Desktop(Engine backend, bool checkAtStartup, string assetDirectory)
    {
        Window = new MainWindow { Title = "IPA Desktop · " + AppInfo.Version };
        var interaction = new AvaloniaDesktopInteraction(Window);
        var engine = backend ?? new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPA-Desktop"));
        Model = new DesktopViewModel(engine.CreateDesktopServices(), interaction);
        Window.DataContext = Model;
        interaction.BindMenus(Model);
        using (var icon = AssetLoader.Open(new Uri("avares://IPA-Desktop/Assets/App.ico"))) Window.Icon = new WindowIcon(icon);
        using (var icon = AssetLoader.Open(new Uri("avares://IPA-Desktop/Assets/App.ico"))) C<Image>("BrandIcon").Source = new Bitmap(icon);
        Window.Opened += (_, _) =>
        {
            var screen = Window.Screens.ScreenFromWindow(Window);
            if (screen != null) Window.Height = Math.Min(Window.Height, Math.Max(Window.MinHeight, screen.WorkingArea.Height / screen.Scaling));
            Model.Opened(checkAtStartup);
        };
        Window.Closing += (_, args) => { if (!Model.RequestClose()) args.Cancel = true; };
        Window.Closed += (_, _) => { Model.Dispose(); interaction.Dispose(); };
    }
    // Named controls are retained only for the shipping UI smoke test, never used by ViewModels.
    internal T C<T>(string name) where T : Control => Window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing UI control: " + name);
    internal void SetAccountIdentity(string email) => Model.SetAccountIdentity(email);
}
