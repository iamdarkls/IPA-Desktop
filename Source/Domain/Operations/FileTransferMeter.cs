using System;
using System.Collections.Generic;
namespace IpaDesktop
{
    /// <summary>Оценка записи по монотонным отсчётам размера одного файла, без вывода объёма из округлённых процентов.</summary>
    public sealed class FileTransferMeter
    {
        readonly Queue<Tuple<double, long>> samples = new Queue<Tuple<double, long>>();
        public void Reset() { samples.Clear(); }
        public FileTransferSample Sample(long bytes, double seconds)
        {
            if (bytes < 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0) throw new ArgumentException("Некорректное измерение передачи.");
            if (samples.Count > 0 && (seconds <= LastTime || bytes < LastBytes)) Reset();
            samples.Enqueue(Tuple.Create(seconds, bytes)); LastTime = seconds; LastBytes = bytes;
            while (samples.Count > 8 || (samples.Count > 1 && seconds - samples.Peek().Item1 > 3.5)) samples.Dequeue();
            var first = samples.Peek(); double elapsed = seconds - first.Item1;
            return new FileTransferSample { Bytes = bytes, BytesPerSecond = elapsed >= 0.5 ? (double?)(bytes - first.Item2) / elapsed : null };
        }
        double LastTime; long LastBytes;
    }
    public sealed class FileTransferSample
    {
        public long Bytes { get; set; }
        public double? BytesPerSecond { get; set; }
        public string Message
        {
            get { return "FILE_TRANSFER:" + Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (BytesPerSecond.HasValue ? BytesPerSecond.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "?"); }
        }
    }
}
