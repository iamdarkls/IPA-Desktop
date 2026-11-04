using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class AppSetEditorViewModel : ObservableObject
{
    string name, status = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public SelectionViewModel<AppRow> Apps { get; } = new();
    public OperationCommand SaveCommand { get; }
    public OperationCommand RemoveCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public event Action<bool> CloseRequested;
    public AppSetEditorViewModel(string name, IEnumerable<AppRow> apps, Action<string, IEnumerable<AppRow>> save)
    {
        Name = name;
        Apps.Items = new System.Collections.ObjectModel.ObservableCollection<AppRow>(apps.Select(x => new AppRow { ID = x.ID, Name = x.Name }));
        RemoveCommand = new(_ => { foreach (var item in Apps.SelectedItems.ToArray()) Apps.Items.Remove(item); Apps.SelectedItems.Clear(); Changed(nameof(Apps)); return Task.CompletedTask; }, error => Status = error.Message);
        SaveCommand = new(_ => { save(Name?.Trim() ?? "", Apps.Items.ToArray()); CloseRequested?.Invoke(true); return Task.CompletedTask; }, error => Status = error.Message);
        CancelCommand = new(() => CloseRequested?.Invoke(false));
    }
}
