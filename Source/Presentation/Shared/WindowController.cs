using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace IpaDesktop;

/// <summary>Shared access to the window, operation lifecycle, journal and action state.</summary>
public abstract class WindowController
{
    protected readonly Desktop view;
    protected readonly Engine engine;
    protected Window w => view.Window;
    protected bool busy => view.IsBusy;
    protected bool setting => view.IsUpdatingSettings;

    protected WindowController(Desktop view, Engine engine)
    {
        this.view = view;
        this.engine = engine;
    }

    protected T C<T>(string name) where T : Control => view.C<T>(name);
    protected void On(string name, Action action) => view.On(name, action);
    protected void OnAsync(string name, Func<Task> action) => view.OnAsync(name, action);
    protected void Work(string title, Func<CancellationToken, Task> action) => view.Work(title, action);
    protected void Log(string text) => view.Log(text);
    protected void Error(Exception error) => view.Error(error);
    protected void ApplyActions() => view.ApplyActions();
    protected void StateView() => view.StateView();
    protected void RefreshLocalFiles() => view.RefreshFiles();

    public abstract void RefreshActions();
}
