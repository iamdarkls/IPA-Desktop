using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class NetworkSettingsViewModel : ObservableObject, IDisposable
{
    readonly DesktopViewModel desktop;
    readonly Engine engine;
    readonly CancellationTokenSource lifetime = new();
    int mode;
    string address, status = "";
    bool testing;
    public int Mode { get => mode; set { if (Set(ref mode, value)) Changed(nameof(AddressEnabled)); } }
    public string Address { get => address; set => Set(ref address, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsEnabled => !testing && !desktop.IsBusy;
    public bool AddressEnabled => IsEnabled && Mode == 2;
    public OperationCommand SaveCommand { get; }
    public OperationCommand TestCommand { get; }
    public NetworkSettingsViewModel(DesktopViewModel desktop, Engine engine)
    {
        this.desktop = desktop; this.engine = engine;
        Mode = engine.Settings.ProxyMode == "Custom" ? 2 : engine.Settings.ProxyMode == "Direct" ? 1 : 0;
        Address = engine.Settings.ProxyUrl ?? "";
        SaveCommand = new(_ => { Save(); return Task.CompletedTask; }, failure => Status = failure.Message);
        TestCommand = new(async _ =>
        {
            Save(); testing = true; Refresh();
            try { Status = await engine.Network.DiagnoseAsync(lifetime.Token); }
            catch (OperationCanceledException) { Status = "Проверка отменена."; }
            finally { testing = false; Refresh(); }
        }, failure => Status = failure.Message);
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
        var previous = engine.Settings.Copy();
        try { engine.Settings.ProxyMode = selected; engine.Settings.ProxyUrl = selected == "Custom" ? Address.Trim() : null; engine.Save(); }
        catch { engine.Settings = previous; throw; }
        Status = "Настройки сохранены. Защитные ограничения аккаунта не сброшены.";
    }
    public void Dispose() { desktop.PropertyChanged -= DesktopChanged; lifetime.Cancel(); lifetime.Dispose(); }
}
