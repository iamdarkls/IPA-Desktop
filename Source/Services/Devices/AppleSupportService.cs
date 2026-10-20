using System;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Преобразует снимок служб и реестра в понятный статус готовности компонентов Apple.</summary>
    public sealed class AppleSupportService
    {
        readonly IAppleSupportProbe probe;

        public AppleSupportService(IAppleSupportProbe probe)
        {
            if (probe == null) throw new ArgumentNullException("probe");
            this.probe = probe;
        }

        /// <summary>Выполняет чтение системного состояния вне UI-потока и возвращает оценку компонентов Apple.</summary>
        public Task<AppleSupportReport> CheckAsync(CancellationToken cancellation)
        {
            return Task.Run(delegate
            {
                cancellation.ThrowIfCancellationRequested();
                var snapshot = probe.Read();
                cancellation.ThrowIfCancellationRequested();
                return Evaluate(snapshot);
            }, cancellation);
        }

        /// <summary>Различает отсутствие драйверов, остановленную службу и невозможность достоверной проверки.</summary>
        public static AppleSupportReport Evaluate(AppleSupportSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            AppleSupportStatus status;
            string message;
            string guidance;
            if (snapshot.ServiceFound && snapshot.ServiceStatusKnown)
            {
                status = snapshot.ServiceRunning ? AppleSupportStatus.Running : AppleSupportStatus.Stopped;
                message = snapshot.ServiceRunning ? "Служба Apple запущена" : "Служба Apple остановлена";
                guidance = snapshot.ServiceRunning
                    ? "Служба Apple Mobile Device работает. Если iPhone не найден, проверьте USB, разблокировку и доверие."
                    : "Откройте services.msc, найдите Apple Mobile Device Service и запустите или перезапустите её. Для изменения состояния службы могут потребоваться права администратора.";
            }
            else if (snapshot.ServiceFound || !snapshot.ServiceScanCompleted || !snapshot.RegistryScanCompleted)
            {
                status = AppleSupportStatus.Unknown;
                message = "Статус компонентов Apple неизвестен";
                guidance = "Не удалось полностью проверить компоненты Apple. Подробности — ниже. Поиск iPhone можно выполнить вручную.";
            }
            else if (snapshot.InstallationDetected)
            {
                status = AppleSupportStatus.ServiceMissing;
                message = "Компоненты Apple найдены, служба отсутствует";
                guidance = "Найдена регистрация Apple Mobile Device Support, но служба отсутствует. Проверьте или восстановите установку ПО Apple, затем повторите проверку.";
            }
            else
            {
                status = AppleSupportStatus.NotDetected;
                message = "Apple Mobile Device Support не обнаружен";
                guidance = "Служба и регистрация Apple Mobile Device Support не найдены. Установите ПО для устройств с официального сайта Apple или Microsoft Store, затем повторите проверку.";
            }
            return new AppleSupportReport
            {
                Status = status,
                Message = message,
                Details = guidance + (String.IsNullOrWhiteSpace(snapshot.Details) ? "" : Environment.NewLine + snapshot.Details)
            };
        }
    }
}
