using System;
using System.IO;
using System.Text;
namespace IpaDesktop
{
    /// <summary>Записывает конфигурацию целиком, затем атомарно заменяет прежний файл.</summary>
    public static class AtomicFile
    {
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
