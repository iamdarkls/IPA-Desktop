using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class StorageViewModel
{
    public SelectionViewModel<LocalRow> StorageFiles { get; } = new();
    string storageSummary = "Подсчёт файлов…";
    public string StorageSummary { get => storageSummary; set { if (Set(ref storageSummary, value)) RefreshCommands(); } }
    bool selectOldEnabled = true;
    public bool SelectOldEnabled { get => selectOldEnabled; set { if (Set(ref selectOldEnabled, value)) RefreshCommands(); } }
    bool deleteStorageEnabled = true;
    public bool DeleteStorageEnabled { get => deleteStorageEnabled; set { if (Set(ref deleteStorageEnabled, value)) RefreshCommands(); } }
    public OperationCommand RefreshStorageCommand { get; private set; }
    public OperationCommand DeleteStorageCommand { get; private set; }
    public OperationCommand SelectOldCommand { get; private set; }
    public override void ConfigureCommands()
    {
        RefreshStorageCommand?.SetAvailability(() => !busy && true);
        DeleteStorageCommand?.SetAvailability(() => !busy && DeleteStorageEnabled);
        SelectOldCommand?.SetAvailability(() => !busy && SelectOldEnabled);
    }
    public override void RefreshCommands()
    {
        RefreshStorageCommand?.Refresh();
        DeleteStorageCommand?.Refresh();
        SelectOldCommand?.Refresh();
    }

}
