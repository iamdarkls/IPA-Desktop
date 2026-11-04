using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace IpaDesktop;

/// <summary>Shared alignment for action icons, tab titles and optional live counters.</summary>
public sealed class IconLabel : TemplatedControl
{
    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<IconLabel, IconKind>(nameof(Kind));
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<IconLabel, string>(nameof(Text));
    public static readonly StyledProperty<string> CountProperty =
        AvaloniaProperty.Register<IconLabel, string>(nameof(Count));
    public static readonly DirectProperty<IconLabel, bool> HasCountProperty =
        AvaloniaProperty.RegisterDirect<IconLabel, bool>(nameof(HasCount), x => x.HasCount);

    public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Count { get => GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public bool HasCount => !String.IsNullOrEmpty(Count);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty)
            RaisePropertyChanged(HasCountProperty, !String.IsNullOrEmpty(change.GetOldValue<string>()), HasCount);
    }
}
