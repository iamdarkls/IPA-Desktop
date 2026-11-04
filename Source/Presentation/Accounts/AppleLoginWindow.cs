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
        public string SignedInEmail => Model.SignedInEmail;
        public AppleLoginViewModel Model { get; private set; }
        readonly StackPanel credentials, codePanel;
        public AppleLoginWindow(Window owner, IAppleLoginService service, CancellationToken ct)
        {
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
            body.Children.Add(Note("Пароль и код не передаются в командной строке и не записываются в журнал IPA Desktop. Защищённый движок сохраняет сессию без пароля Apple Account. Кнопка «Выйти» удаляет сохранённую сессию.", 12));
            body.Children.Add(Note("Вход в приватном терминале Windows, macOS или Linux. Время одной попытки — до 5 минут.", 10));
            layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            SubmitButton = AppDialogs.Action("Войти", IconKind.Shield, primary: true); SubmitButton.IsDefault = true;
            CancelButton = AppDialogs.Action("Отмена", IconKind.Close); CancelButton.Margin = new Thickness(0);
            actions.Children.Add(SubmitButton); actions.Children.Add(CancelButton); Grid.SetRow(actions, 1); layout.Children.Add(actions);
            Window.Content = layout;
            var names = new NameScope(); NameScope.SetNameScope(Window,names);
            names.Register("AppleLoginEmail",EmailBox); names.Register("AppleLoginPassword",PasswordBox);
            names.Register("AppleLoginCode",CodeBox); names.Register("AppleLoginSubmit",SubmitButton);
            names.Register("AppleLoginCancel",CancelButton); names.Register("AppleLoginStatus",Status);
            Model = new AppleLoginViewModel(service, new SecretInput(this), action => Dispatcher.UIThread.Post(action), ct);
            Window.DataContext = Model;
            ViewBindings.Bind(EmailBox, TextBox.TextProperty, nameof(Model.Email), true);
            ViewBindings.Bind(Status, TextBlock.TextProperty, nameof(Model.Status));
            ViewBindings.Bind(credentials, Control.IsVisibleProperty, nameof(Model.CredentialsVisible));
            ViewBindings.Bind(credentials, Control.IsEnabledProperty, nameof(Model.CredentialsEnabled));
            ViewBindings.Bind(codePanel, Control.IsVisibleProperty, nameof(Model.CodeVisible));
            ViewBindings.Bind(CodeBox, Control.IsEnabledProperty, nameof(Model.CodeEnabled));
            ViewBindings.Bind(SubmitButton, ContentControl.ContentProperty, nameof(Model.SubmitLabel));
            ViewBindings.Bind(SubmitButton,Control.IsEnabledProperty,nameof(Model.SubmitEnabled));
            ViewBindings.Bind(CancelButton,Control.IsEnabledProperty,nameof(Model.CancelEnabled));
            SubmitButton.Command = Model.SubmitCommand; CancelButton.Command = Model.CancelCommand;
            Model.CloseRequested += result => { if (result == true) Window.Close(true); else Window.Close(); };
            Window.Opened += (_, _) => EmailBox.Focus();
            Window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Model.CancelCommand.Execute(null); } };
            Window.Closing += (_, e) => { if (!Model.RequestClose()) e.Cancel = true; };
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
        sealed class SecretInput : ILoginInput
        {
            readonly AppleLoginWindow view;
            public SecretInput(AppleLoginWindow view) => this.view = view;
            static SecureString Take(TextBox box)
            {
                var value = new SecureString();
                foreach (char character in box.Text ?? "") value.AppendChar(character);
                value.MakeReadOnly(); box.Text = ""; return value;
            }
            public SecureString TakePassword() => Take(view.PasswordBox);
            public SecureString TakeCode() => Take(view.CodeBox);
            public void Clear() { view.PasswordBox.Text = ""; view.CodeBox.Text = ""; }
            public void FocusPassword() => view.PasswordBox.Focus();
            public void FocusCode() => view.CodeBox.Focus();
        }
        public void Dispose() => Model.Dispose();
    }
}
