using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class DesktopViewModel
{
    bool connectionNoticeVisible = false;
    public bool ConnectionNoticeVisible { get => connectionNoticeVisible; set { if (Set(ref connectionNoticeVisible, value)) RefreshCommands(); } }
    string connectionTitle = "";
    public string ConnectionTitle { get => connectionTitle; set { if (Set(ref connectionTitle, value)) RefreshCommands(); } }
    string connectionMessage = "";
    public string ConnectionMessage { get => connectionMessage; set { if (Set(ref connectionMessage, value)) RefreshCommands(); } }
    bool resetQueueHistoryEnabled = true;
    public bool ResetQueueHistoryEnabled { get => resetQueueHistoryEnabled; set { if (Set(ref resetQueueHistoryEnabled, value)) RefreshCommands(); } }
    bool noticeRetryEnabled = true;
    public bool NoticeRetryEnabled { get => noticeRetryEnabled; set { if (Set(ref noticeRetryEnabled, value)) RefreshCommands(); } }
    bool showLastReportEnabled = false;
    public bool ShowLastReportEnabled { get => showLastReportEnabled; set { if (Set(ref showLastReportEnabled, value)) RefreshCommands(); } }
    bool operationNoticeVisible = false;
    public bool OperationNoticeVisible { get => operationNoticeVisible; set { if (Set(ref operationNoticeVisible, value)) RefreshCommands(); } }
    string noticeTextTip = "";
    public string NoticeTextTip { get => noticeTextTip; set { if (Set(ref noticeTextTip, value)) RefreshCommands(); } }
    string noticeText = "";
    public string NoticeText { get => noticeText; set { if (Set(ref noticeText, value)) RefreshCommands(); } }
    string noticeCause = "";
    public string NoticeCause { get => noticeCause; set { if (Set(ref noticeCause, value)) RefreshCommands(); } }
    bool noticeCauseVisible = true;
    public bool NoticeCauseVisible { get => noticeCauseVisible; set { if (Set(ref noticeCauseVisible, value)) RefreshCommands(); } }
    string noticeRemedy = "";
    public string NoticeRemedy { get => noticeRemedy; set { if (Set(ref noticeRemedy, value)) RefreshCommands(); } }
    bool noticeRemedyVisible = true;
    public bool NoticeRemedyVisible { get => noticeRemedyVisible; set { if (Set(ref noticeRemedyVisible, value)) RefreshCommands(); } }
    bool noticeRetryVisible = true;
    public bool NoticeRetryVisible { get => noticeRetryVisible; set { if (Set(ref noticeRetryVisible, value)) RefreshCommands(); } }
    public OperationCommand ShowLastReportCommand { get; private set; }
    public OperationCommand NoticeDetailsCommand { get; private set; }
    public OperationCommand DismissNoticeCommand { get; private set; }
    public OperationCommand NoticeRetryCommand { get; private set; }
    public OperationCommand ExportDiagnosticsCommand { get; private set; }
    public OperationCommand OpenFolderCommand { get; private set; }
    public OperationCommand ResetQueueHistoryCommand { get; private set; }
    string noticeBackground = "#38232C", noticeBorder = "#D36B7B";
    public string OperationNoticeBackground { get => noticeBackground; set => Set(ref noticeBackground, value); }
    public string OperationNoticeBorder { get => noticeBorder; set => Set(ref noticeBorder, value); }
    public void ConfigureCommands()
    {
        ShowLastReportCommand?.SetAvailability(() => !IsClosing && ShowLastReportEnabled);
        NoticeDetailsCommand?.SetAvailability(() => !IsClosing && lastReport != null);
        DismissNoticeCommand?.SetAvailability(() => !IsClosing && true);
        NoticeRetryCommand?.SetAvailability(() => !IsClosing && !IsBusy && NoticeRetryEnabled);
        ExportDiagnosticsCommand?.SetAvailability(() => !IsClosing && true);
        OpenFolderCommand?.SetAvailability(() => !IsClosing && true);
        ResetQueueHistoryCommand?.SetAvailability(() => !IsClosing && !IsBusy && ResetQueueHistoryEnabled);
    }
    public void RefreshCommands()
    {
        ShowLastReportCommand?.Refresh();
        NoticeDetailsCommand?.Refresh();
        DismissNoticeCommand?.Refresh();
        NoticeRetryCommand?.Refresh();
        ExportDiagnosticsCommand?.Refresh();
        OpenFolderCommand?.Refresh();
        ResetQueueHistoryCommand?.Refresh();
    }

}
