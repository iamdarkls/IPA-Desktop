using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Единая обвязка CLI: транспорт, ожидание ответа, идентификация устройства и запуск без скрытых повторов.</summary>
    public sealed class DeviceCommandClient
    {
        readonly IProcessRunner runner; readonly ToolLocator locator;
        public DeviceCommandClient(IProcessRunner runner, ToolLocator locator) { this.runner = runner; this.locator = locator; }
        public Task<Result> ListAsync(bool network, CancellationToken ct) { return Probe(locator.Find("idevice_id*.exe"), new[] { network ? "-n" : "-l" }, ct); }
        public Task<Result> NameAsync(DeviceRow device, CancellationToken ct, int seconds = 5) { return Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", device.ID, "-k", "DeviceName"), ct, seconds); }
        public async Task InstallAsync(DeviceRow device, InstallationPackage package, CancellationToken ct)
        {
            ToolResponse.Ensure(await runner.Run(locator.Find("ideviceinstaller*.exe"), Arguments(device, "--udid", device.ID, "install", package.RelativePath), ct, false, true, package.WorkingDirectory));
        }
        public static string[] Arguments(DeviceRow device, params string[] args)
        {
            if (device == null || !System.Text.RegularExpressions.Regex.IsMatch(device.ID ?? "", "^[0-9A-Fa-f-]{20,}$")) throw new IOException("Некорректный идентификатор iPhone.");
            return device.Network ? new[] { "--network" }.Concat(args).ToArray() : args;
        }
        async Task<Result> Probe(string exe, string[] args, CancellationToken ct, int seconds = 15)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                try { var result = await runner.Run(exe, args, timeout.Token, false, false); ToolResponse.Ensure(result); return result; }
                catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new IOException("iPhone не ответил за " + seconds + " секунд. Разблокируйте телефон; проверьте USB либо Wi-Fi и доверие компьютеру.");
                }
            }
        }
        public async Task<Dictionary<string, string>> ReadDeviceAsync(DeviceRow device, CancellationToken ct)
        {
            var result = await Probe(locator.Find("ideviceinfo*.exe"), Arguments(device, "-u", device.ID), ct);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in result.Output.Split('\n'))
            {
                int split = line.IndexOf(':');
                if (split > 0) values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
            string actual;
            if (values.TryGetValue("UniqueDeviceID", out actual) && !String.Equals(actual, device.ID, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ответил другой iPhone. Повторите поиск устройств.");
            if (device.Network && (!values.TryGetValue("ProductType", out actual) || !actual.StartsWith("iPhone", StringComparison.Ordinal)))
                throw new IOException("Не удалось подтвердить связь с выбранным iPhone по Wi-Fi. Разблокируйте телефон и проверьте сетевую синхронизацию.");
            return values;
        }
    }
}
