using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class UpdatesViewModel
{
    string deviceComponents = "", deviceUpdateStatus = "Обновления ещё не проверялись.", deviceReleaseNotes = "";
    public string DeviceComponents { get => deviceComponents; set => Set(ref deviceComponents, value); }
    public string DeviceUpdateStatus { get => deviceUpdateStatus; set => Set(ref deviceUpdateStatus, value); }
    public string DeviceReleaseNotes { get => deviceReleaseNotes; set => Set(ref deviceReleaseNotes, value); }
    public void RefreshInstalledTools() => DeviceComponents = toolUpdates.DescribeInstalled();
    bool soundsEnabled = false;
    public bool SoundsEnabled { get => soundsEnabled; set { if (Set(ref soundsEnabled, value)) RefreshCommands(); } }
    bool checkGuiUpdates = false;
    public bool CheckGuiUpdates { get => checkGuiUpdates; set { if (Set(ref checkGuiUpdates, value)) RefreshCommands(); } }
    bool autoUpdateDeviceTools = false;
    public bool AutoUpdateDeviceTools { get => autoUpdateDeviceTools; set { if (Set(ref autoUpdateDeviceTools, value)) RefreshCommands(); } }
    bool autoUpdate = false;
    public bool AutoUpdate { get => autoUpdate; set { if (Set(ref autoUpdate, value)) RefreshCommands(); } }
    bool installGuiUpdateEnabled = false;
    public bool InstallGuiUpdateEnabled { get => installGuiUpdateEnabled; set { if (Set(ref installGuiUpdateEnabled, value)) RefreshCommands(); } }
    bool rollbackDevicesEnabled = true;
    public bool RollbackDevicesEnabled { get => rollbackDevicesEnabled; set { if (Set(ref rollbackDevicesEnabled, value)) RefreshCommands(); } }
    string guiVersionStatus = "Проверка версии…";
    public string GuiVersionStatus { get => guiVersionStatus; set { if (Set(ref guiVersionStatus, value)) RefreshCommands(); } }
    string installGuiUpdateLabel = "Установить обновление";
    public string InstallGuiUpdateLabel { get => installGuiUpdateLabel; set { if (Set(ref installGuiUpdateLabel, value)) RefreshCommands(); } }
    string updateNotice = "";
    public string UpdateNotice { get => updateNotice; set { if (Set(ref updateNotice, value)) RefreshCommands(); } }
    bool updateNoticeVisible = false;
    public bool UpdateNoticeVisible { get => updateNoticeVisible; set { if (Set(ref updateNoticeVisible, value)) RefreshCommands(); } }
    bool engineUpdateNeeded;
    public bool EngineUpdateNeeded { get => engineUpdateNeeded; private set => Set(ref engineUpdateNeeded, value); }
    string engineVersion = "Движок не установлен";
    public string EngineVersion { get => engineVersion; set { if (Set(ref engineVersion, value)) RefreshCommands(); } }
    string deviceToolsVersion = "Комплект не установлен";
    public string DeviceToolsVersion { get => deviceToolsVersion; set { if (Set(ref deviceToolsVersion, value)) RefreshCommands(); } }
    bool checkGuiUpdatesEnabled = true;
    public bool CheckGuiUpdatesEnabled { get => checkGuiUpdatesEnabled; set { if (Set(ref checkGuiUpdatesEnabled, value)) RefreshCommands(); } }
    bool autoUpdateEnabled = true;
    public bool AutoUpdateEnabled { get => autoUpdateEnabled; set { if (Set(ref autoUpdateEnabled, value)) RefreshCommands(); } }
    bool autoUpdateDeviceToolsEnabled = true;
    public bool AutoUpdateDeviceToolsEnabled { get => autoUpdateDeviceToolsEnabled; set { if (Set(ref autoUpdateDeviceToolsEnabled, value)) RefreshCommands(); } }
    public OperationCommand CheckGuiUpdateCommand { get; private set; }
    public OperationCommand InstallGuiUpdateCommand { get; private set; }
    public OperationCommand OpenGuiReleasesCommand { get; private set; }
    public OperationCommand OpenReleaseCommand { get; private set; }
    public OperationCommand CheckDeviceUpdateCommand { get; private set; }
    public OperationCommand RollbackDevicesCommand { get; private set; }
    public OperationCommand OpenDeviceReleasesCommand { get; private set; }
    public OperationCommand CheckUpdateCommand { get; private set; }
    public OperationCommand RollbackCommand { get; private set; }
    public override void ConfigureCommands()
    {
        CheckGuiUpdateCommand?.SetAvailability(() => !busy && true);
        InstallGuiUpdateCommand?.SetAvailability(() => !busy && InstallGuiUpdateEnabled);
        OpenGuiReleasesCommand?.SetAvailability(() => true);
        OpenReleaseCommand?.SetAvailability(() => true);
        CheckDeviceUpdateCommand?.SetAvailability(() => !busy && true);
        RollbackDevicesCommand?.SetAvailability(() => !busy && RollbackDevicesEnabled);
        OpenDeviceReleasesCommand?.SetAvailability(() => true);
        CheckUpdateCommand?.SetAvailability(() => !busy && true);
        RollbackCommand?.SetAvailability(() => RuntimePlatform.Windows && !busy && !String.IsNullOrEmpty(settings.Settings.Previous));
    }
    public override void RefreshCommands()
    {
        CheckGuiUpdateCommand?.Refresh();
        InstallGuiUpdateCommand?.Refresh();
        OpenGuiReleasesCommand?.Refresh();
        OpenReleaseCommand?.Refresh();
        CheckDeviceUpdateCommand?.Refresh();
        RollbackDevicesCommand?.Refresh();
        OpenDeviceReleasesCommand?.Refresh();
        CheckUpdateCommand?.Refresh();
        RollbackCommand?.Refresh();
    }

}
