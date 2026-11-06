using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

/// <summary>Isolated subscription process: closes stdin to unsubscribe, then bounds shutdown.</summary>
public sealed class DeviceEventMonitor : IDeviceEvents
{
    readonly ToolLocator locator;
    public DeviceEventMonitor(ToolLocator locator) => this.locator = locator;
    public async Task WatchAsync(Action changed, Action<bool> status, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try { await WatchOnce(changed, status, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception) { /* Missing/old tools or stopped usbmuxd: use polling and retry later. */ }
            finally { status(false); }
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
        }
    }
    async Task WatchOnce(Action changed, Action<bool> status, CancellationToken cancellation)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(locator.Find("ipa-device-events.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        process.Start();
        var errors = process.StandardError.BaseStream.CopyToAsync(System.IO.Stream.Null);
        try
        {
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            readyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            bool ready = false;
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(ready ? cancellation : readyTimeout.Token);
                if (line == null) break;
                if (line == "ready") { ready = true; status(true); changed(); }
                else if (line == "changed") changed();
            }
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (System.IO.IOException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); }
            await errors;
        }
    }
}
