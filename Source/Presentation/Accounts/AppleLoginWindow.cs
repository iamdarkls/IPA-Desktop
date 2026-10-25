using System;
using System.Security;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;

namespace IpaDesktop
{
    /// <summary>Модальный вход с отдельным этапом 2FA. Пароль и код находятся в PasswordBox и краткоживущих SecureString.</summary>
    public sealed class AppleLoginWindow : IDisposable
    {
        public Window Window { get; private set; }
        public TextBox EmailBox { get; private set; }
        public TextBox PasswordBox { get; private set; }
        public TextBox CodeBox { get; private set; }
        public Button SubmitButton { get; private set; }
        public Button CancelButton { get; private set; }
        public TextBlock Status { get; private set; }
        public string SignedInEmail { get; private set; }
        readonly IAppleLoginService service;
        readonly CancellationTokenSource lifetime;
        CancellationTokenSource operation;
        TaskCompletionSource<SecureString> codeSource;
        readonly StackPanel credentials, codePanel;
        bool loading, closing;
        public AppleLoginWindow(Window owner, IAppleLoginService service, CancellationToken ct)
        {
            this.service = service; lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Window = AppDialogs.Create(owner, "Вход в Apple Account", 560, 580);
            Window.MinWidth = 480; Window.MinHeight = 480;
            var layout = new Grid { Margin = new Thickness(22) };
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Apple Account", FontSize = 25, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            body.Children.Add(Note("Войдите под аккаунтом, для которого будете скачивать приложения. При запросе Apple подготовьте доверенное устройство.", 0));
            credentials = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            credentials.Children.Add(Note("Apple Account — почта или номер телефона", 0));
            EmailBox = new TextBox { MaxLength = 320, Margin = new Thickness(0, 6, 0, 12), MinHeight = 38 };
            credentials.Children.Add(EmailBox);
            credentials.Children.Add(Note("Пароль аккаунта", 0));
            PasswordBox = SecretBox(1024); credentials.Children.Add(PasswordBox); body.Children.Add(credentials);
            codePanel = new StackPanel { IsVisible = false, Margin = new Thickness(0, 16, 0, 0) };
            codePanel.Children.Add(Note("Подтвердите вход на доверенном устройстве Apple и введите шестизначный код.", 0));
            CodeBox = SecretBox(6); codePanel.Children.Add(CodeBox); body.Children.Add(codePanel);
            Status = new TextBlock { Text = "Введите данные для входа.", TextWrapping = TextWrapping.Wrap, MinHeight = 46, Margin = new Thickness(0, 14, 0, 8), Foreground = Brush("#AFC6E7") };
            body.Children.Add(Status);
            body.Children.Add(Note("Пароль и код не передаются в командной строке и не записываются в журнал IPA Desktop. Сам ipatool сохраняет данные аккаунта, включая пароль, в зашифрованном файле профиля Windows. Кнопка «Выйти» завершает сохранённую сессию.", 12));
            body.Children.Add(Note("Вход в окне: Windows 10 1809+ и Windows 11. Время одной попытки — до 5 минут.", 10));
            layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            SubmitButton = new Button { Content = "Войти", IsDefault = true, Background = Brush("#3878E5") };
            CancelButton = new Button { Content = "Отмена", Margin = new Thickness(0) };
            SubmitButton.Click += Submit;
            CancelButton.Click += delegate { RequestClose(); };
            actions.Children.Add(SubmitButton); actions.Children.Add(CancelButton); Grid.SetRow(actions, 1); layout.Children.Add(actions);
            Window.Content = layout;
            var names = new NameScope(); NameScope.SetNameScope(Window,names);
            names.Register("AppleLoginEmail",EmailBox); names.Register("AppleLoginPassword",PasswordBox);
            names.Register("AppleLoginCode",CodeBox); names.Register("AppleLoginSubmit",SubmitButton);
            names.Register("AppleLoginCancel",CancelButton); names.Register("AppleLoginStatus",Status);
            Window.Opened += delegate { EmailBox.Focus(); };
            Window.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; RequestClose(); } };
            Window.Closing += delegate(object sender, WindowClosingEventArgs e)
            {
                if (loading) { e.Cancel = true; RequestClose(); }
            };
        }
        static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)Color.Parse(color)); }
        static TextBlock Note(string text, double top) { return new TextBlock { Text = text, FontSize = 12, Foreground = Brush("#9BACBF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) }; }
        static TextBox SecretBox(int limit)
        {
            var box = new TextBox { MaxLength = limit, PasswordChar = '●', MinHeight = 38,
                Padding = new Thickness(10,6), Margin = new Thickness(0,6,0,0),
                Background = Brush("#1C293B"), Foreground = Brush("#EDF1F8"), BorderBrush = Brush("#34445C") };
            return box;
        }
        async void Submit(object sender, RoutedEventArgs e)
        {
            if (loading)
            {
                if (codeSource == null || codeSource.Task.IsCompleted) return;
                var code = Secret(CodeBox);
                if (!ValidCode(code)) { code.Dispose(); Status.Text = "Введите шесть цифр без пробелов."; CodeBox.Focus(); return; }
                CodeBox.Text = ""; CodeBox.IsEnabled = false; SubmitButton.IsEnabled = false;
                Status.Text = "Проверяем код…";
                if (!codeSource.TrySetResult(code)) code.Dispose();
                return;
            }
            using (var password = Secret(PasswordBox))
            {
                if (String.IsNullOrWhiteSpace(EmailBox.Text) || password.Length == 0)
                { Status.Text = "Введите Apple Account и пароль."; return; }
                PasswordBox.Text = ""; loading = true;
                operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                credentials.IsEnabled = false; SubmitButton.IsEnabled = false;
                Status.Text = "Связываемся с Apple…";
                bool success = false;
                try
                {
                    SignedInEmail = await service.LoginAsync(EmailBox.Text.Trim(), password, RequestCode, operation.Token);
                    success = true;
                }
                catch (OperationCanceledException) { Status.Text = "Вход отменён."; }
                catch (AppleLoginException error) { Status.Text = error.Message + "\nПеред новой попыткой действует защитная пауза."; }
                catch (AppleAccountException error) { Status.Text = error.Message; }
                catch (Exception) { Status.Text = "Не удалось выполнить вход. Повторите попытку; детали авторизации не записываются в журнал."; }
                finally
                {
                    loading = false; operation.Dispose(); operation = null;
                    if (codeSource != null) codeSource.TrySetCanceled(); codeSource = null;
                    CodeBox.Text = ""; PasswordBox.Text = ""; codePanel.IsVisible = false;
                    credentials.IsVisible = true; credentials.IsEnabled = true;
                    SubmitButton.IsEnabled = true; SubmitButton.Content = "Войти"; PasswordBox.Focus();
                }
                if (success) Window.Close(true);
                else if (closing || lifetime.IsCancellationRequested) Window.Close();
            }
        }
        async Task<SecureString> RequestCode(CancellationToken ct)
        {
            var pending = new TaskCompletionSource<SecureString>();
            using (ct.Register(delegate { pending.TrySetCanceled(); }))
            {
                Dispatcher.UIThread.Post(new Action(delegate
                {
                    if (ct.IsCancellationRequested) return;
                    codeSource = pending; credentials.IsVisible = false; codePanel.IsVisible = true;
                    CodeBox.Text = ""; CodeBox.IsEnabled = true; SubmitButton.Content = "Подтвердить"; SubmitButton.IsEnabled = true;
                    Status.Text = "Apple запросила код подтверждения."; CodeBox.Focus();
                }));
                return await pending.Task.ConfigureAwait(false);
            }
        }
        static SecureString Secret(TextBox box)
        {
            var value = new SecureString();
            foreach (char character in box.Text ?? "") value.AppendChar(character);
            value.MakeReadOnly(); return value;
        }
        static bool ValidCode(SecureString code)
        {
            if (code.Length != 6) return false;
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(code);
            try { for (int i = 0; i < 6; i++) { char c = (char)Marshal.ReadInt16(pointer, i * 2); if (c < '0' || c > '9') return false; } return true; }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
        void RequestClose()
        {
            if (!loading) { Window.Close(); return; }
            closing = true; SubmitButton.IsEnabled = false; CancelButton.IsEnabled = false; Status.Text = "Останавливаем вход…"; operation.Cancel();
        }
        public void Dispose()
        {
            lifetime.Cancel(); lifetime.Dispose(); PasswordBox.Text = ""; CodeBox.Text = "";
        }
    }
}
