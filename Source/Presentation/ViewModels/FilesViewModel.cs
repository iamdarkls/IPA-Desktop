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
    /// <summary>Управляет локальными IPA и установкой на iPhone; наборы и хранилище обслуживаются отдельно.</summary>
    public sealed partial class FilesViewModel : FeatureViewModel
    {
        public OperationCommand AddRowCommand { get; private set; }
        public OperationCommand RemoveChosenCommand { get; private set; }
        AppleSupportReport appleSupport;
        readonly HashSet<string> verifiedConnections = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> blockedConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static string ConnectionKey(DeviceRow device) => device.ID + "|" + device.Network;
        void ShowConnection(DeviceConnectionProgress state)
        {
            var device = DeviceList.SelectedItem as DeviceRow;
            if (device != null)
            {
                if (state.WaitingForTrust) blockedConnections.Add(ConnectionKey(device));
                if (state.Ready) { blockedConnections.Remove(ConnectionKey(device)); verifiedConnections.Add(ConnectionKey(device)); }
            }
            desktop.Device.SetState(state.Ready ? PhoneStatus.Ready : state.WaitingForTrust ? PhoneStatus.WaitingForTrust : PhoneStatus.Checking, state.Message);
            desktop.ConnectionNoticeVisible = true;
            desktop.ConnectionTitle = state.Title;
            desktop.ConnectionMessage = state.Message;
            desktop.Status = state.Title;
        }
        internal void ConnectionCanceled()
        {
            if (desktop.ConnectionNoticeVisible) ShowConnection(new("Операция отменена", "Очередь сохранена. Автоматического продолжения не будет; при необходимости проверьте связь и запустите оставшиеся IPA.", false));
            if (desktop.Device.Status is PhoneStatus.Checking or PhoneStatus.WaitingForTrust) desktop.Device.SetState(PhoneStatus.Lost, "Проверка отменена. Восстановите связь перед продолжением очереди.");
        }
        async Task EnsureConnection(DeviceRow device, CancellationToken ct)
        {
            try
            {
                await deviceService.EnsureReadyAsync(device, ShowConnection, ct); blockedConnections.Remove(ConnectionKey(device));
                ShowConnection(new("iPhone готов к установке", "Доверие подтверждено. Устройство: " + device.DisplayLabel, false, true));
            }
            catch (OperationCanceledException) { blockedConnections.Add(ConnectionKey(device)); throw; }
            catch (DeviceConnectionException error) { blockedConnections.Add(ConnectionKey(device)); ShowConnection(new(error.Failure.Title, error.Failure.Remedy, false)); desktop.Device.SetState(PhoneStatus.Lost, error.Failure.Remedy); throw; }
        }
        readonly InstallationSelection installChosen = new InstallationSelection();
        readonly InstallationSelection failedInstalls = new InstallationSelection();
        List<LocalRow> allFiles = new List<LocalRow>();
        List<LocalRow> visibleFiles = new List<LocalRow>();
        bool updatingAccounts;
        IDisposable deviceTimer;
        CancellationTokenSource deviceScanCancellation;
        Task deviceScan = Task.FromResult(true);
        bool deviceMonitoring, deviceClosed;
        int deviceGeneration;
        string deviceSignature = "", deviceWarning = "";
        internal bool IsDetecting { get { return !deviceScan.IsCompleted; } }
        string AccountKey
        {
            get
            {
                var account = FileAccount.SelectedItem as LocalFileAccount;
                return account == null ? null : account.Key;
            }
        }
        readonly ISettingsStore settings;
        readonly IDeviceService deviceService;
        readonly ILocalApps localApps;
        readonly IAppleSupport support;
        public FilesViewModel(DesktopViewModel desktop, ISettingsStore settings, IDeviceService devices, ILocalApps localApps, IAppleSupport support) : base(desktop) { this.settings = settings; deviceService = devices; this.localApps = localApps; this.support = support; }
        internal void Initialize()
        {
            FileAccount.SelectionChanged += delegate
            {
                if (!updatingAccounts) ApplyFileFilter();
            };
            SelectAllFilesCommand = Command(delegate
            {
                if (busy) return;
                // Снимок фильтра не затрагивает ранее выбранные IPA других аккаунтов.
                foreach (var row in visibleFiles) installChosen.Add(row);
                RefreshInstallChosen();
            });
            AddRowCommand = new OperationCommand(item =>
            {
                if (busy) return Task.CompletedTask;
                var row = item as LocalRow;
                if (row == null) return Task.CompletedTask;
                installChosen.Add(row);
                RefreshInstallChosen(); return Task.CompletedTask;
            }, Error);
            Files.SelectionChanged += delegate
            {
                ApplyActions();
            };
            DeviceList.SelectionChanged += delegate
            {
                if (!busy) desktop.ConnectionNoticeVisible = false;
                var selected = DeviceList.SelectedItem as DeviceRow;
                desktop.Device.Select(selected == null ? null : ConnectionKey(selected), selected != null && blockedConnections.Contains(ConnectionKey(selected)), selected != null && verifiedConnections.Contains(ConnectionKey(selected)));
                ApplyActions();
            };
            AddInstallCommand = Command(delegate
            {
                foreach (var row in Files.SelectedItems.Cast<LocalRow>()) installChosen.Add(row);
                RefreshInstallChosen();
            });
            ClearInstallCommand = Command(delegate
            {
                installChosen.Clear();
                RefreshInstallChosen();
            });
            RemoveChosenCommand = new OperationCommand(key =>
            {
                if (!busy) { installChosen.Remove(Convert.ToString(key)); RefreshInstallChosen(); }
                return Task.CompletedTask;
            }, Error);
            FileInfoCommand = AsyncCommand(async delegate
            {
                var row = Files.SelectedItem as LocalRow;
                if (row != null) await desktop.Interaction.ShowIpa(row);
            });
            DeviceInfoCommand = Command(delegate
            {
                var device = DeviceList.SelectedItem as DeviceRow;
                if (device == null) return;
                Work("Сведения об iPhone…", async delegate (CancellationToken ct)
                {
                    var details = await deviceService.DetailsAsync(device, ct);
                    ct.ThrowIfCancellationRequested();
                    await desktop.Interaction.ShowText("Об iPhone", details);
                });
            });
            RefreshFilesCommand = AsyncCommand(RefreshFilesAsync);
            DeleteFilesCommand = AsyncCommand(() => DeleteRows(installChosen.Snapshot()));
            ImportFileCommand = AsyncCommand(async delegate
            {
                var paths = await desktop.Interaction.ImportFiles();
                if (paths.Length == 0) return;
                Work("Добавление IPA…", ct => localApps.ImportAsync(paths, ct));
            });
            CheckAppleSupportCommand = Command(delegate
            {
                Work("Проверка компонентов Apple…", CheckSupport);
            });
            OpenAppleSupportHelpCommand = Command(delegate
            {
                string url = appleSupport == null ? "https://support.apple.com/ru-ru/118290" : appleSupport.HelpUrl;
                SystemIntegration.Open(url);
            });
            PropertyChanged += async (_, change) =>
            {
                if (change.PropertyName != nameof(SearchWifi)) return;
                await desktop.RunAsyncCommand(ChangeWifi);
            };
        }
        async Task ChangeWifi()

        {
            deviceGeneration++;
            DeviceList.Items = null;
            deviceSignature = "";
            desktop.Device.SetState(PhoneStatus.Disconnected, "Определение iPhone…");
            WifiStatus = "";
            ApplyActions();
            await PauseDeviceDetectionAsync();
            await RefreshDevicesAsync();
        }
        internal void InitializeDeviceCommands()
        {
            WifiHelpCommand = AsyncCommand(async delegate
            {
                await desktop.Interaction.ShowText("Подключение iPhone по Wi-Fi",
                "1. Подключите iPhone по USB и подтвердите доверие компьютеру.\n2. В Apple Devices включите «Показывать это устройство при подключении к Wi-Fi» и примените настройки. В iTunes — «Синхронизировать с этим iPhone по Wi-Fi».\n3. Подключите компьютер и iPhone к одной локальной сети, затем отключите USB.\n4. Включите «USB + Wi-Fi (экспериментально)»: список обновится автоматически. Выберите строку с отметкой Wi-Fi.\n\nЕсли телефон не найден: разблокируйте его, проверьте службу Apple Mobile Device, Bonjour, разрешения Apple в брандмауэре и отсутствие изоляции устройств в гостевой сети. VPN может мешать обнаружению.\n\nПри обрыве установка не переключается автоматически на USB. Проверьте результат на телефоне перед повтором.");
            });
            desktop.Device.CheckRequested = () =>
            {
                var device = DeviceList.SelectedItem as DeviceRow;
                if (device != null) Work("Проверка связи и доверия…", ct => EnsureConnection(device, ct));
            };
            InstallFilesCommand = AsyncCommand(() => StartInstall(installChosen.Snapshot()));
            RetryInstallsCommand = AsyncCommand(() => StartInstall(failedInstalls.Snapshot()));
            ForgetInstallErrorsCommand = Command(delegate { failedInstalls.Clear(); ApplyActions(); });
        }
        internal void StartDeviceDetection()
        {
            if (deviceClosed) return;
            deviceMonitoring = true;
            deviceTimer ??= desktop.Tasks.Own(desktop.Interaction.Repeat(TimeSpan.FromSeconds(3), () => _ = desktop.RunAsyncCommand(RefreshDevicesAsync)));
        }
        internal void StopDeviceDetection()
        {
            deviceClosed = true;
            deviceMonitoring = false;
            deviceGeneration++;
            deviceTimer?.Dispose(); deviceTimer = null;
            if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
        }
        internal async Task PauseDeviceDetectionAsync()
        {
            if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
            await deviceScan;
        }
        internal void ResumeDeviceDetection()
        {
            if (deviceMonitoring) _ = desktop.RunAsyncCommand(RefreshDevicesAsync);
        }
        /// <summary>Один фоновый поиск за раз; рабочая операция отменяет и дожидается его до запуска CLI.</summary>
        internal Task RefreshDevicesAsync()
        {
            if (deviceClosed || busy || IsDetecting) return deviceScan;
            if (String.IsNullOrEmpty(settings.Settings.DeviceCurrent ?? settings.Settings.Current))
            {
                ApplyDevices(new List<DeviceRow>());
                desktop.Device.SetState(PhoneStatus.Disconnected, "Установите инструменты iPhone в настройках.");
                return Task.FromResult(true);
            }
            var wifi = SearchWifi; var generation = deviceGeneration;
            deviceScan = desktop.Tasks.Run(cancellation =>
            {
                var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                stop.CancelAfter(TimeSpan.FromSeconds(25)); deviceScanCancellation = stop;
                return DetectDevicesAsync(wifi, generation, stop);
            });
            return deviceScan;
        }
        async Task DetectDevicesAsync(bool wifi, int generation, CancellationTokenSource stop)
        {
            try
            {
                var warnings = new List<string>();
                var devices = await deviceService.FindAsync(wifi, stop.Token, warnings.Add);
                if (deviceClosed || busy || generation != deviceGeneration || stop.IsCancellationRequested) return;
                ApplyDevices(devices);
                ShowDeviceWarning(String.Join(Environment.NewLine, warnings));
                var selected = DeviceList.SelectedItem as DeviceRow;
                if (selected != null && !verifiedConnections.Contains(ConnectionKey(selected)) && !blockedConnections.Contains(ConnectionKey(selected)))
                {
                    try
                    {
                        await deviceService.ValidateReadyAsync(selected, stop.Token);
                        if (deviceClosed || busy || generation != deviceGeneration || stop.IsCancellationRequested) return;
                        verifiedConnections.Add(ConnectionKey(selected));
                        desktop.Device.SetState(PhoneStatus.Ready, "Доверие и связь с выбранным iPhone проверены.");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (DeviceConnectionException error)
                    {
                        blockedConnections.Add(ConnectionKey(selected));
                        desktop.Device.SetState(error.Failure.Code is FailureCode.Pairing or FailureCode.TrustPending or FailureCode.Locked
                            ? PhoneStatus.WaitingForTrust : PhoneStatus.Lost, error.Failure.Remedy + " Нажмите «Проверить связь».");
                    }
                    ApplyActions();
                }
                else if (selected != null && blockedConnections.Contains(ConnectionKey(selected)))
                {
                    try
                    {
                        await deviceService.ValidateReadyAsync(selected, stop.Token);
                        if (deviceClosed || busy || generation != deviceGeneration || stop.IsCancellationRequested) return;
                        blockedConnections.Remove(ConnectionKey(selected));
                        ShowConnection(new("Связь с iPhone восстановлена", "Очередь сохранена. Можно запустить оставшиеся IPA или повторить ошибки; успешные установки не повторяются.", false, true));
                        ApplyActions();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Keep the queue blocked until this same device answers a real handshake. */ }
                }
            }
            catch (OperationCanceledException)
            {
                // User work and window close intentionally cancel background discovery.
                if (!deviceClosed && !busy && generation == deviceGeneration)
                {
                    ApplyDevices(new List<DeviceRow>());
                    ShowDeviceWarning("iPhone не ответил вовремя. Разблокируйте телефон и проверьте соединение.");
                }
            }
            catch (Exception error)
            {
                if (!deviceClosed && !busy && generation == deviceGeneration)
                {
                    ApplyDevices(new List<DeviceRow>());
                    ShowDeviceWarning(error.Message);
                }
            }
            finally
            {
                if (deviceScanCancellation == stop) deviceScanCancellation = null;
                stop.Dispose();
            }
        }
        void ShowDeviceWarning(string warning)
        {
            DeviceDetectionStatusTip = warning.Length == 0 ? "Список проверяется автоматически каждые 3 секунды в отсутствие других операций." : warning;
            if (warning.Length > 0) desktop.Device.SetState(desktop.Device.Status, "Проверьте подключение iPhone — подробности в подсказке.");
            if (warning != deviceWarning && warning.Length > 0) Log("Автоопределение iPhone: " + warning);
            deviceWarning = warning;
        }
        void ApplyDevices(List<DeviceRow> devices)
        {
            var combo = DeviceList;
            var previous = combo.SelectedItem as DeviceRow;
            if (previous != null && !devices.Any(x => ConnectionKey(x).Equals(ConnectionKey(previous), StringComparison.OrdinalIgnoreCase)))
            {
                verifiedConnections.Remove(ConnectionKey(previous)); blockedConnections.Add(ConnectionKey(previous));
            }
            var signature = String.Join("\n", devices.Select(x => x.ID.ToUpperInvariant() + "|" + x.Network + "|" + x.Name).OrderBy(x => x, StringComparer.Ordinal));
            if (signature != deviceSignature)
            {
                combo.Items = devices;
                combo.SelectedItem = previous == null ? (devices.Count == 1 ? devices[0] : null) :
                    devices.FirstOrDefault(x => x.Network == previous.Network && String.Equals(x.ID, previous.ID, StringComparison.OrdinalIgnoreCase));
                foreach (var device in devices) { desktop.RegisterPrivate(device.ID); desktop.RegisterPrivate(device.Name); }
                Log(devices.Count == 0 ? "iPhone отключён." : "Обнаружены iPhone: " + String.Join(", ", devices.Select(x => x.DisplayLabel)));
                deviceSignature = signature;
            }
            var selected = combo.SelectedItem as DeviceRow;
            desktop.Device.Select(selected == null ? null : ConnectionKey(selected),
                previous != null && selected == null || selected != null && blockedConnections.Contains(ConnectionKey(selected)),
                selected != null && verifiedConnections.Contains(ConnectionKey(selected)));
            if (selected == null && previous != null) desktop.Device.SetState(PhoneStatus.Lost, "iPhone отключён. Подключите тот же телефон и проверьте связь; очередь сохранена.");
            WifiStatus = "USB: " + devices.Count(x => !x.Network) + " · Wi-Fi: " + devices.Count(x => x.Network);
            if (devices.Count > 1 && selected == null) desktop.Device.SetState(PhoneStatus.Disconnected, "Выберите iPhone для установки.");
            ApplyActions();
        }
        /// <summary>После подтверждения перемещает IPA в корзину, продолжая обработку при ошибке отдельного файла.</summary>
        internal Task DeleteRows(List<LocalRow> rows) => desktop.RunAsyncCommand(() => DeleteRowsCore(rows));
        async Task DeleteRowsCore(List<LocalRow> rows)
        {
            if (busy || rows.Count == 0) return;
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(SelectionLabel));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (!await desktop.Interaction.Confirm("Удаление IPA", "Переместить в корзину выбранные IPA (" + rows.Count + ")?\n\n" + summary + "\n\nПриложения на iPhone останутся установленными.")) return;
            await desktop.RunWorkAsync("Перемещение IPA в корзину…", async delegate (CancellationToken ct)
            {
                var failures = new List<string>(); int removed = 0;
                await Task.Run(delegate
                {
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        try { localApps.Delete(row.Path); removed++; Log("Перемещено в корзину: " + row.DisplayName); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e) { failures.Add(row.DisplayName + ": " + e.Message); }
                    }
                }, ct);
                Log("Удалено из папки IPA: " + removed);
                if (failures.Count > 0) throw new IOException("Не удалось удалить:" + Environment.NewLine + String.Join(Environment.NewLine, failures));
            });
        }
        /// <summary>Подтверждает выбранное устройство и запускает последовательную установку снимка очереди.</summary>
        internal Task StartInstall(List<LocalRow> rows, QueueBatchViewModel resumeBatch = null, string expectedDevice = null) => desktop.RunAsyncCommand(() => StartInstallCore(rows, resumeBatch, expectedDevice));
        async Task StartInstallCore(List<LocalRow> rows, QueueBatchViewModel resumeBatch, string expectedDevice)
        {
            var device = DeviceList.SelectedItem as DeviceRow;
            if (device == null || rows.Count == 0) throw new Exception("Выберите устройство и IPA для установки.");
            if (busy) return;
            if (expectedDevice != null && !String.Equals(ConnectionKey(device), expectedDevice, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выбран другой iPhone. Повторите подготовку набора.");
            if (resumeBatch != null && !CanResume(resumeBatch.TargetKey)) throw new Exception("Для продолжения восстановите связь с тем же iPhone.");
            var summary = String.Join(Environment.NewLine, rows.Take(8).Select(SelectionLabel));
            if (rows.Count > 8) summary += Environment.NewLine + "… и ещё " + (rows.Count - 8);
            if (!await desktop.Interaction.Confirm("Установка", "Установить IPA (" + rows.Count + ") на " + device.DisplayLabel + "?\n\n" + summary)) return;
            await desktop.RunWorkAsync("Установка IPA…", async delegate (CancellationToken ct)
            {
                desktop.StartReport("Установка", rows.Count, () => StartInstall(failedInstalls.Snapshot()));
                QueueBatchViewModel batch = null;
                batch = desktop.Queue.Start("Установка", rows.Select(row => Seed(row)), ConnectionKey(device), device.DisplayLabel,
                    keys => _ = StartInstall(rows.Where(row => keys.Contains(row.Path)).ToList(), batch), resumeBatch);
                await CheckSupport(ct);
                await EnsureConnection(device, ct);
                try
                {
                    var result = await BatchRunner.Run(rows,
                        (row, token) => deviceService.InstallAsync(device, row, ShowConnection, token),
                        (row, index) => { desktop.BeginQueue("Установка", index, rows.Count, row.DisplayName); Log("Установка " + row.DisplayName); },
                        row => { desktop.ReportItem(row.DisplayName, null); installChosen.Remove(row.Path); failedInstalls.Remove(row.Path); RefreshInstallChosen(); },
                        (row, error) => { desktop.ReportItem(row.DisplayName, error); failedInstalls.Add(row); Log("Ошибка установки " + row.DisplayName + ": " + error.Message); },
                        null, ct);
                    desktop.CompleteQueue(result.Succeeded, result.Failed.Count);
                    if (result.Failed.Count > 0) throw new Exception("Не удалось установить: " + String.Join(", ", result.Failed.Select(x => x.DisplayName)) + ". Кнопка «Повторить ошибки» повторит только эти IPA.");
                }
                catch (DeviceConnectionException error)
                {
                    blockedConnections.Add(ConnectionKey(device));
                    ShowConnection(new(error.Failure.Title, error.Failure.Remedy, false));
                    verifiedConnections.Remove(ConnectionKey(device));
                    desktop.Device.SetState(PhoneStatus.Lost, error.Failure.Remedy);
                    throw;
                }
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
            if (!RuntimePlatform.Windows) CheckAppleSupportLabel = "Проверить подключение";
            appleSupport = await support.CheckAsync(cancellation);
            desktop.Device.SetSupportReport(appleSupport.Message, appleSupport.Details);
            Log(appleSupport.Message + ". " + appleSupport.Details);
        }

        /// <summary>Пересчитывает доступность действий по состоянию выбора и выполняемой операции.</summary>
        internal static QueueSeed Seed(LocalRow row)
        {
            var file = new FileInfo(row.Path);
            return new QueueSeed(row.Path, row.DisplayName, row.Version) { BundleID = row.BundleID, Build = row.Build, FileSize = file.Length, ModifiedTicks = file.LastWriteTimeUtc.Ticks };
        }
        internal void ResolveVerified(string path) { installChosen.Remove(path); failedInstalls.Remove(path); RefreshInstallChosen(); }
        internal void MarkConnectionLost(DeviceRow phone, DeviceConnectionException error)
        {
            blockedConnections.Add(ConnectionKey(phone)); verifiedConnections.Remove(ConnectionKey(phone));
            ShowConnection(new(error.Failure.Title, error.Failure.Remedy, false)); desktop.Device.SetState(PhoneStatus.Lost, error.Failure.Remedy);
        }
        internal bool CanResume(string key)
        {
            var device = DeviceList.SelectedItem as DeviceRow;
            return !busy && device != null && String.Equals(ConnectionKey(device), key, StringComparison.OrdinalIgnoreCase) &&
                !blockedConnections.Contains(key) && verifiedConnections.Contains(key);
        }
        public override void RefreshActions()
        {
            var device = DeviceList.SelectedItem as DeviceRow;
            bool connectionAvailable = device != null && !blockedConnections.Contains(ConnectionKey(device));


            FileAccount.IsEnabled = !busy;
            var chosenPaths = new HashSet<string>(installChosen.Snapshot().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            SelectAllFilesEnabled = !busy && visibleFiles.Any(x => !chosenPaths.Contains(x.Path));
            SelectAllFilesLabel = "Выбрать все (" + visibleFiles.Count + ")";
            SearchWifiEnabled = !busy;
            WifiHelpEnabled = !busy;
            FileInfoEnabled = !busy && Files.SelectedItems.Count == 1;
            DeviceInfoEnabled = !busy && DeviceList.SelectedItem != null;
            RetryInstallsEnabled = !busy && failedInstalls.Count > 0 && connectionAvailable;
            RetryInstallsLabel = "Повторить ошибки (" + failedInstalls.Count + ")";
            RetryInstallsTip = String.Join(Environment.NewLine, failedInstalls.Snapshot().Select(x => x.DisplayName));
            ForgetInstallErrorsEnabled = !busy && failedInstalls.Count > 0;
            ChosenFilesEnabled = !busy;
            AddInstallEnabled = !busy && Files.SelectedItems.Count > 0;
            DeleteFilesEnabled = !busy && installChosen.Count > 0;
            DeleteFilesLabel = "В корзину (" + installChosen.Count + ")";
            ClearInstallEnabled = !busy && installChosen.Count > 0;
            InstallFilesEnabled = !busy && installChosen.Count > 0 && connectionAvailable;
            DeviceList.IsEnabled = !busy;
        }
        void RefreshInstallChosen()
        {
            var chosen = installChosen.Snapshot();
            ChosenFilesOptions = chosen;
            int hidden = chosen.Count(x => !LocalFileAccounts.Matches(x, AccountKey));
            InstallCount = installChosen.Count == 0 ? "Двойной клик — выбрать IPA" : "Выбрано IPA: " + installChosen.Count + (hidden == 0 ? "" : " · вне фильтра: " + hidden);
            InstallCountTip = "Установка и удаление используют все IPA в нижнем списке, включая выбранные у других аккаунтов.";
            InstallSelectionVisible = installChosen.Count > 0;
            InstallFilesLabel = "Установить (" + installChosen.Count + ")";
            ApplyActions();
        }
        /// <summary>Фильтрует готовый снимок в памяти, сохраняя видимое выделение по пути и всю очередь действий.</summary>
        void ApplyFileFilter()
        {
            var grid = Files;
            visibleFiles = allFiles.Where(x => LocalFileAccounts.Matches(x, AccountKey)).ToList();
            grid.ReplacePreservingSelection(visibleFiles, x => x.Path);
            FilesSummary = "Показано: " + visibleFiles.Count + " из " + allFiles.Count + " · " + StorageAnalysis.Bytes(visibleFiles.Sum(x => x.Bytes));
            FilesEmptyVisible = visibleFiles.Count == 0;
            RefreshInstallChosen();
        }
        int filesGeneration;
        /// <summary>Применяет только последний результат сканирования, сохраняя выделение по пути файла.</summary>
        public Task RefreshFilesAsync() => desktop.Tasks.Run(async cancellation =>
        {
            int generation = ++filesGeneration;
            try
            {
                var rows = await localApps.ListAsync(cancellation);
                if (cancellation.IsCancellationRequested || desktop.IsClosing || generation != filesGeneration) return;
                allFiles = rows;
                var key = AccountKey;
                var accounts = LocalFileAccounts.Choices(rows);
                // Подавляем промежуточный SelectionChanged при замене списка аккаунтов.
                updatingAccounts = true;
                try
                {
                    FileAccount.Items = accounts;
                    FileAccount.SelectedItem = accounts.FirstOrDefault(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)) ?? accounts[0];
                }
                finally { updatingAccounts = false; }
                desktop.ShowStorage(rows);
                installChosen.Reconcile(rows);
                ApplyFileFilter();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (!desktop.IsClosing && generation == filesGeneration) Log("Не удалось обновить список IPA: " + e.Message);
            }
        });
    }
}
