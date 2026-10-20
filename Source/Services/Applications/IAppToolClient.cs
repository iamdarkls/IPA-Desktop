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
    /// <summary>Подменяемый контракт команд ipatool; тесты выполняются без Apple Account и сетевого входа.</summary>
    public interface IAppToolClient
    {
        /// <summary>Добавляет параметры протокола CLI и выполняет команду через подменяемый процессный адаптер.</summary>
        Task<Result> RunAsync(string[] arguments, CancellationToken cancellation, bool publish);
    }
}
