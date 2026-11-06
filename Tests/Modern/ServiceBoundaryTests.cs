using IpaDesktop;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class ServiceBoundaryTests
{
    sealed class EventDevices : IDeviceService, IDeviceEvents
    {
        public Action Changed;
        public bool Stopped;
        public int Reads;
        public List<DeviceRow> Rows = new() { new() { ID = "00008030-0000111ABC000DEF", Name = "Fixture" } };
        public Task<List<DeviceRow>> FindAsync(bool wifi, CancellationToken cancellation, Action<string> report) { Reads++; return Task.FromResult(Rows.ToList()); }
        public Task<Dictionary<string, string>> ValidateReadyAsync(DeviceRow device, CancellationToken cancellation) => Task.FromResult(new Dictionary<string, string> { ["ProductType"] = "iPhone16,2", ["ProductVersion"] = "18.0" });
        public Task<Dictionary<string, string>> EnsureReadyAsync(DeviceRow device, Action<DeviceConnectionProgress> progress, CancellationToken cancellation) => ValidateReadyAsync(device, cancellation);
        public Task<List<LibraryApp>> ApplicationsAsync(DeviceRow device, CancellationToken cancellation) => Task.FromResult(new List<LibraryApp>());
        public Task InstallAsync(DeviceRow device, LocalRow row, Action<DeviceConnectionProgress> progress, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<string> DetailsAsync(DeviceRow device, CancellationToken cancellation) => Task.FromResult("");
        public async Task WatchAsync(Action changed, Action<bool> status, CancellationToken cancellation)
        {
            Changed = changed; status(true); changed();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Stopped = true; status(false); }
        }
    }
    [Fact]
    public async Task DeviceEventsRefreshSelectionClearCardOnDisconnectAndStopWithDesktop()
    {
        using var folder = new TempFolder(); var engine = new Engine(folder.Path, new RecordingProcessRunner(), new InstalledAppleSupportProbe());
        engine.Settings.Current = "fixture";
        var devices = new EventDevices();
        await using var desktop = new DesktopViewModel(engine.CreateDesktopServices() with { Devices = devices }, new NoWindowInteraction());
        desktop.Files.StartDeviceDetection();
        Assert.Single(desktop.Files.DeviceList.Items); Assert.True(desktop.Device.HasSummary); Assert.Contains("iPhone16,2", desktop.Device.Summary);
        devices.Rows.Clear(); devices.Changed();
        Assert.Empty(desktop.Files.DeviceList.Items); Assert.False(desktop.Device.HasSummary);
        await desktop.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(devices.Stopped); Assert.Equal(0, desktop.Tasks.PendingCount);
        var reads = devices.Reads; devices.Changed(); Assert.Equal(reads, devices.Reads);
    }
    sealed class Session : IAccountSession
    {
        public int Reads;
        public Task<string> GetEmailAsync(CancellationToken cancellation) { Reads++; return Task.FromResult("fixture@example.com"); }
        public Task RevokeAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
    sealed class Protection : IAccountProtection
    {
        public bool RequiresLogin => false; public bool SafetyStopped => false; public string SafetyStatus => "";
        public void AcknowledgeRecovery(CancellationToken cancellation) { }
    }
    [Fact]
    public async Task AccountModelUsesOnlyItsAccountContracts()
    {
        await using var desktop = new DesktopViewModel(); var session = new Session();
        var account = new AccountViewModel(desktop, session, new Protection(), new WindowLoginService());
        await account.Account(TestContext.Current.CancellationToken);
        Assert.Equal(1, session.Reads); Assert.Equal("fixture@example.com", account.AccountLabel); Assert.NotNull(desktop.AccountKey);
    }
    sealed class DelayedApps : ILocalApps
    {
        public TaskCompletionSource<List<LocalRow>> Rows = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public Task<List<LocalRow>> ListAsync(CancellationToken cancellation = default) { Token = cancellation; return Rows.Task; }
        public Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellation) => Task.CompletedTask;
        public void Delete(string path) { }
    }
    [Fact]
    public async Task ShutdownWaitsForMetadataReadAndDiscardsItsStaleResult()
    {
        using var folder = new TempFolder(); var engine = new Engine(folder.Path, new RecordingProcessRunner(), new InstalledAppleSupportProbe());
        var previous = new Action<string>(_ => { }); engine.Log = previous;
        var local = new DelayedApps(); var services = engine.CreateDesktopServices() with { LocalApps = local };
        await using var desktop = new DesktopViewModel(services, new NoWindowInteraction());
        var stop = desktop.ShutdownAsync();
        Assert.True(local.Token.IsCancellationRequested); Assert.False(stop.IsCompleted);
        Assert.False(desktop.Files.ImportFileCommand.CanExecute(null));
        local.Rows.SetResult(new() { new LocalRow { Path = "/fixture.ipa", Name = "stale" } });
        await stop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(desktop.Files.Files.Items); Assert.Equal(0, desktop.Tasks.PendingCount); Assert.Same(previous, engine.Log);
    }
}
