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
    sealed class PreparedDownload : IApplicationDownloads
    {
        readonly string folder;
        public string Version = "456";
        public PreparedDownload(string folder) => this.folder = folder;
        public Task DownloadAsync(DownloadItem item, CancellationToken cancellation) => DownloadFileAsync(item, cancellation);
        public Task<string> DownloadFileAsync(DownloadItem item, CancellationToken cancellation)
        {
            Assert.Equal("123", item.ID); Assert.Equal("456", item.VersionID);
            var path = Path.Combine(folder, Guid.NewGuid() + ".ipa");
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("iTunesMetadata.plist").Open());
            writer.Write($"<plist><dict><key>itemId</key><integer>123</integer><key>softwareVersionExternalIdentifier</key><integer>{Version}</integer><key>appleId</key><string>fixture@example.com</string></dict></plist>");
            return Task.FromResult(path);
        }
    }
    [Fact]
    public async Task AccountPreparationRequiresMatchingStoreBuildAndKeepsOriginal()
    {
        using var folder = new TempFolder();
        var engine = new Engine(folder.Path, new RecordingProcessRunner(), new InstalledAppleSupportProbe());
        var download = new PreparedDownload(engine.Downloads);
        var services = engine.CreateDesktopServices() with { Accounts = new Session(), Downloads = download };
        await using var desktop = new DesktopViewModel(services, new NoWindowInteraction());
        desktop.SetAccountIdentity("fixture@example.com");
        var original = new LocalRow { Path = Path.Combine(engine.Downloads, "original.ipa"), Name = "Original.ipa", AppStoreID = "123", ExternalVersionID = "456", AppleAccount = "other@example.com" };
        File.WriteAllText(original.Path, "untouched");
        var rows = new List<LocalRow> { original };
        var prepared = await desktop.PrepareInstallCopies(rows);
        Assert.Equal("fixture@example.com", prepared[original.Path].AppleAccount);
        Assert.Equal("456", prepared[original.Path].ExternalVersionID);
        Assert.Equal("untouched", File.ReadAllText(original.Path));
        download.Version = "789";
        Assert.Null(await desktop.PrepareInstallCopies(rows));
    }
    [Fact]
    public async Task DisablingCachePreservesArchivesAndClearingLeavesFinishedIpas()
    {
        using var folder = new TempFolder();
        var engine = new Engine(folder.Path, new RecordingProcessRunner(), new InstalledAppleSupportProbe());
        var services = engine.CreateDesktopServices();
        var cache = services.ArchiveCache;
        await using var desktop = new DesktopViewModel(services, new NoWindowInteraction());
        Directory.CreateDirectory(cache.Folder);
        var original = Path.Combine(cache.Folder, "123-" + new string('a', 32) + ".raw");
        var unrelated = Path.Combine(cache.Folder, "unrelated.raw");
        var finished = Path.Combine(folder.Path, "finished.ipa");
        File.WriteAllText(original, "original"); File.WriteAllText(unrelated, "unrelated"); File.WriteAllText(finished, "finished");
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(new FileInfo(original).Length, await cache.SizeAsync(ct));
        desktop.ArchiveCache.Limit = 0;
        await desktop.ArchiveCache.SaveCommand.ExecuteAsync();
        Assert.Equal(0, new SettingsStore(folder.Path).Settings.IpaCacheGiB);
        Assert.True(File.Exists(original));
        await desktop.ArchiveCache.RefreshUsage(ct);
        Assert.Contains("Кэш отключён", desktop.ArchiveCache.Usage);
        await desktop.ArchiveCache.ClearCommand.ExecuteAsync(); // The interaction declines confirmation.
        Assert.True(File.Exists(original));
        await cache.ClearAsync(ct);
        Assert.Equal(0, await cache.SizeAsync(ct));
        Assert.True(File.Exists(unrelated)); Assert.True(File.Exists(finished));
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
