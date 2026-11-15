using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace IpaDesktop.Tests;

public sealed class ReleaseArchiveTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(-1, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    public async Task DownloadChecksSizeAndDigest(int sizeOffset, bool wrongDigest, bool accepted)
    {
        using var folder = new TempFolder();
        using var client = Client(out var asset, out var payload);
        asset.size += sizeOffset;
        if (wrongDigest) asset.digest = "sha256:" + new string('0', 64);
        var path = Path.Combine(folder.Path, "package.zip");
        var progress = new List<double>();
        var download = ReleaseArchiveDownload.SaveAsync(client, asset, path, TestContext.Current.CancellationToken, progress.Add);

        if (accepted)
        {
            await download;
            Assert.Equal(payload, File.ReadAllBytes(path));
            Assert.True(progress.Count > 1);
            Assert.Equal(100, progress[^1]);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => download);
        }
    }

    [Fact]
    public async Task DownloadStopsWhenCanceledBetweenChunks()
    {
        using var folder = new TempFolder();
        using var client = Client(out var asset, out _);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var path = Path.Combine(folder.Path, "package.zip");
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReleaseArchiveDownload.SaveAsync(client, asset, path, cancellation.Token, _ =>
            {
                calls++;
                cancellation.Cancel();
            }));

        Assert.Equal(1, calls);
        Assert.InRange(new FileInfo(path).Length, 1L, asset.size - 1);
    }

    private static HttpClient Client(out Asset asset, out byte[] payload)
    {
        var bytes = new byte[140_000];
        Random.Shared.NextBytes(bytes);
        payload = bytes;
        asset = new Asset
        {
            browser_download_url = "https://github.com/fixture/package.zip",
            size = bytes.Length,
            digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes))
        };
        return new HttpClient(new CatalogResponseHandler
        {
            Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
        });
    }
}
