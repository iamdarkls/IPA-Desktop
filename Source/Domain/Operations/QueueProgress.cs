using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
namespace IpaDesktop
{
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class TransferUpdate
    {
        public bool IsMeasurement { get; set; }
        public double Percent { get; set; }
        public string Metrics { get; set; }
        public static TransferUpdate Parse(string line, bool installing)
        {
            if (line == null) return null;
            var sample = Regex.Match(line, @"^FILE_TRANSFER:(\d+):(\?|\d+(?:\.\d+)?)$");
            if (sample.Success)
            {
                long bytes; double rate;
                if (!Int64.TryParse(sample.Groups[1].Value, out bytes)) return null;
                string speed = "измерение скорости…";
                if (sample.Groups[2].Value != "?")
                {
                    if (!Double.TryParse(sample.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate) || Double.IsInfinity(rate)) return null;
                    speed = "≈ " + (rate / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " МБ/с";
                }
                return new TransferUpdate { IsMeasurement = true, Metrics = "записано " + StorageAnalysis.Bytes(bytes) + " · " + speed };
            }
            var match = Regex.Match(line, @"^PROGRESS:(\d{1,3})\s*$");
            if (!match.Success && installing) match = Regex.Match(line, @"\((\d{1,3})%\)\s*$");
            if (match.Success)
            {
                int percent;
                if (!Int32.TryParse(match.Groups[1].Value, out percent) || percent > 100) return null;
                return new TransferUpdate { Percent = percent };
            }
            match = Regex.Match(line, @"^Downloading:\s*(\d{1,3})%.*\((\d+(?:\.\d+)?\s*(?:MB|GB)/\d+(?:\.\d+)?\s*(?:MB|GB),\s*\d+(?:\.\d+)?\s*MB/s)\)");
            if (!match.Success) return null;
            var value = Double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return value <= 100 ? new TransferUpdate { Percent = value, Metrics = match.Groups[2].Value } : null;
        }
    }
    /// <summary>Потокобезопасное состояние прогресса; использует только метрики, реально полученные от утилиты.</summary>
    public sealed class QueueProgress
    {
        readonly object gate = new object();
        readonly Stopwatch elapsed = new Stopwatch();
        string kind, name, metrics, fileMetrics, summary;
        int index, count;
        double? percent;
        public void Reset() { lock (gate) { kind = null; summary = null; elapsed.Reset(); } }
        public void Begin(string operation, int position, int total, string app)
        {
            lock (gate) { kind = operation; index = position; count = total; name = app; metrics = null; fileMetrics = null; summary = null; percent = null; elapsed.Restart(); }
        }
        /// <summary>Разбирает реальный вывод CLI под блокировкой; неподдерживаемые метрики не выдумываются.</summary>
        public bool Feed(string line)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return false;
                var update = TransferUpdate.Parse(line, kind == "Установка");
                if (update == null) return false;
                if (update.IsMeasurement) { if (kind != "Скачивание") return false; fileMetrics = update.Metrics; }
                else { percent = update.Percent; metrics = update.Metrics; }
                return true;
            }
        }
        public void Complete(int success, int failures)
        {
            lock (gate) { summary = "Готово: " + success + " · ошибок: " + failures + " · всего: " + count; percent = 100; index = count; elapsed.Stop(); }
        }
        /// <summary>Фиксирует отмену или ошибку и прекращает приём поздних сообщений прогресса.</summary>
        public void Stop(string reason)
        {
            lock (gate)
            {
                if (kind == null || summary != null) return;
                elapsed.Stop(); summary = reason + " · " + index + "/" + count + ": " + name;
            }
        }
        public bool Active { get { lock (gate) return kind != null; } }
        public double Value { get { lock (gate) return count > 0 ? 100.0 * (index - 1 + (percent ?? 0) / 100.0) / count : 0; } }
        public bool Indeterminate { get { lock (gate) return !percent.HasValue; } }
        string MetricsText()
        {
            return kind == "Скачивание" && summary == null ? metrics ?? fileMetrics ?? "объём и скорость недоступны" : "";
        }
        string Describe(bool includeMetrics)
        {
            if (kind == null) return "";
            if (summary != null) return summary;
            string text = kind + " " + index + "/" + count + ": " + name + " · осталось после текущего: " + Math.Max(0, count - index) + " · " +
                (percent.HasValue ? percent.Value.ToString("0") + "%" : "выполняется") + " · " + elapsed.Elapsed.ToString(@"hh\:mm\:ss");
            if (kind == "Скачивание" && percent.HasValue && percent.Value >= 100) text += " · проверка и подготовка IPA";
            string transfer = MetricsText();
            return includeMetrics && transfer.Length > 0 ? text + " · " + transfer : text;
        }
        public string TransferText { get { lock (gate) return MetricsText(); } }
        public string StatusText { get { lock (gate) return Describe(false); } }
        public string Text { get { lock (gate) return Describe(true); } }
    }
}
