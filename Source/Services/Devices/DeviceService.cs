using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Фасад инструментов iPhone. Обнаружение, команды и установка разделены и доступны для независимых проверок.</summary>
    public sealed class DeviceService
    {
        readonly DeviceCommandClient client; readonly DeviceDiscovery discovery; readonly DeviceInstallation installation;
        Action<string> log;
        public Action<string> Log
        {
            get { return log; }
            set { log = value ?? delegate { }; discovery.Log = log; installation.Log = log; }
        }
        public DeviceService(IProcessRunner runner, ToolLocator locator)
        {
            client = new DeviceCommandClient(runner, locator); discovery = new DeviceDiscovery(client); installation = new DeviceInstallation(client); Log = delegate { };
        }
        public Task<List<DeviceRow>> FindAsync(CancellationToken ct) { return discovery.FindAsync(ct); }
        public Task<List<DeviceRow>> FindAsync(bool includeWifi, CancellationToken ct) { return discovery.FindAsync(includeWifi, ct); }
        public Task<Dictionary<string, string>> ReadDeviceAsync(DeviceRow device, CancellationToken ct) { return client.ReadDeviceAsync(device, ct); }
        public Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct) { return installation.InstallAsync(device, file, ct); }
        public static string[] Arguments(DeviceRow device, params string[] args) { return DeviceCommandClient.Arguments(device, args); }
        public async Task<string> DetailsAsync(DeviceRow device, CancellationToken ct)
        {
            var values = await client.ReadDeviceAsync(device, ct);
            string model, version;
            if (!values.TryGetValue("ProductType", out model)) model = "не удалось прочитать";
            if (!values.TryGetValue("ProductVersion", out version)) version = "не удалось прочитать";
            return "Имя: " + device.Name + Environment.NewLine + "Подключение: " + device.Connection + Environment.NewLine +
                "Модель (ProductType): " + model + Environment.NewLine + "iOS: " + version + Environment.NewLine + "UDID: " + device.ID;
        }
    }
}
