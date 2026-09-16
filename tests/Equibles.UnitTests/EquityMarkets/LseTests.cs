using System.Net;
using Equibles.Integrations.Lse;
using Equibles.Integrations.Lse.Xlsx;

namespace Equibles.UnitTests.EquityMarkets;

public class LseTests
{
    private const long MaxPartBytes = 64_000_000;

    private static Task<byte[]> Fixture(string name) =>
        File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Lse", name)
        );

    private static Task<byte[]> InstrumentList() => Fixture("instrument-list.trimmed.xlsx");

    [Fact]
    public async Task InstrumentList_ReadsEveryShareLineUnderTheVenueItsMarketNames()
    {
        var list = LseInstrumentListParser.Read(await InstrumentList(), MaxPartBytes);

        list.AsAt.Should().Be(new DateOnly(2026, 7, 31));
        list.StatedCount.Should().Be(18);
        list.Instruments.Should().HaveCount(18);
        list.Instruments.Should().AllSatisfy(row => row.MifirIdentifier.Should().Be("SHRS"));
        var group = list.Instruments.Single(row => row.Tidm == "III");
        group.Isin.Should().Be("GB00B1YW4409");
        group.IssuerName.Should().Be("3I GROUP PLC");
        group.InstrumentName.Should().Be("ORD 73 19/22P");
        group.TradingCurrency.Should().Be("GBX");
        group.LseMarket.Should().Be("MAIN MARKET");
        group.MarketIdentifierCode.Should().Be("XLON");
        list.Instruments.Single(row => row.Tidm == "4BB").MarketIdentifierCode.Should().Be("AIMX");
        list.Instruments.Single(row => row.Tidm == "AOF")
            .MarketIdentifierCode.Should()
            .Be("XLON", "the specialist fund segment is part of the main market");
        list.Instruments.Single(row => row.Tidm == "GPM")
            .MarketIdentifierCode.Should()
            .Be("XLON", "a share admitted to trading only still trades on the same venue");
        list.Instruments.Select(row => row.TradingCurrency)
            .Distinct()
            .Should()
            .BeEquivalentTo(["GBX", "GBP", "USD", "EUR", "JPY"]);
        list.Instruments.Should().Contain(row => row.Tidm == "BT.A");
        list.Instruments.Should().Contain(row => row.Tidm == "RR.");
    }

    [Fact]
    public async Task UnknownMarket_LeavesTheLineWithoutAVenue()
    {
        LseMarketSegments.TryResolve("Main Market").Should().Be("XLON");
        LseMarketSegments.TryResolve("aim").Should().Be("AIMX");
        LseMarketSegments.TryResolve("INTERNATIONAL SECURITIES MARKET").Should().BeNull();
        LseMarketSegments.TryResolve(null).Should().BeNull();
        var list = LseInstrumentListParser.Read(await InstrumentList(), MaxPartBytes);
        list.Instruments.Should().AllSatisfy(row => row.MarketIdentifierCode.Should().NotBeNull());
    }

    [Theory]
    [InlineData("instrument-list.count-mismatch.derived.xlsx")]
    [InlineData("instrument-list.no-shares-sheet.derived.xlsx")]
    public async Task TruncatedOrReshapedWorkbook_IsRefused(string name)
    {
        var workbook = await Fixture(name);
        var read = () => LseInstrumentListParser.Read(workbook, MaxPartBytes);
        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task ABodyThatIsNotAWorkbook_IsRefused()
    {
        var open = () => XlsxWorkbook.Open("<html>Error 404</html>"u8.ToArray(), MaxPartBytes);
        open.Should().Throw<InvalidDataException>();
        var workbook = await InstrumentList();
        var small = () => LseInstrumentListParser.Read(workbook, 64);
        small
            .Should()
            .Throw<InvalidDataException>("a part past the read limit is never decompressed");
    }

    [Fact]
    public async Task EditionWalk_TakesTheHighestPublishedNumberOverAGap()
    {
        var workbook = await InstrumentList();
        var handler = Handler(
            workbook,
            LseInstrumentListClient.FirstEdition,
            LseInstrumentListClient.FirstEdition + 2
        );
        using var http = new HttpClient(handler);

        var edition = await new LseInstrumentListClient(http).FindLatestEdition(
            CancellationToken.None
        );

        edition.Should().Be(LseInstrumentListClient.FirstEdition + 2);
        handler
            .Requests.Should()
            .HaveCount(
                3 + LseInstrumentListClient.MissesBeforeStop,
                "the walk carries on over a gap and stops after the misses that follow the last edition"
            );
        handler
            .Requests.Should()
            .AllSatisfy(request => request.Method.Should().Be(HttpMethod.Head));
    }

    [Fact]
    public async Task AnEditionThatServesAPageOrNothingAtAll_IsNotAnEdition()
    {
        using var page = new HttpClient(
            new LseInstrumentListTestHandler(
                new Dictionary<string, (HttpStatusCode, string, byte[])>
                {
                    [
                        LseInstrumentListClient
                            .EditionUrl(LseInstrumentListClient.FirstEdition)
                            .AbsoluteUri
                    ] = (HttpStatusCode.OK, "text/html", [0x3c]),
                }
            )
        );
        var served = () =>
            new LseInstrumentListClient(page).FindLatestEdition(CancellationToken.None);
        await served.Should().ThrowAsync<InvalidDataException>();

        using var empty = new HttpClient(
            new LseInstrumentListTestHandler(
                new Dictionary<string, (HttpStatusCode, string, byte[])>()
            )
        );
        var none = () =>
            new LseInstrumentListClient(empty).FindLatestEdition(CancellationToken.None);
        await none.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task GetInstruments_ReadsTheHighestEditionAndKeepsWhereItCameFrom()
    {
        var workbook = await InstrumentList();
        var handler = Handler(workbook, LseInstrumentListClient.FirstEdition);
        using var http = new HttpClient(handler);

        var list = await new LseInstrumentListClient(http).GetInstruments(CancellationToken.None);

        list.Edition.Should().Be(LseInstrumentListClient.FirstEdition);
        list.SourceUrl.Should().Be(LseInstrumentListClient.EditionUrl(list.Edition));
        list.SourceUrl.AbsoluteUri.Should().Contain("Instrument%20list_");
        list.PageUrl.Should().Be(LseInstrumentListClient.PublisherPage);
        list.CapturedAt.Kind.Should().Be(DateTimeKind.Utc);
        list.Instruments.Should().HaveCount(18);
        handler.Requests.Last().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Last().Url.Should().Be(list.SourceUrl);
    }

    private static LseInstrumentListTestHandler Handler(byte[] workbook, params int[] editions) =>
        new(
            editions.ToDictionary(
                edition => LseInstrumentListClient.EditionUrl(edition).AbsoluteUri,
                _ =>
                    (
                        HttpStatusCode.OK,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        workbook
                    )
            )
        );
}
