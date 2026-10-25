using System;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop;

public sealed class AppleAccountException : IOException
{
    public UserError Failure { get; }
    public AppleAccountException(UserError failure) : base(failure.Description) => Failure = failure;
}

/// <summary>One ipatool profile per OS user: serialize requests across portable copies, persist pauses without account data.</summary>
public sealed class AccountRequestGuard
{
    readonly string folder;
    readonly Func<DateTimeOffset> clock;
    string StatePath => Path.Combine(folder, "account-requests.txt");
    public AccountRequestGuard(string folder, Func<DateTimeOffset> clock = null)
    { this.folder = folder; this.clock = clock ?? (() => DateTimeOffset.UtcNow); }
    sealed class State
    {
        public FailureCode Blocked;
        public long LoginAfter, RequestAfter;
        public int LoginFailures, RequestFailures;
    }
    State Read()
    {
        if (!File.Exists(StatePath)) return new State();
        using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var parts = reader.ReadToEnd().TrimEnd('\r', '\n').Split('\n');
        for (int index=0; index<parts.Length; index++) parts[index]=parts[index].Trim();
        if (parts.Length != 5 || !Enum.TryParse<FailureCode>(parts[0], out var blocked) || !Enum.IsDefined(blocked) ||
            !long.TryParse(parts[1], out var login) || !long.TryParse(parts[2], out var request) ||
            !int.TryParse(parts[3], out var lf) || !int.TryParse(parts[4], out var rf) || lf < 0 || rf < 0)
            throw new IOException("Не удалось прочитать состояние защиты Apple Account. Проверьте доступ к профилю приложения.");
        return new State { Blocked=blocked, LoginAfter=login, RequestAfter=request, LoginFailures=lf, RequestFailures=rf };
    }
    void Save(State state)
    {
        string temporary = StatePath + ".new";
        File.WriteAllLines(temporary, new[] { state.Blocked.ToString(), state.LoginAfter.ToString(CultureInfo.InvariantCulture),
            state.RequestAfter.ToString(CultureInfo.InvariantCulture), state.LoginFailures.ToString(CultureInfo.InvariantCulture), state.RequestFailures.ToString(CultureInfo.InvariantCulture) });
        File.Move(temporary, StatePath, true);
    }
    public bool RequiresLogin => Read().Blocked != FailureCode.Unknown;
    public static bool IsAccountFailure(FailureCode code) => code is FailureCode.PasswordChanged or FailureCode.Credentials or FailureCode.Terms or FailureCode.AccountRestricted;
    static UserError Blocked(FailureCode code) => new(code, "Загрузки приостановлены: нужен вход в Apple Account",
        "Предыдущий запрос отклонён из-за пароля, состояния аккаунта или условий Apple.",
        "Проверьте аккаунт в App Store. При блокировке восстановите доступ через Apple; затем нажмите «Войти» в программе. Оставшиеся IPA сохранены в очереди.");
    void CheckTime(long after)
    {
        long seconds = (long)Math.Ceiling((after - clock().ToUnixTimeMilliseconds()) / 1000d);
        if (seconds > 0) throw new AppleAccountException(new(FailureCode.RateLimited, "Пауза перед следующим запросом к Apple",
            "Предыдущая попытка не удалась или сервер ограничил запросы.", "Подождите " + seconds + " сек. и повторите действие вручную. Автоматических повторов нет."));
    }
    public IDisposable Enter(bool login, CancellationToken ct, bool recovery = false)
    {
        ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(folder);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(folder, "account-requests.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new AppleAccountException(new(FailureCode.RateLimited, "Запрос к Apple уже выполняется",
            "Другой запрос или другая копия IPA Desktop использует общий профиль ipatool.", "Дождитесь завершения операции. Повторный вход не запущен.")); }
        try
        {
            var state = Read();
            if (!recovery && !login && state.Blocked != FailureCode.Unknown) throw new AppleAccountException(Blocked(state.Blocked));
            if (!recovery) CheckTime(login ? Math.Max(state.LoginAfter, state.RequestAfter) : state.RequestAfter);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    // Called while holding the lease. Failed auth info must never clear a blocked session.
    public void LoginSucceeded() => Save(new State());
    public void LoginStarted()
    {
        var state = Read();
        state.LoginFailures = Math.Min(5, state.LoginFailures + 1);
        state.LoginAfter = clock().AddSeconds(Math.Min(900, 60 * (1 << (state.LoginFailures - 1)))).ToUnixTimeMilliseconds();
        Save(state);
    }
    public void LoginFailed(UserError error)
    {
        var state = Read();
        state.LoginFailures = Math.Max(1, state.LoginFailures);
        int seconds = error.Code == FailureCode.AccountRestricted ? 900 : Math.Min(900, 60 * (1 << (state.LoginFailures - 1)));
        state.LoginAfter = clock().AddSeconds(seconds).ToUnixTimeMilliseconds();
        if (IsAccountFailure(error.Code)) state.Blocked = error.Code;
        Save(state);
    }
    public void RequestFailed(UserError error, TimeSpan? serverDelay = null)
    {
        var state = Read();
        if (IsAccountFailure(error.Code)) state.Blocked = error.Code;
        if (error.Code is FailureCode.Network or FailureCode.RateLimited)
        {
            state.RequestFailures = Math.Min(5, state.RequestFailures + 1);
            double seconds = Math.Max(Math.Min(300, 15 * (1 << (state.RequestFailures - 1))), serverDelay?.TotalSeconds ?? 0);
            state.RequestAfter = clock().AddSeconds(seconds).ToUnixTimeMilliseconds();
        }
        Save(state);
    }
    public void RequestSucceeded()
    {
        var state=Read(); state.RequestAfter=0; state.RequestFailures=0; Save(state);
    }
}

/// <summary>Never retries Apple commands. Account and shared network failures stop a batch before the next IPA.</summary>
public sealed class ProtectedAppToolClient : IAppToolClient
{
    readonly IAppToolClient inner;
    readonly AccountRequestGuard guard;
    public ProtectedAppToolClient(IAppToolClient inner, AccountRequestGuard guard) { this.inner=inner; this.guard=guard; }
    static UserError Describe(string output, Exception error = null)
    {
        var failure=ErrorLocalizer.Describe(output,error);
        // Here the command is ipatool, so its TLS/socket errors concern Apple, not lockdownd.
        return failure.Code == FailureCode.Transport
            ? new UserError(FailureCode.Network,"Соединение с Apple прервано","Сетевой запрос не завершён.","Очередь остановлена. Проверьте интернет и повторите оставшиеся загрузки после паузы.")
            : failure;
    }
    static TimeSpan? RetryAfter(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output ?? "", @"retry[-_ ]after[""' ]*[:=][""' ]*([^\r\n""}]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var value=match.Groups[1].Value.Trim();
        if (int.TryParse(value, out int seconds) && seconds >= 0) return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date - DateTimeOffset.UtcNow;
        return null;
    }
    public async Task<Result> RunAsync(string[] args, CancellationToken ct, bool publish)
    {
        // Local session inspection/revocation remains available for recovery, but cannot overlap a login/download.
        bool recovery = args.Length > 1 && args[0] == "auth" && (args[1] == "info" || args[1] == "revoke");
        using (guard.Enter(false, ct, recovery))
        {
            Result result;
            try { result = await inner.RunAsync(args, ct, publish).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                var failure = Describe(error.Message,error);
                if (failure.Code is FailureCode.Network or FailureCode.RateLimited || AccountRequestGuard.IsAccountFailure(failure.Code))
                { guard.RequestFailed(failure); throw new AppleAccountException(failure); }
                throw;
            }
            var described = Describe(result.Output);
            bool shared = described.Code is FailureCode.Network or FailureCode.RateLimited || AccountRequestGuard.IsAccountFailure(described.Code);
            bool failed = result.Code != 0;
            try { failed |= Object.Equals(ToolResponse.Get(ToolResponse.Object(result.Output), "success"), false); }
            catch { /* Some commands return a catalogue rather than JSON. */ }
            if (failed && shared)
            {
                guard.RequestFailed(described, RetryAfter(result.Output));
                throw new AppleAccountException(described);
            }
            if (failed && result.Code == 0) result = new Result { Code=1, Output=result.Output };
            if (!failed && !recovery) guard.RequestSucceeded();
            return result;
        }
    }
}
