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