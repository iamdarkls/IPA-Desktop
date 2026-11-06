using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class DesktopViewModel : IDisposable, IAsyncDisposable
{
    readonly ISettingsStore settings;
    readonly WorkspacePaths paths;
    readonly IAccountSession accounts;
    readonly IAccountProtection protection;
    readonly ILocalApps localApps;
    readonly INetworkDiagnostics network;
    IDisposable eventSubscription;
    CancellationTokenSource cancellation;
    bool setting, exitingForUpdate, closingAfterTasks, disposed;
    bool isClosing;
    public bool IsClosing { get => isClosing; private set => Set(ref isClosing, value); }
    public BackgroundTasks Tasks { get; }
    public CancellationToken LifetimeToken => Tasks.Token;
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
    public Task RunAsyncCommand(Func<Task> action) => Tasks.Run(_ => action());
    OperationCommand Command(Action action) => new(_ => { Interaction.CloseMenus(); action(); return Task.CompletedTask; }, Error);
    OperationCommand AsyncCommand(Func<Task> action) => new(_ => { Interaction.CloseMenus(); return action(); }, Error);
    /// <summary>Отображает настройки, блокируя повторную реакцию обработчиков переключателей.</summary>
    internal void StateView()
    {
        Updates.EngineVersion = "Движок: " + (settings.Settings.Version ?? "не установлен");
        setting = true;
        Updates.CheckGuiUpdates = settings.Settings.CheckGuiUpdates;
        Updates.SoundsEnabled = settings.Settings.SoundsEnabled;
        Updates.AutoUpdate = settings.Settings.AutoUpdate;
        Updates.AutoUpdateDeviceTools = settings.Settings.AutoUpdateDeviceTools;
        Updates.DeviceToolsVersion = (!RuntimePlatform.Windows ? "Встроенный комплект: " : settings.Settings.DeviceSource == EngineUpdater.DeviceRepository ? "Наш комплект: " : "Прежний комплект: ") + (settings.Settings.DeviceVersion ?? (settings.Settings.Source == null ? settings.Settings.Version : null) ?? "не установлен");
        Updates.RefreshInstalledTools();
        setting = false;
    }
    /// <summary>Не допускает параллельных операций, управляет отменой и восстанавливает UI в finally.</summary>
    internal void Work(string title, Func<CancellationToken, Task> action) => _ = RunWorkAsync(title, action);
    internal Task<bool> RunWorkAsync(string title, Func<CancellationToken, Task> action)
    {
        if (IsBusy || IsClosing) return Task.FromResult(false);
        var pending = Tasks.Run(ct => RunWorkCoreAsync(title, action, ct)); ActiveOperation = pending; return pending;
    }
    async Task<bool> RunWorkCoreAsync(string title, Func<CancellationToken, Task> action, CancellationToken lifetime)
    {
        if (IsBusy) return false;
        bool succeeded = false;
        IsBusy = true;
        ConnectionNoticeVisible = false;
        queueProgress.Reset();
        OperationDetail = "";
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Indeterminate = true;
        Status = title;
        try
        {
            Log(title);
            ApplyActions();
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
    public DesktopViewModel(DesktopServices services, IDesktopInteraction interaction) : this()
    {
        ArgumentNullException.ThrowIfNull(services);
        settings = services.Settings; paths = services.Paths; accounts = services.Accounts;
        protection = services.Protection; localApps = services.LocalApps; network = services.Network;
        Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        CancelRequested = () => cancellation?.Cancel();
        JournalRequested = ShowJournal;
        ExportRequested = () => _ = RunAsyncCommand(ExportDiagnostics);
        account = new(this, services.Accounts, services.Protection, services.Login);
        search = new(this, services.Tools, services.Catalog, services.Versions, services.Downloads, services.Protection);
        files = new(this, services.Settings, services.Devices, services.LocalApps, services.AppleSupport, services.Devices as IDeviceEvents, services.Devices as IDeviceDiagnostics);
        updates = new(this, services.Settings, services.ToolUpdates, services.GuiUpdates);
        storage = new(this); appSets = new(this, services.AppSets, services.LocalApps, services.Downloads);
        library = new(this, services.Devices, services.LocalApps, services.Purchases, services.Protection);
        Features = new FeatureViewModel[] { account, search, files, updates, appSets, storage, library };
        eventSubscription = Tasks.Own(services.Events.Subscribe(Log, value =>
        {
            long now = Stopwatch.GetTimestamp();
            if (value < 100 && (now - lastProgress) / (double)Stopwatch.Frequency < 0.15) return;
            lastProgress = now;
            Interaction.Post(() => { if (!disposed && !IsClosing) { Indeterminate = false; Progress = value; } });
        }));
        account.Initialize(); search.Initialize(); files.Initialize(); files.InitializeDeviceCommands();
        updates.Initialize(); storage.Initialize(); appSets.Initialize(); library.Initialize();
        Queue.CanResumeBatch = batch => batch.Kind == "Скачивание"
            ? !protection.RequiresLogin && AccountKey != null && batch.AccountKey == AccountKey
            : files.CanResume(batch.TargetKey);
        ConfigureQueueStorage();
        Updates.GuiVersionStatus = "Установлена версия " + AppInfo.Version;
        StateView(); RefreshFiles();
        Func<Task> showReport = () => lastReport == null ? Task.CompletedTask : Interaction.ShowText("Итог операции", lastReport.Details);
        ShowLastReportCommand = AsyncCommand(showReport); NoticeDetailsCommand = AsyncCommand(showReport);
        DismissNoticeCommand = Command(() => OperationNoticeVisible = false);
        NoticeRetryCommand = AsyncCommand(() => !IsBusy && noticeRetry != null ? noticeRetry() : Task.CompletedTask);
        ExportDiagnosticsCommand = AsyncCommand(ExportDiagnostics);
        OpenFolderCommand = Command(() => SystemIntegration.Open(paths.Downloads));
        journalTimer = Tasks.Own(Interaction.Repeat(TimeSpan.FromMilliseconds(150), () =>
        {
            FlushJournal(); if (IsBusy) RenderQueue();
            if (IsBusy && DateTime.UtcNow - lastQueueSave > TimeSpan.FromSeconds(2)) SaveQueue();
        }));
        foreach (var feature in Features) feature.ConfigureCommands();
        ConfigureCommands(); ApplyActions();
    }
    internal NetworkSettingsViewModel CreateNetworkSettings() => new(this, settings, network);
    public void Opened(bool checkAtStartup)
    {
        Log("IPA Desktop " + AppInfo.Version + ". Файлы: " + paths.Downloads);
        if (!checkAtStartup) return;
        files.StartDeviceDetection();
        Work("Проверка движка и сохранённого входа…", async delegate (CancellationToken ct)
        {
            await files.CheckSupport(ct);
            if (settings.Settings.CheckGuiUpdates)
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
            if (settings.Settings.AutoUpdate)
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
            if (settings.Settings.AutoUpdateDeviceTools)
            {
                try { await updates.UpdateDevices(ct, true); }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) throw;
                    Log("Проверка инструментов для iPhone: время ожидания истекло.");
                }
                catch (Exception e) { Log("Не удалось обновить инструменты для iPhone: " + e.Message); }
            }
            if (!String.IsNullOrEmpty(settings.Settings.Current))
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
    public Task ShutdownAsync()
    {
        if (!IsClosing)
        {
            IsClosing = true; files?.StopDeviceDetection();
            CancelCommand.Refresh(); ShowJournalCommand.Refresh(); ExportCommand.Refresh();
            Queue.SetBusy(true); Device.SetBusy(true); ApplyActions();
        }
        return Tasks.StopAsync();
    }
    public bool RequestClose()
    {
        if (IsBusy) { Status = "Сначала отмените текущую операцию и дождитесь её завершения."; return false; }
        var pending = ShutdownAsync();
        if (pending.IsCompleted) return true;
        if (!closingAfterTasks)
        {
            closingAfterTasks = true;
            Interaction.Post(() => _ = CloseAfterShutdown(pending));
        }
        return false;
    }
    async Task CloseAfterShutdown(Task pending)
    {
        await pending; SaveQueue(); Interaction.Close();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; _ = ShutdownAsync(); SaveQueue();
    }
    public async ValueTask DisposeAsync() { await ShutdownAsync(); Dispose(); }
}
