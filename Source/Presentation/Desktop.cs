using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;

namespace IpaDesktop
{
    /// <summary>Точка сборки Avalonia-приложения: связывает контроллеры, управляет операцией, отменой и журналом.</summary>
    public sealed partial class Desktop
    {
        readonly Window w;
        readonly Engine engine;
        CancellationTokenSource cancellation;
        bool busy;
        bool setting;
        bool exitingForUpdate;
        bool closingAfterDeviceScan;
        long lastProgress;
        AccountController account;
        SearchController search;
        FilesController files;
        UpdatesController updates;
        AppSetsController appSets;
        StorageController storage;
        public Window Window => w;
        internal bool IsBusy => busy;
        internal bool IsUpdatingSettings => setting;
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
        internal T C<T>(string name) where T : Control
        {
            return w.FindControl<T>(name) ?? throw new InvalidOperationException("Missing UI control: " + name);
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
        internal async void RunAsyncCommand(Func<Task> action)
        {
            try { await action(); } catch (Exception error) { Error(error); }
        }
        internal void OnAsync(string name, Func<Task> action)
        {
            C<Button>(name).Click += async delegate
            {
                try { await action(); } catch (Exception error) { Error(error); }
            };
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
            C<Border>("ConnectionNotice").IsVisible=false;
            queueProgress.Reset();
            C<TextBlock>("OperationDetail").Text = "";
            cancellation = new CancellationTokenSource();
            string[] buttons =
            {
                "CheckConnection", "AccountSafetyReview", "Login", "AccountInfo", "Logout", "Search", "Versions", "VersionInfo", "Download", "RefreshFiles", "ImportFile", "DeleteFiles", "InstallFiles", "CheckUpdate", "Rollback", "CheckGuiUpdate", "InstallGuiUpdate", "AddChosen", "ClearChosen", "AddInstall", "ClearInstall", "CheckAppleSupport", "CheckDeviceUpdate", "RollbackDevices", "RetryDownloads", "ForgetDownloadErrors", "RetryInstalls", "ForgetInstallErrors", "SaveSet", "AddToSet", "UseSet", "DeleteSet", "EditSet", "FileInfo", "DeviceInfo", "SelectOld", "DeleteStorage", "RefreshStorage"
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
                await files.PauseDeviceDetectionAsync();
                await action(cancellation.Token);
                C<TextBlock>("Status").Text = "Готово";
            } catch (OperationCanceledException)
            {
                queueProgress.Stop("Отменено");
                if (activeReport != null) { activeReport.Canceled = true; lastReport = activeReport; RenderNotice(); }
                files.ConnectionCanceled();
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
                else files.ResumeDeviceDetection();
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
            w = new MainWindow();
            w.Title = "IPA Desktop · " + AppInfo.Version;
            C<TextBlock>("GuiVersionBadge").Text = AppInfo.Version;
            C<TextBlock>("GuiVersionStatus").Text = "Установлена версия " + AppInfo.Version;
            journalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            JournalView.Attach(C<TextBox>("Journal"), journalBuffer);
            journalTimer.Tick += delegate { FlushJournal(); if (busy) RenderQueue(); };
            journalTimer.Start();
            w.Closed += delegate { journalTimer.Stop(); };
            w.Opened += delegate
            {
                var screen = w.Screens.ScreenFromWindow(w);
                if (screen != null) w.Height = Math.Min(w.Height, Math.Max(w.MinHeight, screen.WorkingArea.Height / screen.Scaling));
            };
            using (var icon = AssetLoader.Open(new Uri("avares://IPA-Desktop/Assets/App.ico"))) w.Icon = new WindowIcon(icon);
            using (var icon = AssetLoader.Open(new Uri("avares://IPA-Desktop/Assets/App.ico"))) C<Image>("BrandIcon").Source = new Bitmap(icon);
            engine = backend ?? new Engine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPA-Desktop"));
            engine.Log = Log;
            engine.Progress = delegate(double value)
            {
                long now = Stopwatch.GetTimestamp();
                if (value < 100 && (now - lastProgress) / (double) Stopwatch.Frequency < 0.15) return;
                lastProgress = now;
                Dispatcher.UIThread.Post(new Action(delegate
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
            Func<Task> showReport = async delegate
            {
                if (lastReport != null) await AppDialogs.ShowText(w, "Итог операции", lastReport.Details);
            };
            OnAsync("ShowLastReport", showReport);
            OnAsync("NoticeDetails", showReport);
            On("DismissNotice", delegate { C<Border>("OperationNotice").IsVisible = false; });
            On("NoticeRetry", delegate { if (!busy && noticeRetry != null) noticeRetry(); });
            On("Cancel", delegate
            {
                if (cancellation != null) cancellation.Cancel();
            });
            OnAsync("ExportDiagnostics", async delegate
            {
                JournalView.Flush(C<TextBox>("Journal"), journalBuffer, true);
                List<string> privateValues;
                lock (privateGate) privateValues = reportPrivateValues.ToList();
                privateValues.Add(Environment.UserName);
                var email = C<TextBlock>("AccountLabel").Text;
                if (email.Contains("@")) privateValues.Add(email);
                foreach (var device in C<ComboBox>("DeviceList").Items.Cast<DeviceRow>()) { privateValues.Add(device.ID); privateValues.Add(device.Name); }
                var report = DiagnosticReport.Create(C<TextBox>("Journal").Text, engine.Settings.Version ?? "not installed", engine.Settings.DeviceVersion ?? "not installed", privateValues);
                var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Экспорт диагностики", SuggestedFileName = "IPA-Desktop-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                    FileTypeChoices = new[] { new FilePickerFileType("Текстовый отчёт") { Patterns = new[] { "*.txt" } } }
                });
                if (file == null) return;
                await using (var stream = await file.OpenWriteAsync())
                using (var writer = new StreamWriter(stream, Encoding.UTF8)) await writer.WriteAsync(report);
                Log("Отчёт диагностики сохранён.");
            });
            On("OpenFolder", delegate
            {
                Process.Start(new ProcessStartInfo(engine.Downloads)
                {
                    UseShellExecute = true
                });
            });
            w.Closing += delegate(object sender, WindowClosingEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                    C<TextBlock>("Status").Text = "Сначала отмените текущую операцию и дождитесь её завершения.";
                }
                else if (!closingAfterDeviceScan && files.IsDetecting)
                {
                    e.Cancel = true;
                    closingAfterDeviceScan = true;
                    w.IsEnabled = false;
                    files.StopDeviceDetection();
                    // Defer the second Close until this Closing event has returned and CLI cleanup is complete.
                    Dispatcher.UIThread.Post(new Action(async delegate
                    {
                        await files.PauseDeviceDetectionAsync();
                        w.Close();
                    }));
                }
            };
            ApplyActions();
            w.Opened += delegate
            {
                Log("IPA Desktop " + AppInfo.Version + ". Файлы: " + engine.Downloads);
                if (!checkAtStartup) return;
                files.StartDeviceDetection();
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
    }
}
