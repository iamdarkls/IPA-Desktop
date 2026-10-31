using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IpaDesktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}

public sealed class DelegateCommand : ICommand
{
    readonly Action execute;
    readonly Func<bool> allowed;
    public DelegateCommand(Action execute, Func<bool> allowed = null)
    { this.execute = execute; this.allowed = allowed ?? (() => true); }
    public bool CanExecute(object parameter) => allowed();
    public void Execute(object parameter) { if (CanExecute(parameter)) execute(); }
    public event EventHandler CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
