using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class FilesViewModel
{
    IDisposable deviceTimer;
    readonly IDeviceEvents deviceEvents;
    readonly IDeviceDiagnostics diagnostics;
    CancellationTokenSource eventCancellation;
    Task eventTask = Task.CompletedTask;
    bool eventsReady, refreshPending = true;
    long lastDiscovery, lastSnapshot;
    CancellationTokenSource deviceScanCancellation;
    Task deviceScan = Task.FromResult(true);
    bool deviceMonitoring, deviceClosed;
    int deviceGeneration;
    string deviceSignature = "", deviceWarning = "";
    internal bool IsDetecting { get { return !deviceScan.IsCompleted; } }

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
    internal void StartDeviceDetection()
    {
        if (deviceClosed) return;
        deviceMonitoring = true;
        StartEventMonitor();
        deviceTimer ??= desktop.Tasks.Own(desktop.Interaction.Repeat(TimeSpan.FromSeconds(1), () =>
        {
            if (refreshPending || Stopwatch.GetElapsedTime(lastDiscovery) >= TimeSpan.FromSeconds(eventsReady ? 30 : 3))
                _ = desktop.RunAsyncCommand(RefreshDevicesAsync);
        }));
    }
    void StartEventMonitor()
    {
        if (!deviceMonitoring || deviceClosed || deviceEvents == null || !eventTask.IsCompleted || desktop.IsClosing) return;
        eventTask = desktop.Tasks.Run(async cancellation =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); eventCancellation = stop;
            try
            {
                await deviceEvents.WatchAsync(() => desktop.Interaction.Post(() =>
                {
                    if (deviceClosed || stop.IsCancellationRequested) return;
                    refreshPending = true;
                    _ = desktop.RunAsyncCommand(RefreshDevicesAsync);
                }), ready => desktop.Interaction.Post(() =>
                {
                    if (!deviceClosed && !stop.IsCancellationRequested) eventsReady = ready;
                }), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { if (eventCancellation == stop) eventCancellation = null; eventsReady = false; }
        });
    }
    internal void StopDeviceDetection()
    {
        deviceClosed = true;
        deviceMonitoring = false;
        deviceGeneration++;
        deviceTimer?.Dispose(); deviceTimer = null;
        if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
        eventCancellation?.Cancel();
    }
    internal async Task PauseDeviceDetectionAsync()
    {
        if (deviceScanCancellation != null) deviceScanCancellation.Cancel();
        eventCancellation?.Cancel();
        await eventTask;
        await deviceScan;
    }
    internal void ResumeDeviceDetection()
    {
        if (deviceMonitoring) { StartEventMonitor(); refreshPending = true; _ = desktop.RunAsyncCommand(RefreshDevicesAsync); }
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
        refreshPending = false; lastDiscovery = Stopwatch.GetTimestamp();
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
                    var values = await deviceService.ValidateReadyAsync(selected, stop.Token);
                    if (deviceClosed || busy || generation != deviceGeneration || stop.IsCancellationRequested) return;
                    verifiedConnections.Add(ConnectionKey(selected));
                    desktop.Device.ShowSnapshot(ConnectionKey(selected), DeviceSnapshot.From(values));
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
            if (selected != null && diagnostics != null && desktop.Device.Status == PhoneStatus.Ready &&
                (lastSnapshot == 0 || Stopwatch.GetElapsedTime(lastSnapshot) >= TimeSpan.FromSeconds(30)))
            {
                var snapshot = await diagnostics.InspectAsync(selected, false, stop.Token);
                if (!deviceClosed && !busy && generation == deviceGeneration && !stop.IsCancellationRequested)
                { desktop.Device.ShowSnapshot(ConnectionKey(selected), snapshot); lastSnapshot = Stopwatch.GetTimestamp(); }
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
        DeviceDetectionStatusTip = warning.Length == 0 ? (eventsReady ? "События подключения включены. Контрольная проверка — каждые 30 секунд." : "События недоступны: резервная проверка каждые 3 секунды в отсутствие операций.") : warning;
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
}
