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

    sealed class FakeTools : IAppToolClient
    {
        public int Calls; public Result Response;
        public Task<Result> RunAsync(string[] args,CancellationToken ct,bool publish) { ct.ThrowIfCancellationRequested(); Calls++;return Task.FromResult(Response); }
    }
}
