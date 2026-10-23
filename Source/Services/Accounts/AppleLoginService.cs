using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IpaDesktop
{
    public interface IAppleLoginService
    {
        Task<string> LoginAsync(string identifier, SecureString password, Func<CancellationToken, Task<SecureString>> requestCode, CancellationToken ct);
    }
    /// <summary>Только заранее определённые сообщения могут выйти из приватного канала авторизации.</summary>
    public sealed class AppleLoginException : Exception
    {
        public AppleLoginException(string message) : base(message) { }
    }
    public enum AppleLoginPrompt { None, Identifier, Password, Code }
    /// <summary>Ограниченный протокол интерактивных запросов; исходный вывод не отдаётся UI или журналу.</summary>
    public sealed class AppleLoginProtocol
    {
        string buffer = "";
        bool identifierSent, passwordSent, codeSent;
        static readonly Regex[] prompts =
        {
            new Regex(@"\bEnter[ \t]+(?:e-?mail|Apple[ \t]+(?:ID|Account))[ \t]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bEnter[ \t]+password[ \t]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"\bEnter[ \t]+(?:2FA|two-factor|verification)[ \t]+code[ \t]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        };
        public AppleLoginPrompt Append(string chunk)
        {
            if (buffer.Length + chunk.Length > 65536) throw new AppleLoginException("Неожиданный ответ ipatool. Обновите движок и повторите вход.");
            buffer += chunk;
            string text = Text();
            // ConPTY может повторно прислать всю строку при перерисовке, включая старые запросы.
            // Реагируем только на следующий этап; пароль/код повторно не отправляем.
            var identifier = prompts[0].Match(text);
            var password = prompts[1].Match(text);
            var code = prompts[2].Match(text);
            if (!identifierSent && identifier.Success)
            {
                identifierSent = true; buffer = text.Substring(identifier.Index + identifier.Length); return AppleLoginPrompt.Identifier;
            }
            if (!passwordSent && password.Success)
            {
                if (!identifierSent) throw Changed();
                passwordSent = true; buffer = text.Substring(password.Index + password.Length); return AppleLoginPrompt.Password;
            }
            if (!codeSent && code.Success)
            {
                if (!passwordSent) throw Changed();
                codeSent = true; buffer = text.Substring(code.Index + code.Length); return AppleLoginPrompt.Code;
            }
            // Отбрасываем обработанный префикс перерисовки, сохраняя хвост и частичные ANSI-последовательности.
            // Иначе многократное эхо старого запроса раздувало бы ограниченный буфер.
            int consumed = 0;
            foreach (var prompt in prompts)
            {
                foreach (Match match in prompt.Matches(text)) consumed = Math.Max(consumed, match.Index + match.Length);
            }
            if (consumed > 0) buffer = text.Substring(consumed);
            return AppleLoginPrompt.None;
        }
        static AppleLoginException Changed() { return new AppleLoginException("Не удалось распознать последовательность запросов входа. Закройте окно и повторите попытку. Если ошибка повторяется, проверьте обновления программы."); }
        string Text()
        {
            var text = Regex.Replace(buffer, "\u001b\\][^\u0007]*(?:\u0007|\u001b\\\\)", "");
            return Regex.Replace(text, "\u001b\\[[0-?]*[ -/]*[@-~]", "").Replace("\r", "");
        }
        public string Complete(int exitCode)
        {
            var text = Text();
            try
            {
                if (text.Contains("could not save account")) throw new AppleLoginException("Движок не смог сохранить сессию. Проверьте доступ к папке .ipatool в профиле Windows и повторите вход.");
                if (exitCode == 0 && passwordSent)
                {
                    try
                    {
                        var data = ToolResponse.Object(text);
                        var email = Convert.ToString(ToolResponse.Get(data, "email"));
                        if (Object.Equals(ToolResponse.Get(data, "success"), true) && !String.IsNullOrWhiteSpace(email)) return email;
                    }
                    catch (AppleLoginException) { throw; }
                    catch (Exception) { }
                }
                var lower = text.ToLowerInvariant();
                if (lower.Contains("locked") || lower.Contains("disabled")) throw new AppleLoginException("Apple сообщает об ограничении аккаунта. Проверьте его состояние на сайте Apple.");
                if (lower.Contains("resolve") || lower.Contains("connect") || lower.Contains("timeout") || lower.Contains("ssl") || lower.Contains("tls"))
                    throw new AppleLoginException("Не удалось связаться с Apple. Проверьте подключение, дату Windows и повторите вход позже.");
                if (lower.Contains("terms") || lower.Contains("agreement")) throw new AppleLoginException("Apple требует принять условия аккаунта. Откройте App Store на устройстве и завершите настройку аккаунта.");
                throw new AppleLoginException(codeSent ? "Apple не подтвердила вход с этим кодом. Проверьте пароль и повторите вход со свежим кодом." : "Вход не подтверждён. Проверьте Apple Account и обычный пароль аккаунта, затем повторите попытку.");
            }
            finally { Clear(); }
        }
        public void Clear() { buffer = ""; }
    }
    /// <summary>Авторизует штатным ipatool и затем отдельно проверяет сохранённую сессию. Без CLI-паролей и сырых ошибок.</summary>
    public sealed class AppleLoginService : IAppleLoginService
    {
        readonly ILoginProcessFactory factory;
        readonly Func<string> executable;
        readonly string directory;
        readonly IAppToolClient tools;
        public AppleLoginService(ILoginProcessFactory factory, Func<string> executable, string directory, IAppToolClient tools)
        { this.factory = factory; this.executable = executable; this.directory = directory; this.tools = tools; }
        public Task<string> LoginAsync(string identifier, SecureString password, Func<CancellationToken, Task<SecureString>> requestCode, CancellationToken ct)
        {
            return Task.Run(async delegate
            {
                if (String.IsNullOrWhiteSpace(identifier) || identifier.Length > 320 || password == null || password.Length == 0 || password.Length > 1024)
                    throw new AppleLoginException("Введите Apple Account и пароль.");
                foreach (char c in identifier) if (Char.IsControl(c)) throw new AppleLoginException("Apple Account не должен содержать переносов строк.");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var token = timeout.Token; var protocol = new AppleLoginProtocol(); string email = null;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        using (var session = factory.Start(executable(), directory))
                        {
                            string chunk;
                            while ((chunk = await session.ReadAsync(token).ConfigureAwait(false)) != null)
                            {
                                var prompt = protocol.Append(chunk);
                                if (prompt == AppleLoginPrompt.Identifier)
                                {
                                    using (var value = new SecureString())
                                    {
                                        foreach (char c in identifier.Trim()) value.AppendChar(c);
                                        value.MakeReadOnly(); await session.SendAsync(value, token).ConfigureAwait(false);
                                    }
                                }
                                else if (prompt == AppleLoginPrompt.Password) await session.SendAsync(password, token).ConfigureAwait(false);
                                else if (prompt == AppleLoginPrompt.Code)
                                {
                                    using (var code = await requestCode(token).ConfigureAwait(false))
                                    {
                                        if (code == null) throw new OperationCanceledException(token);
                                        await session.SendAsync(code, token).ConfigureAwait(false);
                                    }
                                }
                            }
                            email = protocol.Complete(session.ExitCode);
                        }
                        // Не принимаем старую сессию за результат нового входа, даже если CLI напечатал success.
                        var info = await tools.RunAsync(new[] { "auth", "info" }, token, false).ConfigureAwait(false);
                        var saved = ToolResponse.Object(info.Output);
                        if (info.Code != 0 || !Object.Equals(ToolResponse.Get(saved, "success"), true) ||
                            !String.Equals(email, Convert.ToString(ToolResponse.Get(saved, "email")), StringComparison.OrdinalIgnoreCase))
                            throw new AppleLoginException("Новая сессия не подтверждена. Повторите вход; текущий аккаунт можно проверить основной кнопкой.");
                        return email;
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                        throw new AppleLoginException("Время входа истекло. Повторите попытку и подготовьте доверенное устройство для получения кода.");
                    }
                    catch (AppleLoginException) { throw; }
                    catch (NotSupportedException) { throw new AppleLoginException("Вход в окне требует Windows 10 версии 1809 или новее."); }
                    catch (Exception) { throw new AppleLoginException("Не удалось завершить вход. Проверьте установку ipatool и повторите попытку. Подробности авторизации не записываются в журнал."); }
                    finally { protocol.Clear(); }
                }
            }, ct);
        }
    }
}
