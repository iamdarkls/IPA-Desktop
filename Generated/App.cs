// Generated from Source/. Edit the source classes, then run Sync-Sources.ps1.
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows;
using System;

// Presentation/AccountController.cs
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

// Presentation/AppDialogs.cs
namespace IpaDesktop
{
    /// <summary>Создаёт диалоги с общими стилями приложения, владельцем и тёмной рамкой Windows.</summary>
    public static class AppDialogs
    {
        public static Window Create(Window owner, string title, double width, double height)
        {
            var dialog = new Window
            {
                Owner = owner, Title = title, Width = width, Height = height,
                MinWidth = 460, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.Background, Foreground = owner.Foreground,
                FontFamily = owner.FontFamily, FontSize = owner.FontSize,
                ShowInTaskbar = false, Icon = owner.Icon
            };
            dialog.Resources.MergedDictionaries.Add(owner.Resources);
            dialog.SourceInitialized += delegate { WindowTheme.Apply(dialog); };
            return dialog;
        }
        /// <summary>Полный текст доступен для выделения и копирования, без автоматического открытия при ошибке.</summary>
        public static Window Text(Window owner, string title, string details)
        {
            var dialog = Create(owner, title, 740, 530);
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBox
            {
                Text = details ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            layout.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var copy = new Button { Content = "Копировать" };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            copy.Click += delegate
            {
                try { Clipboard.SetText(text.Text); status.Text = "Скопировано"; }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Буфер занят; выделите текст и нажмите Ctrl+C."; }
            };
            var close = new Button { Content = "Закрыть", IsCancel = true };
            close.Click += delegate { dialog.Close(); };
            actions.Children.Add(copy); actions.Children.Add(close); actions.Children.Add(status);
            Grid.SetRow(actions, 1); layout.Children.Add(actions); dialog.Content = layout;
            return dialog;
        }
        public static void ShowText(Window owner, string title, string details) { Text(owner, title, details).ShowDialog(); }
        /// <summary>Данные уже прочитаны репозиторием; просмотр IPA не запускает дополнительное сканирование диска.</summary>
        public static Window Ipa(Window owner, LocalRow row)
        {
            return Text(owner, "Сведения об IPA — " + row.DisplayName,
                row.FileDetails + Environment.NewLine + "App Store ID: " + (row.AppStoreID ?? "не указан") +
                Environment.NewLine + "Bundle ID: " + (row.BundleID ?? "не указан") +
                Environment.NewLine + "Сборка: " + (row.Build ?? "не указана") +
                Environment.NewLine + "Размер: " + row.Size);
        }
    }
}

// Presentation/AppleLoginWindow.cs
namespace IpaDesktop
{
    /// <summary>Модальный вход с отдельным этапом 2FA. Пароль и код находятся в PasswordBox и краткоживущих SecureString.</summary>
    public sealed class AppleLoginWindow : IDisposable
    {
        public Window Window { get; private set; }
        public TextBox EmailBox { get; private set; }
        public PasswordBox PasswordBox { get; private set; }
        public PasswordBox CodeBox { get; private set; }
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
            body.Children.Add(new TextBlock { Text = "Apple Account", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            body.Children.Add(Note("Войдите под аккаунтом, для которого будете скачивать приложения. При запросе Apple подготовьте доверенное устройство.", 0));
            credentials = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            credentials.Children.Add(Note("Apple Account — почта или номер телефона", 0));
            EmailBox = new TextBox { MaxLength = 320, Margin = new Thickness(0, 6, 0, 12), MinHeight = 38 };
            credentials.Children.Add(EmailBox);
            credentials.Children.Add(Note("Пароль аккаунта", 0));
            PasswordBox = SecretBox(1024); credentials.Children.Add(PasswordBox); body.Children.Add(credentials);
            codePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) };
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
            NameScope.SetNameScope(Window, new NameScope());
            Window.RegisterName("AppleLoginEmail", EmailBox); Window.RegisterName("AppleLoginPassword", PasswordBox);
            Window.RegisterName("AppleLoginCode", CodeBox); Window.RegisterName("AppleLoginSubmit", SubmitButton);
            Window.RegisterName("AppleLoginCancel", CancelButton); Window.RegisterName("AppleLoginStatus", Status);
            Window.Loaded += delegate { EmailBox.Focus(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; RequestClose(); } };
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (loading) { e.Cancel = true; RequestClose(); }
            };
        }
        static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        static TextBlock Note(string text, double top) { return new TextBlock { Text = text, FontSize = 12, Foreground = Brush("#9BACBF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) }; }
        static PasswordBox SecretBox(int limit)
        {
            var box = new PasswordBox { MaxLength = limit, MinHeight = 38, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 6, 0, 0),
                Background = Brush("#1C293B"), Foreground = Brush("#EDF1F8"), BorderBrush = Brush("#34445C"), CaretBrush = Brush("#EDF1F8") };
            // Штатная светлая тема Windows не должна менять фон скрытых полей при блокировке ввода.
            box.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='PasswordBox'>" +
                "<Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='{TemplateBinding Padding}'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#8CB9FF'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return box;
        }
        async void Submit(object sender, RoutedEventArgs e)
        {
            if (loading)
            {
                if (codeSource == null || codeSource.Task.IsCompleted) return;
                var code = CodeBox.SecurePassword;
                if (!ValidCode(code)) { code.Dispose(); Status.Text = "Введите шесть цифр без пробелов."; CodeBox.Focus(); return; }
                CodeBox.Clear(); CodeBox.IsEnabled = false; SubmitButton.IsEnabled = false;
                Status.Text = "Проверяем код…";
                if (!codeSource.TrySetResult(code)) code.Dispose();
                return;
            }
            using (var password = PasswordBox.SecurePassword)
            {
                if (String.IsNullOrWhiteSpace(EmailBox.Text) || password.Length == 0)
                { Status.Text = "Введите Apple Account и пароль."; return; }
                PasswordBox.Clear(); loading = true;
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
                catch (AppleLoginException error) { Status.Text = error.Message; }
                catch (Exception) { Status.Text = "Не удалось выполнить вход. Повторите попытку; детали авторизации не записываются в журнал."; }
                finally
                {
                    loading = false; operation.Dispose(); operation = null;
                    if (codeSource != null) codeSource.TrySetCanceled(); codeSource = null;
                    CodeBox.Clear(); PasswordBox.Clear(); codePanel.Visibility = Visibility.Collapsed;
                    credentials.Visibility = Visibility.Visible; credentials.IsEnabled = true;
                    SubmitButton.IsEnabled = true; SubmitButton.Content = "Войти"; PasswordBox.Focus();
                }
                if (success) Window.DialogResult = true;
                else if (closing || lifetime.IsCancellationRequested) Window.Close();
            }
        }
        async Task<SecureString> RequestCode(CancellationToken ct)
        {
            var pending = new TaskCompletionSource<SecureString>();
            using (ct.Register(delegate { pending.TrySetCanceled(); }))
            {
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (ct.IsCancellationRequested) return;
                    codeSource = pending; credentials.Visibility = Visibility.Collapsed; codePanel.Visibility = Visibility.Visible;
                    CodeBox.Clear(); CodeBox.IsEnabled = true; SubmitButton.Content = "Подтвердить"; SubmitButton.IsEnabled = true;
                    Status.Text = "Apple запросила код подтверждения."; CodeBox.Focus();
                }));
                return await pending.Task.ConfigureAwait(false);
            }
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
            lifetime.Cancel(); lifetime.Dispose(); PasswordBox.Clear(); CodeBox.Clear();
        }
    }
}

// Presentation/AppSetEditor.cs
namespace IpaDesktop
{
    /// <summary>Редактирует независимую копию набора; отмена не меняет файл конфигурации.</summary>
    public static class AppSetEditor
    {
        public static Window Create(Window owner, string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save)
        {
            var draft = new ObservableCollection<AppRow>(apps.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
            var dialog = AppDialogs.Create(owner, "Редактирование набора", 680, 580);
            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBox { Text = name, MaxLength = 64, Margin = new Thickness(0, 0, 0, 12) };
            title.Name = "DraftName"; grid.Children.Add(title);
            var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemsSource = draft, DisplayMemberPath = "Name" };
            list.Style = (Style)owner.FindResource("SetList"); list.Name = "DraftApps";
            Grid.SetRow(list, 1); grid.Children.Add(list);
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var hint = new TextBlock { Text = "Клик — отметить приложение. Новые приложения добавляются в набор через поиск.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            status.Foreground = System.Windows.Media.Brushes.Salmon;
            var buttons = new WrapPanel();
            var remove = new Button { Content = "Убрать отмеченные" }; remove.Name = "DraftRemove";
            remove.Click += delegate { foreach (var item in list.SelectedItems.Cast<AppRow>().ToArray()) draft.Remove(item); };
            var apply = new Button { Content = "Сохранить" }; apply.Name = "DraftSave";
            apply.Click += delegate
            {
                try
                {
                    save(title.Text.Trim(), draft.ToArray());
                    dialog.DialogResult = true;
                }
                catch (Exception e) { status.Text = e.Message; }
            };
            var cancel = new Button { Content = "Отмена", IsCancel = true };
            cancel.Click += delegate { dialog.Close(); };
            buttons.Children.Add(remove); buttons.Children.Add(apply); buttons.Children.Add(cancel);
            bottom.Children.Add(hint); bottom.Children.Add(status); bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            dialog.Content = grid;
            return dialog;
        }
    }
}

// Presentation/AppSetsController.cs
namespace IpaDesktop
{
    /// <summary>Управляет наборами в обеих вкладках: сохранением, выбором, предпросмотром и добавлением в очередь.</summary>
    public sealed class AppSetsController : WindowController
    {
        public AppSetsController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ListBox>("AppSets").SelectionChanged += delegate { Preview(); ApplyActions(); };
            On("AddToSet", delegate { Save(C<ComboBox>("SearchSet").Text, "SearchSetStatus"); });
            On("SaveSet", delegate { Save(C<TextBox>("SetName").Text, "SetStatus"); });
            On("EditSet", delegate
            {
                var selected = Selected();
                if (selected.Count != 1) return;
                var original = selected[0];
                Work("Открытие набора…", async delegate(CancellationToken ct)
                {
                    var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveApps(original); }, ct);
                    ct.ThrowIfCancellationRequested();
                    var editor = AppSetEditor.Create(w, original.Name, apps,
                        delegate(string name, IEnumerable<AppRow> rows) { engine.AppSets.Update(original.Name, name, rows); });
                    if (editor.ShowDialog() == true) RefreshSets();
                });
            });
            On("DeleteSet", DeleteSelected);
            On("UseSet", UseSelected);
            RefreshSets();
        }
        /// <summary>Обе кнопки дополняют набор одинаково; повторное имя не уничтожает прежние приложения.</summary>
        void Save(string name, string status)
        {
            name = (name ?? "").Trim();
            try
            {
                var rows = view.SelectedDownloads();
                if (name.Length == 0) throw new InvalidOperationException("Введите название нового набора или выберите существующий.");
                if (rows.Count == 0) throw new InvalidOperationException("Добавьте приложения в выбранное в поиске.");
                engine.AppSets.AddApps(name, rows.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
                RefreshSets();
                C<ComboBox>("SearchSet").Text = name;
                C<TextBlock>(status).Text = "Набор «" + name + "» сохранён. Повторы по ID исключены.";
                view.RegisterPrivate(name);
            }
            catch (Exception e) { C<TextBlock>(status).Text = e.Message; Error(e); }
        }
        /// <summary>Сохраняет выделение по именам после перезагрузки списка, независимо от ссылок на модели.</summary>
        internal void RefreshSets()
        {
            var list = C<ListBox>("AppSets");
            var selected = new HashSet<string>(Selected().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var picker = C<ComboBox>("SearchSet");
            var name = picker.Text;
            try
            {
                var sets = engine.AppSets.Load();
                list.ItemsSource = sets;
                foreach (var set in sets.Where(x => selected.Contains(x.Name))) list.SelectedItems.Add(set);
                if (list.SelectedItems.Count == 0 && sets.Count > 0) list.SelectedIndex = 0;
                picker.ItemsSource = sets.Select(x => x.Name).ToArray(); picker.Text = name;
                C<TextBlock>("SetsCount").Text = sets.Count == 0 ? "Наборов пока нет" : "Сохранено наборов: " + sets.Count;
                Preview(); ApplyActions();
            }
            catch (Exception e) { C<TextBlock>("SetStatus").Text = e.Message; Error(e); }
        }
        List<AppSet> Selected() { return C<ListBox>("AppSets").SelectedItems.Cast<AppSet>().ToList(); }
        void Preview()
        {
            var selected = Selected();
            var apps = selected.Where(x => x.Apps != null).SelectMany(x => x.Apps).GroupBy(x => x.ID).Select(g => g.First()).ToArray();
            C<TextBlock>("SetPreview").Text = selected.Count == 0 ? "Отметьте один или несколько наборов." :
                "Выбрано наборов: " + selected.Count + " · уникальных приложений: " + apps.Length +
                (selected.Any(x => x.Apps == null) ? " · есть наборы прежнего формата" : "") + Environment.NewLine +
                String.Join(", ", apps.Take(12).Select(x => x.Name)) + (apps.Length > 12 ? "…" : "");
        }
        /// <summary>Сначала разрешает все наборы; при ошибке ни один из них не попадает в очередь частично.</summary>
        void UseSelected()
        {
            var selected = Selected();
            if (selected.Count == 0) return;
            Work("Добавление наборов…", async delegate(CancellationToken ct)
            {
                var apps = await Task.Run(delegate { ct.ThrowIfCancellationRequested(); return engine.AppSets.ResolveMany(selected); }, ct);
                ct.ThrowIfCancellationRequested();
                int added = view.AddSetApps(apps);
                RefreshSets();
                foreach (var set in selected) view.RegisterPrivate(set.Name);
                string message = "Наборов: " + selected.Count + ". Добавлено приложений: " + added + ", уже в очереди: " + (apps.Length - added) + ".";
                C<TextBlock>("SetStatus").Text = message;
                C<TextBlock>("SearchSetStatus").Text = message;
                Log(message);
            });
        }
        void DeleteSelected()
        {
            var selected = Selected();
            // Удаление только конфигурации, без изменения IPA на диске или приложений на телефоне.
            engine.AppSets.DeleteMany(selected.Select(x => x.Name));
            RefreshSets(); C<TextBlock>("SetStatus").Text = "Удалено наборов: " + selected.Count + ". IPA сохранены.";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            int count = Selected().Count;
            C<Button>("AddToSet").IsEnabled = C<Button>("SaveSet").IsEnabled = !busy && view.SelectedDownloads().Count > 0;
            C<Button>("EditSet").IsEnabled = !busy && count == 1;
            C<Button>("UseSet").IsEnabled = C<Button>("DeleteSet").IsEnabled = !busy && count > 0;
            C<Button>("UseSet").Content = "В очередь скачивания (" + count + ")";
            C<Button>("DeleteSet").Content = "Удалить наборы (" + count + ")";
            C<ComboBox>("SearchSet").IsEnabled = C<ListBox>("AppSets").IsEnabled = C<TextBox>("SetName").IsEnabled = !busy;
        }
    }
}

// Presentation/AppVersionWindow.cs
namespace IpaDesktop
{
    /// <summary>Таблица версий с ручным ID, выборочной загрузкой метаданных и прерываемыми запросами.</summary>
    public sealed class AppVersionWindow : IDisposable
    {
        public Window Window { get; private set; }
        public List<string> SelectedIDs { get; private set; }
        readonly CancellationTokenSource lifetime;
        CancellationTokenSource operation;
        bool loading, closing;
        readonly DataGrid grid;
        readonly TextBlock status;
        readonly TextBox manual;
        readonly Button details, add, insert, cancel;
        readonly List<AppVersionRow> rows;
        readonly AppVersions service;
        readonly string appId;
        public AppVersionWindow(Window owner, string name, string appId, List<AppVersionRow> rows, AppVersions service, CancellationToken ct)
        {
            this.appId = appId; this.rows = rows; this.service = service;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            SelectedIDs = new List<string>();
            Window = AppDialogs.Create(owner, "Версии — " + name, 920, 610); Window.MinWidth = 750; Window.MinHeight = 470;
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock { Text = "Выберите несколько версий (Ctrl / Shift). Сведения запрашиваются только по кнопке, до 20 версий за раз.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
            layout.Children.Add(heading);
            grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, EnableRowVirtualization = true, EnableColumnVirtualization = false, ItemsSource = rows };
            foreach (var column in new[] { new[] { "ID версии", "ID", "135" }, new[] { "Версия", "Version", "100" }, new[] { "Мин. ОС", "MinimumOS", "90" }, new[] { "Платформы", "Platforms", "135" }, new[] { "Состояние", "State", "280" } })
                grid.Columns.Add(new DataGridTextColumn { Header = column[0], Binding = new Binding(column[1]), Width = new DataGridLength(Int32.Parse(column[2])) });
            Grid.SetRow(grid,1);layout.Children.Add(grid);
            var input = new DockPanel { Margin = new Thickness(0,12,0,0) };
            insert = new Button { Content = "Добавить ID в таблицу", Margin = new Thickness(8,0,0,0) };DockPanel.SetDock(insert,Dock.Right);input.Children.Add(insert);
            manual = new TextBox { ToolTip = "Один или несколько ID версий через запятую или пробел", MinWidth = 150 };input.Children.Add(manual);Grid.SetRow(input,2);layout.Children.Add(input);
            var footer = new StackPanel { Margin = new Thickness(0,10,0,0) };
            status = new TextBlock { Text = "Версий: " + rows.Count + ". Можно ввести известный ID вручную; доступность проверит Apple.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };footer.Children.Add(status);
            var actions = new WrapPanel();details = new Button { Content = "Получить сведения" }; add = new Button { Content = "Добавить выбранные в очередь" };cancel = new Button { Content = "Закрыть" };
            actions.Children.Add(details);actions.Children.Add(add);actions.Children.Add(cancel);footer.Children.Add(actions);Grid.SetRow(footer,3);layout.Children.Add(footer);Window.Content=layout;
            grid.SelectionChanged += delegate { Refresh(); };
            insert.Click += delegate
            {
                try
                {
                    var ids = AppVersions.ParseManual(manual.Text);grid.SelectedItems.Clear();
                    foreach(var id in ids)
                    {
                        var row=rows.FirstOrDefault(x=>x.ID==id);
                        if(row==null){row=new AppVersionRow{ID=id};rows.Insert(0,row);}
                    }
                    grid.Items.Refresh();foreach(var row in rows.Where(x=>ids.Contains(x.ID)))grid.SelectedItems.Add(row);
                    manual.Text="";status.Text="Добавлено ID: "+ids.Count+". Можно запросить сведения или добавить в очередь.";Refresh();
                }
                catch(ArgumentException e){status.Text=e.Message;}
            };
            details.Click += async delegate
            {
                var selected=grid.SelectedItems.Cast<AppVersionRow>().ToList();
                if(selected.Count>20){status.Text="Выберите не более 20 версий для одного запроса сведений.";return;}
                loading=true;operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);Refresh();
                try
                {
                    for(int i=0;i<selected.Count;i++)
                    {
                        operation.Token.ThrowIfCancellationRequested();var row=selected[i];
                        status.Text="Сведения: "+(i+1)+" / "+selected.Count+" · ID "+row.ID;
                        try { await service.ReadAsync(appId,row,operation.Token); }
                        catch(OperationCanceledException){throw;}
                        catch(Exception e){row.State=e.Message.Length>240?e.Message.Substring(0,240):e.Message;}
                        grid.Items.Refresh();
                    }
                    status.Text="Обработано версий: "+selected.Count+". Неизвестные данные не подтверждают совместимость с iPhone.";
                }
                catch(OperationCanceledException){status.Text="Запрос отменён. Полученные сведения сохранены в таблице.";}
                finally{loading=false;operation.Dispose();operation=null;Refresh();if(closing)Window.Close();}
            };
            add.Click+=delegate {SelectedIDs=grid.SelectedItems.Cast<AppVersionRow>().Select(x=>x.ID).ToList();Window.DialogResult=true;};
            cancel.Click+=delegate{if(loading)operation.Cancel();else Window.Close();};
            Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(loading){closing=true;e.Cancel=true;operation.Cancel();}};
            Refresh();
        }
        void Refresh()
        {
            grid.IsEnabled=!loading;manual.IsEnabled=!loading;insert.IsEnabled=!loading;
            details.IsEnabled=!loading&&grid.SelectedItems.Count>0;add.IsEnabled=details.IsEnabled;
            cancel.Content=loading?"Отменить запрос":"Закрыть";
        }
        public void Dispose(){lifetime.Dispose();}
    }
}

// Presentation/Desktop.cs
namespace IpaDesktop
{
    /// <summary>Точка сборки WPF-приложения: связывает контроллеры, управляет операцией, отменой и журналом.</summary>
    public sealed class Desktop
    {
        Window w;
        Engine engine;
        CancellationTokenSource cancellation;
        bool busy;
        bool setting;
        bool exitingForUpdate;
        long lastProgress;
        AccountController account;
        SearchController search;
        FilesController files;
        UpdatesController updates;
        AppSetsController appSets;
        StorageController storage;
        public Window Window
        {
            get
            {
                return w;
            }
        }
        internal bool IsBusy
        {
            get
            {
                return busy;
            }
        }
        internal bool IsUpdatingSettings
        {
            get
            {
                return setting;
            }
        }
        internal void RequestRestart()
        {
            exitingForUpdate = true;
        }
        /// <summary>Возвращает текущий выбор, а при пустой очереди — независимый снимок последнего пакета.</summary>
        internal List<DownloadItem> SelectedDownloads() { return search.SelectedDownloads(); }
        /// <summary>Дополняет очередь ID из наборов и сохраняет уже выбранные версии приложений.</summary>
        internal int AddSetApps(IEnumerable<AppRow> apps) { return search.AddSetApps(apps); }
        internal void RefreshAppSets()
        {
            if (appSets != null) appSets.RefreshSets();
        }
        internal void DeleteFiles(List<LocalRow> rows) { files.DeleteRows(rows); }
        internal void ShowStorage(List<LocalRow> rows) { if (storage != null) storage.Show(rows); }
        /// <summary>Применяет только последний результат сканирования, сохраняя выделение по пути файла.</summary>
        internal void RefreshFiles()
        {
            if (files != null) files.RefreshFiles();
        }
        /// <summary>Обновляет состояния всех созданных контроллеров; допускает частичную инициализацию окна.</summary>
        internal void ApplyActions()
        {
            if (account != null) account.RefreshActions();
            if (search != null) search.RefreshActions();
            if (files != null) files.RefreshActions();
            if (updates != null) updates.RefreshActions();
            if (appSets != null) appSets.RefreshActions();
            if (storage != null) storage.RefreshActions();
            C<Button>("NoticeRetry").IsEnabled = !busy && noticeRetry != null && lastReport != null && lastReport.Failed > 0 &&
                (lastReport.Kind == "Скачивание" ? C<Button>("RetryDownloads").IsEnabled : C<Button>("RetryInstalls").IsEnabled);
            C<Button>("ShowLastReport").IsEnabled = lastReport != null;
        }
        internal T C<T>(string name) where T : class
        {
            return w.FindName(name) as T;
        }
        /// <summary>Подключает действие к кнопке и передаёт исключения в общий обработчик интерфейса.</summary>
        internal void On(string name, Action action)
        {
            C<Button>(name).Click += delegate
            {
                try
                {
                    action();
                } catch (Exception e)
                {
                    Error(e);
                }
            };
        }
        readonly BufferedJournal journalBuffer = new BufferedJournal();
        System.Windows.Threading.DispatcherTimer journalTimer;
        readonly object privateGate = new object();
        readonly HashSet<string> reportPrivateValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Запоминает значения для удаления из диагностического отчёта с ограничением размера набора.</summary>
        internal void RegisterPrivate(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 1024) return;
            lock (privateGate) if (reportPrivateValues.Count < 2000) reportPrivateValues.Add(value);
        }
        readonly QueueProgress queueProgress = new QueueProgress();
        readonly CompletionSound completionSound = new CompletionSound();
        OperationReport activeReport;
        OperationReport lastReport;
        Action noticeRetry;
        /// <summary>Фиксирует самостоятельный снимок очереди и её действие повтора.</summary>
        internal void StartReport(string kind, int total, Action retry)
        {
            activeReport = new OperationReport { Kind = kind, Total = total };
            noticeRetry = retry;
            C<Border>("OperationNotice").Visibility = Visibility.Collapsed;
        }
        internal void ReportItem(string name, Exception error)
        {
            if (activeReport != null) activeReport.Add(name, error);
        }
        void RenderNotice()
        {
            if (lastReport == null) return;
            var names = String.Join(", ", lastReport.Items.Where(x => !x.Success).Take(1).Select(x => x.Name + ": " + x.ShortError));
            C<TextBlock>("NoticeText").Text = lastReport.Summary + (names.Length == 0 ? "" : " · " + names);
            C<TextBlock>("NoticeText").ToolTip = lastReport.Details;
            bool errors = lastReport.Failed > 0;
            var notice = C<Border>("OperationNotice");
            notice.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(errors ? "#38232C" : lastReport.Canceled ? "#383122" : "#173529"));
            notice.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(errors ? "#E17B8B" : lastReport.Canceled ? "#D7BC79" : "#6AB893"));
            C<Button>("NoticeRetry").Visibility = errors && noticeRetry != null ? Visibility.Visible : Visibility.Collapsed;
            notice.Visibility = Visibility.Visible;
            ApplyActions();
        }

        /// <summary>Показывает название приложения и позицию в текущем пакете.</summary>
        internal void BeginQueue(string kind, int index, int total, string app)
        {
            queueProgress.Begin(kind, index, total, app);
            C<TextBlock>("Status").Text = kind + " " + index + "/" + total + ": " + app;
            RenderQueue();
        }
        /// <summary>Фиксирует итоговые счётчики, прекращая обновление завершённого прогресса.</summary>
        internal void CompleteQueue(int success, int failed)
        {
            queueProgress.Complete(success, failed);
            RenderQueue();
            completionSound.Notify(engine.Settings.SoundsEnabled, success, failed);
            if (activeReport != null) { lastReport = activeReport; RenderNotice(); }
        }
        /// <summary>Отрисовывает снимок прогресса в UI-потоке без прямого доступа фоновых процессов к WPF.</summary>
        void RenderQueue()
        {
            C<TextBlock>("OperationDetail").Text = queueProgress.StatusText;
            C<TextBlock>("TransferDetail").Text = queueProgress.TransferText;
            C<TextBlock>("OperationDetail").ToolTip = queueProgress.Text;
            if (!queueProgress.Active) return;
            C<ProgressBar>("Progress").IsIndeterminate = busy && queueProgress.Indeterminate;
            C<ProgressBar>("Progress").Value = queueProgress.Value;
        }
        /// <summary>Передаёт метрики прогрессу, а обычные сообщения — в ограниченный буфер журнала.</summary>
        internal void Log(string text)
        {
            if (queueProgress.Feed(text)) return;
            journalBuffer.Add(text);
        }
        /// <summary>Переносит накопленные сообщения журнала в интерфейс.</summary>
        void FlushJournal()
        {
            JournalView.Flush(C<TextBox>("Journal"), journalBuffer, false);
        }
        /// <summary>Показывает ошибку пользователю и сохраняет сообщение в журнале.</summary>
        internal void Error(Exception e)
        {
            Log(e.Message);
            C<TextBlock>("Status").Text = "Ошибка — подробности в журнале";
            if (activeReport == null || activeReport.Failed == 0)
            {
                lastReport = new OperationReport { Kind = "Операция", Total = 1 };
                lastReport.Add(activeReport == null ? "Ошибка операции" : activeReport.Kind, e);
                noticeRetry = null;
            }
            else lastReport = activeReport;
            RenderNotice();
        }
        /// <summary>Отображает настройки, блокируя повторную реакцию обработчиков переключателей.</summary>
        internal void StateView()
        {
            C<TextBlock>("EngineVersion").Text = "Движок: " + (engine.Settings.Version ?? "не установлен");
            setting = true;
            C<CheckBox>("SoundsEnabled").IsChecked = engine.Settings.SoundsEnabled;
            C<CheckBox>("AutoUpdate").IsChecked = engine.Settings.AutoUpdate;
            C<CheckBox>("AutoUpdateDeviceTools").IsChecked = engine.Settings.AutoUpdateDeviceTools;
            C<TextBlock>("DeviceToolsVersion").Text = (engine.Settings.DeviceSource == EngineUpdater.DeviceRepository ? "Наш комплект: " : "Прежний комплект: ") + (engine.Settings.DeviceVersion ?? (engine.Settings.Source == null ? engine.Settings.Version : null) ?? "не установлен");
            setting = false;
        }
        /// <summary>Не допускает параллельных операций, управляет отменой и восстанавливает UI в finally.</summary>
        internal async void Work(string title, Func<CancellationToken, Task> action)
        {
            if (busy) return;
            busy = true;
            queueProgress.Reset();
            C<TextBlock>("OperationDetail").Text = "";
            cancellation = new CancellationTokenSource();
            string[] buttons =
            {
                "Login", "AccountInfo", "Logout", "Search", "Versions", "VersionInfo", "Download", "RefreshFiles", "ImportFile", "DeleteFiles", "Devices", "InstallFiles", "CheckUpdate", "Rollback", "CheckGuiUpdate", "InstallGuiUpdate", "AddChosen", "ClearChosen", "AddInstall", "ClearInstall", "CheckAppleSupport", "CheckDeviceUpdate", "RollbackDevices", "RetryDownloads", "ForgetDownloadErrors", "RetryInstalls", "ForgetInstallErrors", "SaveSet", "AddToSet", "UseSet", "DeleteSet", "EditSet", "FileInfo", "DeviceInfo", "SelectOld", "DeleteStorage", "RefreshStorage"
            };
            foreach (var name in buttons) C<Button>(name).IsEnabled = false;
            C<ItemsControl>("ChosenFiles").IsEnabled = false;
            C<ItemsControl>("ChosenApps").IsEnabled = false;
            C<CheckBox>("CheckGuiUpdates").IsEnabled = false;
            C<CheckBox>("AutoUpdate").IsEnabled = false;
            C<CheckBox>("AutoUpdateDeviceTools").IsEnabled = false;
            C<Button>("Cancel").IsEnabled = true;
            C<ProgressBar>("Progress").IsIndeterminate = true;
            C<TextBlock>("Status").Text = title;
            Log(title);
            ApplyActions();
            try
            {
                await action(cancellation.Token);
                C<TextBlock>("Status").Text = "Готово";
            } catch (OperationCanceledException)
            {
                queueProgress.Stop("Отменено");
                if (activeReport != null) { activeReport.Canceled = true; lastReport = activeReport; RenderNotice(); }
                Log("Операция отменена.");
                C<TextBlock>("Status").Text = "Отменено";
            } catch (Exception e)
            {
                queueProgress.Stop("Завершено с ошибкой");
                Error(e);
            } finally
            {
                busy = false;
                activeReport = null;
                cancellation.Dispose();
                cancellation = null;
                foreach (var name in buttons) C<Button>(name).IsEnabled = true;
                C<CheckBox>("CheckGuiUpdates").IsEnabled = true;
                C<CheckBox>("AutoUpdate").IsEnabled = true;
                C<CheckBox>("AutoUpdateDeviceTools").IsEnabled = true;
                C<Button>("Cancel").IsEnabled = false;
                C<ProgressBar>("Progress").IsIndeterminate = false;
                C<ProgressBar>("Progress").Value = 0;
                RenderQueue();
                StateView();
                RefreshFiles();
                ApplyActions();
                if (exitingForUpdate) w.Close();
            }
        }
        public Desktop() : this(null, true)
        {
        }
        public Desktop(Engine backend, bool checkAtStartup) : this(backend, checkAtStartup, AppDomain.CurrentDomain.BaseDirectory)
        {
        }
        public Desktop(Engine backend, bool checkAtStartup, string assetDirectory)
        {
            InstallationLayout.Ensure(assetDirectory);
            using (var stream = File.OpenRead(Path.Combine(assetDirectory, "MainWindow.xaml"))) w = (Window) XamlReader.Load(stream);
            journalTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, w.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            JournalView.Attach(C<TextBox>("Journal"), journalBuffer);
            journalTimer.Tick += delegate
            {
                FlushJournal();
                if (busy) RenderQueue();
            };
            journalTimer.Start();
            w.Closed += delegate
            {
                journalTimer.Stop();
            };
            w.SourceInitialized += delegate
            {
                // На небольшом экране сохраняем прежний минимум; увеличенную высоту ограничиваем рабочей областью.
                w.Height = Math.Min(w.Height, Math.Max(w.MinHeight, SystemParameters.WorkArea.Height));
                WindowTheme.Apply(w);
            };
            w.Activated += delegate
            {
                WindowTheme.Apply(w);
            };
            using (var icon = File.OpenRead(Path.Combine(assetDirectory, "Assets", "App.ico")))
            {
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(icon, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                w.Icon = decoder.Frames[0];
                C<Image>("BrandIcon").Source = decoder.Frames[decoder.Frames.Count - 1];
            }
            engine = backend ?? new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPA-Desktop"));
            engine.Log = Log;
            engine.Progress = delegate(double value)
            {
                long now = Stopwatch.GetTimestamp();
                if (value < 100 && (now - lastProgress) / (double) Stopwatch.Frequency < 0.15) return;
                lastProgress = now;
                w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    C < ProgressBar >("Progress").IsIndeterminate = false;
                    C < ProgressBar >("Progress").Value = value;
                }));
            };
            account = new AccountController(this, engine);
            search = new SearchController(this, engine);
            files = new FilesController(this, engine);
            updates = new UpdatesController(this, engine);
            storage = new StorageController(this, engine);
            appSets = new AppSetsController(this, engine);
            StateView();
            RefreshFiles();
            Action showReport = delegate
            {
                if (lastReport != null) AppDialogs.ShowText(w.OwnedWindows.Cast<Window>().FirstOrDefault(x => x.IsActive) ?? w, "Итог операции", lastReport.Details);
            };
            On("ShowLastReport", showReport);
            On("NoticeDetails", showReport);
            On("DismissNotice", delegate { C<Border>("OperationNotice").Visibility = Visibility.Collapsed; });
            On("NoticeRetry", delegate { if (!busy && noticeRetry != null) noticeRetry(); });
            On("Cancel", delegate
            {
                if (cancellation != null) cancellation.Cancel();
            });
            On("ExportDiagnostics", delegate
            {
                JournalView.Flush(C<TextBox>("Journal"), journalBuffer, true);
                List<string> privateValues;
                lock (privateGate) privateValues = reportPrivateValues.ToList();
                privateValues.Add(Environment.UserName);
                var email = C<TextBlock>("AccountLabel").Text;
                if (email.Contains("@")) privateValues.Add(email);
                foreach (var device in C<ComboBox>("DeviceList").Items.Cast<DeviceRow>()) { privateValues.Add(device.ID); privateValues.Add(device.Name); }
                var report = DiagnosticReport.Create(C<TextBox>("Journal").Text, engine.Settings.Version ?? "not installed", engine.Settings.DeviceVersion ?? "not installed", privateValues);
                var dialog = new SaveFileDialog { Filter = "Текстовый отчёт (*.txt)|*.txt", FileName = "IPA-Desktop-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
                if (dialog.ShowDialog(w) != true) return;
                File.WriteAllText(dialog.FileName, report, Encoding.UTF8);
                Log("Отчёт диагностики сохранён.");
            });
            On("OpenFolder", delegate
            {
                Process.Start(new ProcessStartInfo(engine.Downloads)
                {
                    UseShellExecute = true
                });
            });
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(w, "Сначала отмените текущую операцию и дождитесь её завершения.", "Выполняется операция");
                }
            };
            ApplyActions();
            w.Loaded += delegate
            {
                Log("IPA Desktop " + AppInfo.Version + ". Файлы: " + engine.Downloads);
                if (!checkAtStartup) return;
                Work("Проверка движка и сохранённого входа…", async delegate(CancellationToken ct)
                {
                    await files.CheckSupport(ct);
                    if (engine.Settings.CheckGuiUpdates)
                    {
                        try
                        {
                            await updates.CheckGui(ct);
                        } catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) throw;
                            Log("Проверка IPA Desktop: время ожидания истекло.");
                        } catch (Exception e)
                        {
                            Log("Проверка IPA Desktop: " + e.Message);
                        }
                    }
                    if (engine.Settings.AutoUpdate)
                    {
                        try
                        {
                            await updates.Update(ct, true);
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            Log("Не удалось обновить движок: " + e.Message);
                        }
                    }
                    if (engine.Settings.AutoUpdateDeviceTools)
                    {
                        try { await updates.UpdateDevices(ct, true); }
                        catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) throw;
                            Log("Проверка инструментов для iPhone: время ожидания истекло.");
                        }
                        catch (Exception e) { Log("Не удалось обновить инструменты для iPhone: " + e.Message); }
                    }
                    if (!String.IsNullOrEmpty(engine.Settings.Current))
                    {
                        try
                        {
                            await account.Account(ct);
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            Log("Проверка входа: " + e.Message);
                        }
                    }
                });
            };
        }
        /// <summary>Создаёт единственный экземпляр приложения и запускает цикл сообщений WPF.</summary>
        [STAThread] public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\IPA-Desktop-0.1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("IPA Desktop уже запущен.");
                    return;
                }
                try
                {
                    var app = new Application();
                    var desktop = new Desktop();
                    app.Run(desktop.w);
                } catch (Exception e)
                {
                    MessageBox.Show(e.ToString(), "Ошибка запуска IPA Desktop");
                }
            }
        }
    }
}

// Presentation/FilesController.cs
namespace IpaDesktop
{
    /// <summary>Управляет локальными IPA и установкой на iPhone; наборы и хранилище обслуживаются отдельно.</summary>
    public sealed class FilesController : WindowController
    {
        AppleSupportReport appleSupport;
        readonly InstallationSelection installChosen = new InstallationSelection();
        readonly InstallationSelection failedInstalls = new InstallationSelection();
        List<LocalRow> allFiles = new List<LocalRow>();
        List<LocalRow> visibleFiles = new List<LocalRow>();
        bool updatingAccounts;
        string AccountKey
        {
            get
            {
                var account = C<ComboBox>("FileAccount").SelectedItem as LocalFileAccount;
                return account == null ? null : account.Key;
            }
        }
        public FilesController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ComboBox>("FileAccount").SelectionChanged += delegate
            {
                if (!updatingAccounts) ApplyFileFilter();
            };
            On("SelectAllFiles", delegate
            {
                if (busy) return;
                // Снимок фильтра не затрагивает ранее выбранные IPA других аккаунтов.
                foreach (var row in visibleFiles) installChosen.Add(row);
                RefreshInstallChosen();
            });
            RowClickSelection.Attach(C<DataGrid>("Files"), delegate(object item)
            {
                if (busy) return;
                var row = item as LocalRow;
                if (row == null) return;
                installChosen.Add(row);
                RefreshInstallChosen();
            });
            C<DataGrid>("Files").SelectionChanged += delegate
            {
                ApplyActions();
            };
            C<ComboBox>("DeviceList").SelectionChanged += delegate
            {
                ApplyActions();
            };
            On("AddInstall", delegate
            {
                foreach (var row in C<DataGrid>("Files").SelectedItems.Cast<LocalRow>()) installChosen.Add(row);
                RefreshInstallChosen();
            });
            On("ClearInstall", delegate
            {
                installChosen.Clear();
                RefreshInstallChosen();
            });
            SelectionRemove.Attach(C<ItemsControl>("ChosenFiles"), delegate(string path)
            {
                if (busy) return;
                installChosen.Remove(path);
                RefreshInstallChosen();
            });
            On("FileInfo", delegate
            {
                var row = C<DataGrid>("Files").SelectedItem as LocalRow;
                if (row != null) AppDialogs.Ipa(w, row).ShowDialog();
            });
            On("DeviceInfo", delegate
            {
                var device = C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
                if (device == null) return;
                Work("Сведения об iPhone…", async delegate(CancellationToken ct)
                {
                    var details = await engine.Devices.DetailsAsync(device, ct);
                    ct.ThrowIfCancellationRequested();
                    AppDialogs.ShowText(w, "Об iPhone", details);
                });
            });
            On("RefreshFiles", RefreshFiles);
            On("DeleteFiles", delegate { DeleteRows(installChosen.Snapshot()); });
            On("ImportFile", delegate
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "IPA files (*.ipa)|*.ipa", Multiselect = true
                };
                if (dialog.ShowDialog(w) != true) return;
                var paths = dialog.FileNames;
                Work("Добавление IPA…", ct => engine.LocalFiles.ImportAsync(paths, ct));
            });
            On("CheckAppleSupport", delegate
            {
                Work("Проверка компонентов Apple…", CheckSupport);
            });
            On("OpenAppleSupportHelp", delegate
            {
                string url = appleSupport == null ? "https://support.apple.com/ru-ru/118290" : appleSupport.HelpUrl;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            });
            On("Devices", delegate
            {
                Work("Поиск iPhone…", async delegate(CancellationToken ct)
                {
                    await CheckSupport(ct);
                    var combo = C<ComboBox>("DeviceList");
                    combo.ItemsSource = null;
                    ApplyActions();
                    var devices = await engine.Devices.FindAsync(C<CheckBox>("SearchWifi").IsChecked == true, ct);
                    combo.ItemsSource = devices;
                    C<TextBlock>("WifiStatus").Text = "USB: " + devices.Count(x => !x.Network) + " · Wi-Fi: " + devices.Count(x => x.Network);
                    if (devices.Count > 0)
                    {
                        foreach (var device in devices) { view.RegisterPrivate(device.ID); view.RegisterPrivate(device.Name); }
                        combo.SelectedIndex = 0;
                        Log("Найдено: " + String.Join(", ", devices.Select(x => x.DisplayLabel)));
                    } else Log("iPhone не обнаружен. Проверьте USB, доверие и драйвер Apple.");
                    ApplyActions();
                });
            });
            C<CheckBox>("SearchWifi").Click += delegate
            {
                C<ComboBox>("DeviceList").ItemsSource = null;
                C<TextBlock>("WifiStatus").Text = "Нажмите «Найти iPhone»";
                ApplyActions();
            };
            On("WifiHelp", delegate { AppDialogs.ShowText(w, "Подключение iPhone по Wi-Fi",
                "1. Подключите iPhone по USB и подтвердите доверие компьютеру.\n2. В Apple Devices включите «Показывать это устройство при подключении к Wi-Fi» и примените настройки. В iTunes — «Синхронизировать с этим iPhone по Wi-Fi».\n3. Подключите компьютер и iPhone к одной локальной сети, затем отключите USB.\n4. Включите «USB + Wi-Fi (экспериментально)» и нажмите «Найти iPhone». Выберите строку с отметкой Wi-Fi.\n\nЕсли телефон не найден: разблокируйте его, проверьте службу Apple Mobile Device, Bonjour, разрешения Apple в брандмауэре и отсутствие изоляции устройств в гостевой сети. VPN может мешать обнаружению.\n\nРежим экспериментальный. При обрыве не переключаемся автоматически на USB и не повторяем установку без вашего действия. Проверьте результат на телефоне, прежде чем нажимать «Повторить ошибки». "); });
            On("InstallFiles", delegate { StartInstall(installChosen.Snapshot()); });
            On("RetryInstalls", delegate { StartInstall(failedInstalls.Snapshot()); });
            On("ForgetInstallErrors", delegate { failedInstalls.Clear(); ApplyActions(); });
        }
        /// <summary>После подтверждения перемещает IPA в корзину, продолжая обработку при ошибке отдельного файла.</summary>
        internal void DeleteRows(List<LocalRow> rows)
        {
            if (busy || rows.Count == 0) return;
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(SelectionLabel));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (MessageBox.Show(w, "Переместить в корзину выбранные IPA (" + rows.Count + ")?" + Environment.NewLine + Environment.NewLine + summary + Environment.NewLine + Environment.NewLine + "Приложения на iPhone останутся установленными.", "Удаление IPA", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Перемещение IPA в корзину…", async delegate(CancellationToken ct)
            {
                var failures = new List<string>(); int removed = 0;
                await Task.Run(delegate
                {
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        try { engine.LocalFiles.Delete(row.Path); removed++; Log("Перемещено в корзину: " + row.DisplayName); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e) { failures.Add(row.DisplayName + ": " + e.Message); }
                    }
                }, ct);
                Log("Удалено из папки IPA: " + removed);
                if (failures.Count > 0) throw new IOException("Не удалось удалить:" + Environment.NewLine + String.Join(Environment.NewLine, failures));
            });
        }
        /// <summary>Подтверждает выбранное устройство и запускает последовательную установку снимка очереди.</summary>
        void StartInstall(List<LocalRow> rows)
        {
            var device = C<ComboBox>("DeviceList").SelectedItem as DeviceRow;
            if (device == null || rows.Count == 0) throw new Exception("Выберите устройство и IPA для установки.");
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(SelectionLabel));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (MessageBox.Show(w, "Установить IPA (" + rows.Count + ") на " + device.DisplayLabel + "?" + Environment.NewLine + Environment.NewLine + summary, "Установка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Work("Установка IPA…", async delegate(CancellationToken ct)
            {
                view.StartReport("Установка", rows.Count, delegate { StartInstall(failedInstalls.Snapshot()); });
                await CheckSupport(ct);
                var result = await BatchRunner.Run(rows,
                    (row, token) => engine.Devices.InstallAsync(device, row, token),
                    (row, index) => { view.BeginQueue("Установка", index, rows.Count, row.DisplayName); Log("Установка " + row.DisplayName); },
                    row => { view.ReportItem(row.DisplayName, null); installChosen.Remove(row.Path); failedInstalls.Remove(row.Path); RefreshInstallChosen(); },
                    (row, error) => { view.ReportItem(row.DisplayName, error); failedInstalls.Add(row); Log("Ошибка установки " + row.DisplayName + ": " + error.Message); },
                    null, ct);
                view.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", result.Failed.Select(x => x.DisplayName)) + ". Кнопка «Повторить ошибки» повторит только эти IPA.");
            });
        }
        /// <summary>В подтверждении различает одноимённые копии разных версий и аккаунтов.</summary>
        static string SelectionLabel(LocalRow row)
        {
            return row.DisplayName + (String.IsNullOrWhiteSpace(row.Version) ? "" : " · " + row.Version) + " · " +
                (String.IsNullOrWhiteSpace(row.AppleAccount) ? "аккаунт не указан" : row.AppleAccount);
        }
        /// <summary>Обновляет статус компонентов Apple из отдельного сервиса проверки Windows.</summary>
        public async Task CheckSupport(CancellationToken cancellation)
        {
            appleSupport = await engine.AppleSupport.CheckAsync(cancellation);
            var label = C<TextBlock>("AppleSupportStatus");
            label.Text = appleSupport.Message;
            label.ToolTip = appleSupport.Details;
            string color = appleSupport.Status == AppleSupportStatus.Running ? "#81D4AD" :
                appleSupport.Status == AppleSupportStatus.NotDetected ? "#F2A0A0" : "#E8C382";
            label.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            Log(appleSupport.Message + ". " + appleSupport.Details);
        }

        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<ComboBox>("FileAccount").IsEnabled = !busy;
            var chosenPaths = new HashSet<string>(installChosen.Snapshot().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            C<Button>("SelectAllFiles").IsEnabled = !busy && visibleFiles.Any(x => !chosenPaths.Contains(x.Path));
            C<Button>("SelectAllFiles").Content = "Выбрать все (" + visibleFiles.Count + ")";
            C<CheckBox>("SearchWifi").IsEnabled = !busy;
            C<Button>("WifiHelp").IsEnabled = !busy;
            C<Button>("FileInfo").IsEnabled = !busy && C<DataGrid>("Files").SelectedItems.Count == 1;
            C<Button>("DeviceInfo").IsEnabled = !busy && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("RetryInstalls").IsEnabled = !busy && failedInstalls.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("RetryInstalls").Content = "Повторить ошибки (" + failedInstalls.Count + ")";
            C<Button>("RetryInstalls").ToolTip = String.Join(Environment.NewLine, failedInstalls.Snapshot().Select(x => x.DisplayName));
            C<Button>("ForgetInstallErrors").IsEnabled = !busy && failedInstalls.Count > 0;
            C<ItemsControl>("ChosenFiles").IsEnabled = !busy;
            C<Button>("AddInstall").IsEnabled = !busy && C<DataGrid>("Files").SelectedItems.Count > 0;
            C<Button>("DeleteFiles").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("DeleteFiles").Content = "Удалить выбранные IPA (" + installChosen.Count + ")";
            C<Button>("ClearInstall").IsEnabled = !busy && installChosen.Count > 0;
            C<Button>("InstallFiles").IsEnabled = !busy && installChosen.Count > 0 && C<ComboBox>("DeviceList").SelectedItem != null;
            C<Button>("Devices").Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color) System.Windows.Media.ColorConverter.ConvertFromString(C<ComboBox>("DeviceList").Items.Count == 0 ? "#16704C" : "#243247"));
        }
        void RefreshInstallChosen()
        {
            var chosen = installChosen.Snapshot();
            C<ItemsControl>("ChosenFiles").ItemsSource = chosen;
            int hidden = chosen.Count(x => !LocalFileAccounts.Matches(x, AccountKey));
            C<TextBlock>("InstallCount").Text = installChosen.Count == 0 ? "Двойной клик — выбрать IPA" : "Выбрано IPA: " + installChosen.Count + (hidden == 0 ? "" : " · вне фильтра: " + hidden);
            C<TextBlock>("InstallCount").ToolTip = "Установка и удаление используют все IPA в нижнем списке, включая выбранные у других аккаунтов.";
            C<Button>("InstallFiles").Content = "Установить (" + installChosen.Count + ")";
            ApplyActions();
        }
        /// <summary>Фильтрует готовый снимок в памяти, сохраняя видимое выделение по пути и всю очередь действий.</summary>
        void ApplyFileFilter()
        {
            var grid = C<DataGrid>("Files");
            var selected = new HashSet<string>(grid.SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            visibleFiles = allFiles.Where(x => LocalFileAccounts.Matches(x, AccountKey)).ToList();
            grid.ItemsSource = visibleFiles;
            foreach (var row in visibleFiles) if (selected.Contains(row.Path)) grid.SelectedItems.Add(row);
            C<TextBlock>("FilesSummary").Text = "Показано: " + visibleFiles.Count + " из " + allFiles.Count + " · " + StorageAnalysis.Bytes(visibleFiles.Sum(x => x.Bytes));
            C<TextBlock>("FilesEmpty").Visibility = visibleFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshInstallChosen();
        }
        int filesGeneration;
        /// <summary>Применяет только последний результат сканирования, сохраняя выделение по пути файла.</summary>
        public async void RefreshFiles()
        {
            int generation = ++filesGeneration;
            try
            {
                var rows = await engine.LocalFiles.ListAsync();
                if (generation != filesGeneration) return;
                allFiles = rows;
                var key = AccountKey;
                var accounts = LocalFileAccounts.Choices(rows);
                // Подавляем промежуточный SelectionChanged при замене списка аккаунтов.
                updatingAccounts = true;
                try
                {
                    C<ComboBox>("FileAccount").ItemsSource = accounts;
                    C<ComboBox>("FileAccount").SelectedItem = accounts.FirstOrDefault(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)) ?? accounts[0];
                }
                finally { updatingAccounts = false; }
                view.ShowStorage(rows);
                installChosen.Reconcile(rows);
                ApplyFileFilter();
            } catch (Exception e)
            {
                if (generation == filesGeneration) Log("Не удалось обновить список IPA: " + e.Message);
            }
        }
    }
}

// Presentation/JournalView.cs
namespace IpaDesktop
{
    /// <summary>Выводит буфер журнала в WPF и поддерживает прокрутку к последним сообщениям.</summary>
    public static class JournalView
    {
        /// <summary>Подключает обновление журнала при открытии вкладки и прокрутку к последней записи.</summary>
        public static void Attach(TextBox journal, BufferedJournal buffer)
        {
            journal.IsVisibleChanged += delegate
            {
                if (!journal.IsVisible) return;
                journal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate
                {
                    Flush(journal, buffer, true);
                    journal.ScrollToEnd();
                }));
            };
        }
        /// <summary>Добавляет накопленные строки и прокручивает вид к актуальным данным.</summary>
        public static void Flush(TextBox journal, BufferedJournal buffer, bool all)
        {
            var text = new StringBuilder();
            string chunk;
            do
            {
                chunk = buffer.Drain();
                text.Append(chunk);
            }
            while (all && chunk.Length > 0);
            if (text.Length == 0) return;
            if (text.Length > 120000) text.Remove(0, text.Length - 120000);
            if (journal.Text.Length > 120000) journal.Text = journal.Text.Substring(journal.Text.Length - 80000);
            journal.AppendText(text.ToString());
            if (journal.IsVisible) journal.ScrollToEnd();
        }
    }
}

// Presentation/RowClickSelection.cs
namespace IpaDesktop
{
    /// <summary>Добавляет строку двойным левым кликом, оставляя одиночный клик для обычного выделения.</summary>
    public static class RowClickSelection
    {
        /// <summary>Использует системное событие двойного клика WPF и исключает вложенные кнопки.</summary>
        public static void Attach(DataGrid grid, Action<object> add)
        {
            grid.AddHandler(Control.MouseDoubleClickEvent, new System.Windows.Input.MouseButtonEventHandler(delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
                var source = e.OriginalSource as DependencyObject;
                var row = ItemsControl.ContainerFromElement(grid, source) as DataGridRow;
                if (row == null) return;
                // Двойной клик по кнопке внутри строки не должен добавлять саму строку.
                for (var node = source; node != null && node != row; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node is System.Windows.Controls.Primitives.ButtonBase) return;
                add(row.Item);
            }), true);
        }
    }
}

// Presentation/SearchController.cs
namespace IpaDesktop
{
    /// <summary>Поиск, выбор версий и очередь скачивания; сохраняет снимки ошибок и последнего пакета.</summary>
    public sealed class SearchController : WindowController
    {
        readonly DownloadSelection chosen = new DownloadSelection();
        readonly SearchSession searchSession = new SearchSession();
        readonly DownloadSelection failedDownloads = new DownloadSelection();
        string versionApp;
        List<DownloadItem> lastDownloads = new List<DownloadItem>();
        public SearchController(Desktop view, Engine engine) : base(view, engine)
        {
            C<ComboBox>("VersionList").Items.Add("Последняя версия");
            C<ComboBox>("VersionList").SelectedIndex = 0;
            C<TextBox>("Query").TextChanged += delegate
            {
                searchSession.Change(C<TextBox>("Query").Text);
                if (String.IsNullOrWhiteSpace(C<TextBox>("Query").Text))
                {
                    C<DataGrid>("Apps").ItemsSource = null;
                    ApplyActions();
                }
            };
            On("Search", delegate
            {
                string query = C<TextBox>("Query").Text.Trim();
                if (query.Length == 0) return;
                int request = searchSession.Revision;
                Work("Поиск приложений…", async delegate(CancellationToken ct)
                {
                    if (query.All(Char.IsDigit))
                    {
                        if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").ItemsSource = new[]
                        {
                            new AppRow
                            {
                                Name = "Приложение по ID " + query, ID = query
                            }
                        };
                    } else
                    {
                        var local = await engine.CatalogSearch(query, ct);
                        if (!searchSession.IsCurrent(request)) return;
                        C<DataGrid>("Apps").ItemsSource = local;
                        Log("Найдено в каталогах: " + local.Count);
                        try
                        {
                            var r = await engine.Tool(new[]
                            {
                                "search", query, "--limit", "10"
                            }, ct, false);
                            Engine.Ensure(r);
                            if (!searchSession.IsCurrent(request)) return;
                            C<DataGrid>("Apps").ItemsSource = Engine.MergeApps(local, Engine.ParseApps(r.Output));
                        } catch (OperationCanceledException)
                        {
                            throw;
                        } catch (Exception e)
                        {
                            if (!searchSession.IsCurrent(request)) return;
                            if (local.Count == 0) throw;
                            Log("App Store: " + e.Message + ". Показаны результаты каталога.");
                        }
                    }
                    if (searchSession.IsCurrent(request)) C<DataGrid>("Apps").SelectedIndex = 0;
                });
            });
            C<TextBox>("Query").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter && C<Button>("Search").IsEnabled) C<Button>("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            C<DataGrid>("Apps").SelectionChanged += delegate
            {
                versionApp = null;
                C<ComboBox>("VersionList").Items.Clear();
                C<ComboBox>("VersionList").Items.Add("Последняя версия");
                C<ComboBox>("VersionList").SelectedIndex = 0;
                ApplyActions();
            };
            On("Versions", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Для списка версий выберите одно приложение.");
                string id = selected[0].ID;
                Work("Получение доступных версий…", async delegate(CancellationToken ct)
                {
                    var r = await engine.Tool(new[]
                    {
                        "list-versions", "-i", id
                    }, ct, false);
                    Engine.Ensure(r);
                    var values = Engine.Get(Engine.Object(r.Output), "externalVersionIdentifiers") as object[];
                    if (values == null) throw new Exception("Список версий не получен: формат ответа не поддерживается.");
                    if (SelectedApps().Count != 1 || SelectedApps()[0].ID != id) throw new Exception("Выбор приложения изменился. Повторите запрос версий.");
                    var combo = C<ComboBox>("VersionList");
                    combo.Items.Clear();
                    combo.Items.Add("Последняя версия");
                    foreach (var v in values.Reverse())
                    {
                        var s = Convert.ToString(v);
                        if (s.Length > 0 && s.All(Char.IsDigit)) combo.Items.Add(s);
                    }
                    combo.SelectedIndex = 0;
                    versionApp = id;
                    Log("Версий: " + values.Length + ". В списке отображаются идентификаторы Apple.");
                });
            });
            On("VersionTable", delegate
            {
                var apps = SelectedApps();
                if (apps.Count != 1) throw new Exception("Для таблицы версий выберите одно приложение.");
                var app = apps[0];
                Work("Получение списка версий…", async ct =>
                {
                    var rows = new List<AppVersionRow>();
                    try { rows = await engine.AppVersions.ListAsync(app.ID, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Log("Список версий недоступен: " + e.Message + ". Можно указать известный ID вручную."); }
                    ct.ThrowIfCancellationRequested();
                    using (var dialog = new AppVersionWindow(w, app.Name, app.ID, rows, engine.AppVersions, ct))
                    {
                        if (dialog.Window.ShowDialog() == true)
                        {
                            foreach (var id in dialog.SelectedIDs) chosen.AddVariant(app, id);
                            RefreshChosen();
                        }
                    }
                });
            });
            On("VersionInfo", delegate
            {
                var selected = SelectedApps();
                if (selected.Count != 1) throw new Exception("Выберите одно приложение.");
                var row = selected[0];
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && row.ID != versionApp) throw new Exception("Обновите список версий выбранного приложения.");
                Work("Получение сведений о версии…", async delegate(CancellationToken ct)
                {
                    if (version == null)
                    {
                        var list = await engine.Tool(new[]
                        {
                            "list-versions", "-i", row.ID
                        }, ct, false);
                        Engine.Ensure(list);
                        var ids = Engine.VersionIds(list.Output);
                        if (ids.Count == 0) throw new Exception("Движок не вернул доступных версий приложения.");
                        version = ids[ids.Count - 1];
                        Log("Последняя доступная версия: ID " + version);
                    }
                    var r = await engine.Tool(new[]
                    {
                        "get-version-metadata", "-i", row.ID, "--external-version-id", version
                    }, ct, false);
                    Engine.Ensure(r);
                    var data = Engine.Object(r.Output);
                    AppDialogs.ShowText(w, "Сведения о версии", row.Name + Environment.NewLine + Environment.NewLine +
                        ToolResponse.VersionMetadataText(data) + Environment.NewLine + "ID версии: " + version + Environment.NewLine + "App Store ID: " + row.ID);
                });
            });
            RowClickSelection.Attach(C<DataGrid>("Apps"), delegate(object item)
            {
                if (busy) return;
                var row = item as AppRow;
                if (row == null || chosen.Snapshot().Any(x => x.ID == row.ID)) return;
                var combo = C<ComboBox>("VersionList");
                chosen.AddVariant(row, combo.SelectedIndex > 0 && versionApp == row.ID ? Convert.ToString(combo.SelectedItem) : null);
                RefreshChosen();
            });
            On("AddChosen", delegate
            {
                var rows = SelectedApps();
                var combo = C<ComboBox>("VersionList");
                string version = combo.SelectedIndex > 0 ? Convert.ToString(combo.SelectedItem) : null;
                if (version != null && (rows.Count != 1 || rows[0].ID != versionApp)) throw new Exception("Для конкретной версии выберите одно приложение и обновите список версий.");
                foreach (var row in rows) chosen.AddVariant(row, version);
                RefreshChosen();
            });
            On("ClearChosen", delegate
            {
                chosen.Clear();
                lastDownloads.Clear();
                RefreshChosen();
            });
            SelectionRemove.Attach(C<ItemsControl>("ChosenApps"), delegate(string id)
            {
                if (busy) return;
                chosen.RemoveKey(id);
                RefreshChosen();
            });
            On("Download", delegate { StartDownloads(chosen.Snapshot()); });
            On("RetryDownloads", delegate { StartDownloads(failedDownloads.Snapshot(), false); });
            On("ForgetDownloadErrors", delegate { failedDownloads.Clear(); ApplyActions(); });

        }
        /// <summary>Возвращает текущий выбор, а при пустой очереди — независимый снимок последнего пакета.</summary>
        internal List<DownloadItem> SelectedDownloads()
        {
            return chosen.Count > 0 ? chosen.Snapshot() : lastDownloads.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
        }
        /// <summary>Дополняет очередь ID из наборов и сохраняет уже выбранные версии приложений.</summary>
        internal int AddSetApps(IEnumerable<AppRow> apps)
        {
            int added = chosen.AddMissing(apps);
            RefreshChosen();
            C<TabItem>("SearchTab").IsSelected = true;
            return added;
        }
        /// <summary>Запускает пакет и обновляет отдельные списки успешных и неудачных запросов.</summary>
        void StartDownloads(List<DownloadItem> rows, bool remember = true)
        {
            if (rows.Count == 0) throw new Exception("Добавьте приложения в выбранное.");
            if (remember) lastDownloads = rows.Select(x => new DownloadItem { ID = x.ID, Name = x.Name, VersionID = x.VersionID }).ToList();
            Work("Скачивание IPA…", async delegate(CancellationToken ct)
            {
                view.StartReport("Скачивание", rows.Count, delegate { StartDownloads(failedDownloads.Snapshot(), false); });
                var result = await BatchRunner.Run(rows,
                    (row, token) => engine.ApplicationDownloads.DownloadAsync(row, token),
                    (row, index) => { view.BeginQueue("Скачивание", index, rows.Count, row.Name); Log("В очереди: " + row.Label); },
                    row =>
                    {
                        view.ReportItem(row.Name, null);
                        chosen.RemoveKey(row.Key);
                        failedDownloads.RemoveKey(row.Key);
                        RefreshChosen();
                    },
                    (row, error) =>
                    {
                        view.ReportItem(row.Name, error);
                        failedDownloads.AddVariant(new AppRow { ID = row.ID, Name = row.Name }, row.VersionID);
                        Log("Не удалось скачать " + row.Name + ": " + error.Message);
                    }, RefreshLocalFiles, ct);
                view.CompleteQueue(result.Succeeded, result.Failed.Count);
                if (result.Failed.Count > 0) throw new Exception("Не удалось скачать: " + String.Join(", ", result.Failed.Select(x => x.Name)) + ". Кнопка «Повторить ошибки» повторит только эти приложения.");
            });
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("RetryDownloads").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("RetryDownloads").Content = "Повторить ошибки (" + failedDownloads.Count + ")";
            C<Button>("RetryDownloads").ToolTip = String.Join(Environment.NewLine, failedDownloads.Snapshot().Select(x => x.Label));
            C<Button>("ForgetDownloadErrors").IsEnabled = !busy && failedDownloads.Count > 0;
            C<Button>("AddChosen").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count > 0;
            C<Button>("ClearChosen").IsEnabled = !busy && (chosen.Count > 0 || lastDownloads.Count > 0);
            C<Button>("Download").IsEnabled = !busy && chosen.Count > 0;
            C<ItemsControl>("ChosenApps").IsEnabled = !busy;
            C<Button>("VersionTable").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count == 1;
            C<Button>("VersionInfo").IsEnabled = !busy && C<DataGrid>("Apps").SelectedItems.Count == 1;
        }
        void RefreshChosen()
        {
            C<ItemsControl>("ChosenApps").ItemsSource = chosen.Snapshot();
            C<TextBlock>("ChosenCount").Text = chosen.Count == 0 ? "Добавьте приложения из результатов поиска" : "Выбрано: " + chosen.Count + " · сохраняются при новом поиске";
            C<Button>("Download").Content = "Скачать (" + chosen.Count + ")";
            if (chosen.Count == 0 && lastDownloads.Count > 0) C<TextBlock>("SearchSetStatus").Text = "Для набора: приложения последней загрузки (" + lastDownloads.Count + ").";
            else C<TextBlock>("SearchSetStatus").Text = chosen.Count > 0 ? "Для набора выбрано: " + chosen.Count + ". Введите название или выберите набор." : "Выберите приложения, затем введите название набора.";
            ApplyActions();
        }
        List<AppRow> SelectedApps()
        {
            var rows = C<DataGrid>("Apps").SelectedItems.Cast<AppRow>().ToList();
            if (rows.Count == 0) throw new Exception("Выберите приложение в таблице.");
            return rows;
        }
    }
}

// Presentation/SelectionRemove.cs
namespace IpaDesktop
{
    /// <summary>Обрабатывает крестики в шаблонах выбранных элементов по стабильному ID или пути.</summary>
    public static class SelectionRemove
    {
        /// <summary>Находит крестик во вложенном WPF-шаблоне и передаёт идентификатор удаляемого элемента.</summary>
        public static void Attach(ItemsControl list, Action<string> remove)
        {
            list.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                // WPF can retarget Source to an item container; OriginalSource identifies the button.
                var current = e.OriginalSource as DependencyObject;
                while (current != null && !(current is Button))
                {
                    current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
                }
                var button = current as Button;
                if (button == null || button.Tag == null) return;
                remove(Convert.ToString(button.Tag));
                e.Handled = true;
            }), true);
        }
    }
}

// Presentation/StorageController.cs
namespace IpaDesktop
{
    /// <summary>Отображает место на диске и кандидатов на очистку, используя общий снимок локальных IPA.</summary>
    public sealed class StorageController : WindowController
    {
        List<LocalRow> rows = new List<LocalRow>();
        public StorageController(Desktop view, Engine engine) : base(view, engine)
        {
            On("RefreshStorage", view.RefreshFiles);
            On("DeleteStorage", delegate { view.DeleteFiles(C<DataGrid>("StorageFiles").SelectedItems.Cast<LocalRow>().ToList()); });
            On("SelectOld", delegate
            {
                var grid = C<DataGrid>("StorageFiles"); grid.SelectedItems.Clear();
                foreach (var row in rows.Where(x => x.IsOld)) grid.SelectedItems.Add(row);
                ApplyActions();
            });
            C<DataGrid>("StorageFiles").SelectionChanged += delegate { ApplyActions(); };
        }
        /// <summary>Обновляет таблицу и сводку, сохраняя выделение файлов по полному пути.</summary>
        internal void Show(List<LocalRow> available)
        {
            var grid = C<DataGrid>("StorageFiles");
            var selected = new HashSet<string>(grid.SelectedItems.Cast<LocalRow>().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            rows = available; grid.ItemsSource = rows;
            foreach (var row in rows) if (selected.Contains(row.Path)) grid.SelectedItems.Add(row);
            C<TextBlock>("StorageSummary").Text = "IPA: " + rows.Count + " · занято: " + StorageAnalysis.Bytes(rows.Sum(x => x.Bytes)) + " · старых версий: " + rows.Count(x => x.IsOld) + " (" + StorageAnalysis.Bytes(rows.Where(x => x.IsOld).Sum(x => x.Bytes)) + ")";
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("SelectOld").IsEnabled = !busy && rows.Any(x => x.IsOld);
            C<Button>("DeleteStorage").IsEnabled = !busy && C<DataGrid>("StorageFiles").SelectedItems.Count > 0;
        }
    }
}

// Presentation/UpdatesController.cs
namespace IpaDesktop
{
    /// <summary>Связывает кнопки обновления и отката GUI, ipatool и инструментов установки.</summary>
    public sealed class UpdatesController : WindowController
    {
        Release guiRelease;
        public UpdatesController(Desktop view, Engine engine) : base(view, engine)
        {
            C<CheckBox>("SoundsEnabled").Click += delegate
            {
                bool previous = engine.Settings.SoundsEnabled;
                engine.Settings.SoundsEnabled = C<CheckBox>("SoundsEnabled").IsChecked == true;
                try { engine.Save(); }
                catch (Exception e)
                {
                    engine.Settings.SoundsEnabled = previous;
                    StateView();
                    Error(e);
                }
            };
            C<CheckBox>("CheckGuiUpdates").IsChecked = engine.Settings.CheckGuiUpdates;
            C<CheckBox>("CheckGuiUpdates").Click += delegate
            {
                engine.Settings.CheckGuiUpdates = C<CheckBox>("CheckGuiUpdates").IsChecked == true;
                try
                {
                    engine.Save();
                } catch (Exception e)
                {
                    Error(e);
                }
            };
            On("CheckGuiUpdate", delegate
            {
                Work("Проверка версии IPA Desktop…", CheckGui);
            });
            On("InstallGuiUpdate", delegate
            {
                if (guiRelease == null) return;
                if (MessageBox.Show(w, "Скачать и установить IPA Desktop " + guiRelease.tag_name + "? Программа закроется и запустится снова. Скачанные IPA и настройки сохранятся.", "Обновление программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Work("Скачивание обновления IPA Desktop…", async delegate(CancellationToken ct)
                {
                    string stage = await GuiUpdater.Prepare(guiRelease, ct, delegate(double value)
                    {
                        w.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            C<ProgressBar>("Progress").IsIndeterminate = false;
                            C<ProgressBar>("Progress").Value = value;
                        }));
                    });
                    ct.ThrowIfCancellationRequested();
                    GuiUpdater.Launch(stage);
                    view.RequestRestart();
                });
            });
            On("OpenGuiReleases", delegate
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl)
                {
                    UseShellExecute = true
                });
            });
            On("OpenRelease", delegate
            {
                Process.Start(new ProcessStartInfo("https://github.com/Sorvigolova/ipatool/releases")
                {
                    UseShellExecute = true
                });
            });
            C<CheckBox>("AutoUpdateDeviceTools").Click += delegate
            {
                engine.Settings.AutoUpdateDeviceTools = C<CheckBox>("AutoUpdateDeviceTools").IsChecked == true;
                try { engine.Save(); } catch (Exception e) { Error(e); }
            };
            On("CheckDeviceUpdate", delegate
            {
                Work("Проверка инструментов для iPhone…", ct => UpdateDevices(ct, false));
            });
            On("RollbackDevices", delegate
            {
                if (MessageBox.Show(w, "Вернуть предыдущий комплект для iPhone и отключить его автообновление?", "Откат инструментов", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                try { engine.RollbackDeviceTools(); StateView(); ApplyActions(); }
                catch (Exception e) { Error(e); }
            });
            On("OpenDeviceReleases", delegate
            {
                Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl + "?q=device-tools") { UseShellExecute = true });
            });
            On("CheckUpdate", delegate
            {
                Work("Проверка обновлений…", ct => Update(ct, false));
            });
            On("Rollback", delegate
            {
                if (MessageBox.Show(w, "Вернуть предыдущий движок и отключить автообновление?", "Откат", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    engine.Rollback();
                    StateView();
                }
            });
            C<CheckBox>("AutoUpdate").Checked += delegate
            {
                if (!setting)
                {
                    engine.Settings.AutoUpdate = true;
                    try
                    {
                        engine.Save();
                    } catch (Exception e)
                    {
                        Error(e);
                    }
                }
            };
            C<CheckBox>("AutoUpdate").Unchecked += delegate
            {
                if (!setting)
                {
                    engine.Settings.AutoUpdate = false;
                    try
                    {
                        engine.Save();
                    } catch (Exception e)
                    {
                        Error(e);
                    }
                }
            };
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public override void RefreshActions()
        {
            C<Button>("InstallGuiUpdate").IsEnabled = !busy && guiRelease != null;
            C<Button>("RollbackDevices").IsEnabled = !busy && !String.IsNullOrEmpty(engine.Settings.PreviousDeviceCurrent);
        }
        public async Task Update(CancellationToken ct, bool startup)
        {
            var release = await engine.Check(ct);
            if (engine.Settings.Source == EngineUpdater.SourceRepository && String.Equals(release.tag_name, engine.Settings.Version, StringComparison.Ordinal) && !String.IsNullOrEmpty(engine.Settings.DeviceCurrent))
            {
                Log("Установлен актуальный релиз " + release.tag_name);
                return;
            }
            if (!startup && MessageBox.Show(w, "Установить официальный релиз " + release.tag_name + "?\nТекущая версия будет сохранена для отката.", "Обновление движка", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await engine.Install(release, ct);
            if (startup) Log("Автоматическое обновление завершено.");
        }
        public async Task UpdateDevices(CancellationToken ct, bool startup)
        {
            var release = await engine.CheckDeviceTools(ct);
            if (engine.DeviceToolsCurrent(release))
            {
                Log("Наш комплект для iPhone актуален: " + release.tag_name);
                return;
            }
            if (!startup && MessageBox.Show(w, "Обновить наш комплект инструментов для iPhone до " + release.tag_name + "? Предыдущий комплект будет сохранён для отката.", "Обновление инструментов", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await engine.InstallDeviceTools(release, ct);
        }
        public async Task CheckGui(CancellationToken ct)
        {
            guiRelease = await GuiUpdater.Check(ct);
            string text = guiRelease == null ? "Установлена актуальная версия " + GuiUpdater.CurrentVersion : "Доступна версия " + guiRelease.tag_name;
            C<TextBlock>("GuiVersionStatus").Text = text;
            C<Button>("InstallGuiUpdate").Content = guiRelease == null ? "Установить обновление" : "Обновить до " + guiRelease.tag_name;
            C<TextBlock>("UpdateNotice").Text = guiRelease == null ? "" : text + " — вкладка «Настройки»";
            C<TextBlock>("UpdateNotice").Visibility = guiRelease == null ? Visibility.Collapsed : Visibility.Visible;
            Log(text);
        }
    }
}

// Presentation/WindowController.cs
namespace IpaDesktop
{
    /// <summary>Общий доступ контроллеров к окну, рабочей операции, журналу и обновлению состояния действий.</summary>
    public abstract class WindowController
    {
        protected readonly Desktop view;
        protected readonly Engine engine;
        protected Window w
        {
            get
            {
                return view.Window;
            }
        }
        protected bool busy
        {
            get
            {
                return view.IsBusy;
            }
        }
        protected bool setting
        {
            get
            {
                return view.IsUpdatingSettings;
            }
        }
        protected WindowController(Desktop view, Engine engine)
        {
            this.view = view;
            this.engine = engine;
        }
        protected T C<T>(string name) where T : class
        {
            return view.C<T>(name);
        }
        /// <summary>Подключает действие к кнопке и передаёт исключения в общий обработчик интерфейса.</summary>
        protected void On(string name, Action action)
        {
            view.On(name, action);
        }
        /// <summary>Не допускает параллельных операций, управляет отменой и восстанавливает UI в finally.</summary>
        protected void Work(string title, Func<CancellationToken, Task> action)
        {
            view.Work(title, action);
        }
        /// <summary>Передаёт метрики прогрессу, а обычные сообщения — в ограниченный буфер журнала.</summary>
        protected void Log(string text)
        {
            view.Log(text);
        }
        /// <summary>Показывает ошибку пользователю и сохраняет сообщение в журнале.</summary>
        protected void Error(Exception error)
        {
            view.Error(error);
        }
        /// <summary>Обновляет состояния всех созданных контроллеров; допускает частичную инициализацию окна.</summary>
        protected void ApplyActions()
        {
            view.ApplyActions();
        }
        /// <summary>Отображает настройки, блокируя повторную реакцию обработчиков переключателей.</summary>
        protected void StateView()
        {
            view.StateView();
        }
        protected void RefreshLocalFiles()
        {
            view.RefreshFiles();
        }
        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        public abstract void RefreshActions();
    }
}
