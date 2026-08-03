using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

public class FinraClientTests
{
    [Fact]
    public async Task GetShortInterest_FirstPageBelowMaxPageSize_StopsAfterOneDataRequest()
    {
        // FinraClient paginates short-interest queries via offset. The loop must break
        // immediately once a page comes back smaller than MaxPageSize — without that
        // check, every import wastes an extra round-trip (and on partial pages where
        // the FINRA backend errors on out-of-bounds offsets, the import would fail).
        var tokenResponse = "{\"access_token\":\"test-token\",\"expires_in\":3600}";
        var dataResponse =
            "[{\"settlementDate\":\"2024-12-31\",\"symbolCode\":\"AAPL\",\"currentShortPositionQuantity\":1000}]";

        var handler = new RoutingHandler(tokenResponse, dataResponse);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" });
        var sut = new FinraClient(httpClient, Substitute.For<ILogger<FinraClient>>(), options);

        var result = await sut.GetShortInterest(new DateOnly(2024, 12, 31));

        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
        handler.DataRequestCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyShortVolume_OfficialFiles_CombinesNmsAndOrfWithFractionalShares()
    {
        var handler = new DailyFileHandler(
            ReadFixture("CNMSshvol20260730.txt"),
            ReadFixture("FORFshvol20260730.txt")
        );
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions())
        );

        var result = await sut.GetDailyShortVolume(new DateOnly(2026, 7, 30));

        result.Should().HaveCount(5);
        result
            .Should()
            .ContainSingle(record =>
                record.Symbol == "AAPL"
                && record.ShortVolume == 9_699_860.835956m
                && record.TotalVolume == 20_246_331.798531m
                && record.MarketCode == "B,Q,N"
            );
        result.Should().ContainSingle(record => record.Symbol == "AABB");
        result.Should().ContainSingle(record => record.Symbol == "BRK-B");
        handler
            .RequestPaths.Should()
            .BeEquivalentTo(
                "/equity/regsho/daily/CNMSshvol20260730.txt",
                "/equity/regsho/daily/FORFshvol20260730.txt"
            );
    }

    [Theory]
    [InlineData(
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n20260729|AAPL|1|0|2|Q\n1\n",
        "does not match"
    )]
    [InlineData(
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n20260730|AAPL|1|0|2|Q\n2\n",
        "declares 2 records"
    )]
    [InlineData(
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n20260730|AAPL|1|0|2|Q\n",
        "missing its record-count trailer"
    )]
    [InlineData(
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n20260730|AAPL|-1|0|2|Q\n1\n",
        "cannot be negative"
    )]
    [InlineData(
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n20260730|AAPL|1.0000001|0|2|Q\n1\n",
        "more than 6 decimal places"
    )]
    public async Task DailyFileParser_MalformedPartition_RejectsFile(
        string content,
        string expectedMessage
    )
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var act = () => FinraDailyShortVolumeFileParser.Parse(stream, new DateOnly(2026, 7, 30));

        await act.Should().ThrowAsync<FormatException>().WithMessage($"*{expectedMessage}*");
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Finra", fileName));

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public int DataRequestCount { get; private set; }

        public RoutingHandler(string tokenBody, string dataBody)
        {
            _tokenBody = tokenBody;
            _dataBody = dataBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token")
                ? _tokenBody
                : DataResponse();

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }

        private string DataResponse()
        {
            DataRequestCount++;
            return _dataBody;
        }
    }

    private sealed class DailyFileHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _files;
        private readonly object _requestPathsLock = new();

        public DailyFileHandler(string nmsFile, string orfFile)
        {
            _files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/equity/regsho/daily/CNMSshvol20260730.txt"] = nmsFile,
                ["/equity/regsho/daily/FORFshvol20260730.txt"] = orfFile,
            };
        }

        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (_requestPathsLock)
                RequestPaths.Add(path);

            if (!_files.TryGetValue(path, out var content))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
            );
        }
    }
}
