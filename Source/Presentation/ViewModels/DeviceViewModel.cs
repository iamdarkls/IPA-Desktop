using System;

namespace IpaDesktop;

public enum PhoneStatus { Disconnected, Detected, Checking, WaitingForTrust, Ready, Lost }

public sealed class DeviceViewModel : ObservableObject
{
    PhoneStatus status;
    string selectedKey, detail = "Подключите и разблокируйте iPhone — он появится автоматически.";
    bool busy;
    public PhoneStatus Status => status;
    public string SelectedKey => selectedKey;
    public string Title => status switch
    {
        PhoneStatus.Ready => "Готов к установке",
        PhoneStatus.WaitingForTrust => "Ожидает доверия",
        PhoneStatus.Checking => "Проверка связи",
        PhoneStatus.Lost => "Связь потеряна",
        PhoneStatus.Detected => "iPhone подключён",
        _ => "Не подключён"
    };
    public string Detail => detail;
    public string ActionText => status == PhoneStatus.Lost ? "Восстановить связь" : "Проверить связь";
    public DelegateCommand CheckCommand { get; }
    public Action CheckRequested { get; set; }
    public DeviceViewModel() => CheckCommand = new(() => CheckRequested?.Invoke(), () => !busy && selectedKey != null);
    public void Select(string key, bool lost, bool ready)
    {
        if (selectedKey == key) return;
        selectedKey = key;
        SetState(key == null ? (lost ? PhoneStatus.Lost : PhoneStatus.Disconnected) : ready ? PhoneStatus.Ready : lost ? PhoneStatus.Lost : PhoneStatus.Detected,
            key == null ? "Подключите и разблокируйте iPhone. Очередь автоматически не запускается." :
            ready ? "Доверие и связь с выбранным iPhone проверены." : "Перед установкой проверим доверие. Можно проверить связь сейчас.");
        Changed(nameof(SelectedKey)); CheckCommand.Refresh();
    }
    public void SetState(PhoneStatus value, string message)
    {
        status = value; detail = message;
        Changed(nameof(Status)); Changed(nameof(Title)); Changed(nameof(Detail)); Changed(nameof(ActionText)); CheckCommand.Refresh();
    }
    public void SetBusy(bool value) { busy = value; CheckCommand.Refresh(); }
}
