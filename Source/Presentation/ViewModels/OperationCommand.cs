using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IpaDesktop;

/// <summary>Awaitable UI command: guards reentry and reports failures through the shared error path.</summary>
public sealed class OperationCommand : ICommand
{
    readonly Func<object, Task> execute;
    readonly Action<Exception> error;
    Func<bool> allowed = () => true;
    bool running;
    bool? lastAvailability;
    public Task Execution { get; private set; } = Task.CompletedTask;
    public OperationCommand(Func<object, Task> execute, Action<Exception> error) { this.execute = execute; this.error = error; }
    public bool CanExecute(object parameter) => !running && allowed();
    public async void Execute(object parameter) => await ExecuteAsync(parameter);
    public Task ExecuteAsync(object parameter = null)
    {
        if (!CanExecute(parameter)) return running ? Execution : Task.CompletedTask;
        return Execution = Run(parameter);
    }
    async Task Run(object parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true; Refresh();
        try { await execute(parameter); }
        catch (OperationCanceledException) { }
        catch (Exception failure) { error(failure); }
        finally { running = false; Refresh(); }
    }
    public void SetAvailability(Func<bool> predicate) { allowed = predicate; Refresh(); }
    public event EventHandler CanExecuteChanged;
    public void Refresh()
    {
        var available = CanExecute(null);
        if (lastAvailability == available) return;
        lastAvailability = available; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
