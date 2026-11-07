using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

/// <summary>The view owns secret text; the model only receives disposable SecureString input.</summary>
public interface ILoginInput
{
    SecureString TakePassword();
    SecureString TakeCode();
    void Clear();
    void FocusPassword();
    void FocusCode();
}
public sealed class AppleLoginViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    readonly IAppleLoginService service;
    readonly ILoginInput input;
    readonly Action<Action> post;
    readonly BackgroundTasks tasks;
    CancellationTokenSource operation;
    TaskCompletionSource<SecureString> codeSource;
    string email = "", status = "Введите данные для входа.";
    bool loading, closing, codeVisible, submitEnabled = true, cancelEnabled = true;
    public string Email { get => email; set => Set(ref email, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string SignedInEmail { get; private set; }
    public bool IsLoading => loading;
    public bool CredentialsVisible => !codeVisible;
    public bool CredentialsEnabled => !loading;
    public bool CodeVisible => codeVisible;
    public bool CodeEnabled => codeVisible && submitEnabled;
    public bool SubmitEnabled => submitEnabled;
    public bool CancelEnabled => cancelEnabled;
    public string SubmitLabel => codeVisible ? "Подтвердить" : "Войти";
    public DelegateCommand SubmitCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public Task Attempt { get; private set; } = Task.CompletedTask;
    public event Action<bool?> CloseRequested;
    public AppleLoginViewModel(IAppleLoginService service, ILoginInput input, Action<Action> post, CancellationToken cancellation, BackgroundTasks owner = null)
    {
        this.service = service; this.input = input; this.post = post;
        tasks = owner?.CreateChild(_ => Status = "Не удалось завершить вход.", cancellation) ?? new BackgroundTasks(_ => Status = "Не удалось завершить вход.", cancellation);
        SubmitCommand = new(Submit, () => submitEnabled);
        CancelCommand = new(RequestCancel, () => cancelEnabled);
    }
    void Refresh()
    {
        Changed(nameof(IsLoading)); Changed(nameof(CredentialsVisible)); Changed(nameof(CredentialsEnabled)); Changed(nameof(CodeVisible));
        Changed(nameof(CodeEnabled)); Changed(nameof(SubmitEnabled)); Changed(nameof(CancelEnabled)); Changed(nameof(SubmitLabel)); SubmitCommand.Refresh(); CancelCommand.Refresh();
    }
    void Submit()
    {
        if (loading)
        {
            if (codeSource == null || codeSource.Task.IsCompleted) return;
            var code = input.TakeCode();
            if (!ValidCode(code)) { code.Dispose(); Status = "Введите шесть цифр без пробелов."; input.FocusCode(); return; }
            submitEnabled = false; Status = "Проверяем код…"; Refresh();
            if (!codeSource.TrySetResult(code)) code.Dispose();
            return;
        }
        Attempt = tasks.Run(_ => Login());
    }
    async Task Login()
    {
        using var password = input.TakePassword();
        if (String.IsNullOrWhiteSpace(Email) || password.Length == 0) { Status = "Введите Apple Account и пароль."; return; }
        loading = true; submitEnabled = false; operation = CancellationTokenSource.CreateLinkedTokenSource(tasks.Token);
        Status = "Связываемся с Apple…"; Refresh();
        bool success = false;
        try { SignedInEmail = await service.LoginAsync(Email.Trim(), password, RequestCode, operation.Token); success = true; }
        catch (OperationCanceledException) { Status = "Вход отменён."; }
        catch (AppleLoginException error) { Status = error.Message + (error.Failure?.Code == FailureCode.Credentials ? "\nПеред новой попыткой действует защитная пауза." : ""); }
        catch (AppleAccountException error) { Status = error.Message; }
        catch (Exception) { Status = "Не удалось выполнить вход. Повторите попытку; детали авторизации не записываются в журнал."; }
        finally
        {
            loading = false; operation.Dispose(); operation = null;
            codeSource?.TrySetCanceled(); codeSource = null; input.Clear();
            codeVisible = false; submitEnabled = true; Refresh(); input.FocusPassword();
        }
        if (success) CloseRequested?.Invoke(true);
        else if (closing || tasks.IsStopping) CloseRequested?.Invoke(null);
    }
    async Task<SecureString> RequestCode(CancellationToken cancellation)
    {
        var pending = new TaskCompletionSource<SecureString>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellation.Register(() => pending.TrySetCanceled(cancellation)))
        {
            post(() =>
            {
                if (cancellation.IsCancellationRequested) return;
                codeSource = pending; codeVisible = true; submitEnabled = true; input.Clear();
                Status = "Apple запросила код подтверждения."; Refresh(); input.FocusCode();
            });
            return await pending.Task.ConfigureAwait(false);
        }
    }
    static bool ValidCode(SecureString code)
    {
        if (code.Length != 6) return false;
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(code);
        try { for (int i = 0; i < 6; i++) { char c = (char)Marshal.ReadInt16(pointer, i * 2); if (c < '0' || c > '9') return false; } return true; }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }
    void RequestCancel()
    {
        if (!loading) { CloseRequested?.Invoke(null); return; }
        closing = true; submitEnabled = cancelEnabled = false; Status = "Останавливаем вход…"; Refresh(); operation?.Cancel();
    }
    public bool RequestClose() { if (!loading) return true; RequestCancel(); return false; }
    public void Dispose() { tasks.Dispose(); input.Clear(); }
    public async ValueTask DisposeAsync() { Dispose(); await tasks.StopAsync(); }
}
