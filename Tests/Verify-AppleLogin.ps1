param()
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.IO.Compression,System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'IPA-Desktop.exe'))|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipa-login-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
try {
 $fixture=Join-Path $temp 'FakeAppleLogin.exe'
 Add-Type -OutputAssembly $fixture -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
public static class FakeAppleLogin {
 [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 public static int Main(string[] args) {
  File.WriteAllText("pid.txt",System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
  File.WriteAllText("stage.txt","redirected="+Console.IsInputRedirected+" visible="+IsWindowVisible(GetConsoleWindow())+" argc="+args.Length);
  if(Console.IsInputRedirected || IsWindowVisible(GetConsoleWindow()))return 41;
  if(args.Length!=4 || String.Join(" ",args)!="auth login --format json")return 42;
  string mode=File.ReadAllText("mode.txt");
  File.WriteAllText("stage.txt","email");Console.Write("\x1b[32mEnter em");Console.Out.Flush();Thread.Sleep(25);Console.Write("ail: \x1b[0m");Console.Out.Flush();
  if(Console.ReadLine()!="fixture@example.com")return 43;
  if(mode=="changed"){Console.Write("Enter 2FA code: ");Console.Out.Flush();Thread.Sleep(20000);return 46;}
  if(mode=="repaint"){Console.Write("\r\x1b[1GEnter email: fixture@example.com\r\n");Console.Out.Flush();}
  File.WriteAllText("stage.txt","password");Console.Write("Enter password: ");Console.Out.Flush();
  var password=new StringBuilder();ConsoleKeyInfo key;
  while((key=Console.ReadKey(true)).Key!=ConsoleKey.Enter){password.Append(key.KeyChar);Console.Write('*');Console.Out.Flush();}
  Console.WriteLine();
  if(password.ToString()!="Fixture-Password\"\\!7")return 44;
  if(mode=="slow"){Thread.Sleep(20000);return 45;}

  if(mode=="huge"){Console.Write(new string('x',100000));Console.Out.Flush();return 47;}
  if(mode=="repaint"){Console.Write("\r\x1b[1GEnter password: ********************\r\n");Console.Out.Flush();}
  if(mode=="2fa" || mode=="reject" || mode=="repaint"){
   Console.Write("Enter 2FA code: ");Console.Out.Flush();
   string code=Console.ReadLine();
   if(code!="123456")return 48;
   if(mode=="repaint"){Console.Write("\r\x1b[1GEnter 2FA code: ******\r\n");Console.Out.Flush();}
   if(mode=="reject"){Console.WriteLine("Login error: password "+password+" token=fixture-token-secret code="+code);return 1;}
  }
  if(mode=="save")Console.WriteLine("Warning: could not save account");
  Console.WriteLine("{\"success\":true,\"email\":\"fixture@example.com\"}");
  return 0;
 }
}
'@
 Copy-Item (Join-Path $projectRoot 'IPA-Desktop.exe') (Join-Path $temp 'IpaLoginTests.dll')
 Add-Type -ReferencedAssemblies @((Join-Path $temp 'IpaLoginTests.dll'),'System.dll','System.Core.dll') -TypeDefinition @'
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
'@
 [LoginContract]::Run($fixture,$temp)
 Write-Host 'PASS: real Windows ConPTY, fragmented/ANSI prompts without newline, repeated full-line redraws and coalesced old/new prompts without resending secrets, private input, exact secret-free arguments, invisible console, 2FA, rejected credentials, excessive output, save failure, stale session rejection and cancellation cleanup'
 # Drive actual modal windows through their controls, including invalid 2FA and retry.
 $engine=New-Object IpaDesktop.Engine((Join-Path $temp 'engine'))
 $engine.Settings.CheckGuiUpdates=$false;$engine.Settings.AutoUpdate=$false;$engine.Settings.AutoUpdateDeviceTools=$false;$engine.Settings.SoundsEnabled=$false
 $desktop=New-Object IpaDesktop.Desktop($engine,$false,$projectRoot)
 $owner=$desktop.Window;$owner.Show()
 function Pump {$owner.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle);$owner.UpdateLayout()}
 function Press($control){$control.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))}
 foreach($cancel in @($true,$false)) {
  $service=New-Object WindowLoginService;$service.CancelOnly=$cancel;$engine.AppleLogin=$service
  $script:phase=0;$script:uiError=$null
  $timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds(50)
  $clock=[Diagnostics.Stopwatch]::StartNew()
  $timer.Add_Tick({
   try {
    if($clock.Elapsed.TotalSeconds -gt 12){throw 'Login modal test timed out'}
    $dialog=@($owner.OwnedWindows|Where-Object Title -eq 'Вход в Apple Account')[0]
    if(!$dialog){return}
    $email=$dialog.FindName('AppleLoginEmail');$password=$dialog.FindName('AppleLoginPassword');$code=$dialog.FindName('AppleLoginCode')
    $submit=$dialog.FindName('AppleLoginSubmit');$status=$dialog.FindName('AppleLoginStatus')
    if($script:phase -eq 0){
     $email.Text='fixture@example.com';$password.Password='Fixture-Password"\!7';$script:phase=1;Press $submit
     if($password.Password.Length -ne 0){throw 'Password box retained password after submission'}
    }elseif(!$cancel -and $script:phase -eq 1 -and $submit.IsEnabled -and $submit.Content -eq 'Войти'){
     if(!$status.Text.Contains('Повтор') -and !$status.Text.Contains('повтор')){throw 'Login failure is not visible inline'}
     $password.Password='Fixture-Password"\!7';$script:phase=2;Press $submit
    }elseif($code.IsVisible -and $submit.IsEnabled){
     if($cancel){$script:phase=3;Press ($dialog.FindName('AppleLoginCancel'))}
     else {
      $dialog.UpdateLayout()
      $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$dialog.ActualWidth,[int]$dialog.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
      $bitmap.Render($dialog);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
      $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=New-Object IO.MemoryStream
      $encoder.Save($stream);Write-Host ('IPA_LOGIN_PREVIEW:'+ [Convert]::ToBase64String($stream.ToArray()));$stream.Dispose()
      $code.Password='12';Press $submit
      if(!$status.Text.Contains('шесть')){throw 'Invalid 2FA code was submitted'}
      $code.Password='123456';$script:phase=3;Press $submit
      if($code.Password.Length -ne 0){throw '2FA code remained in PasswordBox after submission'}
     }
    }
   }catch{$script:uiError=$_;$timer.Stop();foreach($dialog in @($owner.OwnedWindows)){Press ($dialog.FindName('AppleLoginCancel'))}}
  })
  $timer.Start()
  try {Press ($owner.FindName('Login'))}finally{$timer.Stop()}
  Pump
  if($script:uiError){throw $script:uiError}
  if($script:phase -ne 3){throw 'Modal login did not reach the code step'}
  if($cancel -and !$owner.FindName('Login').IsEnabled){throw 'Cancel left login disabled'}
  if(!$cancel -and ($owner.FindName('Login').IsEnabled -or !$owner.FindName('Logout').IsEnabled -or $owner.FindName('AccountLabel').Text -ne 'fixture@example.com')){throw 'Successful modal login did not update the account controls'}
 }
 $owner.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
 $journal=$owner.FindName('Journal').Text
 if($journal.Contains('Fixture-Password') -or $journal.Contains('123456') -or $journal.Contains('fixture-token-secret')){throw 'Login secret leaked into journal'}
 Write-Host 'PASS: actual WPF login modal, inline failure/retry, masked password/code clearing, invalid code validation, modal cancel, signed-in controls and journal privacy'
} finally {
 if($owner){$owner.Close()}
 if(Test-Path $temp){Remove-Item $temp -Recurse -Force}
}
