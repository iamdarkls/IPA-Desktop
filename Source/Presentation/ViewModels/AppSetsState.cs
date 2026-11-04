using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class AppSetsViewModel
{
    public SelectionViewModel<AppSet> AppSets { get; } = new();
    string setName = "";
    public string SetName { get => setName; set { if (Set(ref setName, value)) RefreshCommands(); } }
    string setsCount = "Наборов пока нет";
    public string SetsCount { get => setsCount; set { if (Set(ref setsCount, value)) RefreshCommands(); } }
    string setStatus = "Наборы хранят только ID. Скачивание выполняется под текущим Apple Account.";
    public string SetStatus { get => setStatus; set { if (Set(ref setStatus, value)) RefreshCommands(); } }
    string setPreview = "Отметьте один или несколько наборов.";
    public string SetPreview { get => setPreview; set { if (Set(ref setPreview, value)) RefreshCommands(); } }
    bool addToSetEnabled = true;
    public bool AddToSetEnabled { get => addToSetEnabled; set { if (Set(ref addToSetEnabled, value)) RefreshCommands(); } }
    bool saveSetEnabled = true;
    public bool SaveSetEnabled { get => saveSetEnabled; set { if (Set(ref saveSetEnabled, value)) RefreshCommands(); } }
    bool editSetEnabled = true;
    public bool EditSetEnabled { get => editSetEnabled; set { if (Set(ref editSetEnabled, value)) RefreshCommands(); } }
    bool useSetEnabled = true;
    public bool UseSetEnabled { get => useSetEnabled; set { if (Set(ref useSetEnabled, value)) RefreshCommands(); } }
    bool deleteSetEnabled = true;
    public bool DeleteSetEnabled { get => deleteSetEnabled; set { if (Set(ref deleteSetEnabled, value)) RefreshCommands(); } }
    bool installSetEnabled = true;
    public bool InstallSetEnabled { get => installSetEnabled; set { if (Set(ref installSetEnabled, value)) RefreshCommands(); } }
    string useSetLabel = "В очередь скачивания (0)";
    public string UseSetLabel { get => useSetLabel; set { if (Set(ref useSetLabel, value)) RefreshCommands(); } }
    string deleteSetLabel = "Удалить наборы (0)";
    public string DeleteSetLabel { get => deleteSetLabel; set { if (Set(ref deleteSetLabel, value)) RefreshCommands(); } }
    bool setNameEnabled = true;
    public bool SetNameEnabled { get => setNameEnabled; set { if (Set(ref setNameEnabled, value)) RefreshCommands(); } }
    public OperationCommand AddToSetCommand { get; private set; }
    public OperationCommand SaveSetCommand { get; private set; }
    public OperationCommand EditSetCommand { get; private set; }
    public OperationCommand DeleteSetCommand { get; private set; }
    public OperationCommand UseSetCommand { get; private set; }
    public OperationCommand InstallSetCommand { get; private set; }
    public override void ConfigureCommands()
    {
        AddToSetCommand?.SetAvailability(() => !busy && AddToSetEnabled);
        SaveSetCommand?.SetAvailability(() => !busy && SaveSetEnabled);
        EditSetCommand?.SetAvailability(() => !busy && EditSetEnabled);
        DeleteSetCommand?.SetAvailability(() => !busy && DeleteSetEnabled);
        UseSetCommand?.SetAvailability(() => !busy && UseSetEnabled);
        InstallSetCommand?.SetAvailability(() => !busy && InstallSetEnabled);
    }
    public override void RefreshCommands()
    {
        AddToSetCommand?.Refresh();
        SaveSetCommand?.Refresh();
        EditSetCommand?.Refresh();
        DeleteSetCommand?.Refresh();
        UseSetCommand?.Refresh();
        InstallSetCommand?.Refresh();
    }

}
