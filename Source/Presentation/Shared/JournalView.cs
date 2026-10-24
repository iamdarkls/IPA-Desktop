using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace IpaDesktop;
public static class JournalView
{
    public static void Attach(TextBox journal, BufferedJournal buffer)
    {
        journal.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.IsVisibleProperty && journal.IsVisible)
                Dispatcher.UIThread.Post(() => Flush(journal, buffer, true));
        };
    }
    public static void Flush(TextBox journal, BufferedJournal buffer, bool all)
    {
        var text = new StringBuilder(); string chunk;
        do { chunk = buffer.Drain(); text.Append(chunk); } while (all && chunk.Length > 0);
        if (text.Length == 0) return;
        if (text.Length > 120000) text.Remove(0,text.Length-120000);
        var current = journal.Text ?? "";
        if (current.Length > 120000) current = current.Substring(current.Length-80000);
        journal.Text = current + text; journal.CaretIndex = journal.Text.Length;
        if (journal.IsVisible) Dispatcher.UIThread.Post(() => journal.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd());
    }
}
