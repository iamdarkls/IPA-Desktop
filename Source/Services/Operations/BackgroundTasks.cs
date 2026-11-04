using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

/// <summary>Owns asynchronous work and resources for one application/window lifetime.
/// Stop rejects new work, cancels existing work, stops timers and awaits all admitted tasks.</summary>
public sealed class BackgroundTasks : IDisposable, IAsyncDisposable
{
    readonly object gate = new();
    readonly CancellationTokenSource cancellation;
    readonly CancellationToken token;
    readonly CancellationTokenRegistration parentRegistration;
    readonly HashSet<Task> pending = new();
    readonly List<IDisposable> resources = new();
    readonly Action<Exception> report;
    readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => stopped.Task;
    Task shutdown;
    bool stopping;
    public CancellationToken Token => token;
    public bool IsStopping { get { lock (gate) return stopping || token.IsCancellationRequested; } }
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public BackgroundTasks(Action<Exception> report = null, CancellationToken parent = default, CancellationToken additional = default)
    {
        this.report = report;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent, additional);
        token = cancellation.Token;
        if (parent.CanBeCanceled) parentRegistration = parent.Register(() => _ = StopAsync());
    }
    public Task Run(Func<CancellationToken, Task> action) => Run(async ct => { await action(ct); return true; });
    public Task<T> Run<T>(Func<CancellationToken, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (stopping || token.IsCancellationRequested) return Task.FromResult(default(T));
            pending.Add(completion.Task);
        }
        _ = Execute(action, completion);
        return completion.Task;
    }
    async Task Execute<T>(Func<CancellationToken, Task<T>> action, TaskCompletionSource<T> completion)
    {
        T value = default;
        try { token.ThrowIfCancellationRequested(); value = await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Report(error); }
        finally { lock (gate) pending.Remove(completion.Task); completion.TrySetResult(value); }
    }
    void Report(Exception error)
    {
        // Error reporting must never strand task completion or prevent other resources from stopping.
        try { report?.Invoke(error); } catch { }
    }
    sealed class Owned : IDisposable
    {
        IDisposable resource;
        public Owned(IDisposable resource) => this.resource = resource;
        public void Dispose() => Interlocked.Exchange(ref resource, null)?.Dispose();
    }
    public IDisposable Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var owned = new Owned(resource);
        lock (gate)
        {
            if (!stopping && !token.IsCancellationRequested) { resources.Add(owned); return owned; }
        }
        owned.Dispose(); return owned;
    }
    public BackgroundTasks CreateChild(Action<Exception> report = null, CancellationToken cancellation = default)
    {
        var child = new BackgroundTasks(report, token, cancellation);
        var owned = new Owned(child);
        lock (gate)
        {
            if (stopping || token.IsCancellationRequested) { child.Dispose(); return child; }
            resources.Add(owned); pending.Add(child.Completion);
        }
        _ = child.Completion.ContinueWith(_ =>
        {
            lock (gate) { pending.Remove(child.Completion); resources.Remove(owned); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return child;
    }
    public Task StopAsync()
    {
        IDisposable[] owned;
        TaskCompletionSource done;
        lock (gate)
        {
            if (shutdown != null) return shutdown;
            stopping = true; done = stopped; shutdown = done.Task;
            owned = resources.ToArray(); resources.Clear();
        }
        try { cancellation.Cancel(); } catch (Exception error) { Report(error); }
        foreach (var resource in owned.Reverse()) { try { resource.Dispose(); } catch (Exception error) { Report(error); } }
        _ = Drain(done);
        return done.Task;
    }
    async Task Drain(TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                Task[] tasks; lock (gate) tasks = pending.ToArray();
                if (tasks.Length == 0) break;
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            parentRegistration.Dispose(); cancellation.Dispose();
        }
        finally { completion.TrySetResult(); }
    }
    public void Dispose() => _ = StopAsync();
    public async ValueTask DisposeAsync() => await StopAsync();
}
