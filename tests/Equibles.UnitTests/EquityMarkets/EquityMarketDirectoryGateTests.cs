using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;

namespace Equibles.UnitTests.EquityMarkets;

/// <summary>
/// Contract: a row is the market's own share listing when FIRDS records the ISIN as a live share on one of the
/// market's venues and the home decision agrees, taken by the directory's stated primary market when it states one
/// (unless FIRDS places the home in another country) and by FIRDS' relevant venue otherwise.
/// </summary>
public class EquityMarketDirectoryGateTests
{
    [Theory]
    [InlineData("euronext-paris", "XPAR", null, "XPAR", "XPAR", "FR", true)]
    [InlineData("euronext-paris", "XPAR", null, "XPAR", "ALXP", "FR", true)]
    [InlineData("euronext-paris", "XPAR", null, "XPAR", "XAMS", "NL", false)]
    [InlineData("euronext-paris", "XPAR", null, "XAMS", "XPAR", "FR", false)]
    [InlineData("euronext-paris", "XAMS", null, "XPAR", "XPAR", "FR", false)]
    [InlineData("euronext-paris", "XPAR", "XAMS", "XPAR", "XPAR", "FR", false)]
    [InlineData("xetra", "XETR", "XFRA", "XETA", "FRAA", "DE", true)]
    [InlineData("xetra", "XETR", "XFRA", "XETA", "MUNB", "DE", true)]
    [InlineData("xetra", "XETR", "FRAB", "XETB", "XGAT", "DE", true)]
    [InlineData("xetra", "XETR", "XFRA", "XETA", "XETA", "LV", true)]
    [InlineData("xetra", "XETR", null, "XETA", "XETA", "LV", true)]
    [InlineData("xetra", "XETR", null, "XETA", "FRAA", "DE", true)]
    [InlineData("xetra", "XETR", "XFRA", "XETA", "WBAH", "AT", false)]
    [InlineData("xetra", "XETR", "XWBO", "XETB", "FRAB", "DE", false)]
    [InlineData("xetra", "XETR", "XNAS", "XETA", "HAMB", "DE", false)]
    [InlineData("xetra", "XETR", null, "XETA", "MUNB", "DE", true)]
    [InlineData("xetra", "XETR", "XHAN", "XETA", "FRAA", "DE", true)]
    [InlineData("xetra", "XETR", "XSTU", "XETA", "XETA", "DE", true)]
    [InlineData("xetra", "XETR", "MUNB", "XETB", "MUNB", "DE", true)]
    [InlineData("xetra", "XETR", "XNYS", "XETB", "FRAB", "DE", false)]
    [InlineData("xetra", "XETR", "XFRA", "XPAR", "FRAA", "DE", false)]
    [InlineData("xetra", "XETR", "XFRA", "XETR", "FRAA", "DE", false)]
    [InlineData("nasdaq-stockholm", "XSTO", null, "DSTO", "XSTO", "SE", true)]
    [InlineData("nasdaq-stockholm", "XSTO", null, "XSTO", "MSTO", "SE", true)]
    [InlineData("nasdaq-stockholm", "XSTO", null, "XSTO", "XHEL", "FI", false)]
    [InlineData("nasdaq-stockholm", "FNSE", null, "DNSE", "MNSE", "SE", true)]
    [InlineData("nasdaq-stockholm", "FNSE", null, "SSME", "SSME", "SE", true)]
    [InlineData("nasdaq-stockholm", "FNSE", null, "FNSE", "XSTO", "SE", true)]
    [InlineData("nasdaq-stockholm", "XSTO", null, "XSTO", "SSME", "SE", true)]
    [InlineData("nasdaq-stockholm", "XSTO", null, "XSTO", "XCSE", "DK", false)]
    [InlineData("nasdaq-helsinki", "FNFI", null, "FSME", "FSME", "FI", true)]
    [InlineData("nasdaq-copenhagen", "XCSE", null, "DCSE", "XCSE", "DK", true)]
    [InlineData("nasdaq-copenhagen", "FNDK", null, "DSME", "DNDK", "DK", true)]
    [InlineData("bme", "XMAD", null, "DMAD", "XMAD", "ES", true)]
    [InlineData("bme", "XMAD", null, "XMAD", "DMAD", "ES", true)]
    [InlineData("bme", "XMAD", null, "XMAD", "XAMS", "NL", false)]
    [InlineData("bme", "XMAD", null, "XMAD", "XLON", "GB", false)]
    [InlineData("gpw", "XWAR", null, "XWAR", "XWAR", "PL", true)]
    [InlineData("gpw", "XWAR", null, "XWAR", "XPAR", "FR", false)]
    [InlineData("gpw", "XNCO", null, "XWAR", "XWAR", "PL", false)]
    [InlineData("lse", "XLON", null, "XLON", "XLON", "GB", true)]
    [InlineData("lse", "AIMX", null, "AIMX", "AIMX", "GB", true)]
    [InlineData("lse", "AIMX", null, "AIMX", "XLON", "GB", true)]
    [InlineData("lse", "XLON", null, "XLON", "AIMX", "GB", true)]
    [InlineData("lse", "XLON", null, "XLOM", "XLON", "GB", false)]
    [InlineData("lse", "XLON", null, "XLON", "XLOM", "GB", false)]
    [InlineData("lse", "XLON", null, "XLON", "XSTO", "SE", false)]
    public void IsHomeShare_CombinesTheDirectorysPrimaryMarketWithFirds(
        string marketCode,
        string rowMic,
        string statedPrimaryMarket,
        string firdsMic,
        string relevantVenue,
        string competentAuthority,
        bool expected
    )
    {
        var market = EquityMarketCatalog.TryGet(marketCode);
        var row = Row(rowMic, statedPrimaryMarket);
        var firds = Firds(firdsMic, relevantVenue, competentAuthority);
        EquityMarketDirectoryGate.IsHomeShare(market, row, firds).Should().Be(expected);
    }

    [Fact]
    public void IsHomeShare_RefusesAMissingOrForeignFirdsLine()
    {
        var market = EquityMarketCatalog.TryGet("euronext-paris");
        var row = Row("XPAR", null);
        EquityMarketDirectoryGate.IsHomeShare(market, row, null).Should().BeFalse();
        var other = Firds("XPAR", "XPAR", "FR");
        other.Isin = "FR0000120073";
        EquityMarketDirectoryGate.IsHomeShare(market, row, other).Should().BeFalse();
        var gate = () =>
            EquityMarketDirectoryGate.IsHomeShare(null, row, Firds("XPAR", "XPAR", "FR"));
        gate.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsHomeShare_TakesTheIssuerRequestedLineWhenFirdsHomesTheShareOnAVenueTheIssuerNeverSought()
    {
        // A new Madrid ISIN that a German venue traded first: FIRDS homes it there, the issuer sought only Madrid.
        var market = EquityMarketCatalog.TryGet("bme");
        var row = Row("XMAD", null);
        var firds = Line("XMAD", "EQTB", true, "DE");
        List<FirdsInstrumentRecord> lines =
        [
            firds,
            Line("DMAD", "EQTB", true, "DE"),
            Line("EQTB", "EQTB", false, "DE"),
            Line("DUSB", "EQTB", false, "DE"),
        ];

        EquityMarketDirectoryGate.IsHomeShare(market, row, firds).Should().BeFalse();
        EquityMarketDirectoryGate.IsHomeShare(market, row, firds, lines).Should().BeTrue();
        EquityMarketDirectoryGate
            .IsHomeShare(market, Row("XMAD", "XMAD"), firds, lines)
            .Should()
            .BeTrue();
        EquityMarketDirectoryGate
            .IsHomeShare(EquityMarketCatalog.TryGet("xetra"), Row("XETR", null), firds, lines)
            .Should()
            .BeFalse("the share has no line on Xetra's own venues");

        var unflagged = Line("DMAD", "EQTB", null, "DE");
        EquityMarketDirectoryGate
            .IsHomeShare(
                market,
                row,
                unflagged,
                [unflagged, firds, Line("EQTB", "EQTB", false, "DE")]
            )
            .Should()
            .BeTrue(
                "any line the issuer sought on the market grants it, not only the one looked up first"
            );
        EquityMarketDirectoryGate
            .IsHomeShare(market, row, firds, [.. lines, Line("XETB", "EQTB", false, "DE")])
            .Should()
            .BeTrue("Xetra homes a share only where the issuer sought a German admission");
    }

    [Theory]
    [InlineData("CA03753D1042")]
    [InlineData("US0378331005")]
    [InlineData("JP3633400001")]
    public void IsHomeShare_LeavesTheRequestRuleToSharesIssuedWhereFirdsSeesEveryHome(string isin)
    {
        // A Canadian issuer's requested Paris line may be secondary to a Toronto home that no FIRDS register reports.
        var market = EquityMarketCatalog.TryGet("euronext-paris");
        var row = Row("ALXP", null);
        var firds = Line("ALXP", "FRAB", true, "DE");
        var unsought = Line("FRAB", "FRAB", false, "DE");
        EquityMarketDirectoryGate
            .IsHomeShare(market, row, firds, [firds, unsought])
            .Should()
            .BeTrue();

        row.Isin = isin;
        firds.Isin = isin;
        unsought.Isin = isin;
        EquityMarketDirectoryGate
            .IsHomeShare(market, row, firds, [firds, unsought])
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsHomeShare_AdmitsAGrowthTransferWhoseRelevantVenueIsAnUnsoughtMtf()
    {
        var market = EquityMarketCatalog.TryGet("euronext-paris");
        var firds = Line("ALXP", "SGMU", true, "FR");
        List<FirdsInstrumentRecord> lines = [firds, Line("SGMU", "SGMU", false, "FR")];

        EquityMarketDirectoryGate.IsHomeShare(market, Row("ALXP", null), firds).Should().BeFalse();
        EquityMarketDirectoryGate
            .IsHomeShare(market, Row("ALXP", null), firds, lines)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsHomeShare_LeavesTheRequestRuleWhenAnotherHomeIsPossible()
    {
        var market = EquityMarketCatalog.TryGet("euronext-paris");
        var row = Row("ALXP", null);
        var firds = Line("ALXP", "SGMU", true, "FR");
        var unsought = Line("SGMU", "SGMU", false, "FR");
        bool Gate(params FirdsInstrumentRecord[] others) =>
            EquityMarketDirectoryGate.IsHomeShare(market, row, firds, [firds, .. others]);

        Gate(unsought).Should().BeTrue();
        Gate(unsought, Line("XMAD", "SGMU", true, "FR"))
            .Should()
            .BeFalse("the issuer also sought Madrid, so Paris is not its only home");
        Gate(unsought, Line("XMAD", "SGMU", null, "FR"))
            .Should()
            .BeFalse("an unknown flag elsewhere counts as sought");
        Gate(Line("SGMU", "SGMU", null, "FR"))
            .Should()
            .BeFalse("the relevant line's flag is unknown");
        Gate(Line("SGMU", "SGMU", true, "FR"))
            .Should()
            .BeFalse("the issuer sought the relevant venue");
        Gate().Should().BeFalse("FIRDS states no line on the relevant venue");

        var firstNorth = Line("ALXP", "FNSE", true, "SE");
        EquityMarketDirectoryGate
            .IsHomeShare(market, row, firstNorth, [firstNorth, Line("FNSE", "FNSE", false, "SE")])
            .Should()
            .BeFalse("Stockholm homes the share through its own First North line");

        var london = Line("XLON", "XLON", true, "GB");
        london.Authority = "FCA";
        Gate(unsought, london).Should().BeFalse("the FCA register homes the share in London");

        var unknownHere = Line("ALXP", "SGMU", null, "FR");
        EquityMarketDirectoryGate
            .IsHomeShare(market, row, unknownHere, [unknownHere, unsought])
            .Should()
            .BeFalse("only an explicit request grants a home");
    }

    private static FirdsInstrumentRecord Line(
        string mic,
        string relevantVenue,
        bool? issuerRequested,
        string authority
    )
    {
        var line = Firds(mic, relevantVenue, authority);
        line.IssuerRequested = issuerRequested;
        return line;
    }

    private static EquityMarketDirectoryRow Row(string mic, string statedPrimaryMarket) =>
        new()
        {
            Isin = "FR0000120271",
            MarketIdentifierCode = mic,
            Symbol = "TTE",
            Name = "TOTALENERGIES",
            ReportedCurrency = "EUR",
            StatedPrimaryMarketIdentifierCode = statedPrimaryMarket,
            SourceUrl = new Uri("https://live.euronext.com/en/product/equities/FR0000120271-XPAR"),
        };

    private static FirdsInstrumentRecord Firds(
        string mic,
        string relevantVenue,
        string authority
    ) =>
        new()
        {
            Authority = "ESMA",
            Isin = "FR0000120271",
            Mic = mic,
            Lei = "529900S21EQ1BO4ESM68",
            Cfi = "ESVUFR",
            RelevantCompetentAuthority = authority,
            RelevantTradingVenue = relevantVenue,
        };
}
