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
        /// <summary>Обработчики вызываются в исходном контексте UI. Долгая операция обязана сама быть асинхронной.</summary>
        public static async Task<BatchResult<T>> Run<T>(IList<T> items, Func<T, CancellationToken, Task> operation,
            Action<T, int> started, Action<T> succeeded, Action<T, Exception> failed, Action refreshed, CancellationToken ct)
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
            }
            return result;
        }
    }
}
