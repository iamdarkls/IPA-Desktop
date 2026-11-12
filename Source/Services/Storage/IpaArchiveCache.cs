using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

public sealed class IpaArchiveCache
{
    public const int DefaultGiB = 3, MaxGiB = 32;
    const long GiB = 1024L * 1024 * 1024;
    static readonly Regex EntryName = new(@"\A[0-9]+-[a-f0-9]{32}\.raw\z", RegexOptions.CultureInvariant);
    public string Folder { get; }
    public IpaArchiveCache(string dataRoot) => Folder = Path.GetFullPath(Path.Combine(dataRoot, "Cache", "IPA"));

    FileInfo[] Entries()
    {
        var parent = new DirectoryInfo(Folder);
        if (!parent.Exists) return Array.Empty<FileInfo>();
        if ((parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (parent.Parent.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Папка кэша указывает на другое место. Проверьте её в папке данных IPA Desktop.");
        return parent.EnumerateFiles("*.raw").Where(x => EntryName.IsMatch(x.Name) &&
            (x.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
    }
    public Task<long> SizeAsync(CancellationToken ct) => Task.Run(() =>
    {
        long bytes = 0;
        foreach (var file in Entries())
        {
            ct.ThrowIfCancellationRequested();
            try { bytes += file.Length; } catch (FileNotFoundException) { }
        }
        return bytes;
    }, ct);
    public Task TrimAsync(int limitGiB, CancellationToken ct) => Task.Run(() =>
    {
        long remaining = Math.Clamp(limitGiB, 0, MaxGiB) * GiB;
        foreach (var file in Entries().OrderByDescending(x => x.LastWriteTimeUtc))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (remaining > 0 && file.Length <= remaining) remaining -= file.Length;
                else file.Delete();
            }
            catch (FileNotFoundException) { }
        }
    }, ct);
}
