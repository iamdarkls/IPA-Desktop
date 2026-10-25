using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop;

/// <summary>Old/rolled-back engines cannot receive credentials or run account commands.</summary>
public sealed class GuardedEnginePolicy
{
    public const string Profile = "IPA-DESKTOP-AUTH-SAFETY/1";
    readonly IProcessRunner runner;
    readonly Func<string> executable;
    string verifiedHash;
    public GuardedEnginePolicy(IProcessRunner runner, Func<string> executable) { this.runner=runner; this.executable=executable; }
    static AppleAccountException Missing() => new(new(FailureCode.MissingTool,"Нужен движок с защитой авторизации",
        "Выбранный ipatool не подтвердил отключение скрытых входов и повторов пароля. Запрос к аккаунту не отправлен.",
        "Откройте «Настройки» → обновление движка и установите наш ipatool-guard. Локальные IPA можно устанавливать без входа."));
    public async Task RequireAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string path=executable();
        using var input=File.OpenRead(path);
        var hash=Convert.ToHexString(SHA256.HashData(input));
        if(hash==verifiedHash) return;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Result result;
        try { result=await runner.Run(path,new[]{"account-safety-info"},timeout.Token,false,false).ConfigureAwait(false); }
        catch(OperationCanceledException) { if(ct.IsCancellationRequested) throw; throw Missing(); }
        if(result.Code!=0 || result.Output?.Trim()!=Profile) throw Missing();
        verifiedHash=hash;
    }
}
