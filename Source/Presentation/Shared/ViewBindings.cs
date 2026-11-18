using System;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Data;

namespace IpaDesktop;

/// <summary>Binding plumbing for code-created views; application decisions stay in ViewModels.</summary>
internal static class ViewBindings
{
    internal static void Bind<TModel, TValue>(AvaloniaObject target, AvaloniaProperty property,
        Expression<Func<TModel, TValue>> path, bool twoWay = false) =>
        target.Bind(property, CompiledBinding.Create(path, mode: twoWay ? BindingMode.TwoWay : BindingMode.OneWay));
}
