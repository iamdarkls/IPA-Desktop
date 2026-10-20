using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Управляет входом, проверкой и выходом из Apple Account, синхронизируя доступность кнопок.</summary>
    public sealed class AccountController : WindowController
    {
        bool signedIn;
        public AccountController(Desktop view, Engine engine) : base(view, engine)
        {
            On("Login", delegate
            {
                Work("Вход в Apple Account…", delegate(CancellationToken ct)
                {
                    using (var login = new AppleLoginWindow(w, engine.AppleLogin, ct))
                    {
                        if (login.Window.ShowDialog() != true) throw new OperationCanceledException();
                        signedIn = true;
                        view.RegisterPrivate(login.SignedInEmail);
                        C<TextBlock>("AccountLabel").Text = login.SignedInEmail;
                        Log("Вход в Apple Account подтверждён.");
                    }
                    return Task.FromResult(0);
                });
            });
            On("AccountInfo", delegate
            {
                Work("Проверка аккаунта…", Account);
            });
            On("Logout", delegate
            {
                if (MessageBox.Show(w, "Выйти из текущей сессии ipatool? Это завершит сохранённую сессию ipatool.", "Выход", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Work("Выход…", async delegate(CancellationToken ct)
                {
                    Engine.Ensure(await engine.Tool(new[]
                    {
                        "auth", "revoke"
                    }, ct, false));
                    signedIn = false;
                    C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
                });
            });
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("Login").IsEnabled = !busy && !signedIn;
            C<Button>("Logout").IsEnabled = !busy && signedIn;
        }
        /// <summary>Запрашивает актуальный auth info и обновляет состояние входа только по успешному ответу.</summary>
        public async Task Account(CancellationToken ct)
        {
            var r = await engine.Tool(new[]
            {
                "auth", "info"
            }, ct, false);
            signedIn = false;
            C<TextBlock>("AccountLabel").Text = "Вход не выполнен";
            Engine.Ensure(r);
            var data = Engine.Object(r.Output);
            var email = Convert.ToString(Engine.Get(data, "email"));
            if (String.IsNullOrWhiteSpace(email) || !Object.Equals(Engine.Get(data, "success"), true)) throw new Exception("Не удалось подтвердить сохранённый вход в аккаунт.");
            view.RegisterPrivate(email);
            signedIn = true;
            C<TextBlock>("AccountLabel").Text = email;
            Log("Аккаунт проверен.");
        }
    }
}
