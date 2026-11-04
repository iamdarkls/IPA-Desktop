using Avalonia;
using Avalonia.Data;

namespace IpaDesktop;

/// <summary>Binding plumbing for code-created views; application decisions stay in ViewModels.</summary>
internal static class ViewBindings
{
    internal static void Bind(AvaloniaObject target, AvaloniaProperty property, string path, bool twoWay = false) =>
        target.Bind(property, new Binding(path) { Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay });
}
