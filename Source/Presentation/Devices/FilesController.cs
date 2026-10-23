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
    /// <summary>Управляет локальными IPA и установкой на iPhone; наборы и хранилище обслуживаются отдельно.</summary>
    public sealed class FilesController : WindowController
    {
        AppleSupportReport appleSupport;
        readonly InstallationSelection installChosen = new InstallationSelection();
        readonly InstallationSelection failedInstalls = new InstallationSelection();
        List<LocalRow> allFiles = new List<LocalRow>();
        List<LocalRow> visibleFiles = new List<LocalRow>();
        bool updatingAccounts;
        readonly System.Windows.Threading.DispatcherTimer deviceTimer = new System.Windows.Threading.DispatcherTimer();
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
                var account = C<ComboBox>("FileAccount").SelectedItem as LocalFileAccount;
                return account == null ? null : account.Key;
            }
        }
        public FilesController(Desktop view, Engine engine) : base(view, engine)
        {
            deviceTimer.Interval = TimeSpan.FromSeconds(3);
            deviceTimer.Tick += async delegate { await RefreshDevicesAsync(); };
            w.Closed += delegate { StopDeviceDetection(); };
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
            C<CheckBox>("SearchWifi").Click += async delegate
            {
                deviceGeneration++;
                C<ComboBox>("DeviceList").ItemsSource = null;
                deviceSignature = "";
                C<TextBlock>("DeviceDetectionStatus").Text = "Определение iPhone…";
                C<TextBlock>("WifiStatus").Text = "";
                ApplyActions();
                await PauseDeviceDetectionAsync();
                await RefreshDevicesAsync();
            };
            On("WifiHelp", delegate { AppDialogs.ShowText(w, "Подключение iPhone по Wi-Fi",
                "1. Подключите iPhone по USB и подтвердите доверие компьютеру.\n2. В Apple Devices включите «Показывать это устройство при подключении к Wi-Fi» и примените настройки. В iTunes — «Синхронизировать с этим iPhone по Wi-Fi».\n3. Подключите компьютер и iPhone к одной локальной сети, затем отключите USB.\n4. Включите «USB + Wi-Fi (экспериментально)»: список обновится автоматически. Выберите строку с отметкой Wi-Fi.\n\nЕсли телефон не найден: разблокируйте его, проверьте службу Apple Mobile Device, Bonjour, разрешения Apple в брандмауэре и отсутствие изоляции устройств в гостевой сети. VPN может мешать обнаружению.\n\nПри обрыве установка не переключается автоматически на USB. Проверьте результат на телефоне перед повтором."); });
            On("InstallFiles", delegate { StartInstall(installChosen.Snapshot()); });
            On("RetryInstalls", delegate { StartInstall(failedInstalls.Snapshot()); });
            On("ForgetInstallErrors", delegate { failedInstalls.Clear(); ApplyActions(); });
        }
        internal void StartDeviceDetection()
        {
            if (deviceClosed) return;
            deviceMonitoring = true;
            deviceTimer.Start();
        }
        internal void StopDeviceDetection()
        {
            deviceClosed = true;
            deviceMonitoring = false;
            deviceGeneration++;
            deviceTimer.Stop();
            if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
        }
        internal async Task PauseDeviceDetectionAsync()
        {
            if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
            await deviceScan;
        }
        internal async void ResumeDeviceDetection()
        {
            if (deviceMonitoring) await RefreshDevicesAsync();
        }
        /// <summary>Один фоновый поиск за раз; рабочая операция отменяет и дожидается его до запуска CLI.</summary>
        internal Task RefreshDevicesAsync()
        {
            if (deviceClosed || busy || IsDetecting) return deviceScan;
            if (String.IsNullOrEmpty(engine.Settings.DeviceCurrent ?? engine.Settings.Current))
            {
                ApplyDevices(new List<DeviceRow>());
                C<TextBlock>("DeviceDetectionStatus").Text = "Установите инструменты iPhone в настройках";
                return Task.FromResult(true);
            }
            var stop = new CancellationTokenSource();
            stop.CancelAfter(TimeSpan.FromSeconds(25));
            deviceScanCancellation = stop;
            deviceScan = DetectDevicesAsync(C<CheckBox>("SearchWifi").IsChecked == true, deviceGeneration, stop);
            return deviceScan;
        }
        async Task DetectDevicesAsync(bool wifi, int generation, CancellationTokenSource stop)
        {
            try
            {
                var warnings = new List<string>();
                var devices = await engine.Devices.FindAsync(wifi, stop.Token, warnings.Add);
                if (deviceClosed || busy || generation != deviceGeneration || stop.IsCancellationRequested) return;
                ApplyDevices(devices);
                ShowDeviceWarning(String.Join(Environment.NewLine, warnings));
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
            C<TextBlock>("DeviceDetectionStatus").ToolTip = warning.Length == 0 ? "Список проверяется автоматически каждые 3 секунды в отсутствие других операций." : warning;
            if (warning.Length > 0) C<TextBlock>("DeviceDetectionStatus").Text = "Проверьте подключение iPhone — подробности в подсказке";
            if (warning != deviceWarning && warning.Length > 0) Log("Автоопределение iPhone: " + warning);
            deviceWarning = warning;
        }
        void ApplyDevices(List<DeviceRow> devices)
        {
            var combo = C<ComboBox>("DeviceList");
            var previous = combo.SelectedItem as DeviceRow;
            var signature = String.Join("\n", devices.Select(x => x.ID.ToUpperInvariant() + "|" + x.Network + "|" + x.Name).OrderBy(x => x, StringComparer.Ordinal));
            if (signature != deviceSignature)
            {
                combo.ItemsSource = devices;
                combo.SelectedItem = previous == null ? (devices.Count == 1 ? devices[0] : null) :
                    devices.FirstOrDefault(x => x.Network == previous.Network && String.Equals(x.ID, previous.ID, StringComparison.OrdinalIgnoreCase));
                foreach (var device in devices) { view.RegisterPrivate(device.ID); view.RegisterPrivate(device.Name); }
                Log(devices.Count == 0 ? "iPhone отключён." : "Обнаружены iPhone: " + String.Join(", ", devices.Select(x => x.DisplayLabel)));
                deviceSignature = signature;
            }
            C<TextBlock>("WifiStatus").Text = "USB: " + devices.Count(x => !x.Network) + " · Wi-Fi: " + devices.Count(x => x.Network);
            C<TextBlock>("DeviceDetectionStatus").Text = devices.Count == 0 ? "Подключите и разблокируйте iPhone — он появится автоматически" :
                combo.SelectedItem == null ? "Выберите iPhone для установки" : "iPhone подключён · автоопределение включено";
            ApplyActions();
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
            C<ComboBox>("DeviceList").IsEnabled = !busy;
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
