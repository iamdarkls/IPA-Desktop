using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IpaDesktop
{
    /// <summary>Итог одной очереди с полными ошибками; не зависит от журнала и текущей вкладки.</summary>
    public sealed class OperationReport
    {
        public string Kind { get; set; }
        public int Total { get; set; }
        public bool Canceled { get; set; }
        public readonly List<OperationOutcome> Items = new List<OperationOutcome>();
        public int Failed { get { return Items.Count(x => !x.Success); } }
        public int Succeeded { get { return Items.Count(x => x.Success); } }
        public string Summary
        {
            get
            {
                return Kind + (Canceled ? " отменено" : Failed > 0 ? ": есть ошибки" : ": завершено") +
                    " · готово " + Succeeded + "/" + Total + (Failed > 0 ? " · ошибок " + Failed : "");
            }
        }
        public string Details
        {
            get
            {
                var text = new StringBuilder(Summary);
                if (Canceled) text.AppendLine().Append("Не запущено или прервано: ").Append(Math.Max(0, Total - Items.Count));
                foreach (var item in Items)
                {
                    text.AppendLine().AppendLine().Append(item.Success ? "ГОТОВО: " : "ОШИБКА: ").Append(item.Name);
                    if (!item.Success) text.AppendLine().Append(item.Error);
                }
                return text.ToString();
            }
        }
        public void Add(string name, Exception error)
        {
            string message = error == null ? null : error.Message;
            if (message != null && message.Length > 16000) message = message.Substring(0, 16000);
            Items.Add(new OperationOutcome { Name = name, Success = error == null, Error = message });
        }
    }

    /// <summary>Результат отдельного приложения без потери исходного сообщения утилиты.</summary>
    public sealed class OperationOutcome
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public string ShortError
        {
            get
            {
                if (String.IsNullOrWhiteSpace(Error)) return "";
                if (Error.IndexOf("zip_open", StringComparison.OrdinalIgnoreCase) >= 0) return "Не удалось открыть IPA";
                if (Error.IndexOf("ApplicationVerificationFailed", StringComparison.OrdinalIgnoreCase) >= 0) return "iPhone отклонил проверку приложения";
                var line = Error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(x => !x.StartsWith("Код ошибки", StringComparison.OrdinalIgnoreCase)) ?? Error;
                return line.Length > 120 ? line.Substring(0, 120) + "…" : line;
            }
        }
    }
}
