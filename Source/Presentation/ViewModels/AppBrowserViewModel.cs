using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class AppBrowserViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    readonly DesktopViewModel owner;
    readonly Func<bool> contextValid;
    readonly BackgroundTasks tasks;
    List<LibraryApp> rows = new();
    string query = "", status = "";
    int period, year = DateTime.Now.Year;
    bool enabled;
    public string Title { get; }
    public bool Purchases { get; }
    public int[] Years { get; } = Enumerable.Range(2008, DateTime.Now.Year - 2007).Reverse().ToArray();
    public string Query { get => query; set { if (Set(ref query, value)) Filter(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public int Period { get => period; set { if (Set(ref period, value)) Changed(nameof(YearVisible)); } }
    public int Year { get => year; set => Set(ref year, value); }
    public bool YearVisible => Purchases && Period == 1;
    public bool AllYears => Period == 2;
    public int? SelectedYear => Period == 1 ? Year : null;
    public bool IsEnabled { get => enabled; private set => Set(ref enabled, value); }
    public SelectionViewModel<LibraryApp> Apps { get; } = new();
    public Action ReloadRequested { get; set; }
    public Action<IReadOnlyList<LibraryApp>> AddRequested { get; set; }
    public OperationCommand ReloadCommand { get; }
    public OperationCommand AddCommand { get; }
    public CancellationToken Token => tasks.Token;
    public AppBrowserViewModel(DesktopViewModel owner, string title, bool purchases, Func<bool> contextValid)
    {
        this.owner = owner; this.contextValid = contextValid;
        tasks = owner.Tasks.CreateChild(owner.Error); Title = title; Purchases = purchases;
        ReloadCommand = new(_ => { ReloadRequested?.Invoke(); return Task.CompletedTask; }, owner.Error);
        AddCommand = new(_ => { AddRequested?.Invoke(Apps.SelectedItems.ToArray()); return Task.CompletedTask; }, owner.Error);
        ReloadCommand.SetAvailability(() => IsEnabled);
        AddCommand.SetAvailability(() => IsEnabled && Apps.SelectedItems.Count > 0);
        Apps.SelectionChanged += Refresh;
        owner.PropertyChanged += ContextChanged; owner.Device.PropertyChanged += ContextChanged;
        Refresh();
    }
    void ContextChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!contextValid()) { rows.Clear(); Apps.Items = Array.Empty<LibraryApp>(); Status = "Аккаунт или выбранный iPhone изменился. Откройте список заново."; }
        Refresh();
    }
    void Refresh() { IsEnabled = !owner.IsBusy && !tasks.IsStopping && contextValid(); ReloadCommand.Refresh(); AddCommand.Refresh(); }
    public void SetRows(List<LibraryApp> value, string message) { if (tasks.IsStopping) return; rows = value; Status = message; Filter(); }
    void Filter()
    {
        var text = Query?.Trim() ?? "";
        Apps.Items = rows.Where(x => (x.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (x.ID ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (x.BundleID ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        Refresh();
    }
    public void Dispose() { tasks.Dispose(); owner.PropertyChanged -= ContextChanged; owner.Device.PropertyChanged -= ContextChanged; }
    public async ValueTask DisposeAsync() { Dispose(); await tasks.StopAsync(); }
}
