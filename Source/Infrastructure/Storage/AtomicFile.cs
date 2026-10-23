using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace IpaDesktop
{
    /// <summary>Записывает конфигурацию целиком, затем атомарно заменяет прежний файл.</summary>
    public static class AtomicFile
    {
        /// <summary>Публикует копию только целиком; отмена и ошибка не оставляют частичный IPA и не заменяют существующий файл.</summary>
        public static async Task CopyAsync(string source, string destination, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var temporary = Path.Combine(Path.GetDirectoryName(destination), ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    await CopyStreamAsync(input, output, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        internal static async Task CopyStreamAsync(Stream input, Stream output, CancellationToken ct)
        {
            var buffer = new byte[131072];
            int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
            {
                ct.ThrowIfCancellationRequested();
                await output.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false);
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        /// <summary>Уникальный временный файл исключает пересечение записей; ошибка не удаляет оригинал.</summary>
        public static void WriteUtf8(string path, string contents)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, contents, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                // Очистка не должна скрыть первоначальную ошибку записи.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
