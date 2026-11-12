using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed partial class Engine
{
    public DesktopServices CreateDesktopServices() => new(
        settings, new WorkspacePaths(Root, Downloads), new AccountSession(tools), AccountRequests,
        new LoginAdapter(() => AppleLogin), tools, catalog, AppVersions, Purchases, ApplicationDownloads,
        LocalFiles, AppSets, Devices, AppleSupport, Network, updater, new GuiUpdateAdapter(), new DesktopEvents(this), archiveCache);

    sealed class AccountSession : IAccountSession
    {
        readonly IAppToolClient tools;
        public AccountSession(IAppToolClient tools) => this.tools = tools;
        public async Task<string> GetEmailAsync(CancellationToken cancellation)
        {
            var result = await tools.RunAsync(new[] { "auth", "info" }, cancellation, false); ToolResponse.Ensure(result);
            var data = ToolResponse.Object(result.Output); var email = Convert.ToString(ToolResponse.Get(data, "email"));
            if (String.IsNullOrWhiteSpace(email) || !Equals(ToolResponse.Get(data, "success"), true))
                throw new InvalidOperationException("Не удалось подтвердить сохранённый вход в аккаунт.");
            return email;
        }
        public async Task RevokeAsync(CancellationToken cancellation) => ToolResponse.Ensure(await tools.RunAsync(new[] { "auth", "revoke" }, cancellation, false));
    }
    sealed class LoginAdapter : IAppleLoginService
    {
        readonly Func<IAppleLoginService> service;
        public LoginAdapter(Func<IAppleLoginService> service) => this.service = service;
        public Task<string> LoginAsync(string email, System.Security.SecureString password, Func<CancellationToken, Task<System.Security.SecureString>> requestCode, CancellationToken cancellation) => service().LoginAsync(email, password, requestCode, cancellation);
    }
    sealed class GuiUpdateAdapter : IGuiUpdates
    {
        public Task<Release> Check(CancellationToken cancellation) => GuiUpdater.Check(cancellation);
        public Task<string> Prepare(Release release, CancellationToken cancellation, Action<double> progress) => GuiUpdater.Prepare(release, cancellation, progress);
        public void Launch(string stage) => GuiUpdater.Launch(stage);
    }
    sealed class DesktopEvents : IApplicationEvents
    {
        readonly Engine engine;
        public DesktopEvents(Engine engine) => this.engine = engine;
        public IDisposable Subscribe(Action<string> log, Action<double> progress)
        {
            var oldLog = engine.Log; var oldProgress = engine.Progress;
            engine.Log = log; engine.Progress = progress;
            return new Subscription(() => { if (engine.Log == log) engine.Log = oldLog; if (engine.Progress == progress) engine.Progress = oldProgress; });
        }
    }
    sealed class Subscription : IDisposable
    {
        Action unsubscribe;
        public Subscription(Action unsubscribe) => this.unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
    }
}
