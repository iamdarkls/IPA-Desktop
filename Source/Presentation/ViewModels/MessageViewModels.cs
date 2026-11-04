using System;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class TextDialogViewModel : ObservableObject
{
    string status = "";
    public string Details { get; }
    public string Status { get => status; private set => Set(ref status, value); }
    public OperationCommand CopyCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public event Action CloseRequested;
    public TextDialogViewModel(string details, Func<string, Task> copy)
    {
        Details = details ?? "";
        CopyCommand = new(async _ => { await copy(Details); Status = "Скопировано"; }, _ => Status = "Выделите текст и нажмите Ctrl+C.");
        CloseCommand = new(() => CloseRequested?.Invoke());
    }
}

public sealed class ConfirmationViewModel
{
    public string Message { get; }
    public DelegateCommand AcceptCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public event Action<bool> CloseRequested;
    public ConfirmationViewModel(string message)
    {
        Message = message;
        AcceptCommand = new(() => CloseRequested?.Invoke(true));
        CancelCommand = new(() => CloseRequested?.Invoke(false));
    }
}
