using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;

namespace IpaDesktop
{
    // Presentation state for reports, journal and queue progress; operation lifecycle stays in Desktop.
    public sealed partial class Desktop
    {
        readonly BufferedJournal journalBuffer = new BufferedJournal();
        Avalonia.Threading.DispatcherTimer journalTimer;
        readonly object privateGate = new object();
        readonly HashSet<string> reportPrivateValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Запоминает значения для удаления из диагностического отчёта с ограничением размера набора.</summary>
        internal void RegisterPrivate(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 1024) return;
            lock (privateGate) if (reportPrivateValues.Count < 2000) reportPrivateValues.Add(value);
        }
        readonly QueueProgress queueProgress = new QueueProgress();
        readonly CompletionSound completionSound = new CompletionSound();
        OperationReport activeReport;
        OperationReport lastReport;
        Action noticeRetry;
        /// <summary>Фиксирует самостоятельный снимок очереди и её действие повтора.</summary>
        internal void StartReport(string kind, int total, Action retry)
        {
            activeReport = new OperationReport { Kind = kind, Total = total };
            noticeRetry = retry;
            C<Border>("OperationNotice").IsVisible = false;
        }
        internal void ReportItem(string name, Exception error)
        {
            if (activeReport != null) activeReport.Add(name, error);
        }
        void RenderNotice()
        {
            if (lastReport == null) return;
            var names = String.Join(", ", lastReport.Items.Where(x => !x.Success).Take(1).Select(x => x.Name + ": " + x.ShortError));
            C<TextBlock>("NoticeText").Text = lastReport.Summary + (names.Length == 0 ? "" : " · " + names);
            ToolTip.SetTip(C<TextBlock>("NoticeText"), lastReport.Details);
            var failure=lastReport.Items.LastOrDefault(x=>!x.Success);
            C<TextBlock>("NoticeCause").Text=failure?.Cause;
            C<TextBlock>("NoticeCause").IsVisible=failure != null;
            C<TextBlock>("NoticeRemedy").Text=failure == null ? lastReport.StopReason : "Что сделать: " + failure.Remedy;
            C<TextBlock>("NoticeRemedy").IsVisible=failure != null || !String.IsNullOrEmpty(lastReport.StopReason);
            bool errors = lastReport.Failed > 0 || !String.IsNullOrEmpty(lastReport.StopReason);
            var notice = C<Border>("OperationNotice");
            notice.Background = new Avalonia.Media.SolidColorBrush((Avalonia.Media.Color)Avalonia.Media.Color.Parse(errors ? "#38232C" : lastReport.Canceled ? "#383122" : "#173529"));
            notice.BorderBrush = new Avalonia.Media.SolidColorBrush((Avalonia.Media.Color)Avalonia.Media.Color.Parse(errors ? "#E17B8B" : lastReport.Canceled ? "#D7BC79" : "#6AB893"));
            C<Button>("NoticeRetry").IsVisible = errors && noticeRetry != null;
            notice.IsVisible = true;
            ApplyActions();
        }

        /// <summary>Показывает название приложения и позицию в текущем пакете.</summary>
        internal void BeginQueue(string kind, int index, int total, string app)
        {
            queueProgress.Begin(kind, index, total, app);
            C<TextBlock>("Status").Text = kind + " " + index + "/" + total + ": " + app;
            RenderQueue();
        }
        /// <summary>Фиксирует итоговые счётчики, прекращая обновление завершённого прогресса.</summary>
        internal void CompleteQueue(int success, int failed)
        {
            queueProgress.Complete(success, failed);
            RenderQueue();
            completionSound.Notify(engine.Settings.SoundsEnabled, success, failed);
            if (activeReport != null) { lastReport = activeReport; RenderNotice(); }
        }
        /// <summary>Отрисовывает снимок прогресса в UI-потоке без прямого доступа фоновых процессов к Avalonia.</summary>
        void RenderQueue()
        {
            C<TextBlock>("OperationDetail").Text = queueProgress.StatusText;
            C<TextBlock>("TransferDetail").Text = queueProgress.TransferText;
            ToolTip.SetTip(C<TextBlock>("OperationDetail"), queueProgress.Text);
            if (!queueProgress.Active) return;
            C<ProgressBar>("Progress").IsIndeterminate = busy && queueProgress.Indeterminate;
            C<ProgressBar>("Progress").Value = queueProgress.Value;
        }
        /// <summary>Передаёт метрики прогрессу, а обычные сообщения — в ограниченный буфер журнала.</summary>
        internal void Log(string text)
        {
            if (queueProgress.Feed(text)) return;
            journalBuffer.Add(text);
        }
        /// <summary>Переносит накопленные сообщения журнала в интерфейс.</summary>
        void FlushJournal()
        {
            JournalView.Flush(C<TextBox>("Journal"), journalBuffer, false);
        }
        /// <summary>Показывает ошибку пользователю и сохраняет сообщение в журнале.</summary>
        internal void Error(Exception e)
        {
            Log(e.Message);
            var failure=ErrorLocalizer.Describe(e);
            C<TextBlock>("Status").Text = failure.Title;
            if(activeReport != null && e is DeviceConnectionException) activeReport.StopReason=failure.Description;
            if (activeReport == null || (activeReport.Failed == 0 && activeReport.Succeeded == 0))
            {
                lastReport = new OperationReport { Kind = "Операция", Total = 1 };
                lastReport.Add(activeReport == null ? "Ошибка операции" : activeReport.Kind, e);
                noticeRetry = null;
            }
            else lastReport = activeReport;
            RenderNotice();
        }
    }
}
