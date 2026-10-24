using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using IpaDesktop;
public sealed class RecordingLoginFactory : ILoginProcessFactory {
 public string Error="none";
 public ILoginProcess Start(string exe,string folder){try{return new ConPtyLoginFactory().Start(exe,folder);}catch(Exception e){Error=e.GetType().Name+":"+e.Message;throw;}}
}
public sealed class LoginTools : IAppToolClient {
 public bool Mismatch;public int Calls;
 public Task<Result> RunAsync(string[] args,CancellationToken ct,bool publish){
  ct.ThrowIfCancellationRequested();Calls++;
  if(publish || String.Join(" ",args)!="auth info")throw new Exception("Unexpected published auth command");
  return Task.FromResult(new Result{Code=0,Output="{\"success\":true,\"email\":\""+(Mismatch?"old@example.com":"fixture@example.com")+"\"}"});
 }
}
public static class LoginContract {
 public static SecureString Secret(string value){var secret=new SecureString();foreach(char c in value)secret.AppendChar(c);secret.MakeReadOnly();return secret;}
 public static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(string exe,string folder){
  var protocol=new AppleLoginProtocol();
  Assert(protocol.Append("Enter em")==AppleLoginPrompt.None,"Fragmented prompt was completed early");
  Assert(protocol.Append("ail: ")==AppleLoginPrompt.Identifier,"Email request missing");
  Assert(protocol.Append("\r\x1b[1GEnter email: fixture@example.com")==AppleLoginPrompt.None,"Email redraw requested another input");
  Assert(protocol.Append("\rEnter email: fixture@example.com\r\nEnter password: ")==AppleLoginPrompt.Password,"Old email redraw hid the next password prompt");
  for(int i=0;i<200;i++)Assert(protocol.Append("\r\x1b[1GEnter password: "+new String('*',500))==AppleLoginPrompt.None,"Password redraw resent the secret");
  Assert(protocol.Append("\rEnter password: ******\r\nEnter 2FA code: ")==AppleLoginPrompt.Code,"Password redraw hid the code prompt");
  Assert(protocol.Append("\rEnter 2FA code: ******")==AppleLoginPrompt.None,"2FA redraw resent the code");
  protocol.Append("\r\n{\"success\":true,\"email\":\"fixture@example.com\"}\r\n");
  Assert(protocol.Complete(0)=="fixture@example.com","Redraw consumed the login result");
  protocol=new AppleLoginProtocol();
  Assert(protocol.Append("\x1b[3")==AppleLoginPrompt.None,"Partial ANSI was treated as a prompt");
  Assert(protocol.Append("2mENTER\tE-MAIL : \x1b[0m")==AppleLoginPrompt.Identifier,"Case/spacing/ANSI changed identifier recognition");
  Assert(protocol.Append("\rENTER E-MAIL : fixture@example.com\nEnter  pass")==AppleLoginPrompt.None,"Redraw or incomplete password resent input");
  Assert(protocol.Append("word : ")==AppleLoginPrompt.Password,"Fragmented password variant missing");
  Assert(protocol.Append("ENTER PASSWORD : ******\nEnter verification code : ")==AppleLoginPrompt.Code,"Verification code variant missing");
  Assert(protocol.Append("Enter verification code : ******\n{\"success\":true,\"email\":\"fixture@example.com\"}")==AppleLoginPrompt.None,"Code redraw resent secret");
  Assert(protocol.Complete(0)=="fixture@example.com","Variant redraw lost success response");
  protocol=new AppleLoginProtocol();bool outOfOrder=false;
  try{protocol.Append("ENTER PASSWORD : ");}catch(AppleLoginException){outOfOrder=true;}
  Assert(outOfOrder,"Variant prompt bypassed stage validation");
  protocol=new AppleLoginProtocol();
  Assert(protocol.Append("Enter Apple Account:")==AppleLoginPrompt.Identifier,"Apple Account identifier variant missing");
  protocol.Append("Enter password:");
  protocol.Append("{\"success\":true,\"email\":\"fixture@example.com\"}");
  Assert(protocol.Complete(0)=="fixture@example.com","Account variant could not complete");

  foreach(string mode in new[]{"success","2fa","repaint","reject","save","changed","huge","slow","mismatch"}){
   File.WriteAllText(Path.Combine(folder,"mode.txt"),mode=="mismatch"?"success":mode);
   var tools=new LoginTools{Mismatch=mode=="mismatch"};int codes=0;bool cancelled=false,failed=false;
   var factory=new RecordingLoginFactory();var service=new AppleLoginService(factory,delegate{return exe;},folder,tools);
   using(var stop=new CancellationTokenSource())using(var password=Secret("Fixture-Password\"\\!7")){
    stop.CancelAfter(mode=="slow"?1800:12000);
    try{
     string email=service.LoginAsync("fixture@example.com",password,delegate(CancellationToken ct){codes++;return Task.FromResult(Secret("123456"));},stop.Token).GetAwaiter().GetResult();
     Assert(email=="fixture@example.com","Confirmed account changed");
    }
    catch(OperationCanceledException){cancelled=true;}
    catch(AppleLoginException e){failed=true;Assert(!e.ToString().Contains("Fixture-Password")&&!e.ToString().Contains("123456")&&!e.ToString().Contains("fixture-token-secret"),"Raw secret escaped into login exception");}
    Assert(mode=="slow"?cancelled:!cancelled,"Cancellation/timeout behavior changed: "+mode);
    Assert(mode=="success"||mode=="2fa"||mode=="repaint"?!failed:mode=="slow"||failed,"Unexpected login outcome: "+mode+" stage="+(File.Exists(Path.Combine(folder,"stage.txt"))?File.ReadAllText(Path.Combine(folder,"stage.txt")):factory.Error));
    Assert(codes==((mode=="2fa"||mode=="reject"||mode=="repaint")?1:0),"Unexpected/repeated 2FA request: "+mode);
    Assert(tools.Calls==((mode=="success"||mode=="2fa"||mode=="repaint"||mode=="mismatch")?1:0),"Session verification was skipped or attempted after failure: "+mode);
   }
   int pid=Int32.Parse(File.ReadAllText(Path.Combine(folder,"pid.txt")));bool exited=false;
   try{using(var process=Process.GetProcessById(pid))exited=process.HasExited;}catch(ArgumentException){exited=true;}
   Assert(exited,"Login left a running child process: "+mode);
  }
  // A cancelled code prompt also terminates the child without sending a code.
  File.WriteAllText(Path.Combine(folder,"mode.txt"),"2fa");
  var finalTools=new LoginTools();var cancelService=new AppleLoginService(new ConPtyLoginFactory(),delegate{return exe;},folder,finalTools);
  using(var stop=new CancellationTokenSource())using(var password=Secret("Fixture-Password\"\\!7")){
   stop.CancelAfter(4000);bool cancelled=false;
   try{cancelService.LoginAsync("fixture@example.com",password,async delegate(CancellationToken ct){stop.Cancel();await Task.Delay(1000,ct);return Secret("123456");},stop.Token).GetAwaiter().GetResult();}
   catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled&&finalTools.Calls==0,"2FA cancellation did not stop login");
  }
 }
}
public sealed class WindowLoginService : IAppleLoginService {
 public int Calls;public bool CancelOnly;
 public async Task<string> LoginAsync(string email,SecureString password,Func<CancellationToken,Task<SecureString>> request,CancellationToken ct){
  Calls++;await Task.Delay(30,ct);
  if(!CancelOnly&&Calls==1)throw new AppleLoginException("Проверьте пароль и повторите вход.");
  using(var code=await request(ct)){if(code.Length!=6)throw new Exception("Code length changed");}
  return "fixture@example.com";
 }
}