using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class SearchViewModel
{
    public SelectionViewModel<AppRow> Apps { get; } = new();
    public SelectionViewModel<string> VersionList { get; } = new();
    string retryDownloadsTip = "";
    public string RetryDownloadsTip { get => retryDownloadsTip; set { if (Set(ref retryDownloadsTip, value)) RefreshCommands(); } }
    string query = "";
    public string Query { get => query; set { if (Set(ref query, value)) RefreshCommands(); } }
    bool searchEnabled = true;
    public bool SearchEnabled { get => searchEnabled; set { if (Set(ref searchEnabled, value)) RefreshCommands(); } }
    bool searchEmptyVisible = true;
    public bool SearchEmptyVisible { get => searchEmptyVisible; set { if (Set(ref searchEmptyVisible, value)) RefreshCommands(); } }
    bool retryDownloadsEnabled = true;
    public bool RetryDownloadsEnabled { get => retryDownloadsEnabled; set { if (Set(ref retryDownloadsEnabled, value)) RefreshCommands(); } }
    string retryDownloadsLabel = "Повторить ошибки (0)";
    public string RetryDownloadsLabel { get => retryDownloadsLabel; set { if (Set(ref retryDownloadsLabel, value)) RefreshCommands(); } }
    bool forgetDownloadErrorsEnabled = true;
    public bool ForgetDownloadErrorsEnabled { get => forgetDownloadErrorsEnabled; set { if (Set(ref forgetDownloadErrorsEnabled, value)) RefreshCommands(); } }
    bool addChosenEnabled = true;
    public bool AddChosenEnabled { get => addChosenEnabled; set { if (Set(ref addChosenEnabled, value)) RefreshCommands(); } }
    bool clearChosenEnabled = true;
    public bool ClearChosenEnabled { get => clearChosenEnabled; set { if (Set(ref clearChosenEnabled, value)) RefreshCommands(); } }
    bool downloadEnabled = true;
    public bool DownloadEnabled { get => downloadEnabled; set { if (Set(ref downloadEnabled, value)) RefreshCommands(); } }
    bool chosenAppsEnabled = true;
    public bool ChosenAppsEnabled { get => chosenAppsEnabled; set { if (Set(ref chosenAppsEnabled, value)) RefreshCommands(); } }
    bool versionTableEnabled = true;
    public bool VersionTableEnabled { get => versionTableEnabled; set { if (Set(ref versionTableEnabled, value)) RefreshCommands(); } }
    bool versionInfoEnabled = true;
    public bool VersionInfoEnabled { get => versionInfoEnabled; set { if (Set(ref versionInfoEnabled, value)) RefreshCommands(); } }
    IEnumerable<DownloadItem> chosenAppsOptions = Array.Empty<DownloadItem>();
    public IEnumerable<DownloadItem> ChosenAppsOptions { get => chosenAppsOptions; set { if (Set(ref chosenAppsOptions, value)) RefreshCommands(); } }
    bool searchSelectionVisible = false;
    public bool SearchSelectionVisible { get => searchSelectionVisible; set { if (Set(ref searchSelectionVisible, value)) RefreshCommands(); } }
    string chosenCount = "Двойной клик — добавить в выбранное";
    public string ChosenCount { get => chosenCount; set { if (Set(ref chosenCount, value)) RefreshCommands(); } }
    string downloadLabel = "Скачать (0)";
    public string DownloadLabel { get => downloadLabel; set { if (Set(ref downloadLabel, value)) RefreshCommands(); } }
    string searchSetStatus = "Выберите набор или введите новое название";
    public string SearchSetStatus { get => searchSetStatus; set { if (Set(ref searchSetStatus, value)) RefreshCommands(); } }
    string searchSet = "";
    public string SearchSet { get => searchSet; set { if (Set(ref searchSet, value)) RefreshCommands(); } }
    bool searchSetEnabled = true;
    public bool SearchSetEnabled { get => searchSetEnabled; set { if (Set(ref searchSetEnabled, value)) RefreshCommands(); } }
    public OperationCommand SearchCommand { get; private set; }
    public OperationCommand VersionsCommand { get; private set; }
    public OperationCommand VersionTableCommand { get; private set; }
    public OperationCommand VersionInfoCommand { get; private set; }
    public OperationCommand AddChosenCommand { get; private set; }
    public OperationCommand ClearChosenCommand { get; private set; }
    public OperationCommand DownloadCommand { get; private set; }
    public OperationCommand RetryDownloadsCommand { get; private set; }
    public OperationCommand ForgetDownloadErrorsCommand { get; private set; }
    IEnumerable<string> searchSetOptions = Array.Empty<string>();
    public IEnumerable<string> SearchSetOptions { get => searchSetOptions; set => Set(ref searchSetOptions, value); }
    public override void ConfigureCommands()
    {
        SearchCommand?.SetAvailability(() => !busy && SearchEnabled);
        VersionsCommand?.SetAvailability(() => !busy && true);
        VersionTableCommand?.SetAvailability(() => !busy && VersionTableEnabled);
        VersionInfoCommand?.SetAvailability(() => !busy && VersionInfoEnabled);
        AddChosenCommand?.SetAvailability(() => !busy && AddChosenEnabled);
        ClearChosenCommand?.SetAvailability(() => !busy && ClearChosenEnabled);
        DownloadCommand?.SetAvailability(() => !busy && DownloadEnabled);
        RetryDownloadsCommand?.SetAvailability(() => !busy && RetryDownloadsEnabled);
        ForgetDownloadErrorsCommand?.SetAvailability(() => !busy && ForgetDownloadErrorsEnabled);
    }
    public override void RefreshCommands()
    {
        SearchCommand?.Refresh();
        VersionsCommand?.Refresh();
        VersionTableCommand?.Refresh();
        VersionInfoCommand?.Refresh();
        AddChosenCommand?.Refresh();
        ClearChosenCommand?.Refresh();
        DownloadCommand?.Refresh();
        RetryDownloadsCommand?.Refresh();
        ForgetDownloadErrorsCommand?.Refresh();
    }

}
