using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Управляет входом, проверкой и выходом из Apple Account, синхронизируя доступность кнопок.</summary>
    public sealed partial class AccountViewModel : FeatureViewModel
    {
        bool signedIn;
        readonly IAccountSession accounts;
        readonly IAccountProtection protection;
        readonly IAppleLoginService login;
        public AccountViewModel(DesktopViewModel desktop, IAccountSession accounts, IAccountProtection protection, IAppleLoginService login) : base(desktop) { this.accounts = accounts; this.protection = protection; this.login = login; }
        internal void Initialize()
        {
            AccountSafetyReviewCommand = AsyncCommand(async delegate
            {
                string detail = protection.SafetyStatus;
                if (!protection.SafetyStopped) { await desktop.Interaction.Confirm("Ограничения входа", String.IsNullOrEmpty(detail) ? "Активной защитной паузы нет. Это не проверка состояния аккаунта у Apple." : detail); return; }
                if (!await desktop.Interaction.Confirm("Возобновление после блокировки",
                    "Вы восстановили доступ через Apple на доверенном устройстве и разобрали причину блокировки?\n\nПродолжение снимает только ручной запрет IPA Desktop. Паузы и общий счётчик ошибок сохранятся. Это не подтверждает снятие ограничений Apple или сети.")) return;
                protection.AcknowledgeRecovery(CancellationToken.None);
                ApplyActions();
            });
            LoginCommand = Command(delegate
            {
                Work("Вход в Apple Account…", async delegate (CancellationToken ct)
                {
                    var email = await desktop.Interaction.Login(login, ct);
                    if (email == null) throw new OperationCanceledException();
                    signedIn = true; desktop.SetAccountIdentity(email); desktop.RegisterPrivate(email);
                    AccountLabel = email; Log("Вход в Apple Account подтверждён.");
                });
            });
            AccountInfoCommand = Command(delegate
            {
                Work("Проверка аккаунта…", Account);
            });
            LogoutCommand = AsyncCommand(async delegate
            {
                if (!await desktop.Interaction.Confirm("Выход", "Выйти из текущей сессии ipatool?")) return;
                Work("Выход…", async delegate (CancellationToken ct)
                {
                    await accounts.RevokeAsync(ct);
                    signedIn = false;
                    desktop.SetAccountIdentity(null);
                    AccountLabel = "Вход не выполнен";
                });
            });
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            bool needsLogin = protection.RequiresLogin;
            AccountSafetyReviewEnabled = !busy;
            var safety = protection.SafetyStatus;
            AccountSafetyReviewVisible = !String.IsNullOrEmpty(safety) || protection.SafetyStopped;
            AccountSafetyStatus = safety;
            AccountSafetyStatusVisible = !String.IsNullOrEmpty(safety);
            LoginEnabled = !busy && (!signedIn || needsLogin);
            if (needsLogin) AccountLabel = "Загрузки остановлены — проверьте аккаунт и войдите заново";
            LogoutEnabled = !busy && signedIn;
        }
        /// <summary>Запрашивает актуальный auth info и обновляет состояние входа только по успешному ответу.</summary>
        public async Task Account(CancellationToken ct)
        {
            signedIn = false; AccountLabel = "Вход не выполнен"; desktop.SetAccountIdentity(null);
            var email = await accounts.GetEmailAsync(ct);
            desktop.RegisterPrivate(email);
            desktop.SetAccountIdentity(email);
            signedIn = true;
            AccountLabel = email;
            Log("Аккаунт проверен.");
        }
    }
}
