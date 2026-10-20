using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace IpaDesktop
{
    /// <summary>Выводит буфер журнала в WPF и поддерживает прокрутку к последним сообщениям.</summary>
    public static class JournalView
    {
        /// <summary>Подключает обновление журнала при открытии вкладки и прокрутку к последней записи.</summary>
        public static void Attach(TextBox journal, BufferedJournal buffer)
        {
            journal.IsVisibleChanged += delegate
            {
                if (!journal.IsVisible) return;
                journal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate
                {
                    Flush(journal, buffer, true);
                    journal.ScrollToEnd();
                }));
            };
        }
        /// <summary>Добавляет накопленные строки и прокручивает вид к актуальным данным.</summary>
        public static void Flush(TextBox journal, BufferedJournal buffer, bool all)
        {
            var text = new StringBuilder();
            string chunk;
            do
            {
                chunk = buffer.Drain();
                text.Append(chunk);
            }
            while (all && chunk.Length > 0);
            if (text.Length == 0) return;
            if (text.Length > 120000) text.Remove(0, text.Length - 120000);
            if (journal.Text.Length > 120000) journal.Text = journal.Text.Substring(journal.Text.Length - 80000);
            journal.AppendText(text.ToString());
            if (journal.IsVisible) journal.ScrollToEnd();
        }
    }
}
