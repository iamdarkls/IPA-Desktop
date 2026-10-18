using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Изолированный интерактивный канал авторизации: вывод никогда не поступает в общий ProcessRunner.</summary>
    public interface ILoginProcess : IDisposable
    {
        Task<string> ReadAsync(CancellationToken ct);
        Task SendAsync(SecureString value, CancellationToken ct);
        int ExitCode { get; }
    }
    public interface ILoginProcessFactory
    {
        ILoginProcess Start(string executable, string workingDirectory);
    }
}
