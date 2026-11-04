using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IpaDesktop;

/// <summary>Selection belongs to the model, independently of a list or table widget.</summary>
public sealed class SelectionViewModel<T> : ObservableObject
{
    IList<T> items = new ObservableCollection<T>();
    bool enabled = true;
    public IList<T> Items
    {
        get => items;
        set
        {
            if (ReferenceEquals(items, value)) return;
            SelectedItems.Clear();
            items = value ?? new ObservableCollection<T>();
            Changed(); Changed(nameof(SelectedItem)); Changed(nameof(SelectedIndex));
        }
    }
    public ObservableCollection<T> SelectedItems { get; } = new();
    public T SelectedItem
    {
        get => SelectedItems.FirstOrDefault();
        set { if (SelectedItems.Count == 1 && EqualityComparer<T>.Default.Equals(SelectedItem, value)) return; SelectedItems.Clear(); if (value is not null) SelectedItems.Add(value); }
    }
    public int SelectedIndex { get => Items.IndexOf(SelectedItem); set => SelectedItem = value >= 0 && value < Items.Count ? Items[value] : default; }
    public bool IsEnabled { get => enabled; set => Set(ref enabled, value); }
    public event Action SelectionChanged;
    public SelectionViewModel() => SelectedItems.CollectionChanged += (_, _) => { Changed(nameof(SelectedItem)); Changed(nameof(SelectedIndex)); SelectionChanged?.Invoke(); };
    public void ReplacePreservingSelection(IList<T> rows, Func<T, string> key)
    {
        var selected = new HashSet<string>(SelectedItems.Select(key), StringComparer.OrdinalIgnoreCase);
        Items = rows;
        foreach (var row in rows) if (selected.Contains(key(row)) && !SelectedItems.Contains(row)) SelectedItems.Add(row);
    }
}
