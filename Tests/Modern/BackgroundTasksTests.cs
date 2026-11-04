using IpaDesktop;
using Xunit;
namespace IpaDesktop.Tests;

public sealed class BackgroundTasksTests
{
    static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static Task Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    sealed class Resource : IDisposable { public int Calls; public void Dispose() => Calls++; }
    [Fact]
    public async Task StopCancelsRejectsNewWorkAndWaitsForCleanup()
    {
        var cleanup = Gate(); var release = Gate(); var started = Gate(); int replay = 0;
        await using var tasks = new BackgroundTasks(); var resource = new Resource(); var owned = tasks.Own(resource);
        var job = tasks.Run(async ct =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cleanup.SetResult(); await release.Task; }
        });
        await Await(started.Task); var stop = tasks.StopAsync(); await Await(cleanup.Task);
        Assert.False(stop.IsCompleted); Assert.True(tasks.Token.IsCancellationRequested);
        await tasks.Run(_ => { replay++; return Task.CompletedTask; }); Assert.Equal(0, replay);
        owned.Dispose(); Assert.Equal(1, resource.Calls); Assert.Same(stop, tasks.StopAsync());
        release.SetResult(); await Await(stop); await job;
        Assert.Equal(0, tasks.PendingCount); Assert.Equal(1, resource.Calls);
    }
    [Fact]
    public async Task ParentStopIncludesChildWindowCleanup()
    {
        await using var parent = new BackgroundTasks(); var child = parent.CreateChild(cancellation: TestContext.Current.CancellationToken);
        var cleanup = Gate(); var release = Gate();
        var childJob = child.Run(async ct =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cleanup.SetResult(); await release.Task; }
        });
        var stop = parent.StopAsync(); await Await(cleanup.Task);
        Assert.True(child.IsStopping); Assert.False(stop.IsCompleted); Assert.False(child.Completion.IsCompleted);
        release.SetResult(); await Await(stop); await childJob;
        Assert.True(child.Completion.IsCompleted); Assert.Equal(0, parent.PendingCount);
    }
    [Fact]
    public async Task FaultsAreObservedOnceAndDoNotStrandShutdown()
    {
        int faults = 0; await using var tasks = new BackgroundTasks(_ => { faults++; throw new Exception("report failure"); });
        await tasks.Run(_ => throw new IOException("work failure"));
        Assert.Equal(1, faults); await Await(tasks.StopAsync()); Assert.Equal(0, tasks.PendingCount);
        var late = new Resource(); tasks.Own(late).Dispose(); Assert.Equal(1, late.Calls);
    }
    [Fact]
    public async Task ConcurrentAdmissionAndStopLeaveNoRunningWork()
    {
        await using var tasks = new BackgroundTasks(); int started = 0, finished = 0;
        var producers = Enumerable.Range(0, 128).Select(_ => Task.Run(() => tasks.Run(async ct =>
        {
            Interlocked.Increment(ref started);
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { Interlocked.Increment(ref finished); }
        }), TestContext.Current.CancellationToken)).ToArray();
        await Await(tasks.StopAsync()); await Await(Task.WhenAll(producers));
        Assert.Equal(started, finished); Assert.Equal(0, tasks.PendingCount);
    }
}
