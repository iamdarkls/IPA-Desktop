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
            Ensure(await runner.Run(locator.Find("ideviceinstaller*.exe"), Arguments(device, "--udid", device.ID, "install", package.RelativePath), ct, false, true, package.WorkingDirectory));
        }
        static void Ensure(Result result, bool probe = false)
        {
            if(result.Code == 0) return;
            var failure=ErrorLocalizer.Describe(result.Output);
            if(probe && !failure.Connection) failure = new(FailureCode.Transport,"iPhone не подтвердил соединение","Проверка устройства завершилась ошибкой.","Разблокируйте выбранный iPhone, проверьте USB и доверие, затем нажмите «Проверить связь». Технический ответ доступен в журнале.");
            if(failure.Connection) throw new DeviceConnectionException(failure,new IOException(result.Output));
            ToolResponse.Ensure(result);
        }
        public Task<Result> PairAsync(DeviceRow device, string command, CancellationToken ct)
        {
            // idevicepair on stable builds pairs iPhones through USB. Never select a different transport/device.
            if(device.Network) throw new DeviceConnectionException(ErrorLocalizer.Describe("Pairing is not possible over this connection"));
            if(command != "validate" && command != "pair" && command != "unpair") throw new ArgumentException("Invalid pairing command");
            return Probe(locator.Find("idevicepair*.exe"), Arguments(device,"-u",device.ID,command),ct,15);
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
                try { var result = await runner.Run(exe, args, timeout.Token, false, false); Ensure(result,true); return result; }
                catch (OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new DeviceConnectionException(ErrorLocalizer.Describe("iPhone не ответил за " + seconds + " секунд"));
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
            if (!values.TryGetValue("UniqueDeviceID", out actual) || !String.Equals(actual, device.ID, StringComparison.OrdinalIgnoreCase)) throw new DeviceConnectionException(new(FailureCode.Transport,"Не удалось подтвердить выбранный iPhone","Ответ устройства не содержит ожидаемый UDID.","Переподключите выбранный iPhone и повторите проверку связи. Установка на другое устройство не выполнялась."));
            if (device.Network && (!values.TryGetValue("ProductType", out actual) || !actual.StartsWith("iPhone", StringComparison.Ordinal)))
                throw new DeviceConnectionException(ErrorLocalizer.Describe("не удалось подтвердить связь"));
            return values;
        }
    }
}
