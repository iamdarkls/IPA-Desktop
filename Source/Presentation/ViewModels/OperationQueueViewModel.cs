using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace IpaDesktop;

public enum QueueItemState { Waiting, Running, Succeeded, Failed, Paused, Interrupted, Uncertain }
public sealed record QueueSeed(string Key, string Name, string Version);

public sealed class QueueItemViewModel : ObservableObject
{
    public QueueSeed Seed { get; }
    public QueueBatchViewModel Batch { get; }
    QueueItemState state;
    double? percent;
    string detail = "Ожидает запуска";
    public QueueItemViewModel(QueueSeed seed, QueueBatchViewModel batch) { Seed = seed; Batch = batch; }
    public string Name => Seed.Name;
    public string Version => String.IsNullOrWhiteSpace(Seed.Version) ? "Не указана" : Seed.Version;
    public string Kind => Batch.Kind;
    public string Target => Batch.TargetLabel;
    public QueueItemState State => state;
    public double Progress => state == QueueItemState.Succeeded ? 100 : percent ?? 0;
    public bool Indeterminate => state == QueueItemState.Running && !percent.HasValue;
    public string ProgressText => state == QueueItemState.Succeeded ? "100%" : percent.HasValue ? percent.Value.ToString("0") + "%" : "—";
    public string Detail => detail;
    public string Status => state switch
    {
        QueueItemState.Running => "Выполняется",
        QueueItemState.Succeeded => "Готово",
        QueueItemState.Failed => "Ошибка",
        QueueItemState.Paused => "Не запущено",
        QueueItemState.Interrupted => "Прервано",
        QueueItemState.Uncertain => "Проверьте на iPhone",
        _ => "Ожидает"
    };
    public void Update(QueueItemState value, string message, double? progress = null)
    {
        state = value; detail = message; percent = value == QueueItemState.Waiting ? null : value == QueueItemState.Succeeded ? 100 : progress ?? percent;
        foreach (var property in new[] { nameof(State), nameof(Status), nameof(Detail), nameof(Progress), nameof(ProgressText), nameof(Indeterminate) }) Changed(property);
    }
    public void Measure(double? value, string metrics)
    {
        if (state != QueueItemState.Running) return;
        percent = value;
        if (!String.IsNullOrWhiteSpace(metrics)) detail = metrics;
        Changed(nameof(Progress)); Changed(nameof(ProgressText)); Changed(nameof(Indeterminate)); Changed(nameof(Detail));
    }
}

public sealed class QueueBatchViewModel
{
    public string Kind { get; init; }
    public string TargetKey { get; init; }
    public string TargetLabel { get; init; }
    public List<QueueItemViewModel> Items { get; } = new();
    public Action<IReadOnlyCollection<string>> Resume { get; init; }
}

/// <summary>In-memory queue history. Successful items are never included in a resume request.</summary>
public sealed class OperationQueueViewModel : ObservableObject
{
    public ObservableCollection<QueueItemViewModel> Items { get; } = new();
    IReadOnlyList<QueueItemViewModel> active = Array.Empty<QueueItemViewModel>();
    QueueItemViewModel current, selected;
    bool busy;
    public Func<QueueBatchViewModel, bool> CanResumeBatch { get; set; } = _ => false;
    public QueueItemViewModel Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) { Changed(nameof(SelectionDetail)); Refresh(); } }
    }
    public string TabTitle => Items.Count == 0 ? "Очередь" : "Очередь (" + Items.Count(x => x.State != QueueItemState.Succeeded) + ")";
    public string Summary => "Всего: " + Items.Count + " · готово: " + Items.Count(x => x.State == QueueItemState.Succeeded) +
        " · осталось: " + Items.Count(x => x.State != QueueItemState.Succeeded);
    public string EmptyMessage => "Здесь появятся скачивания и установки. История сохраняется до закрытия программы.";
    public bool IsEmpty => Items.Count == 0;
    public string SelectionDetail => selected?.Detail ?? "Выберите строку, чтобы увидеть результат и доступное действие.";
    public string ResumeHint => busy ? "Выполняется операция. Можно отменить её кнопкой внизу; продолжение всегда запускается вручную." : selected == null ? "Выберите операцию в очереди." :
        !selected.Batch.Items.Any(x => x.State != QueueItemState.Succeeded) ? "Эта очередь завершена." :
        !CanResumeBatch(selected.Batch) ? (selected.Batch.Kind == "Установка" ? "Восстановите связь с тем же iPhone перед продолжением." : "Проверьте аккаунт и ограничения входа перед продолжением.") :
        "Продолжение — вручную. Готовые элементы повторяться не будут; после обрыва сначала проверьте результат на iPhone.";
    public DelegateCommand ResumeCommand { get; }
    public DelegateCommand ClearCompletedCommand { get; }
    public OperationQueueViewModel()
    {
        ResumeCommand = new(() => selected.Batch.Resume(selected.Batch.Items.Where(x => x.State != QueueItemState.Succeeded).Select(x => x.Seed.Key).ToArray()),
            () => !busy && selected != null && selected.Batch.Items.Any(x => x.State != QueueItemState.Succeeded) && CanResumeBatch(selected.Batch));
        ClearCompletedCommand = new(() =>
        {
            foreach (var item in Items.Where(x => x.State == QueueItemState.Succeeded).ToArray()) Items.Remove(item);
            if (selected != null && !Items.Contains(selected)) Selected = Items.FirstOrDefault();
            Refresh();
        }, () => !busy && Items.Any(x => x.State == QueueItemState.Succeeded));
    }
    public QueueBatchViewModel Start(string kind, IEnumerable<QueueSeed> seeds, string targetKey, string targetLabel,
        Action<IReadOnlyCollection<string>> resume, QueueBatchViewModel previous = null)
    {
        var batch = previous ?? new QueueBatchViewModel { Kind = kind, TargetKey = targetKey, TargetLabel = targetLabel, Resume = resume };
        var rows = new List<QueueItemViewModel>();
        foreach (var seed in seeds)
        {
            var row = previous?.Items.FirstOrDefault(x => x.Seed.Key == seed.Key && x.State != QueueItemState.Succeeded);
            if (row == null) { row = new(seed, batch); batch.Items.Add(row); Items.Add(row); }
            row.Update(QueueItemState.Waiting, "Ожидает запуска"); rows.Add(row);
        }
        active = rows; current = null; Selected = rows.FirstOrDefault(); Refresh();
        return batch;
    }
    public void Begin(int position)
    {
        current = active[position - 1]; Selected = current; current.Update(QueueItemState.Running, "Ожидание прогресса от утилиты"); Refresh();
    }
    public void Result(Exception error)
    {
        if (current == null) return;
        var failure = error == null ? null : ErrorLocalizer.Describe(error);
        current.Update(error == null ? QueueItemState.Succeeded : error is DeviceConnectionException ? QueueItemState.Uncertain : QueueItemState.Failed,
            error == null ? "Операция завершена успешно." : failure.Description +
                (error is DeviceConnectionException ? "\nПеред повтором проверьте, установилось ли приложение на iPhone." : ""));
        if (error == null)
        {
            // A legacy retry action may start another batch. Resolve the same unfinished IPA in older batches too.
            foreach (var row in Items.Where(x => x != current && x.State != QueueItemState.Succeeded && x.Seed.Key == current.Seed.Key &&
                         x.Batch.Kind == current.Batch.Kind && x.Batch.TargetKey == current.Batch.TargetKey))
                row.Update(QueueItemState.Succeeded, "Операция завершена при повторе в другой очереди.", 100);
        }
        current = null; Refresh();
    }
    public void Stop(string reason, bool canceled)
    {
        if (current != null && current.State == QueueItemState.Running)
            current.Update(canceled ? QueueItemState.Interrupted : QueueItemState.Uncertain, reason + " Проверьте результат перед повтором.");
        foreach (var item in active.Where(x => x.State == QueueItemState.Waiting)) item.Update(QueueItemState.Paused, reason + " Автоматического продолжения не будет.");
        current = null; active = Array.Empty<QueueItemViewModel>(); Refresh();
    }
    public void Measure(double? percent, string metrics) { current?.Measure(percent, metrics); Changed(nameof(SelectionDetail)); }
    public void SetBusy(bool value) { busy = value; Refresh(); }
    public void Refresh()
    {
        Changed(nameof(TabTitle)); Changed(nameof(Summary)); Changed(nameof(IsEmpty)); Changed(nameof(SelectionDetail)); Changed(nameof(ResumeHint));
        ResumeCommand.Refresh(); ClearCompletedCommand.Refresh();
    }
}
