using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpaDesktop;

public enum IconKind
{
    None, Search, Phone, Settings, Journal, Layers, User, Shield, Logout,
    Folder, Report, History, List, Info, Plus, Download, Refresh, Close,
    Clear, Link, Wifi, Help, Import, Check, Trash, External, Export, Edit, Save
}

/// <summary>Small vector symbols drawn on a shared 24-unit grid, independent of fonts and DPI.</summary>
public sealed class AppIcon : Control
{
    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<AppIcon, IconKind>(nameof(Kind));
    public static readonly StyledProperty<IBrush> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<AppIcon>();

    static readonly IReadOnlyDictionary<IconKind, Geometry> Shapes = CreateShapes();

    static AppIcon() => AffectsRender<AppIcon>(KindProperty, ForegroundProperty);

    public AppIcon()
    {
        Width = Height = 16;
        IsHitTestVisible = false;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!Shapes.TryGetValue(Kind, out var shape)) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var transform = Matrix.CreateScale(size / 24, size / 24) *
            Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(transform))
            context.DrawGeometry(null, new Pen(Foreground, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shape);
    }

    static IReadOnlyDictionary<IconKind, Geometry> CreateShapes()
    {
        // Original simple line drawings; no external icon fonts, assets or package dependencies.
        var paths = new Dictionary<IconKind, string>
        {
            [IconKind.Search] = "M 10,3 A 7,7 0 1 0 10,17 A 7,7 0 1 0 10,3 M 15,15 L 21,21",
            [IconKind.Phone] = "M 8,2 L 16,2 Q 18,2 18,4 L 18,20 Q 18,22 16,22 L 8,22 Q 6,22 6,20 L 6,4 Q 6,2 8,2 M 10,5 L 14,5 M 11,19 L 13,19",
            [IconKind.Settings] = "M 9,3 L 15,3 L 16,6 L 20,7 L 22,12 L 20,17 L 16,18 L 15,21 L 9,21 L 8,18 L 4,17 L 2,12 L 4,7 L 8,6 Z M 12,8 A 4,4 0 1 0 12,16 A 4,4 0 1 0 12,8",
            [IconKind.Journal] = "M 5,3 L 19,3 L 19,21 L 5,21 Z M 9,7 L 16,7 M 9,11 L 16,11 M 9,15 L 14,15",
            [IconKind.Layers] = "M 3,7 L 12,2 L 21,7 L 12,12 Z M 3,12 L 12,17 L 21,12 M 3,17 L 12,22 L 21,17",
            [IconKind.User] = "M 12,3 A 4,4 0 1 0 12,11 A 4,4 0 1 0 12,3 M 4,21 L 4,19 Q 4,14 12,14 Q 20,14 20,19 L 20,21",
            [IconKind.Shield] = "M 12,2 L 21,6 L 20,14 Q 18,19 12,22 Q 6,19 4,14 L 3,6 Z M 8,12 L 11,15 L 16,9",
            [IconKind.Logout] = "M 10,3 L 4,3 L 4,21 L 10,21 M 9,12 L 21,12 M 17,8 L 21,12 L 17,16",
            [IconKind.Folder] = "M 3,6 L 3,20 L 21,20 L 21,7 L 11,7 L 9,4 L 3,4 Z",
            [IconKind.Report] = "M 5,3 L 15,3 L 20,8 L 20,21 L 5,21 Z M 14,3 L 14,9 L 20,9 M 9,13 L 16,13 M 9,17 L 14,17",
            [IconKind.History] = "M 4,9 A 9,9 0 1 1 4,17 M 4,3 L 4,9 L 10,9 M 12,7 L 12,12 L 16,14",
            [IconKind.List] = "M 4,6 L 5,6 M 9,6 L 21,6 M 4,12 L 5,12 M 9,12 L 21,12 M 4,18 L 5,18 M 9,18 L 21,18",
            [IconKind.Info] = "M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 M 12,7 L 12,7.2 M 12,11 L 12,17",
            [IconKind.Plus] = "M 12,4 L 12,20 M 4,12 L 20,12",
            [IconKind.Download] = "M 12,3 L 12,16 M 7,11 L 12,16 L 17,11 M 4,16 L 4,21 L 20,21 L 20,16",
            [IconKind.Refresh] = "M 20,8 A 9,9 0 0 0 4,7 M 20,3 L 20,8 L 15,8 M 4,16 A 9,9 0 0 0 20,17 M 4,21 L 4,16 L 9,16",
            [IconKind.Close] = "M 6,6 L 18,18 M 18,6 L 6,18",
            [IconKind.Clear] = "M 4,5 L 20,5 M 7,10 L 17,10 M 10,15 L 14,15 M 11,20 L 13,20",
            [IconKind.Link] = "M 9,8 L 12,5 A 5,5 0 0 1 19,12 L 16,15 M 15,16 L 12,19 A 5,5 0 0 1 5,12 L 8,9 M 8,16 L 16,8",
            [IconKind.Wifi] = "M 2,7 Q 12,-1 22,7 M 5,11 Q 12,5 19,11 M 8,15 Q 12,11 16,15 M 12,19 L 12,19.2",
            [IconKind.Help] = "M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 M 9,8 Q 9,5 12,5 Q 17,6 14,10 L 12,12 L 12,14 M 12,18 L 12,18.2",
            [IconKind.Import] = "M 14,3 L 20,3 L 20,21 L 4,21 L 4,16 M 3,9 L 14,9 M 10,5 L 14,9 L 10,13",
            [IconKind.Check] = "M 3,12 L 9,18 L 21,6",
            [IconKind.Trash] = "M 3,6 L 21,6 M 9,6 L 9,3 L 15,3 L 15,6 M 5,6 L 6,21 L 18,21 L 19,6 M 10,10 L 10,17 M 14,10 L 14,17",
            [IconKind.External] = "M 13,3 L 21,3 L 21,11 M 21,3 L 10,14 M 9,4 L 3,4 L 3,21 L 20,21 L 20,15",
            [IconKind.Export] = "M 12,16 L 12,3 M 7,8 L 12,3 L 17,8 M 4,15 L 4,21 L 20,21 L 20,15",
            [IconKind.Edit] = "M 4,16 L 16,4 L 20,8 L 8,20 L 3,21 Z M 13,7 L 17,11",
            [IconKind.Save] = "M 3,3 L 17,3 L 21,7 L 21,21 L 3,21 Z M 7,3 L 7,9 L 16,9 L 16,3 M 7,21 L 7,14 L 17,14 L 17,21"
        };
        var shapes = new Dictionary<IconKind, Geometry>();
        foreach (var entry in paths) shapes.Add(entry.Key, Geometry.Parse(entry.Value));
        return shapes;
    }
}

/// <summary>Keeps button Content and tab Header as text, including live queue counters and accessibility labels.</summary>
public sealed class ActionIcon : AvaloniaObject
{
    public static readonly AttachedProperty<IconKind> KindProperty =
        AvaloniaProperty.RegisterAttached<ActionIcon, ContentControl, IconKind>("Kind");

    static ActionIcon() => KindProperty.Changed.AddClassHandler<ContentControl>((control, change) =>
    {
        var kind = change.GetNewValue<IconKind>();
        var template = kind == IconKind.None ? null : new FuncDataTemplate<object>((content, _) =>
        {
            var icon = new AppIcon { Kind = kind };
            icon.Bind(AppIcon.ForegroundProperty, control.GetObservable(TemplatedControl.ForegroundProperty));
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    icon,
                    new TextBlock { Text = content?.ToString(), VerticalAlignment = VerticalAlignment.Center }
                }
            };
        });
        if (control is TabItem tab) tab.HeaderTemplate = template;
        else control.ContentTemplate = template;
    });

    public static IconKind GetKind(ContentControl control) => control.GetValue(KindProperty);
    public static void SetKind(ContentControl control, IconKind value) => control.SetValue(KindProperty, value);
}
