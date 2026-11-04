using System;
using System.Collections.Generic;

namespace IpaDesktop;

public sealed partial class AccountViewModel
{
    string accountLabel = "Аккаунт не проверен";
    public string AccountLabel { get => accountLabel; set { if (Set(ref accountLabel, value)) RefreshCommands(); } }
    bool accountSafetyReviewEnabled = true;
    public bool AccountSafetyReviewEnabled { get => accountSafetyReviewEnabled; set { if (Set(ref accountSafetyReviewEnabled, value)) RefreshCommands(); } }
    bool accountSafetyReviewVisible = false;
    public bool AccountSafetyReviewVisible { get => accountSafetyReviewVisible; set { if (Set(ref accountSafetyReviewVisible, value)) RefreshCommands(); } }
    string accountSafetyStatus = "";
    public string AccountSafetyStatus { get => accountSafetyStatus; set { if (Set(ref accountSafetyStatus, value)) RefreshCommands(); } }
    bool accountSafetyStatusVisible = false;
    public bool AccountSafetyStatusVisible { get => accountSafetyStatusVisible; set { if (Set(ref accountSafetyStatusVisible, value)) RefreshCommands(); } }
    bool loginEnabled = true;
    public bool LoginEnabled { get => loginEnabled; set { if (Set(ref loginEnabled, value)) RefreshCommands(); } }
    bool logoutEnabled = true;
    public bool LogoutEnabled { get => logoutEnabled; set { if (Set(ref logoutEnabled, value)) RefreshCommands(); } }
    public OperationCommand AccountSafetyReviewCommand { get; private set; }
    public OperationCommand LoginCommand { get; private set; }
    public OperationCommand AccountInfoCommand { get; private set; }
    public OperationCommand LogoutCommand { get; private set; }
    public override void ConfigureCommands()
    {
        AccountSafetyReviewCommand?.SetAvailability(() => !busy && AccountSafetyReviewEnabled);
        LoginCommand?.SetAvailability(() => !busy && LoginEnabled);
        AccountInfoCommand?.SetAvailability(() => !busy && true);
        LogoutCommand?.SetAvailability(() => !busy && LogoutEnabled);
    }
    public override void RefreshCommands()
    {
        AccountSafetyReviewCommand?.Refresh();
        LoginCommand?.Refresh();
        AccountInfoCommand?.Refresh();
        LogoutCommand?.Refresh();
    }

}
