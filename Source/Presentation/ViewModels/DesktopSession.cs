using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class DesktopViewModel : IDisposable
{
    readonly Engine engine;
    CancellationTokenSource cancellation;
    bool setting, exitingForUpdate, closingAfterDeviceScan, disposed;
    long lastProgress;
    IDisposable journalTimer;
    AccountViewModel account;
    SearchViewModel search;
    FilesViewModel files;
    UpdatesViewModel updates;
    AppSetsViewModel appSets;
    StorageViewModel storage;
    LibraryViewModel library;
    public AccountViewModel Account => account;
    public SearchViewModel Search => search;
    public FilesViewModel Files => files;
    public UpdatesViewModel Updates => updates;
    public AppSetsViewModel AppSets => appSets;
    public StorageViewModel Storage => storage;
    public LibraryViewModel Library => library;
    FeatureViewModel[] Features = Array.Empty<FeatureViewModel>();
    public IDesktopInteraction Interaction { get; }
    public bool IsUpdatingSettings => setting;
    public Task ActiveOperation { get; private set; } = Task.CompletedTask;
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    int selectedTab;
    public string GuiVersionBadge => AppInfo.Version;
    internal void RequestRestart() => exitingForUpdate = true;
    /// <summary>Возвращает текущий выбор, а при пустой очереди — независимый снимок последнего пакета.</summary>
    internal List<DownloadItem> SelectedDownloads() { return search.SelectedDownloads(); }
    /// <summary>Дополняет очередь ID из наборов и сохраняет уже выбранные версии приложений.</summary>
    internal int AddSetApps(IEnumerable<AppRow> apps) { return search.AddSetApps(apps); }
    internal void RefreshAppSets()
    {
        if (appSets != null) appSets.RefreshSets();
    }
    internal Task DeleteFiles(List<LocalRow> rows) => files.DeleteRows(rows);
    internal void ShowStorage(List<LocalRow> rows) { if (storage != null) storage.Show(rows); }
    /// <summary>Применяет только последний результат сканирования, сохраняя выделение по пути файла.</summary>
    internal void RefreshFiles()
    {
        if (files != null) _ = files.RefreshFilesAsync();
    }
    /// <summary>Обновляет состояния всех созданных моделей; допускает частичную инициализацию окна.</summary>
    internal void ApplyActions()
    {
        if (account != null) account.RefreshActions();
        if (search != null) search.RefreshActions();
        if (files != null) files.RefreshActions();
        if (updates != null) updates.RefreshActions();
        if (appSets != null) appSets.RefreshActions();
        if (storage != null) storage.RefreshActions();
        library?.RefreshActions();
        NoticeRetryEnabled = !IsBusy && noticeRetry != null && lastReport != null && lastReport.Failed > 0 &&
            (lastReport.Kind == "Скачивание" ? Search.RetryDownloadsEnabled : Files.RetryInstallsEnabled);
        ShowLastReportEnabled = lastReport != null;
        Queue.Refresh();
        foreach (var feature in Features) feature?.RefreshCommands();
        RefreshCommands();
    }
    public async Task RunAsyncCommand(Func<Task> action)
    { try { await action(); } catch (Exception error) { Error(error); } }
    OperationCommand Command(Action action) => new(_ => { Interaction.CloseMenus(); action(); return Task.CompletedTask; }, Error);
    OperationCommand AsyncCommand(Func<Task> action) => new(_ => { Interaction.CloseMenus(); return action(); }, Error);
    /// <summary>Отображает настройки, блокируя повторную реакцию обработчиков переключателей.</summary>
    internal void StateView()
    {
        Updates.EngineVersion = "Движок: " + (engine.Settings.Version ?? "не установлен");
        setting = true;
        Updates.CheckGuiUpdates = engine.Settings.CheckGuiUpdates;
        Updates.SoundsEnabled = engine.Settings.SoundsEnabled;
        Updates.AutoUpdate = engine.Settings.AutoUpdate;
        Updates.AutoUpdateDeviceTools = engine.Settings.AutoUpdateDeviceTools;
        Updates.DeviceToolsVersion = (engine.Settings.DeviceSource == EngineUpdater.DeviceRepository ? "Наш комплект: " : "Прежний комплект: ") + (engine.Settings.DeviceVersion ?? (engine.Settings.Source == null ? engine.Settings.Version : null) ?? "не установлен");
        setting = false;
    }
    /// <summary>Не допускает параллельных операций, управляет отменой и восстанавливает UI в finally.</summary>
    internal void Work(string title, Func<CancellationToken, Task> action) => _ = RunWorkAsync(title, action);
    internal Task<bool> RunWorkAsync(string title, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return Task.FromResult(false);
        var pending = RunWorkCoreAsync(title, action); ActiveOperation = pending; return pending;
    }
    async Task<bool> RunWorkCoreAsync(string title, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return false;
        bool succeeded = false;
        IsBusy = true;
        ConnectionNoticeVisible = false;
        queueProgress.Reset();
        OperationDetail = "";
        cancellation = new CancellationTokenSource();
        Indeterminate = true;
        Status = title;
        Log(title);
        ApplyActions();
        try
        {
            await files.PauseDeviceDetectionAsync();
            await action(cancellation.Token);
            Status = "Готово";
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            queueProgress.Stop("Отменено");
            Queue.Stop("Операция отменена.", true);
            if (activeReport != null) { activeReport.Canceled = true; lastReport = activeReport; RenderNotice(); }
            files.ConnectionCanceled();
            Log("Операция отменена.");
            Status = "Отменено";
        }
        catch (Exception e)
        {
            queueProgress.Stop("Завершено с ошибкой");
            Queue.Stop(ErrorLocalizer.Describe(e).Description, false);
            Error(e);
        }
        finally
        {
            IsBusy = false;
            activeReport = null;
            cancellation.Dispose();
            cancellation = null;
            Indeterminate = false;
            Progress = 0;
            RenderQueue();
            StateView();
            RefreshFiles();
            ApplyActions();
            if (exitingForUpdate) Interaction.Close();
            else files.ResumeDeviceDetection();
        }
        return succeeded;
    }
    public DesktopViewModel(Engine backend, IDesktopInteraction interaction) : this()
    {
        engine = backend ?? throw new ArgumentNullException(nameof(backend));
        Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        CancelRequested = () => cancellation?.Cancel();
        JournalRequested = ShowJournal;
        ExportRequested = () => _ = RunAsyncCommand(ExportDiagnostics);
        account = new(this, engine); search = new(this, engine); files = new(this, engine);
        updates = new(this, engine); storage = new(this, engine); appSets = new(this, engine); library = new(this, engine);
        Features = new FeatureViewModel[] { account, search, files, updates, appSets, storage, library };
        engine.Log = Log;
        engine.Progress = value =>
        {
            long now = Stopwatch.GetTimestamp();
            if (value < 100 && (now - lastProgress) / (double)Stopwatch.Frequency < 0.15) return;
            lastProgress = now;
            Interaction.Post(() => { if (!disposed) { Indeterminate = false; Progress = value; } });
        };
        account.Initialize(); search.Initialize(); files.Initialize(); files.InitializeDeviceCommands();
        updates.Initialize(); storage.Initialize(); appSets.Initialize(); library.Initialize();
        Queue.CanResumeBatch = batch => batch.Kind == "Скачивание"
            ? !engine.AccountRequests.RequiresLogin && AccountKey != null && batch.AccountKey == AccountKey
            : files.CanResume(batch.TargetKey);
        ConfigureQueueStorage();
        Updates.GuiVersionStatus = "Установлена версия " + AppInfo.Version;
        StateView(); RefreshFiles();
        Func<Task> showReport = () => lastReport == null ? Task.CompletedTask : Interaction.ShowText("Итог операции", lastReport.Details);
        ShowLastReportCommand = AsyncCommand(showReport); NoticeDetailsCommand = AsyncCommand(showReport);
        DismissNoticeCommand = Command(() => OperationNoticeVisible = false);
        NoticeRetryCommand = AsyncCommand(() => !IsBusy && noticeRetry != null ? noticeRetry() : Task.CompletedTask);
        ExportDiagnosticsCommand = AsyncCommand(ExportDiagnostics);
        OpenFolderCommand = Command(() => SystemIntegration.Open(engine.Downloads));
        journalTimer = Interaction.Repeat(TimeSpan.FromMilliseconds(150), () =>
        {
            FlushJournal(); if (IsBusy) RenderQueue();
            if (IsBusy && DateTime.UtcNow - lastQueueSave > TimeSpan.FromSeconds(2)) SaveQueue();
        });
        foreach (var feature in Features) feature.ConfigureCommands();
        ConfigureCommands(); ApplyActions();
    }
    public void Opened(bool checkAtStartup)
    {
        Log("IPA Desktop " + AppInfo.Version + ". Файлы: " + engine.Downloads);
        if (!checkAtStartup) return;
        files.StartDeviceDetection();
        Work("Проверка движка и сохранённого входа…", async delegate (CancellationToken ct)
        {
            await files.CheckSupport(ct);
            if (engine.Settings.CheckGuiUpdates)
            {
                try
                {
                    await updates.CheckGui(ct);
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) throw;
                    Log("Проверка IPA Desktop: время ожидания истекло.");
                }
                catch (Exception e)
                {
                    Log("Проверка IPA Desktop: " + e.Message);
                }
            }
            if (engine.Settings.AutoUpdate)
            {
                try
                {
                    await updates.Update(ct, true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
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
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Log("Проверка входа: " + e.Message);
                }
            }
        });
    }
    public bool RequestClose()
    {
        if (IsBusy) { Status = "Сначала отмените текущую операцию и дождитесь её завершения."; return false; }
        if (!closingAfterDeviceScan && files.IsDetecting)
        {
            closingAfterDeviceScan = true; files.StopDeviceDetection();
            Interaction.Post(() => _ = RunAsyncCommand(async () => { await files.PauseDeviceDetectionAsync(); Interaction.Close(); }));
            return false;
        }
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; journalTimer?.Dispose(); files?.StopDeviceDetection(); SaveQueue();
        cancellation?.Cancel();
        if (engine != null) { engine.Log = delegate { }; engine.Progress = delegate { }; }
    }
}
