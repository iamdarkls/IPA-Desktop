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
        readonly DeviceReadiness readiness;
        public Action<string> Log { get; set; }
        public DeviceInstallation(DeviceCommandClient client, DeviceReadiness readiness = null) { this.client = client; this.readiness = readiness ?? new DeviceReadiness(client); Log = delegate { }; }
        public async Task InstallAsync(DeviceRow device, LocalRow file, CancellationToken ct, Action<DeviceConnectionProgress> progress = null)
        {
            if (!File.Exists(file.Path)) throw new IOException("IPA-файл больше не существует.");
            try
            {
                var source = await Task.Run(() => IpaMetadata.ReadDetails(file.Path), ct);
                string platformError = AppCompatibility.BlockReason(source, null);
                if (platformError != null) throw new IOException(platformError);
                var info = await readiness.EnsureAsync(device, progress, ct);
                string ios; info.TryGetValue("ProductVersion", out ios);
                string reason = AppCompatibility.BlockReason(source, ios);
                if (reason != null) throw new IOException(reason);
                await client.CheckInstallationServiceAsync(device, ct);
                var free = await client.FreeSpaceAsync(device, ct);
                using (var archive = System.IO.Compression.ZipFile.OpenRead(file.Path))
                {
                    long required = 0;
                    foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("Payload/", StringComparison.Ordinal))) required = checked(required + entry.Length);
                    if (free.HasValue && (ulong)required > free.Value)
                        throw new IOException("На iPhone недостаточно места: свободно " + DeviceSnapshot.FormatBytes(free.Value) + ", распакованные файлы IPA занимают " + DeviceSnapshot.FormatBytes((ulong)required) + ". Освободите место и повторите установку.");
                    if (!free.HasValue) Log("Свободное место iPhone не удалось прочитать; окончательную проверку выполнит установщик iOS.");
                }
                using (var package = await InstallationPackage.CreateAsync(file.Path, ct))
                {
                    var details = await Task.Run(() => IpaMetadata.ReadDetails(Path.Combine(package.WorkingDirectory, package.RelativePath)), ct);
                    reason = AppCompatibility.BlockReason(details, ios);
                    if (reason != null) throw new IOException(reason);
                    if (String.IsNullOrEmpty(details.MinimumOS) || details.Platforms == null || details.Platforms.Length == 0 || String.IsNullOrEmpty(ios))
                        Log("Совместимость " + file.DisplayName + ": часть метаданных недоступна; окончательную проверку выполнит iPhone.");
                    ct.ThrowIfCancellationRequested();
                    try { await client.InstallAsync(device, package, ct); }
                    catch(DeviceConnectionException connection) when(connection.Failure.Code is FailureCode.Pairing or FailureCode.TrustPending or FailureCode.Locked)
                    {
                        // The first attempt failed at the handshake. Repair and retry this IPA exactly once.
                        await readiness.EnsureAsync(device, progress, ct, true);
                        await client.InstallAsync(device, package, ct);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (DeviceConnectionException) { throw; }
            catch (Exception e)
            {
                if (!device.Network) throw;
                throw new IOException("Установка по Wi-Fi не завершена. Проверьте связь и состояние приложения на iPhone. Разблокируйте телефон, проверьте общую сеть и сетевую синхронизацию; при необходимости выберите USB и повторите только ошибки.\n" + e.Message, e);
            }
        }
    }
}
