using Xunit;
namespace IpaDesktop.Tests;

public sealed class AccountProtectionTests
{
    [Fact] public void ConcurrentCopiesAndRestartCannotBypassLoginPause()
    {
        using var folder = new TempFolder(); var now=DateTimeOffset.UtcNow;
        var guard=new AccountRequestGuard(folder.Path,()=>now);
        using(var lease=guard.Enter(true,TestContext.Current.CancellationToken))
        {
            Assert.Throws<AppleAccountException>(()=>new AccountRequestGuard(folder.Path,()=>now).Enter(true,TestContext.Current.CancellationToken));
            guard.LoginStarted();
            guard.LoginFailed(ErrorLocalizer.Describe("incorrect password"));
        }
        Assert.True(new AccountRequestGuard(folder.Path).RequiresLogin);
        Assert.Throws<AppleAccountException>(()=>guard.Enter(true,TestContext.Current.CancellationToken));
        using(guard.Enter(false,TestContext.Current.CancellationToken,true)) { } // Inspection/logout remains available.
        now=now.AddSeconds(61);
        using(guard.Enter(true,TestContext.Current.CancellationToken)) { guard.LoginStarted(); guard.LoginSucceeded(); }
        Assert.False(guard.RequiresLogin);
        using(guard.Enter(false,TestContext.Current.CancellationToken)) { }
        Assert.DoesNotContain("password",File.ReadAllText(Path.Combine(folder.Path,"account-requests.txt")),StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("purchase error: your password has changed",FailureCode.PasswordChanged)]
    [InlineData("This Apple Account has been locked for security reasons",FailureCode.AccountRestricted)]
    [InlineData("authentication failed",FailureCode.Credentials)]
    [InlineData("terms and conditions",FailureCode.Terms)]
    public async Task AccountFailureStopsQueueAndInfoCannotEnableOldSession(string message,FailureCode code)
    {
        using var folder = new TempFolder();
        var guard=new AccountRequestGuard(folder.Path);
        var inner=new FakeTools { Response=new Result { Code=1, Output=message } };
        var client=new ProtectedAppToolClient(inner,guard);
        var completed=new List<int>(); var failed=new List<int>();
        var error=await Assert.ThrowsAsync<AppleAccountException>(()=>BatchRunner.Run(new[]{1,2,3},
            async (item,ct)=>ToolResponse.Ensure(await client.RunAsync(new[]{"download","-i",item.ToString()},ct,true)),
            (_,_)=>{},completed.Add,(item,_)=>failed.Add(item),null,TestContext.Current.CancellationToken));
        Assert.Equal(code,error.Failure.Code); Assert.Empty(completed); Assert.Equal(new[]{1},failed); Assert.Equal(1,inner.Calls);
        inner.Response=new Result { Code=0, Output="{\"success\":true,\"email\":\"fixture@example.com\"}" };
        await client.RunAsync(new[]{"auth","info"},TestContext.Current.CancellationToken,false);
        Assert.True(guard.RequiresLogin);
        await Assert.ThrowsAsync<AppleAccountException>(()=>client.RunAsync(new[]{"download","-i","2"},TestContext.Current.CancellationToken,true));
        Assert.Equal(2,inner.Calls);
    }

    [Fact] public async Task ServerPauseIsHonoredWithoutAnyAutomaticRetry()
    {
        using var folder=new TempFolder();var now=DateTimeOffset.UtcNow;
        var guard=new AccountRequestGuard(folder.Path,()=>now);
        var inner=new FakeTools { Response=new Result { Code=0,Output="{\"success\":false,\"error\":\"HTTP 429 too many requests\",\"Retry-After\":120}" } };
        var client=new ProtectedAppToolClient(inner,guard);
        await Assert.ThrowsAsync<AppleAccountException>(()=>client.RunAsync(new[]{"download"},TestContext.Current.CancellationToken,true));
        Assert.False(guard.RequiresLogin);
        now=now.AddSeconds(119);
        await Assert.ThrowsAsync<AppleAccountException>(()=>client.RunAsync(new[]{"download"},TestContext.Current.CancellationToken,true));
        Assert.Equal(1,inner.Calls);
        now=now.AddSeconds(2);inner.Response=new Result{Code=0,Output="{\"success\":true}"};
        await client.RunAsync(new[]{"download"},TestContext.Current.CancellationToken,true);
        Assert.Equal(2,inner.Calls);
    }

    [Fact] public async Task UnavailableApplicationDoesNotBlockAccountOrOtherItems()
    {
        using var folder=new TempFolder();var guard=new AccountRequestGuard(folder.Path);
        var inner=new FakeTools{Response=new Result{Code=1,Output="invalid response: empty songlist"}};
        var client=new ProtectedAppToolClient(inner,guard);
        var result=await BatchRunner.Run(new[]{1,2},async (_,ct)=>ToolResponse.Ensure(await client.RunAsync(new[]{"download"},ct,true)),
            (_,_)=>{},_=>{},(_,_)=>{},null,TestContext.Current.CancellationToken);
        Assert.Equal(2,result.Failed.Count);Assert.Equal(2,inner.Calls);Assert.False(guard.RequiresLogin);
    }

    [Fact] public async Task RejectedLoginIsNotRelaunchedByRepeatedSubmission()
    {
        using var folder=new TempFolder();var now=DateTimeOffset.UtcNow;
        var guard=new AccountRequestGuard(folder.Path,()=>now);var factory=new RejectLoginFactory();
        var tools=new FakeTools();
        var service=new AppleLoginService(factory,()=>"fixture",folder.Path,tools,guard);
        using var password=LoginContract.Secret("fixture-password");
        await Assert.ThrowsAsync<AppleLoginException>(()=>service.LoginAsync("fixture@example.com",password,_=>throw new Exception("Unexpected code request"),TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<AppleAccountException>(()=>service.LoginAsync("fixture@example.com",password,_=>throw new Exception("Unexpected code request"),TestContext.Current.CancellationToken));
        Assert.Equal(1,factory.Calls);Assert.Equal(0,tools.Calls);Assert.True(guard.RequiresLogin);
        now=now.AddSeconds(61);
        await Assert.ThrowsAsync<AppleLoginException>(()=>service.LoginAsync("fixture@example.com",password,_=>throw new Exception("Unexpected code request"),TestContext.Current.CancellationToken));
        now=now.AddSeconds(61); // The second failed attempt requires 120 seconds.
        await Assert.ThrowsAsync<AppleAccountException>(()=>service.LoginAsync("fixture@example.com",password,_=>throw new Exception("Unexpected code request"),TestContext.Current.CancellationToken));
        Assert.Equal(2,factory.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyVerifiedNewLoginRestoresDownloads(bool mismatch)
    {
        using var folder=new TempFolder();var guard=new AccountRequestGuard(folder.Path);
        using(guard.Enter(false,TestContext.Current.CancellationToken)) guard.RequestFailed(ErrorLocalizer.Describe("your password has changed"));
        var factory=new RejectLoginFactory { Success=true };
        var tools=new FakeTools { Response=new Result { Code=0,Output="{\"success\":true,\"email\":\""+(mismatch?"old@example.com":"fixture@example.com")+"\"}" } };
        var service=new AppleLoginService(factory,()=>"fixture",folder.Path,tools,guard);
        using var password=LoginContract.Secret("fixture-password");
        var login=service.LoginAsync("fixture@example.com",password,_=>throw new Exception("Unexpected code request"),TestContext.Current.CancellationToken);
        if(mismatch) await Assert.ThrowsAsync<AppleLoginException>(()=>login);
        else Assert.Equal("fixture@example.com",await login);
        Assert.Equal(mismatch,guard.RequiresLogin); Assert.Equal(1,factory.Calls); Assert.Equal(1,tools.Calls);
    }
    sealed class RejectLoginFactory : ILoginProcessFactory
    {
        public int Calls; public bool Success;
        public ILoginProcess Start(string executable,string directory) { Calls++;return new Session(Success); }
        sealed class Session : ILoginProcess
        {
            readonly bool success;
            public Session(bool success) { this.success=success; }
            int index;public int ExitCode=>success?0:1;
            public Task<string> ReadAsync(CancellationToken ct)=>Task.FromResult(index++ switch {0=>"Enter email:",1=>"Enter password:",2=>success?"{\"success\":true,\"email\":\"fixture@example.com\"}":"incorrect password",_=>null});
            public Task SendAsync(System.Security.SecureString value,CancellationToken ct)=>Task.CompletedTask;
            public void Dispose() { }
        }
    }

    [Fact] public void PasswordFailuresAcrossAccountsAndSuccessfulLoginsTriggerGlobalPause()
    {
        using var folder=new TempFolder();var now=DateTimeOffset.UtcNow;
        var guard=new AccountRequestGuard(folder.Path,()=>now);
        for(int index=0;index<3;index++)
        {
            using(guard.Enter(true,TestContext.Current.CancellationToken))
            { guard.LoginStarted();guard.LoginFailed(ErrorLocalizer.Describe("incorrect password")); }
            now=now.AddMinutes(16);
            if(index<2) using(guard.Enter(true,TestContext.Current.CancellationToken)) guard.LoginSucceeded();
        }
        // A third refusal creates a 30-minute pause; 16 minutes is insufficient, even on a new instance/account.
        var restarted=new AccountRequestGuard(folder.Path,()=>now);
        Assert.Throws<AppleAccountException>(()=>restarted.Enter(true,TestContext.Current.CancellationToken));
        restarted.AcknowledgeRecovery(TestContext.Current.CancellationToken); // Review must not reset the timer/counter.
        Assert.Throws<AppleAccountException>(()=>restarted.Enter(true,TestContext.Current.CancellationToken));
        now=now.AddMinutes(15);
        using(restarted.Enter(true,TestContext.Current.CancellationToken)) restarted.LoginSucceeded();
        Assert.False(restarted.RequiresLogin);
    }
    [Fact] public void ExplicitAccountLockStopsEveryAccountUntilReviewedAndPauseExpires()
    {
        using var folder=new TempFolder();var now=DateTimeOffset.UtcNow;var guard=new AccountRequestGuard(folder.Path,()=>now);
        using(guard.Enter(true,TestContext.Current.CancellationToken))
        { guard.LoginStarted();guard.LoginFailed(ErrorLocalizer.Describe("account is locked")); }
        now=now.AddDays(2);
        var restarted=new AccountRequestGuard(folder.Path,()=>now);
        Assert.True(restarted.SafetyStopped);
        Assert.Throws<AppleAccountException>(()=>restarted.Enter(true,TestContext.Current.CancellationToken));
        using(restarted.Enter(false,TestContext.Current.CancellationToken,true)) { }
        restarted.AcknowledgeRecovery(TestContext.Current.CancellationToken);
        Assert.False(restarted.SafetyStopped);
        Assert.True(restarted.RequiresLogin); // Old session is still blocked until a verified new login.
        using(restarted.Enter(true,TestContext.Current.CancellationToken)) restarted.LoginSucceeded();
        Assert.False(restarted.RequiresLogin);
    }
    [Fact] public void OldGuardStateMigratesAndUnknownAuthResponseIsNotIncorrectPassword()
    {
        using var folder=new TempFolder();
        File.WriteAllLines(Path.Combine(folder.Path,"account-requests.txt"),new[]{"AccountRestricted","0","0","1","0"});
        var guard=new AccountRequestGuard(folder.Path);
        Assert.True(guard.SafetyStopped);
        var protocol=new AppleLoginProtocol();protocol.Append("Enter email:");protocol.Append("Enter password:");
        protocol.Append("IPA_AUTH_STATUS http=403\nIPA_AUTH_FAILURE code=5005\nsomething went wrong");
        var error=Assert.Throws<AppleLoginException>(()=>protocol.Complete(1));
        Assert.Equal(FailureCode.Unknown,error.Failure.Code);
        Assert.Equal("Ответ Apple: HTTP 403; код отказа 5005.",protocol.Diagnostics);
    }
    [Fact] public async Task OldOrReplacedEngineCannotReceiveAccountCommands()
    {
        using var folder=new TempFolder();var exe=Path.Combine(folder.Path,"ipatool.exe");File.WriteAllText(exe,"old");
        var runner=new ProfileRunner();var policy=new GuardedEnginePolicy(runner,()=>exe);
        await Assert.ThrowsAsync<AppleAccountException>(()=>policy.RequireAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[]{"account-safety-info"},runner.Commands);
        runner.Valid=true;
        await policy.RequireAsync(TestContext.Current.CancellationToken);
        await policy.RequireAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2,runner.Commands.Count);
        File.WriteAllText(exe,"rolled back");runner.Valid=false;
        await Assert.ThrowsAsync<AppleAccountException>(()=>policy.RequireAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3,runner.Commands.Count);
    }
    sealed class ProfileRunner : IProcessRunner
    {
        public bool Valid; public List<string> Commands=new();
        public Action<string> Log{get;set;} public System.Diagnostics.Process Active=>null;
        public Task<Result> Run(string executable,string[] arguments,CancellationToken ct,bool visible,bool publish,string workingDirectory=null)
        {
            ct.ThrowIfCancellationRequested();Assert.False(publish);Commands.Add(String.Join(" ",arguments));
            return Task.FromResult(new Result{Code=Valid?0:1,Output=Valid?GuardedEnginePolicy.Profile:"unknown command"});
        }
    }

    sealed class FakeTools : IAppToolClient
    {
        public int Calls; public Result Response;
        public Task<Result> RunAsync(string[] args,CancellationToken ct,bool publish) { ct.ThrowIfCancellationRequested(); Calls++;return Task.FromResult(Response); }
    }
}
