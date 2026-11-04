using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class AppVersionViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    readonly IAppVersions service;
    readonly string appId;
    readonly BackgroundTasks tasks;
    CancellationTokenSource operation;
    string manual = "", status;
    bool loading, closing;
    public string Manual { get => manual; set => Set(ref manual, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsEnabled => !loading && !tasks.IsStopping;
    public string CancelLabel => loading ? "Отменить запрос" : "Закрыть";
    public SelectionViewModel<AppVersionRow> Versions { get; } = new();
    public IReadOnlyList<string> SelectedIDs { get; private set; } = Array.Empty<string>();
    public OperationCommand InsertCommand { get; }
    public OperationCommand DetailsCommand { get; }
    public OperationCommand AddCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public event Action<bool?> CloseRequested;
    public AppVersionViewModel(string appId, List<AppVersionRow> rows, IAppVersions service, CancellationToken cancellation, BackgroundTasks owner = null)
    {
        this.appId = appId; this.service = service; tasks = owner?.CreateChild(error => Status = error.Message, cancellation) ?? new BackgroundTasks(error => Status = error.Message, cancellation);
        Versions.Items = new ObservableCollection<AppVersionRow>(rows);
        Status = "Версий: " + rows.Count + ". Можно ввести известный ID вручную; доступность проверит Apple.";
        InsertCommand = new(_ =>
        {
            var ids = AppVersions.ParseManual(Manual); Versions.SelectedItems.Clear();
            foreach (var id in ids) if (!Versions.Items.Any(x => x.ID == id)) Versions.Items.Insert(0, new AppVersionRow { ID = id });
            foreach (var row in Versions.Items.Where(x => ids.Contains(x.ID))) Versions.SelectedItems.Add(row);
            Manual = ""; Status = "Добавлено ID: " + ids.Count + ". Можно запросить сведения или добавить в очередь.";
            return Task.CompletedTask;
        }, error => Status = error.Message);
        DetailsCommand = new(_ => tasks.Run(async cancellation =>
        {
            var selected = Versions.SelectedItems.ToArray();
            if (selected.Length > 20) { Status = "Выберите не более 20 версий для одного запроса сведений."; return; }
            loading = true; operation = CancellationTokenSource.CreateLinkedTokenSource(tasks.Token); Refresh();
            try
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    operation.Token.ThrowIfCancellationRequested(); var row = selected[i];
                    Status = "Сведения: " + (i + 1) + " / " + selected.Length + " · ID " + row.ID;
                    try { await service.ReadAsync(appId, row, operation.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { row.State = error.Message.Length > 240 ? error.Message.Substring(0, 240) : error.Message; }
                }
                Status = "Обработано версий: " + selected.Length + ". Неизвестные данные не подтверждают совместимость с iPhone.";
            }
            catch (OperationCanceledException) { Status = "Запрос отменён. Полученные сведения сохранены в таблице."; }
            finally { loading = false; operation.Dispose(); operation = null; Refresh(); if (closing) CloseRequested?.Invoke(null); }
        }), error => Status = error.Message);
        AddCommand = new(_ => { SelectedIDs = Versions.SelectedItems.Select(x => x.ID).ToArray(); CloseRequested?.Invoke(true); return Task.CompletedTask; }, error => Status = error.Message);
        CancelCommand = new(() => { if (loading) operation?.Cancel(); else CloseRequested?.Invoke(null); });
        InsertCommand.SetAvailability(() => !loading);
        DetailsCommand.SetAvailability(() => !loading && Versions.SelectedItems.Count > 0);
        AddCommand.SetAvailability(() => !loading && Versions.SelectedItems.Count > 0);
        Versions.SelectionChanged += Refresh;
    }
    void Refresh() { Changed(nameof(IsEnabled)); Changed(nameof(CancelLabel)); InsertCommand.Refresh(); DetailsCommand.Refresh(); AddCommand.Refresh(); }
    public bool RequestClose() { if (!loading) return true; closing = true; operation?.Cancel(); return false; }
    public void Dispose() { tasks.Dispose(); }
    public async ValueTask DisposeAsync() => await tasks.StopAsync();
}
