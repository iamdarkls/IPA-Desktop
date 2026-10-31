using System;

namespace IpaDesktop;

public sealed class DesktopViewModel : ObservableObject
{
    bool busy;
    string journal = "", status = "Готово", operationDetail = "", transferDetail = "", progressTip = "";
    double progress;
    bool indeterminate, followJournal = true;
    public bool FollowJournal { get => followJournal; set => Set(ref followJournal, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string OperationDetail { get => operationDetail; set => Set(ref operationDetail, value); }
    public string TransferDetail { get => transferDetail; set => Set(ref transferDetail, value); }
    public string ProgressTip { get => progressTip; set => Set(ref progressTip, value); }
    public double Progress { get => progress; set => Set(ref progress, value); }
    public bool Indeterminate { get => indeterminate; set => Set(ref indeterminate, value); }
    public DeviceViewModel Device { get; } = new();
    public OperationQueueViewModel Queue { get; } = new();
    public bool IsBusy
    {
        get => busy;
        set
        {
            if (!Set(ref busy, value)) return;
            Device.SetBusy(value); Queue.SetBusy(value); CancelCommand.Refresh();
        }
    }
    public string JournalText { get => journal; private set => Set(ref journal, value); }
    public Action CancelRequested { get; set; }
    public Action JournalRequested { get; set; }
    public Action ExportRequested { get; set; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand ShowJournalCommand { get; }
    public DelegateCommand ExportCommand { get; }
    public DesktopViewModel()
    {
        CancelCommand = new(() => CancelRequested?.Invoke(), () => IsBusy);
        ShowJournalCommand = new(() => JournalRequested?.Invoke());
        ExportCommand = new(() => ExportRequested?.Invoke());
    }
    public void AppendJournal(string text)
    {
        if (String.IsNullOrEmpty(text)) return;
        var combined = journal + text;
        JournalText = combined.Length > 120000 ? combined.Substring(combined.Length - 100000) : combined;
    }
}
