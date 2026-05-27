using System.IO.Compression;
using System.Net;
using Equibles.Integrations.Cftc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Sibling to CftcClientDownloadTests (one-row happy path). The
/// `if (entry == null) return records;` guard inside ParseZipArchive
/// fires when the upstream ZIP has no entries — observed when CFTC publishes
/// a placeholder file at quarter-end before the annual roll completes.
/// A refactor that dropped the guard (or replaced FirstOrDefault with First())
/// would throw InvalidOperationException on a legitimate edge case and abort
/// the weekly COT batch. Pin the empty-archive degrade-gracefully contract.
/// </summary>
public class CftcClientDownloadEmptyZipTests
{
    [Fact]
    public async Task DownloadYearlyReport_EmptyZipArchive_ReturnsEmptyListWithoutThrowing()
    {
        var zipBytes = BuildEmptyZip();
        var handler = new ZipHandler(zipBytes);
        var sut = new CftcClient(new HttpClient(handler), Substitute.For<ILogger<CftcClient>>());

        var records = await sut.DownloadYearlyReport(2024);

        records.Should().BeEmpty();
    }

    private static byte[] BuildEmptyZip()
    {
        using var stream = new MemoryStream();
        using (var _ = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // No entries — ZipArchive emits the central-directory record only.
        }
        return stream.ToArray();
    }

    private sealed class ZipHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;

        public ZipHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                }
            );
        }
    }
}
