using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop
{
    /// <summary>Даёт старому CLI ASCII-имя IPA, сохраняя Unicode-каталог через рабочую директорию процесса.</summary>
    public sealed class InstallationPackage : IDisposable
    {
        readonly string directory;
        public string WorkingDirectory { get; private set; }
        public string RelativePath { get; private set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateHardLink(string newName, string existingName, IntPtr securityAttributes);

        InstallationPackage(string source)
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(source));
            string name = ".install-" + Guid.NewGuid().ToString("N");
            directory = Path.Combine(WorkingDirectory, name);
            RelativePath = Path.Combine(name, "package.ipa");
        }

        /// <summary>Предпочитает жёсткую ссылку без копирования больших IPA; на других ФС копирует с отменой.</summary>
        public static Task<InstallationPackage> CreateAsync(string source, CancellationToken ct)
        {
            return Task.Run(delegate
            {
                ct.ThrowIfCancellationRequested();
                var package = new InstallationPackage(source);
                try
                {
                    Directory.CreateDirectory(package.directory);
                    string target = Path.Combine(package.WorkingDirectory, package.RelativePath);
                    if (!CreateHardLink(target, Path.GetFullPath(source), IntPtr.Zero))
                    {
                        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[1024 * 1024];
                            int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, count);
                            }
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    return package;
                }
                catch { package.Dispose(); throw; }
            }, ct);
        }

        /// <summary>Удаляет только временное имя: исходный IPA остаётся после успеха, ошибки или отмены.</summary>
        public void Dispose()
        {
            try
            {
                File.Delete(Path.Combine(WorkingDirectory, RelativePath));
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
