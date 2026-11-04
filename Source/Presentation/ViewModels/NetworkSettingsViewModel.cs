using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class NetworkSettingsViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    readonly DesktopViewModel desktop;
    readonly ISettingsStore settings;
    readonly INetworkDiagnostics network;
    readonly BackgroundTasks tasks;
    int mode;
    string address, status = "";
    bool testing;
    public int Mode { get => mode; set { if (Set(ref mode, value)) Changed(nameof(AddressEnabled)); } }
    public string Address { get => address; set => Set(ref address, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsEnabled => !testing && !desktop.IsBusy && !tasks.IsStopping;
    public bool AddressEnabled => IsEnabled && Mode == 2;
    public OperationCommand SaveCommand { get; }
    public OperationCommand TestCommand { get; }
    public NetworkSettingsViewModel(DesktopViewModel desktop, ISettingsStore settings, INetworkDiagnostics network)
    {
        this.desktop = desktop; this.settings = settings; this.network = network;
        tasks = desktop.Tasks.CreateChild(error => Status = error.Message);
        Mode = settings.Settings.ProxyMode == "Custom" ? 2 : settings.Settings.ProxyMode == "Direct" ? 1 : 0;
        Address = settings.Settings.ProxyUrl ?? "";
        SaveCommand = new(_ => { Save(); return Task.CompletedTask; }, failure => Status = failure.Message);
        TestCommand = new(_ => tasks.Run(async cancellation =>
        {
            Save(); testing = true; Refresh();
            try { Status = await network.DiagnoseAsync(cancellation); }
            catch (OperationCanceledException) { Status = "Проверка отменена."; }
            finally { testing = false; Refresh(); }
        }), failure => Status = failure.Message);
        SaveCommand.SetAvailability(() => IsEnabled); TestCommand.SetAvailability(() => IsEnabled);
        desktop.PropertyChanged += DesktopChanged;
    }
    void DesktopChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(DesktopViewModel.IsBusy)) Refresh(); }
    void Refresh() { Changed(nameof(IsEnabled)); Changed(nameof(AddressEnabled)); SaveCommand.Refresh(); TestCommand.Refresh(); }
    void Save()
    {
        if (desktop.IsBusy) throw new InvalidOperationException("Дождитесь окончания операции.");
        var selected = Mode == 2 ? "Custom" : Mode == 1 ? "Direct" : "System";
        if (selected == "Custom") ProxyConfiguration.Validate(Address);
        var previous = settings.Settings.Copy();
        try { settings.Settings.ProxyMode = selected; settings.Settings.ProxyUrl = selected == "Custom" ? Address.Trim() : null; settings.Save(); }
        catch { settings.Settings = previous; throw; }
        Status = "Настройки сохранены. Защитные ограничения аккаунта не сброшены.";
    }
    public void Dispose() { desktop.PropertyChanged -= DesktopChanged; tasks.Dispose(); }
    public async ValueTask DisposeAsync() { Dispose(); await tasks.StopAsync(); }
}
