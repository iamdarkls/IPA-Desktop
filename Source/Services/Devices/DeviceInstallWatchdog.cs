using System;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop;
/// <summary>Independent presence checks never replace the installer's Active process or change the selected UDID.</summary>
public static class DeviceInstallWatchdog
{
    public static async Task<Result> Run(Func<CancellationToken,Task<Result>> install,
        Func<CancellationToken,Task<bool>> present, CancellationToken ct, TimeSpan? interval=null, TimeSpan? probeTimeout=null)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        DeviceConnectionException lost=null;
        async Task Watch()
        {
            int missing=0;
            try
            {
                while(true)
                {
                    await Task.Delay(interval ?? TimeSpan.FromSeconds(2),stop.Token).ConfigureAwait(false);
                    bool available=false;
                    using(var probe=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                    {
                        probe.CancelAfter(probeTimeout ?? TimeSpan.FromSeconds(5));
                        try { available=await present(probe.Token).ConfigureAwait(false); }
                        catch(OperationCanceledException) when(!stop.IsCancellationRequested) { }
                        catch(Exception) when(!stop.IsCancellationRequested) { }
                    }
                    stop.Token.ThrowIfCancellationRequested();
                    missing=available?0:missing+1;
                    if(missing<2) continue;
                    lost=new DeviceConnectionException(ErrorLocalizer.Describe("No device found"));
                    stop.Cancel();return;
                }
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        }
        var watcher=present == null ? Task.CompletedTask : Watch();
        try
        {
            var result=await install(stop.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if(lost != null) throw lost;
            return result;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested && lost != null) { throw lost; }
        finally { stop.Cancel();await watcher.ConfigureAwait(false); }
    }
}
