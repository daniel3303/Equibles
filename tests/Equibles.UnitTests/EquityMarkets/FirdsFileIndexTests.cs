using System.Net;
using System.Text.Json.Nodes;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Esma.Models;
using Equibles.UnitTests.Euronext;

namespace Equibles.UnitTests.EquityMarkets;

public class FirdsFileIndexTests
{
    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Firds", name)
        );

    [Fact]
    public async Task EsmaIndex_ListsEquityFullSetsAndDeltasOldestFirstWithChecksums()
    {
        var handler = new EuronextDirectoryTestHandler([await Fixture("esma_index.json")]);
        using var http = new HttpClient(handler);
        var files = await new EsmaFirdsClient(http).ListEquityFiles(new DateOnly(2026, 9, 12));
        files.Should().NotBeEmpty();
        files.Should().BeInAscendingOrder(file => file.PublishedOn);
        files
            .Should()
            .AllSatisfy(file =>
            {
                file.Authority.Should().Be("ESMA");
                file.DownloadUrl.Host.Should().Be("firds.esma.europa.eu");
                file.Checksum.Should().MatchRegex("^[0-9a-f]{32}$");
            });
        files
            .Where(file => file.FileType == FirdsFileType.Full)
            .Select(file => file.FileName)
            .Should()
            .Equal("FULINS_E_20260912_01of02.zip", "FULINS_E_20260912_02of02.zip");
        files.Should().NotContain(file => file.FileName.StartsWith("FULINS_D"));
        handler
            .Requests.Should()
            .ContainSingle()
            .Which.Url.Host.Should()
            .Be("registers.esma.europa.eu");
        handler
            .Requests[0]
            .Url.Query.Should()
            .Contain("publication_date")
            .And.Contain("2026-09-12");
    }

    [Fact]
    public async Task FcaIndex_ListsEquityFullSetsAndDeltasWithoutChecksums()
    {
        var handler = new EuronextDirectoryTestHandler([await Fixture("fca_index.json")]);
        using var http = new HttpClient(handler);
        var files = await new FcaFirdsClient(http).ListEquityFiles(new DateOnly(2026, 9, 10));
        files.Should().HaveCount(11);
        files.Should().BeInAscendingOrder(file => file.PublishedOn);
        files
            .Single(file => file.FileType == FirdsFileType.Full)
            .FileName.Should()
            .Be("FULINS_E_20260912_01of01.zip");
        files
            .Should()
            .AllSatisfy(file =>
            {
                file.Authority.Should().Be("FCA");
                file.DownloadUrl.Host.Should().Be("data.fca.org.uk");
                file.Checksum.Should().BeNull();
            });
        handler.Requests.Should().ContainSingle().Which.Url.Host.Should().Be("api.data.fca.org.uk");
    }

    [Fact]
    public async Task FcaIndex_EndsOnAShortPageWhenTheTotalIsAbsent()
    {
        var index = JsonNode.Parse(await Fixture("fca_index.json")).AsObject();
        index["hits"].AsObject().Remove("total").Should().BeTrue();
        var handler = new EuronextDirectoryTestHandler([index.ToJsonString()]);
        using var http = new HttpClient(handler);
        var files = await new FcaFirdsClient(http).ListEquityFiles(new DateOnly(2026, 9, 10));
        files.Should().HaveCount(11);
        handler
            .Requests.Should()
            .ContainSingle("a page shorter than the size asked for ends the walk");
    }

    [Fact]
    public async Task Download_RefusesAnotherOriginBeforeAnyRequest()
    {
        var handler = new EuronextDirectoryTestHandler([]);
        using var http = new HttpClient(handler);
        var file = new FirdsFile
        {
            Authority = "ESMA",
            FileName = "FULINS_E_20260912_01of02.zip",
            FileType = FirdsFileType.Full,
            PublishedOn = new DateOnly(2026, 9, 12),
            DownloadUrl = new Uri("https://example.com/FULINS_E_20260912_01of02.zip"),
        };
        var download = () => new EsmaFirdsClient(http).Download(file);
        await download.Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_VerifiesTheIndexChecksumAndStreamsTheSingleXmlEntry()
    {
        var xml = await Fixture("FULINS_E_sample.xml");
        using var zipBytes = new MemoryStream();
        using (
            var archive = new System.IO.Compression.ZipArchive(
                zipBytes,
                System.IO.Compression.ZipArchiveMode.Create,
                true
            )
        )
        {
            await using var entry = archive.CreateEntry("FULINS_E_20260912_01of02.xml").Open();
            await entry.WriteAsync(System.Text.Encoding.UTF8.GetBytes(xml));
        }
        var bytes = zipBytes.ToArray();
        var checksum = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
        var file = new FirdsFile
        {
            Authority = "ESMA",
            FileName = "FULINS_E_20260912_01of02.zip",
            FileType = FirdsFileType.Full,
            PublishedOn = new DateOnly(2026, 9, 12),
            DownloadUrl = new Uri(
                "https://firds.esma.europa.eu/firds/FULINS_E_20260912_01of02.zip"
            ),
            Checksum = checksum,
        };
        using (var http = new HttpClient(new BytesHandler(bytes)))
        using (var download = await new EsmaFirdsClient(http).Download(file))
        {
            download.Bytes.Should().Be(bytes.Length);
            await using var stream = download.OpenXml();
            var count = 0;
            await foreach (var _ in FirdsRecordReader.Read(stream))
                count++;
            count.Should().Be(8);
        }
        file.Checksum = "0000000000000000000000000000000000000000";
        using var other = new HttpClient(new BytesHandler(bytes));
        var mismatch = () => new EsmaFirdsClient(other).Download(file);
        await mismatch.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                    RequestMessage = request,
                }
            );
    }
}
