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
    /// <summary>Подменяемый контракт запуска процессов, журналирования и отмены для Windows и тестов.</summary>
    public interface IProcessRunner
    {
        Action<string> Log
        {
            get;
            set;
        }
        Process Active
        {
            get;
        }
        Task<Result> Run(string executable, string[] arguments, CancellationToken cancellation, bool visible, bool publish, string workingDirectory = null);
    }
}
