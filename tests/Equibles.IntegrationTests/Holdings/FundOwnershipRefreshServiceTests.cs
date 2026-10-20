using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.FundOwnership;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The fund-ownership drain reads N-PORT reports, N-CEN fund types, fund-to-filer links and 13F
/// positions from Postgres and publishes a quarter's rows and state in one transaction.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FundOwnershipRefreshServiceTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly DateOnly Q2 = new(2026, 6, 30);
    private const string NvdaCusip = "67066G104";
    private const string XyzCusip = "98765X101";

    private EquityIssuer _nvda;
    private EquityIssuer _xyz;
    private InstitutionalHolder _vanguard;
    private int _reports;

    public FundOwnershipRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Seed();
    }

    [Fact]
    public async Task RebuildQuarter_BucketsEachSeriesLatestReportByItsNCenFundType()
    {
        (await Rebuild()).Should().BeTrue();

        await using var verify = Fixture.CreateDbContext();
        var nvda = await verify
            .Set<StockQuarterlyFundOwnership>()
            .Where(row => row.EquityIssuerId == _nvda.Id)
            .ToDictionaryAsync(row => row.Bucket);
        // The index fund's June report wins over its April one; its July report is next quarter's.
        nvda[FundOwnershipBucket.IndexFund].Shares.Should().Be(120);
        nvda[FundOwnershipBucket.IndexFund].SeriesCount.Should().Be(1);
        nvda[FundOwnershipBucket.IndexFund]
            .NewestReportPeriod.Should()
            .Be(new DateOnly(2026, 6, 30));
        // The index ETF holds NVDA once by CUSIP and once by an ISIN alone, one series.
        nvda[FundOwnershipBucket.IndexEtf].Shares.Should().Be(50 + 6);
        nvda[FundOwnershipBucket.IndexEtf].SeriesCount.Should().Be(1);
        nvda[FundOwnershipBucket.ActiveEtf].Shares.Should().Be(30);
        // The active fund's short leg and principal line are not shares it owns; the closed-end
        // fund's id-less report takes its registrant's single N-CEN fund.
        nvda[FundOwnershipBucket.ActiveFund].Shares.Should().Be(40 + 7);
        nvda[FundOwnershipBucket.ActiveFund].SeriesCount.Should().Be(2);
        // No N-CEN, an N-CEN older than the lookback, and a registrant naming two funds.
        nvda[FundOwnershipBucket.Unclassified].Shares.Should().Be(5 + 4 + 3);
        nvda[FundOwnershipBucket.Unclassified].SeriesCount.Should().Be(3);
        nvda[FundOwnershipBucket.ActiveFund].Value.Should().Be((40 + 7) * 100);

        // XYZ split 2-for-1 on June 15: a May report doubles onto the quarter end's basis.
        var xyz = await verify
            .Set<StockQuarterlyFundOwnership>()
            .SingleAsync(row => row.EquityIssuerId == _xyz.Id);
        xyz.Bucket.Should().Be(FundOwnershipBucket.IndexEtf);
        xyz.Shares.Should().Be(20);

        var advised = await verify
            .Set<HolderStockAdvisedFundShares>()
            .ToDictionaryAsync(row => row.EquityIssuerId);
        // Vanguard's linked index fund and index ETF hold 176 NVDA, more than its 150-share 13F.
        advised[_nvda.Id].HolderShares.Should().Be(150);
        advised[_nvda.Id].FundShares.Should().Be(150);
        advised[_nvda.Id].IndexFundShares.Should().Be(150);
        advised[_nvda.Id].IsCapped.Should().BeTrue();
        advised[_nvda.Id].SeriesCount.Should().Be(2);
        advised[_xyz.Id].FundShares.Should().Be(20);
        advised[_xyz.Id].IsCapped.Should().BeFalse();

        var state = await verify.Set<FundOwnershipQuarterState>().SingleAsync();
        state.ReportDate.Should().Be(Q2);
        state.InputFingerprint.Should().Be("fp-1");
        state.CalculationVersion.Should().Be(FundOwnershipRefreshService.CalculationVersion);
        state.SeriesCount.Should().Be(8);
        state.UnclassifiedSeriesCount.Should().Be(3);
    }

    [Fact]
    public async Task RebuildQuarter_ReplacesThePublishedQuarterAndItsFingerprint()
    {
        await Rebuild();
        // The unclassified series' report is withdrawn from the store.
        await DbContext
            .Set<NportFiling>()
            .Where(filing => filing.SeriesId == "S000000005")
            .ExecuteDeleteAsync();

        (await Rebuild("fp-2")).Should().BeTrue();

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<StockQuarterlyFundOwnership>()
            .Where(row => row.EquityIssuerId == _nvda.Id)
            .ToListAsync();
        rows.Should().HaveCount(5);
        rows.Single(row => row.Bucket == FundOwnershipBucket.Unclassified)
            .Shares.Should()
            .Be(4 + 3);
        (await verify.Set<FundOwnershipQuarterState>().SingleAsync())
            .InputFingerprint.Should()
            .Be("fp-2");
    }

    [Fact]
    public async Task RebuildQuarter_AnOpenFilingWindowWritesNothing()
    {
        (await Service().RebuildQuarter(Q2, "fp", new DateOnly(2026, 9, 1), default))
            .Should()
            .BeFalse();
        (await DbContext.Set<FundOwnershipQuarterState>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Fingerprints_MoveWhenAQuartersReportsChange()
    {
        var service = Service();
        var before = await service.Fingerprints([Q2, new DateOnly(2026, 3, 31)], default);
        AddFiling("S000000004", new DateOnly(2026, 6, 30), NvdaCusip, 41, amendment: true);
        await DbContext.SaveChangesAsync();

        var after = await Service().Fingerprints([Q2, new DateOnly(2026, 3, 31)], default);
        after[Q2].Should().NotBe(before[Q2]);
        after[new DateOnly(2026, 3, 31)].Should().Be(before[new DateOnly(2026, 3, 31)]);
    }

    [Fact]
    public async Task Reader_AndHolderMixTool_RenderTheFundTypeDimension()
    {
        await Rebuild();
        var reader = new FundOwnershipReader(
            new FundOwnershipRepository(DbContext),
            new StockSplitRepository(DbContext)
        );

        var history = await reader.ReadRecent(_nvda, "NVDA");
        history.Quarters.Should().ContainSingle();
        var quarter = history.Latest;
        quarter.Total.Shares.Should().Be(120 + 56 + 30 + 47 + 12);
        quarter.Bucket(FundOwnershipBucket.IndexEtf).PercentOfOutstanding.Should().Be(0.056m);
        quarter.IndexPercentOfOutstanding.Should().Be(0.176m);

        var latest = await reader.ReadLatestByIssuer();
        latest.ReportDate.Should().Be(Q2);
        latest.ByIssuer[_nvda.Id].Should().Be(new IssuerFundShares(176, 265));
        latest.IndexFraction(_nvda.Id, 100_000).Should().Be(0.00176m);
        // A computed quarter in which no fund held the issuer reads as none held.
        latest.IndexFraction(Guid.NewGuid(), 1_000).Should().Be(0m);
        var subset = await reader.ReadByIssuer(Q2, [_xyz.Id]);
        subset.ByIssuer.Keys.Should().Equal(_xyz.Id);
        var before = await reader.ReadByIssuer(new DateOnly(2026, 3, 31), null);
        before.ReportDate.Should().BeNull();
        before.IndexFraction(_nvda.Id, 100_000).Should().BeNull();

        var tools = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(DbContext),
            new InstitutionalHolderRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext)
            ),
            ErrorManager,
            NullLogger<InstitutionalHoldingsTools>(),
            fundOwnershipReader: reader
        );
        var output = await tools.GetHolderMix("NVDA", "fundtype");
        output
            .Should()
            .Contain("by N-CEN fund type: each bucket's shares as % of shares outstanding");
        output.Should().Contain("| Index ETFs | 0.06% | — | 1 | 2026-05-31 |");
        output
            .Should()
            .Contain("| All registered funds | 0.27% | — | 8 | 2026-05-31 to 2026-06-30 |");
        output.Should().Contain("index funds and index ETFs hold 0.18% of shares outstanding");
        output.Should().Contain("never added to 13F totals");

        (await tools.GetHolderMix("NVDA", "bogus"))
            .Should()
            .Contain(
                "Use one of: type, style, turnover, horizon, aum, client, pf, country, fundtype."
            );

        var top = await tools.GetTopHolders("NVDA");
        top.Should()
            .Contain(
                "The Vanguard Group, Inc.: at least 150 of its 150 shares, at least 150 in index funds and index ETFs (2 series)"
            );
    }

    private Task<bool> Rebuild(string fingerprint = "fp-1") =>
        Service().RebuildQuarter(Q2, fingerprint, Today, default);

    private FundOwnershipRefreshService Service() =>
        new(
            Fixture.CreateDbContext(),
            new EquityIssuerRepository(Fixture.CreateDbContext()),
            HoldingsCorpusCoverage.Default,
            NullLogger<FundOwnershipRefreshService>()
        );

    private async Task Seed()
    {
        _nvda = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "NVIDIA Corp",
            Cik: "0001045810",
            Cusip: NvdaCusip,
            SharesOutStanding: 100_000
        );
        _xyz = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "XYZ",
            Name: "XYZ Corp",
            Cik: "0000000999",
            Cusip: XyzCusip,
            SharesOutStanding: 1_000
        );
        var closedEnd = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "DNP",
            Name: "Closed End Fund",
            Cik: "0000806628",
            Cusip: "23325P104"
        );
        DbContext.AddRange(_nvda, _xyz, closedEnd);
        DbContext.Add(
            new StockSplit
            {
                EquityIssuerId = _xyz.Id,
                EquityListingId = _xyz.Presentation.EquityListingId,
                PriceSeriesTicker = "XYZ",
                EffectiveDate = new DateOnly(2026, 6, 15),
                Numerator = 2,
                Denominator = 1,
                ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
            }
        );

        _vanguard = new InstitutionalHolder { Cik = "102909", Name = "The Vanguard Group, Inc." };
        DbContext.Add(_vanguard);
        Form13F(_nvda, 150);
        Form13F(_xyz, 100);

        // S1, an index fund linked to Vanguard: April and June reports in Q2, March and July outside.
        FundReport("S000000001", "36405", NCenFundTypes.IndexFund);
        Link("S000000001");
        AddFiling("S000000001", new DateOnly(2026, 3, 31), NvdaCusip, 90);
        AddFiling("S000000001", new DateOnly(2026, 4, 30), NvdaCusip, 100);
        AddFiling("S000000001", new DateOnly(2026, 6, 30), NvdaCusip, 120);
        AddFiling("S000000001", new DateOnly(2026, 7, 31), NvdaCusip, 130);

        // S2, an index ETF linked to Vanguard, holds NVDA by CUSIP and by ISIN alone, and XYZ.
        FundReport(
            "S000000002",
            "36405",
            NCenFundTypes.IndexFund | NCenFundTypes.ExchangeTradedFund
        );
        Link("S000000002");
        var etf = AddFiling("S000000002", new DateOnly(2026, 5, 31), NvdaCusip, 50);
        etf.Holdings.Add(Line(etf, null, 6, isin: "US67066G1040"));
        etf.Holdings.Add(Line(etf, XyzCusip, 10));

        // S3 lists on an exchange without reporting a fund type: an active ETF.
        FundReport("S000000003", "11111", NCenFundTypes.None, listed: true);
        AddFiling("S000000003", new DateOnly(2026, 6, 30), NvdaCusip, 30);

        // S4, an active fund whose superseded report once said index fund; its short and
        // principal lines are not owned shares.
        FundReport("S000000004", "22222", NCenFundTypes.IndexFund, superseded: true);
        FundReport("S000000004", "22222", NCenFundTypes.None, period: new DateOnly(2026, 3, 31));
        var active = AddFiling("S000000004", new DateOnly(2026, 6, 30), NvdaCusip, 40);
        active.Holdings.Add(Line(active, NvdaCusip, 9, payoff: "Short"));
        active.Holdings.Add(Line(active, NvdaCusip, 1_000, units: "PA"));

        // S5 has no N-CEN; S6's only N-CEN is older than the lookback.
        AddFiling("S000000005", new DateOnly(2026, 6, 30), NvdaCusip, 5);
        FundReport(
            "S000000006",
            "33333",
            NCenFundTypes.IndexFund,
            period: new DateOnly(2024, 6, 30)
        );
        AddFiling("S000000006", new DateOnly(2026, 6, 30), NvdaCusip, 4);

        // An id-less closed-end fund fed through its own issuer page, classified by CIK.
        FundReport(null, "806628", NCenFundTypes.None);
        var closedEndReport = AddFiling(null, new DateOnly(2026, 6, 30), NvdaCusip, 7);
        closedEndReport.EquityIssuerId = closedEnd.Id;
        closedEndReport.RegistrantCik = null;

        // An id-less trust whose N-CEN names two funds cannot say which one filed.
        FundReport(null, "44444", NCenFundTypes.IndexFund, extraFund: true);
        var trust = AddFiling(null, new DateOnly(2026, 6, 30), NvdaCusip, 3);
        trust.RegistrantCik = "44444";

        await DbContext.SaveChangesAsync();
    }

    private void Form13F(EquityIssuer stock, long shares) =>
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = _vanguard.Id,
                ReportDate = Q2,
                FilingDate = Q2.AddDays(40),
                Shares = shares,
                Value = shares * 100,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                FilingType = FilingType.Form13F,
                AccessionNumber = "13f-vanguard-q2",
            }
        );

    private void Link(string seriesId) =>
        DbContext.Add(
            new FundSeriesHolderLink
            {
                SeriesId = seriesId,
                RegistrantCik = "36405",
                AdviserCrd = 105958,
                InstitutionalHolderId = _vanguard.Id,
                Basis = FundSeriesHolderLinkBasis.AdviserFiler,
                NCenReportEndingPeriod = new DateOnly(2025, 12, 31),
            }
        );

    private NportFiling AddFiling(
        string seriesId,
        DateOnly period,
        string cusip,
        decimal balance,
        bool amendment = false
    )
    {
        var filing = new NportFiling
        {
            RegistrantCik = "36405",
            RegistrantName = "Registrant",
            AccessionNumber =
                $"nport-{seriesId ?? Guid.NewGuid().ToString("N")[..8]}-{period:yyyyMMdd}{(amendment ? "-a" : "")}",
            FilingDate = period.AddDays(amendment ? 70 : 55),
            IsAmendment = amendment,
            SeriesId = seriesId,
            SeriesName = "Fund " + seriesId,
            ReportPeriodDate = period,
            ReportPeriodEnd = new DateOnly(2026, 12, 31),
            ParserVersion = NportFiling.CurrentParserVersion,
        };
        filing.Holdings.Add(Line(filing, cusip, balance));
        DbContext.Add(filing);
        return filing;
    }

    private static NportHolding Line(
        NportFiling filing,
        string cusip,
        decimal balance,
        string isin = null,
        string payoff = "Long",
        string units = "NS"
    ) =>
        new()
        {
            NportFilingId = filing.Id,
            Name = "Holding",
            Cusip = cusip,
            Isin = isin,
            Balance = balance,
            Units = units,
            ValueUsd = balance * 100,
            PercentValue = 1m,
            PayoffProfile = payoff,
        };

    private void FundReport(
        string seriesId,
        string registrantCik,
        NCenFundTypes types,
        DateOnly? period = null,
        bool listed = false,
        bool superseded = false,
        bool extraFund = false
    )
    {
        var ending = period ?? new DateOnly(2025, 12, 31);
        var accession = $"ncen-{++_reports:D6}";
        var report = new NCenRegistrantReport
        {
            AccessionNumber = accession,
            RegistrantCik = registrantCik,
            RegistrantName = "Registrant " + registrantCik,
            InvestmentCompanyType = "N-1A",
            FilingDate = ending.AddMonths(2),
            ReportEndingPeriod = ending,
        };
        DbContext.Add(report);
        var count = extraFund ? 2 : 1;
        for (var index = 0; index < count; index++)
        {
            var fund = new NCenFundReport
            {
                RegistrantReportId = report.Id,
                FundId = $"{accession}-F{index}",
                SeriesId = seriesId,
                FundName = "Fund",
                RegistrantCik = registrantCik,
                AccessionNumber = accession,
                FilingDate = report.FilingDate,
                ReportEndingPeriod = ending,
                FundTypes = types,
                IsSuperseded = superseded,
            };
            if (listed)
                fund.ExchangeListings.Add(
                    new NCenFundExchangeListing
                    {
                        FundReportId = fund.Id,
                        Exchange = "XNYS",
                        Ticker = "ACTV",
                    }
                );
            DbContext.Add(fund);
        }
    }
}
