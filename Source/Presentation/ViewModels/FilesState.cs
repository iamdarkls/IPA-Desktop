using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class FilesViewModel
{
    public SelectionViewModel<LocalRow> Files { get; } = new();
    public SelectionViewModel<LocalFileAccount> FileAccount { get; } = new();
    public SelectionViewModel<DeviceRow> DeviceList { get; } = new();
    string deviceDetectionStatusTip = "";
    public string DeviceDetectionStatusTip { get => deviceDetectionStatusTip; set { if (Set(ref deviceDetectionStatusTip, value)) RefreshCommands(); } }
    string retryInstallsTip = "";
    public string RetryInstallsTip { get => retryInstallsTip; set { if (Set(ref retryInstallsTip, value)) RefreshCommands(); } }
    string installCountTip = "";
    public string InstallCountTip { get => installCountTip; set { if (Set(ref installCountTip, value)) RefreshCommands(); } }
    string wifiStatus = "USB";
    public string WifiStatus { get => wifiStatus; set { if (Set(ref wifiStatus, value)) RefreshCommands(); } }
    bool searchWifi = false;
    public bool SearchWifi { get => searchWifi; set { if (Set(ref searchWifi, value)) RefreshCommands(); } }
    string checkAppleSupportLabel = "Проверить драйвер";
    public string CheckAppleSupportLabel { get => checkAppleSupportLabel; set { if (Set(ref checkAppleSupportLabel, value)) RefreshCommands(); } }
    bool selectAllFilesEnabled = true;
    public bool SelectAllFilesEnabled { get => selectAllFilesEnabled; set { if (Set(ref selectAllFilesEnabled, value)) RefreshCommands(); } }
    string selectAllFilesLabel = "Выбрать все (0)";
    public string SelectAllFilesLabel { get => selectAllFilesLabel; set { if (Set(ref selectAllFilesLabel, value)) RefreshCommands(); } }
    bool searchWifiEnabled = true;
    public bool SearchWifiEnabled { get => searchWifiEnabled; set { if (Set(ref searchWifiEnabled, value)) RefreshCommands(); } }
    bool wifiHelpEnabled = true;
    public bool WifiHelpEnabled { get => wifiHelpEnabled; set { if (Set(ref wifiHelpEnabled, value)) RefreshCommands(); } }
    bool fileInfoEnabled = true;
    public bool FileInfoEnabled { get => fileInfoEnabled; set { if (Set(ref fileInfoEnabled, value)) RefreshCommands(); } }
    bool deviceInfoEnabled = true;
    public bool DeviceInfoEnabled { get => deviceInfoEnabled; set { if (Set(ref deviceInfoEnabled, value)) RefreshCommands(); } }
    bool retryInstallsEnabled = true;
    public bool RetryInstallsEnabled { get => retryInstallsEnabled; set { if (Set(ref retryInstallsEnabled, value)) RefreshCommands(); } }
    string retryInstallsLabel = "Повторить ошибки (0)";
    public string RetryInstallsLabel { get => retryInstallsLabel; set { if (Set(ref retryInstallsLabel, value)) RefreshCommands(); } }
    bool forgetInstallErrorsEnabled = true;
    public bool ForgetInstallErrorsEnabled { get => forgetInstallErrorsEnabled; set { if (Set(ref forgetInstallErrorsEnabled, value)) RefreshCommands(); } }
    bool chosenFilesEnabled = true;
    public bool ChosenFilesEnabled { get => chosenFilesEnabled; set { if (Set(ref chosenFilesEnabled, value)) RefreshCommands(); } }
    bool addInstallEnabled = true;
    public bool AddInstallEnabled { get => addInstallEnabled; set { if (Set(ref addInstallEnabled, value)) RefreshCommands(); } }
    bool deleteFilesEnabled = true;
    public bool DeleteFilesEnabled { get => deleteFilesEnabled; set { if (Set(ref deleteFilesEnabled, value)) RefreshCommands(); } }
    string deleteFilesLabel = "В корзину (0)";
    public string DeleteFilesLabel { get => deleteFilesLabel; set { if (Set(ref deleteFilesLabel, value)) RefreshCommands(); } }
    bool clearInstallEnabled = true;
    public bool ClearInstallEnabled { get => clearInstallEnabled; set { if (Set(ref clearInstallEnabled, value)) RefreshCommands(); } }
    bool installFilesEnabled = true;
    public bool InstallFilesEnabled { get => installFilesEnabled; set { if (Set(ref installFilesEnabled, value)) RefreshCommands(); } }
    IEnumerable<LocalRow> chosenFilesOptions = Array.Empty<LocalRow>();
    public IEnumerable<LocalRow> ChosenFilesOptions { get => chosenFilesOptions; set { if (Set(ref chosenFilesOptions, value)) RefreshCommands(); } }
    string installCount = "Двойной клик — выбрать IPA";
    public string InstallCount { get => installCount; set { if (Set(ref installCount, value)) RefreshCommands(); } }
    bool installSelectionVisible = false;
    public bool InstallSelectionVisible { get => installSelectionVisible; set { if (Set(ref installSelectionVisible, value)) RefreshCommands(); } }
    string installFilesLabel = "Установить (0)";
    public string InstallFilesLabel { get => installFilesLabel; set { if (Set(ref installFilesLabel, value)) RefreshCommands(); } }
    string filesSummary = "Чтение файлов…";
    public string FilesSummary { get => filesSummary; set { if (Set(ref filesSummary, value)) RefreshCommands(); } }
    bool filesEmptyVisible = false;
    public bool FilesEmptyVisible { get => filesEmptyVisible; set { if (Set(ref filesEmptyVisible, value)) RefreshCommands(); } }
    public OperationCommand SelectAllFilesCommand { get; private set; }
    public OperationCommand AddInstallCommand { get; private set; }
    public OperationCommand ClearInstallCommand { get; private set; }
    public OperationCommand FileInfoCommand { get; private set; }
    public OperationCommand DeviceInfoCommand { get; private set; }
    public OperationCommand RefreshFilesCommand { get; private set; }
    public OperationCommand DeleteFilesCommand { get; private set; }
    public OperationCommand ImportFileCommand { get; private set; }
    public OperationCommand CheckAppleSupportCommand { get; private set; }
    public OperationCommand OpenAppleSupportHelpCommand { get; private set; }
    public OperationCommand WifiHelpCommand { get; private set; }
    public OperationCommand InstallFilesCommand { get; private set; }
    public OperationCommand RetryInstallsCommand { get; private set; }
    public OperationCommand ForgetInstallErrorsCommand { get; private set; }
    public override void ConfigureCommands()
    {
        SelectAllFilesCommand?.SetAvailability(() => !busy && SelectAllFilesEnabled);
        AddInstallCommand?.SetAvailability(() => !busy && AddInstallEnabled);
        ClearInstallCommand?.SetAvailability(() => !busy && ClearInstallEnabled);
        FileInfoCommand?.SetAvailability(() => !busy && FileInfoEnabled);
        DeviceInfoCommand?.SetAvailability(() => !busy && DeviceInfoEnabled);
        RefreshFilesCommand?.SetAvailability(() => !busy && true);
        DeleteFilesCommand?.SetAvailability(() => !busy && DeleteFilesEnabled);
        ImportFileCommand?.SetAvailability(() => !busy && true);
        CheckAppleSupportCommand?.SetAvailability(() => !busy && true);
        OpenAppleSupportHelpCommand?.SetAvailability(() => !busy && true);
        WifiHelpCommand?.SetAvailability(() => !busy && WifiHelpEnabled);
        InstallFilesCommand?.SetAvailability(() => !busy && InstallFilesEnabled);
        RetryInstallsCommand?.SetAvailability(() => !busy && RetryInstallsEnabled);
        ForgetInstallErrorsCommand?.SetAvailability(() => !busy && ForgetInstallErrorsEnabled);
    }
    public override void RefreshCommands()
    {
        SelectAllFilesCommand?.Refresh();
        AddInstallCommand?.Refresh();
        ClearInstallCommand?.Refresh();
        FileInfoCommand?.Refresh();
        DeviceInfoCommand?.Refresh();
        RefreshFilesCommand?.Refresh();
        DeleteFilesCommand?.Refresh();
        ImportFileCommand?.Refresh();
        CheckAppleSupportCommand?.Refresh();
        OpenAppleSupportHelpCommand?.Refresh();
        WifiHelpCommand?.Refresh();
        InstallFilesCommand?.Refresh();
        RetryInstallsCommand?.Refresh();
        ForgetInstallErrorsCommand?.Refresh();
    }

}
