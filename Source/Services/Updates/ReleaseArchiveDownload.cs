using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IpaDesktop;

internal static partial class ReleaseArchiveDownload
{
    const int BufferSize = 64 * 1024;
    const string DigestPrefix = "sha256:";

    [GeneratedRegex(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Digest();

    internal static bool HasSha256(string digest) => digest != null && Sha256Digest().IsMatch(digest);

    internal static async Task SaveAsync(HttpClient client, Asset asset, string path,
        CancellationToken cancellation, Action<double> progress)
    {
        if (!HasSha256(asset.digest))
            throw new InvalidDataException("Для архива требуется SHA-256 от GitHub.");

        using var response = await client.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            received += count;
            if (received > asset.size)
                throw new InvalidDataException("Размер архива не совпадает.");

            sha256.AppendData(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            progress(received * 100.0 / asset.size);
        }

        cancellation.ThrowIfCancellationRequested();
        if (received != asset.size)
            throw new InvalidDataException("Архив загружен не полностью.");

        var actual = Convert.ToHexStringLower(sha256.GetHashAndReset());
        if (!asset.digest.AsSpan(DigestPrefix.Length).Equals(actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 архива не совпал.");
    }
}
