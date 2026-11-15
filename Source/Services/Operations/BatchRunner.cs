using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Итог пакета: успешные операции и элементы с ошибками; отмена не считается ошибкой элемента.</summary>
    public sealed class BatchResult<T>
    {
        public int Succeeded { get; internal set; }
        public List<T> Failed { get; private set; }
        public BatchResult() { Failed = new List<T>(); }
    }
    /// <summary>Последовательно выполняет пакет, продолжая работу после ошибки отдельного приложения.</summary>
    public static class BatchRunner
    {
        public static Task<BatchResult<T>> RunDownloads<T>(IList<T> items, Func<T, string> appId,
            Func<T, CancellationToken, Task> operation, Action<T, int> started, Action<T> succeeded,
            Action<T, Exception> failed, CancellationToken ct)
        {
            var emptyApps = new HashSet<string>(StringComparer.Ordinal);
            return Run(items, operation, started, succeeded, failed, null, ct, (item, error) =>
            {
                if (error == null || ErrorLocalizer.Describe(error).Code != FailureCode.EmptyCatalog)
                {
                    emptyApps.Clear();
                    return null;
                }
                emptyApps.Add(appId(item));
                return emptyApps.Count < 2 ? null : new AppleAccountException(new UserError(
                    FailureCode.EmptyCatalog, "Скачивание приостановлено",
                    "App Store не выдал данные загрузки для двух разных приложений подряд. Причина неизвестна.",
                    "Проверьте скачивание в App Store под тем же аккаунтом, затем вручную продолжите очередь. Кэш очищать не нужно."));
            });
        }

        /// <summary>Обработчики вызываются в исходном контексте UI. Долгая операция обязана сама быть асинхронной.</summary>
        public static async Task<BatchResult<T>> Run<T>(IList<T> items, Func<T, CancellationToken, Task> operation,
            Action<T, int> started, Action<T> succeeded, Action<T, Exception> failed, Action refreshed, CancellationToken ct,
            Func<T, Exception, Exception> stopAfter = null)
        {
            var result = new BatchResult<T>();
            for (int index = 0; index < items.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[index];
                started(item, index + 1);
                Exception error = null;
                try { await operation(item, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { error = e; }
                // Ошибки отображения не выдаются за ошибки скачивания или установки.
                if (error == null) { succeeded(item); result.Succeeded++; }
                else { result.Failed.Add(item); failed(item, error); }
                if (refreshed != null) refreshed();
                if (error is DeviceConnectionException or AppleAccountException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                var stop = stopAfter?.Invoke(item, error);
                if (stop != null) throw stop;
            }
            return result;
        }
    }
}
