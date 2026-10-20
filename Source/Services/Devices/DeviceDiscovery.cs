using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Обнаружение USB и Wi-Fi сохраняет независимые результаты и не выполняет параллельные операции с одним runner.</summary>
    public sealed class DeviceDiscovery
    {
        readonly DeviceCommandClient client;
        public Action<string> Log { get; set; }
        public DeviceDiscovery(DeviceCommandClient client) { this.client = client; Log = delegate { }; }
        public Task<List<DeviceRow>> FindAsync(CancellationToken ct) { return FindAsync(false, ct); }
        public async Task<List<DeviceRow>> FindAsync(bool includeWifi, CancellationToken ct)
        {
            var devices = new List<DeviceRow>(); bool usbFailed = false;
            try { devices = await FindTransport(false, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { if (!includeWifi) throw; usbFailed = true; Log("Поиск USB: " + e.Message); }
            if (includeWifi)
            {
                try { devices.AddRange(await FindTransport(true, ct)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { if (usbFailed) throw new IOException("Не удалось проверить USB и Wi-Fi. Проверьте службу Apple и соединение.\n" + e.Message, e); Log("Поиск Wi-Fi: " + e.Message + ". USB-устройства сохранены. Проверьте сетевую синхронизацию в Apple Devices/iTunes."); }
            }
            // Сначала получаем все UDID, затем имена. Не позволяем множеству спящих Wi-Fi-устройств задержать поиск на минуты.
            var names = Stopwatch.StartNew(); bool omitted = false;
            foreach (var device in devices)
            {
                ct.ThrowIfCancellationRequested();
                int remaining = (int)Math.Floor(15 - names.Elapsed.TotalSeconds);
                if (remaining < 1) { omitted = true; continue; }
                try
                {
                    var info = await client.NameAsync(device, ct, Math.Min(5, remaining));
                    string value = info.Output.Trim();
                    if (value.StartsWith("DeviceName:", StringComparison.Ordinal)) value = value.Substring(11).Trim();
                    if (value.Length > 0 && value.Length <= 200 && !value.Any(Char.IsControl)) device.Name = value;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Log("Имя iPhone (" + device.Connection + ") не получено: " + e.Message); }
            }
            if (omitted) Log("Для части устройств показан короткий идентификатор: общий лимит получения имён 15 секунд. Поиск устройств завершён.");
            foreach (var group in devices.GroupBy(x => x.Name + "|" + x.Connection).Where(g => g.Count() > 1))
                foreach (var device in group) device.Name += " · " + device.ID.Substring(Math.Max(0, device.ID.Length - 6));
            return devices;
        }
        async Task<List<DeviceRow>> FindTransport(bool network, CancellationToken ct)
        {
            var result = await client.ListAsync(network, ct);
            var ids = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[0-9A-Fa-f-]{20,}$")).Distinct().ToList();
            var devices = new List<DeviceRow>();
            foreach (var id in ids) devices.Add(new DeviceRow { ID = id, Network = network, Name = "iPhone · " + id.Substring(Math.Max(0, id.Length - 6)) });
            return devices;
        }
    }
}
