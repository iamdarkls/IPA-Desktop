using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.LogicalTree;

namespace IpaDesktop;

/// <summary>Window ownership, platform dialogs, dispatch and timers; no application state.</summary>
internal sealed class AvaloniaDesktopInteraction : IDesktopInteraction, IDisposable
{
    readonly Window window;
    Window journal;
    bool disposed;
    public AvaloniaDesktopInteraction(Window window) => this.window = window;
    public void Post(Action action) => Dispatcher.UIThread.Post(() => { if (!disposed) action(); });
    public IDisposable Repeat(TimeSpan interval, Action tick) => new UiTimer(interval, tick);
    sealed class UiTimer : IDisposable
    {
        readonly DispatcherTimer timer;
        public UiTimer(TimeSpan interval, Action tick) { timer = new() { Interval = interval }; timer.Tick += (_, _) => tick(); timer.Start(); }
        public void Dispose() => timer.Stop();
    }
    static readonly string[] MenuNames = { "MoreAccount", "MoreSearch", "MoreDownloads", "MoreDevice", "MoreFiles", "MoreGui", "MoreEngine", "MoreTools", "MoreQueue" };
    public void BindMenus(DesktopViewModel model)
    {
        foreach (var name in MenuNames)
            if (window.FindControl<Button>(name)?.Flyout is Flyout { Content: Control content }) content.DataContext = model;
    }
    public void CloseMenus()
    {
        foreach (var name in MenuNames)
            window.FindControl<Button>(name)?.Flyout?.Hide();
    }
    public void Close() => window.Close();
    public void ShowJournal(DesktopViewModel model)
    {
        if (journal != null) { journal.Activate(); return; }
        journal = new JournalWindow(window, model); journal.Closed += (_, _) => journal = null; journal.Show(window);
    }
    public Task ShowText(string title, string details) => AppDialogs.ShowText(window, title, details);
    public Task<bool> Confirm(string title, string message) => AppDialogs.Confirm(window, title, message);
    public async Task<string> Login(IAppleLoginService service, CancellationToken cancellation)
    {
        using var dialog = new AppleLoginWindow(window, service, cancellation);
        return await dialog.Window.ShowDialog<bool>(window) ? dialog.SignedInEmail : null;
    }
    public async Task<IReadOnlyList<string>> SelectVersions(string name, string id, List<AppVersionRow> rows, AppVersions service, CancellationToken cancellation)
    {
        using var dialog = new AppVersionWindow(window, name, id, rows, service, cancellation);
        return await dialog.Window.ShowDialog<bool>(window) ? dialog.SelectedIDs : null;
    }
    public Task<bool> EditSet(string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save) => AppSetEditor.Create(window, name, apps, save).ShowDialog<bool>(window);
    public void ShowLibrary(AppBrowserViewModel model) => new AppBrowserWindow(window, model).Show(window);
    public Task ShowNetwork(NetworkSettingsViewModel model) => new NetworkSettingsWindow(window, model).ShowDialog(window);
    public Task ShowIpa(LocalRow row) => AppDialogs.Ipa(window, row).ShowDialog(window);
    public async Task<string[]> ImportFiles()
    {
        var selected = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить IPA",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("IPA") { Patterns = new[] { "*.ipa" } } }
        });
        return selected.Select(x => x.TryGetLocalPath()).Where(x => x != null).ToArray();
    }
    public async Task<bool> ExportText(string suggestedName, string text)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт диагностики",
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { new FilePickerFileType("Текстовый отчёт") { Patterns = new[] { "*.txt" } } }
        });
        if (file == null) return false;
        await using var stream = await file.OpenWriteAsync();
        using var writer = new StreamWriter(stream, Encoding.UTF8); await writer.WriteAsync(text); return true;
    }
    public void Dispose()
    {
        disposed = true; journal?.Close();
        foreach (var control in window.GetLogicalDescendants().OfType<Control>())
            if (SelectionBinding.GetItems(control) != null) SelectionBinding.SetItems(control, null);
    }
}
