using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;

namespace IpaDesktop;

/// <summary>Owned, modeless journal. Closing it never cancels a transfer or clears the model.</summary>
public sealed partial class JournalWindow : Window
{
    public JournalWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var text = this.FindControl<TextBox>("JournalText");
        void FollowTail()
        {
            if (DataContext is DesktopViewModel { FollowJournal: true })
            {
                text.CaretIndex = text.Text?.Length ?? 0;
                text.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
            }
        }
        text.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && IsVisible) Dispatcher.UIThread.Post(FollowTail);
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(FollowTail);
    }
    public JournalWindow(Window owner, DesktopViewModel model) : this()
    {
        Icon = owner.Icon;
        DataContext = model;
    }
}
