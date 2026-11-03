using System;
using System.IO;
using System.Text;
using System.Security;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace IpaDesktop;
public sealed class UnixLoginFactory : ILoginProcessFactory
{
    readonly Func<Dictionary<string,string>> environment;
    public UnixLoginFactory(Func<Dictionary<string,string>> environment=null)=>this.environment=environment;
    public ILoginProcess Start(string executable,string workingDirectory)=>new UnixLoginProcess(executable,workingDirectory,environment?.Invoke());
}
/// <summary>Private PTY managed by a small native launcher; credentials only pass over stdin.</summary>
public sealed class UnixLoginProcess : ILoginProcess
{
    readonly Process process;
    public UnixLoginProcess(string executable,string directory,Dictionary<string,string> environment=null)
    {
        var helper=Path.Combine(AppContext.BaseDirectory,"native","ipa-pty");
        var info=new ProcessStartInfo(helper){WorkingDirectory=directory,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        info.ArgumentList.Add(Path.GetFullPath(executable));
        if(environment!=null)foreach(var pair in environment){if(pair.Value==null)info.Environment.Remove(pair.Key);else info.Environment[pair.Key]=pair.Value;}
        process=Process.Start(info)??throw new IOException("Не запущен приватный терминал входа.");
        // The helper writes no credentials to stderr; drain without forwarding to the journal.
        _=process.StandardError.ReadToEndAsync();
    }
    public async Task<string> ReadAsync(CancellationToken ct)
    {
        var chars=new char[1024];var count=await process.StandardOutput.ReadAsync(chars.AsMemory(),ct).ConfigureAwait(false);
        if(count>0){var text=new string(chars,0,count);Array.Clear(chars);return text;}
        await process.WaitForExitAsync(ct).ConfigureAwait(false);return null;
    }
    public Task SendAsync(SecureString value,CancellationToken ct)=>Send(value,ct);
    async Task Send(SecureString value,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var inputValue=new LoginInput(value,'\n');
        await process.StandardInput.BaseStream.WriteAsync(inputValue.Bytes,ct).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
    }

    public int ExitCode=>process.ExitCode;
    public void Dispose(){try{if(!process.HasExited){process.StandardInput.Close();if(!process.WaitForExit(2500)){process.Kill(true);process.WaitForExit(1000);}}}catch(InvalidOperationException){}finally{process.Dispose();}}
}
