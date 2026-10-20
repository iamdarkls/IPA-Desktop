using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop
{
    /// <summary>Ограниченный буфер сообщений между фоновыми процессами и таймером интерфейса.</summary>
    public class BufferedJournal
    {
        readonly Queue<string> pending = new Queue<string> ();
        readonly object gate = new object();
        int skipped;
        public void Add(string text)
        {
            if (text == null) return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text.Length > 2000 ? text.Substring(0, 2000) + " …" : text) + Environment.NewLine;
            lock (gate)
            {
                if (pending.Count >= 500)
                {
                    pending.Dequeue();
                    skipped++;
                }
                pending.Enqueue(line);
            }
        }
        /// <summary>Извлекает накопленные сообщения для пакетного обновления интерфейса.</summary>
        public string Drain()
        {
            lock (gate)
            {
                var result = new StringBuilder();
                if (skipped > 0)
                {
                    result.AppendLine("Слишком частый вывод: пропущено строк " + skipped + ".");
                    skipped = 0;
                }
                while (pending.Count > 0 && result.Length < 6000) result.Append(pending.Dequeue());
                return result.ToString();
            }
        }
    }
}
