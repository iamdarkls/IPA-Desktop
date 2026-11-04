using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace IpaDesktop;

/// <summary>View-only bridge for controls whose multi-selection cannot be bound directly.</summary>
public sealed class SelectionBinding : AvaloniaObject
{
    public static readonly AttachedProperty<IList> ItemsProperty = AvaloniaProperty.RegisterAttached<SelectionBinding, Control, IList>("Items");
    public static IList GetItems(Control control) => control.GetValue(ItemsProperty);
    public static void SetItems(Control control, IList value) => control.SetValue(ItemsProperty, value);
    static readonly AttachedProperty<Link> LinkProperty = AvaloniaProperty.RegisterAttached<SelectionBinding, Control, Link>("Link");
    static SelectionBinding() => ItemsProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        control.GetValue(LinkProperty)?.Dispose();
        control.SetValue(LinkProperty, change.GetNewValue<IList>() is { } model && control is DataGrid or ListBox ? new Link(control, model) : null);
    });
    sealed class Link : IDisposable
    {
        readonly Control control;
        readonly IList model;
        IList selected => control is DataGrid grid ? grid.SelectedItems : ((ListBox)control).SelectedItems;
        bool updating;
        public Link(Control control, IList model)
        {
            this.control = control; this.model = model;
            if (model is INotifyCollectionChanged source) source.CollectionChanged += ModelChanged;
            if (control is DataGrid table) table.AddHandler(DataGrid.SelectionChangedEvent, ViewChanged, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            else ((ListBox)control).SelectionChanged += ViewChanged;
            Sync(model, selected);
        }
        void ModelChanged(object sender, NotifyCollectionChangedEventArgs args) => Sync(model, selected);
        void ViewChanged(object sender, SelectionChangedEventArgs args) => Sync(selected, model);
        void Sync(IList source, IList target)
        {
            if (updating || source == null || target == null) return;
            updating = true;
            try
            {
                var values = source.Cast<object>().ToArray();
                for (int i = target.Count - 1; i >= 0; i--) if (Array.IndexOf(values, target[i]) < 0) target.RemoveAt(i);
                foreach (var value in values) if (!target.Contains(value)) target.Add(value);
            }
            finally { updating = false; }
        }
        public void Dispose()
        {
            if (model is INotifyCollectionChanged source) source.CollectionChanged -= ModelChanged;
            if (control is DataGrid table) table.RemoveHandler(DataGrid.SelectionChangedEvent, ViewChanged);
            else ((ListBox)control).SelectionChanged -= ViewChanged;
        }
    }
}
