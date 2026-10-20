using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Проверяет устройство и исходный IPA до копирования; затем повторно проверяет фактически отправляемый пакет.</summary>
    public sealed class DeviceInstallation
    {
        readonly DeviceCommandClient client;
        public Action<string> Log { get; set; }
        public DeviceInstallation(DeviceCommandClient client) { this.client = client; Log = delegate { }; }
        public async Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct)
        {
            if (!File.Exists(file.Path)) throw new IOException("IPA-файл больше не существует.");
            try
            {
                var source = await Task.Run(() => IpaMetadata.ReadDetails(file.Path), ct);
                string platformError = AppCompatibility.BlockReason(source, null);
                if (platformError != null) throw new IOException(platformError);
                var info = await client.ReadDeviceAsync(device, ct);
                string ios; info.TryGetValue("ProductVersion", out ios);
                string reason = AppCompatibility.BlockReason(source, ios);
                if (reason != null) throw new IOException(reason);
                using (var package = await InstallationPackage.CreateAsync(file.Path, ct))
                {
                    var details = await Task.Run(() => IpaMetadata.ReadDetails(Path.Combine(package.WorkingDirectory, package.RelativePath)), ct);
                    reason = AppCompatibility.BlockReason(details, ios);
                    if (reason != null) throw new IOException(reason);
                    if (String.IsNullOrEmpty(details.MinimumOS) || details.Platforms == null || details.Platforms.Length == 0 || String.IsNullOrEmpty(ios))
                        Log("Совместимость " + file.DisplayName + ": часть метаданных недоступна; окончательную проверку выполнит iPhone.");
                    ct.ThrowIfCancellationRequested();
                    await client.InstallAsync(device, package, ct);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!device.Network) throw;
                throw new IOException("Установка по Wi-Fi не завершена. Проверьте связь и состояние приложения на iPhone. Разблокируйте телефон, проверьте общую сеть и сетевую синхронизацию; при необходимости выберите USB и повторите только ошибки.\n" + e.Message, e);
            }
        }
    }
}
