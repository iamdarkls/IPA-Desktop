using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class LibraryViewModel
{
    bool deviceAppsEnabled = true;
    public bool DeviceAppsEnabled { get => deviceAppsEnabled; set { if (Set(ref deviceAppsEnabled, value)) RefreshCommands(); } }
    bool purchaseHistoryEnabled = true;
    public bool PurchaseHistoryEnabled { get => purchaseHistoryEnabled; set { if (Set(ref purchaseHistoryEnabled, value)) RefreshCommands(); } }
    bool networkSettingsEnabled = true;
    public bool NetworkSettingsEnabled { get => networkSettingsEnabled; set { if (Set(ref networkSettingsEnabled, value)) RefreshCommands(); } }
    public OperationCommand DeviceAppsCommand { get; private set; }
    public OperationCommand PurchaseHistoryCommand { get; private set; }
    public OperationCommand NetworkSettingsCommand { get; private set; }
    public override void ConfigureCommands()
    {
        DeviceAppsCommand?.SetAvailability(() => !busy && DeviceAppsEnabled);
        PurchaseHistoryCommand?.SetAvailability(() => !busy && PurchaseHistoryEnabled);
        NetworkSettingsCommand?.SetAvailability(() => !busy && NetworkSettingsEnabled);
    }
    public override void RefreshCommands()
    {
        DeviceAppsCommand?.Refresh();
        PurchaseHistoryCommand?.Refresh();
        NetworkSettingsCommand?.Refresh();
    }

}
