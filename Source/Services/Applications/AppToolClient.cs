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

namespace IpaDesktop
{
    /// <summary>Адаптер ipatool: находит бинарник, добавляет JSON-формат и делегирует запуск процессному слою.</summary>
    public sealed class AppToolClient : IAppToolClient
    {
        readonly IProcessRunner runner;
        readonly ToolLocator locator;
        readonly GuardedEnginePolicy policy;
        public AppToolClient(IProcessRunner runner, ToolLocator locator, GuardedEnginePolicy policy = null)
        {
            this.runner = runner;
            this.locator = locator; this.policy=policy;
        }
        /// <summary>Добавляет параметры протокола CLI и выполняет команду через подменяемый процессный адаптер.</summary>
        public Task<Result> RunAsync(string[] args, CancellationToken ct, bool publish)
        {
            return Task.Run(async delegate
            {
                if (!(args.Length>1 && args[0]=="auth" && (args[1]=="info" || args[1]=="revoke")) && policy!=null) await policy.RequireAsync(ct).ConfigureAwait(false);
                return await runner.Run(locator.Find("ipatool*.exe"), args.Concat(new[]
                {
                    "--format", "json"
                }).ToArray(), ct, false, publish);
            }, ct);
        }
    }
}
