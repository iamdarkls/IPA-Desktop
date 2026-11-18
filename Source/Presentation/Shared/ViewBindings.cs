using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;

namespace IpaDesktop;

/// <summary>Builds binding paths with direct property accessors that survive trimming.</summary>
internal static class ViewBindings
{
    internal static ClrPropertyInfo Property<TModel, TValue>(string name, Func<TModel, TValue> read, Action<TModel, TValue> write = null) =>
        new(name, model => read((TModel)model),
            write == null ? null : (model, value) => write((TModel)model, (TValue)value),
            typeof(TValue));

    internal static CompiledBinding Create(params ClrPropertyInfo[] path)
    {
        var builder = new CompiledBindingPathBuilder();
        foreach (var property in path)
            builder.Property(property, PropertyInfoAccessorFactory.CreateInpcPropertyAccessor);
        return new CompiledBinding(builder.Build()) { Mode = path[^1].CanSet ? BindingMode.TwoWay : BindingMode.OneWay };
    }

    internal static void Bind<TModel, TValue>(AvaloniaObject target, AvaloniaProperty property,
        string name, Func<TModel, TValue> read, Action<TModel, TValue> write = null) =>
        target.Bind(property, Create(Property(name, read, write)));
}
