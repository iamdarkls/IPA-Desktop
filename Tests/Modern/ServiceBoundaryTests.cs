using IpaDesktop;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class ServiceBoundaryTests
{
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
