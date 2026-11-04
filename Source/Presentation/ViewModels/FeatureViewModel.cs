using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public abstract class FeatureViewModel : ObservableObject
{
    protected readonly DesktopViewModel desktop;
    protected readonly Engine engine;
    protected bool busy => desktop.IsBusy;
    protected bool setting => desktop.IsUpdatingSettings;
    protected FeatureViewModel(DesktopViewModel desktop, Engine engine) { this.desktop = desktop; this.engine = engine; }
    protected OperationCommand Command(Action action) => new(async _ =>
    {
        desktop.Interaction.CloseMenus();
        var previous = desktop.ActiveOperation;
        action();
        if (!ReferenceEquals(previous, desktop.ActiveOperation)) await desktop.ActiveOperation;
    }, desktop.Error);
    protected OperationCommand AsyncCommand(Func<Task> action) => new(async _ =>
    {
        desktop.Interaction.CloseMenus(); var previous = desktop.ActiveOperation;
        await action();
        if (!ReferenceEquals(previous, desktop.ActiveOperation)) await desktop.ActiveOperation;
    }, desktop.Error);
    protected void Work(string title, Func<CancellationToken, Task> action) => desktop.Work(title, action);
    protected void Log(string text) => desktop.Log(text);
    protected void Error(Exception error) => desktop.Error(error);
    protected void ApplyActions() => desktop.ApplyActions();
    protected void StateView() => desktop.StateView();
    protected void RefreshLocalFiles() => desktop.RefreshFiles();
    public abstract void RefreshActions();
    public abstract void ConfigureCommands();
    public abstract void RefreshCommands();
}
