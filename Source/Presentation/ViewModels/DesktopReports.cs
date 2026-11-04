using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IpaDesktop
{
    // Presentation state for reports, journal and queue progress; operation lifecycle stays in Desktop.
    public sealed partial class DesktopViewModel
    {
        readonly BufferedJournal journalBuffer = new BufferedJournal();
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
        Func<Task> noticeRetry;
        /// <summary>Фиксирует самостоятельный снимок очереди и её действие повтора.</summary>
        internal void StartReport(string kind, int total, Func<Task> retry)
        {
            activeReport = new OperationReport { Kind = kind, Total = total };
            noticeRetry = retry;
            OperationNoticeVisible = false;
        }
        internal void ReportItem(string name, Exception error)
        {
            Queue.Result(error);
            if (activeReport != null) activeReport.Add(name, error);
        }
        void RenderNotice()
        {
            if (lastReport == null) return;
            var names = String.Join(", ", lastReport.Items.Where(x => !x.Success).Take(1).Select(x => x.Name + ": " + x.ShortError));
            NoticeText = lastReport.Summary + (names.Length == 0 ? "" : " · " + names);
            NoticeTextTip = lastReport.Details;
            var failure = lastReport.Items.LastOrDefault(x => !x.Success);
            NoticeCause = failure?.Cause;
            NoticeCauseVisible = failure != null;
            NoticeRemedy = failure == null ? lastReport.StopReason : "Что сделать: " + failure.Remedy;
            NoticeRemedyVisible = failure != null || !String.IsNullOrEmpty(lastReport.StopReason);
            bool errors = lastReport.Failed > 0 || !String.IsNullOrEmpty(lastReport.StopReason);
            OperationNoticeBackground = errors ? "#38232C" : lastReport.Canceled ? "#383122" : "#173529";
            OperationNoticeBorder = errors ? "#E17B8B" : lastReport.Canceled ? "#D7BC79" : "#6AB893";
            NoticeRetryVisible = errors && noticeRetry != null;
            OperationNoticeVisible = true;
            ApplyActions();
        }

        /// <summary>Показывает название приложения и позицию в текущем пакете.</summary>
        internal void BeginQueue(string kind, int index, int total, string app)
        {
            queueProgress.Begin(kind, index, total, app);
            Queue.Begin(index);
            Status = kind + " " + index + "/" + total + ": " + app;
            RenderQueue();
        }
        /// <summary>Фиксирует итоговые счётчики, прекращая обновление завершённого прогресса.</summary>
        internal void CompleteQueue(int success, int failed)
        {
            queueProgress.Complete(success, failed);
            RenderQueue();
            completionSound.Notify(settings.Settings.SoundsEnabled, success, failed);
            if (activeReport != null) { lastReport = activeReport; RenderNotice(); }
        }
        /// <summary>Отрисовывает снимок прогресса в UI-потоке без прямого доступа фоновых процессов к Avalonia.</summary>
        void RenderQueue()
        {
            OperationDetail = queueProgress.StatusText;
            TransferDetail = queueProgress.TransferText;
            ProgressTip = queueProgress.Text;
            Queue.Measure(queueProgress.ItemPercent, queueProgress.TransferText);
            if (!queueProgress.Active) return;
            Indeterminate = IsBusy && queueProgress.Indeterminate;
            Progress = queueProgress.Value;
        }
        /// <summary>Передаёт метрики прогрессу, а обычные сообщения — в ограниченный буфер журнала.</summary>
        internal void Log(string text)
        {
            if (queueProgress.Feed(text)) return;
            journalBuffer.Add(text);
        }
        /// <summary>Переносит накопленные сообщения журнала в интерфейс.</summary>
        void FlushJournal(bool all = false)
        {
            string chunk;
            do { chunk = journalBuffer.Drain(); AppendJournal(chunk); } while (all && chunk.Length > 0);
        }
        void ShowJournal() { FlushJournal(true); Interaction.ShowJournal(this); }
        async Task ExportDiagnostics()
        {
            FlushJournal(true);
            List<string> privateValues;
            lock (privateGate) privateValues = reportPrivateValues.ToList();
            privateValues.Add(Environment.UserName);
            var email = Account.AccountLabel;
            if (email?.Contains("@") == true) privateValues.Add(email);
            foreach (var device in Files.DeviceList.Items.Cast<DeviceRow>()) { privateValues.Add(device.ID); privateValues.Add(device.Name); }
            var report = DiagnosticReport.Create(JournalText, settings.Settings.Version ?? "not installed", settings.Settings.DeviceVersion ?? "not installed", privateValues);
            if (!await Interaction.ExportText("IPA-Desktop-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt", report)) return;
            Log("Отчёт диагностики сохранён.");
        }
        /// <summary>Показывает ошибку пользователю и сохраняет сообщение в журнале.</summary>
        internal void Error(Exception e)
        {
            Log(e.Message);
            var failure = ErrorLocalizer.Describe(e);
            Status = failure.Title;
            if (activeReport != null && e is DeviceConnectionException) activeReport.StopReason = failure.Description;
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
