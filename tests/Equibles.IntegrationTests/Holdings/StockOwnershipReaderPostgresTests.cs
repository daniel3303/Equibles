using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The unified holder list reads every lane from Postgres: 13F quarters, 13D and 13G filings
/// with their cover pages, Forms 4 and N-PORT reports, nested by the stored rollups the link
/// refresh writes from 13F notices and other-manager lists.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockOwnershipReaderPostgresTests : ParadeDbMcpTestBase
{
    // Q3 2026 has closed, so the link refresh reads the Q3 notices; NVDA's newest 13F is Q2.
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateOnly Q1 = new(2026, 3, 31);
    private static readonly DateOnly Q2 = new(2026, 6, 30);
    private static readonly DateOnly Q3 = new(2026, 9, 30);
    private const string NvdaCusip = "67066G104";
    private const long SharesOutstanding = 10_000_000;

    private EquityIssuer _nvda;
    private EquityIssuer _other;

    public StockOwnershipReaderPostgresTests(ParadeDbFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Seed();
        var links = new HolderLinkRefreshService(
            Fixture.CreateDbContext(),
            [],
            NullLogger<HolderLinkRefreshService>()
        );
        await links.Run(Today, CancellationToken.None);
    }

    [Fact]
    public async Task Read_NestsByConfirmedRollupsAndSumsEachHolderOnce()
    {
        var read = await Reader().Read(_nvda, "NVDA", complete: true);
        var rows = read.Rows.ToDictionary(row => (row.Source, row.Name));

        // The link refresh reads Russell's listing of BlackRock's 028 as unconfirmed, and 777's
        // Q3 notice as reporting through FMR.
        (await RollupOf("2012383"))
            ?.ReportingParentCik.Should()
            .BeNull();
        (await RollupOf("777")).ReportingParentCik.Should().Be("315066");

        var fmr = rows[(StockHolderSource.Form13F, "FMR LLC")];
        var fmrCo = rows[(StockHolderSource.Schedule13G, "Fidelity Management & Research Co")];
        var fund = rows[(StockHolderSource.Fund, "Fidelity Contrafund")];
        var blackRock = rows[(StockHolderSource.Schedule13G, "BlackRock, Inc.")];
        var spunOut = rows[(StockHolderSource.Schedule13G, "Spun Out Advisers")];
        var russell = rows[(StockHolderSource.Form13F, "Russell Investments")];

        fmrCo.ParentRowKey.Should().Be(fmr.RowKey);
        fund.ParentRowKey.Should().Be(fmr.RowKey);
        fmr.Children.Should().BeEquivalentTo([fmrCo, fund]);
        blackRock.ParentRowKey.Should().BeNull("Russell's 13F lists BlackRock's 028, unconfirmed");
        russell.Children.Should().BeEmpty();
        // 777 filed its own Q2 13F, so FMR's Q2 report does not hold it whatever its Q3 notice says.
        spunOut.ParentRowKey.Should().BeNull();
        spunOut.Covering13FCik.Should().BeNull();

        read.Summary.SharesOutstanding.Should().Be(SharesOutstanding);
        read.Summary.Institutional.Shares.Should().Be(1_350_000);
        read.Summary.Institutional.Holders.Should().Be(3);
        read.Summary.Insiders.Shares.Should().Be(800_000);
        read.Summary.Insiders.Holders.Should().Be(1);
        read.Summary.Schedule13DGOnly.Shares.Should().Be(1_100_000);
        read.Summary.Schedule13DGOnly.Holders.Should().Be(3);
        read.Summary.Remaining.Shares.Should().Be(6_750_000);
        read.Summary.Remaining.PercentOutstanding.Should().Be(67.5m);
        read.Summary.Funds.Shares.Should().Be(70_000);
        fmrCo.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        rows[(StockHolderSource.Insider, "Family Office Founder")]
            .Has(StockHolderRowFlags.NotSummed)
            .Should()
            .BeTrue("the insider files the 13F the institutional line already counts");

        // 777 is its own holder for Q2, but its ultimate parent is still FMR, so its counted
        // block joins FMR's group; FMR Co's contained 13G never counts twice.
        read.UltimateGroups.Sum(group => group.Shares).Should().Be(3_250_000);
        var fmrGroup = read.UltimateGroups.Single(group => group.UltimateCik == "315066");
        fmrGroup.Members.Should().BeEquivalentTo([fmr, spunOut]);
        fmrGroup.Shares.Should().Be(1_500_000);
    }

    [Fact]
    public async Task ChangeNeverCrossesSources_EachRowComparesWithItsOwnLane()
    {
        var read = await Reader().Read(_nvda, "NVDA", complete: true);
        var rows = read.Rows.ToDictionary(row => (row.Source, row.Name));

        var fmr = rows[(StockHolderSource.Form13F, "FMR LLC")];
        fmr.ChangeShares.Should().Be(200_000);
        fmr.PriorAsOf.Should().Be(Q1);
        // FMR Co's 13G compares with its previous 13G, never with a 13F.
        var fmrCo = rows[(StockHolderSource.Schedule13G, "Fidelity Management & Research Co")];
        fmrCo.ChangeShares.Should().Be(100_000);
        fmrCo.PriorAsOf.Should().Be(new DateOnly(2025, 11, 1));
        var founder = rows[(StockHolderSource.Insider, "Jensen Founder")];
        founder.ChangeShares.Should().Be(-100_000);
        founder.Has(StockHolderRowFlags.IssuerWide).Should().BeTrue();
        var fund = rows[(StockHolderSource.Fund, "Fidelity Contrafund")];
        fund.ChangeShares.Should().Be(10_000);
        fund.PriorAsOf.Should().Be(new DateOnly(2026, 4, 30));
        // Russell filed no 13F the quarter before, so it is not compared.
        rows[(StockHolderSource.Form13F, "Russell Investments")].ChangeShares.Should().BeNull();
        // A 13D without an earlier one for the listing has no change.
        rows[(StockHolderSource.Schedule13D, "Activist Partners")].ChangeShares.Should().BeNull();
    }

    [Fact]
    public async Task Read_ACachedReadKeepsTheSummaryAndTheFirstDefaultRows()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var first = await Reader(cache).Read(_nvda, "NVDA");
        var second = await Reader(cache).Read(_nvda, "NVDA");

        first.Complete.Should().BeTrue();
        second.Complete.Should().BeFalse();
        second.Rows.Should().BeEmpty();
        second
            .DefaultRows.Select(row => row.RowKey)
            .Should()
            .Equal(first.DefaultRows.Select(row => row.RowKey));
        second.Summary.Should().BeSameAs(first.Summary);
    }

    [Fact]
    public async Task Tools_RenderTheListTheSummaryAndThe13DGRecord()
    {
        var tools = new StockOwnershipTools(
            new EquityIssuerRepository(DbContext),
            Reader(),
            new StockBeneficialOwnershipReader(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FixedTime(Today)
            ),
            ErrorManager,
            NullLogger<StockOwnershipTools>()
        );

        var holders = await tools.GetAllHolders("NVDA");
        holders
            .Should()
            .Contain(
                "| 1 | FMR LLC | CIK 315066 | 13F | 2026-06-30 | 1,200,000 | +200,000 | 12.00% |"
            );
        holders.Should().Contain("| 1.1 | ↳ Fidelity Management & Research Co | CIK 35368 | 13G |");
        holders.Should().Contain("↳ Fidelity Contrafund | S000006037 | Fund |");

        var summary = await tools.GetOwnershipSummary("NVDA", "ultimate");
        summary.Should().Contain("| Institutional (13F) | 3 | 1,350,000 | 13.50% | yes |");
        summary.Should().Contain("| Remaining | — | 6,750,000 | 67.50% | rest |");
        summary.Should().Contain("### Largest ultimate parents");

        var record = await tools.GetBeneficialOwnership("NVDA");
        record.Should().Contain("### 1. BlackRock, Inc. (CIK 2012383)");
        record.Should().Contain("| Activist Partners LP (lead) | 900 | PN |");
        record.Should().Contain("| Activist GP LLC | 901 | OO |");
        record.Should().Contain("Reporting persons are not additive");

        (await tools.GetAllHolders("NVDA", source: "bogus")).Should().StartWith("Unknown source");
    }

    [Fact]
    public async Task Read_AForeignPrivateIssuerHasNoSharesOutstandingOrRemaining()
    {
        var read = await Reader(foreignPrivateIssuer: true).Read(_nvda, "NVDA", complete: true);

        read.Summary.SharesOutstanding.Should().BeNull();
        read.Summary.Remaining.Should().BeNull();
        read.Summary.Institutional.Shares.Should().Be(1_350_000);
        read.Summary.Institutional.PercentOutstanding.Should().BeNull();
        read.Rows.Should().OnlyContain(row => row.PercentOutstanding == null);

        var tools = new StockOwnershipTools(
            new EquityIssuerRepository(DbContext),
            Reader(foreignPrivateIssuer: true),
            new StockBeneficialOwnershipReader(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FixedTime(Today)
            ),
            ErrorManager,
            NullLogger<StockOwnershipTools>()
        );
        var summary = await tools.GetOwnershipSummary("NVDA");
        summary.Should().NotContain("Shares outstanding");
        summary.Should().NotContain("| Remaining |");
        summary
            .Should()
            .Contain("_% Out is unavailable: a foreign private issuer reports ordinary shares");
        (await tools.GetAllHolders("NVDA")).Should().NotContain("shares outstanding.");
    }

    [Fact]
    public async Task Purposes_DescribeEach13DOnTheListAndTheRecord()
    {
        var purposes = new StubPurposes("d-act-1", "Control or board");
        var tools = new StockOwnershipTools(
            new EquityIssuerRepository(DbContext),
            Reader(purposeSource: purposes),
            new StockBeneficialOwnershipReader(
                new InstitutionalHoldingRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FixedTime(Today),
                purposes
            ),
            ErrorManager,
            NullLogger<StockOwnershipTools>()
        );

        var holders = await tools.GetAllHolders("NVDA", source: "13d");
        holders.Should().Contain("purpose: Control or board");
        var record = await tools.GetBeneficialOwnership("NVDA");
        record.Should().Contain("Purpose (Item 4): Control or board");
        record.Split("Purpose (Item 4)").Should().HaveCount(2, "a 13G states no Item 4 purpose");
        purposes
            .Requested.Should()
            .OnlyContain(accession => accession == "d-act-1", "only 13D accessions are described");
    }

    private sealed class StubPurposes(string accession, string purpose)
        : IBeneficialOwnershipPurposeSource
    {
        public List<string> Requested { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> Describe(
            IReadOnlyCollection<string> accessionNumbers,
            CancellationToken cancellationToken = default
        )
        {
            Requested.AddRange(accessionNumbers);
            IReadOnlyDictionary<string, string> described = accessionNumbers
                .Where(requested => requested == accession)
                .ToDictionary(requested => requested, _ => purpose);
            return Task.FromResult(described);
        }
    }

    private StockOwnershipReader Reader(
        IMemoryCache cache = null,
        bool foreignPrivateIssuer = false,
        IBeneficialOwnershipPurposeSource purposeSource = null
    )
    {
        var holdingRepository = new InstitutionalHoldingRepository(DbContext);
        var holderRepository = new InstitutionalHolderRepository(DbContext);
        var rollupReader = new HolderRollupReader(holderRepository);
        var shares = Substitute.For<ISharesOutstandingProvider>();
        shares
            .GetCurrentSharesOutstanding(Arg.Any<EquityIssuer>(), Arg.Any<CancellationToken>())
            .Returns(SharesOutstanding);
        shares
            .IsForeignPrivateIssuer(Arg.Any<EquityIssuer>(), Arg.Any<CancellationToken>())
            .Returns(foreignPrivateIssuer);
        return new StockOwnershipReader(
            [
                new Form13FContributor(holdingRepository, holderRepository),
                new Schedule13DGContributor(holdingRepository),
                new InsiderContributor(new InsiderTransactionRepository(DbContext)),
                new FundContributor(
                    new NportFilingRepository(DbContext),
                    new FundSeriesRepository(DbContext),
                    rollupReader
                ),
            ],
            holdingRepository,
            holderRepository,
            new StockSplitRepository(DbContext),
            new StockCombinedQuarterService(holdingRepository, new StockSplitRepository(DbContext)),
            rollupReader,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            shares,
            new FixedTime(Today),
            purposeSource
        );
    }

    private async Task<HolderRollup> RollupOf(string cik)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<HolderRollup>().SingleOrDefaultAsync(rollup => rollup.Cik == cik);
    }

    private async Task Seed()
    {
        _nvda = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "NVIDIA Corp",
            Cik: "0001045810",
            Cusip: NvdaCusip
        );
        _other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHR",
            Name: "Other Corp",
            Cik: "0000000999",
            Cusip: "000000AA1"
        );
        DbContext.AddRange(_nvda, _other);

        var fmr = Holder("315066", "FMR LLC", "028-00451");
        var fmrCo = Holder("35368", "Fidelity Management & Research Co", "028-00450");
        var spunOut = Holder("777", "Spun Out Advisers", null);
        var blackRock = Holder("2012383", "BlackRock, Inc.", "028-24466");
        var russell = Holder("1692234", "Russell Investments", null);
        var familyOffice = Holder("1100", "Family Office Founder LLC", null);
        var activist = Holder("900", "Activist Partners", null);

        // 13F rows: FMR both quarters, Russell and the family office in Q2 only.
        Form13F(_nvda, fmr, Q1, 1_000_000);
        Form13F(_nvda, fmr, Q2, 1_200_000);
        Form13F(_nvda, russell, Q2, 50_000);
        Form13F(_nvda, familyOffice, Q2, 100_000);
        // 777 filed its own full 13F for Q2, on another stock.
        Form13F(_other, spunOut, Q2, 5_000);

        Filing(fmr, "fmr-q2", Q2);
        Filing(russell, "ru-q2", Q2);
        Filing(spunOut, "so-q2", Q2);
        // Shaped on Russell: its 13F lists BlackRock, Inc.'s file number and BlackRock never names it.
        OtherManager("ru-q2", 1, fileNumber: "028-24466");
        // FMR Co reports through FMR by its Q2 notice; 777 by a Q3 notice after its own Q2 13F.
        Notice("fmrco-q2", "35368", Q2, "000108281", ["315066"]);
        Notice("so-q3", "777", Q3, null, ["315066"]);

        Schedule(
            _nvda,
            fmrCo,
            FilingType.Schedule13G,
            new DateOnly(2025, 11, 1),
            400_000,
            "g-fmrco-1"
        );
        Schedule(
            _nvda,
            fmrCo,
            FilingType.Schedule13G,
            new DateOnly(2026, 8, 10),
            500_000,
            "g-fmrco-2"
        );
        Schedule(
            _nvda,
            blackRock,
            FilingType.Schedule13G,
            new DateOnly(2026, 7, 15),
            600_000,
            "g-br-1",
            ("BlackRock, Inc.", null, ReportingPersonTypes.ParentHoldingCompany, true)
        );
        Schedule(
            _nvda,
            spunOut,
            FilingType.Schedule13G,
            new DateOnly(2026, 8, 20),
            300_000,
            "g-so-1"
        );
        Schedule(
            _nvda,
            activist,
            FilingType.Schedule13D,
            new DateOnly(2025, 11, 3),
            200_000,
            "d-act-1",
            ("Activist Partners LP", "900", ReportingPersonTypes.Partnership, true),
            ("Activist GP LLC", "901", ReportingPersonTypes.Other, false)
        );

        var founder = Owner("1197647", "Jensen Founder");
        Form4(founder, new DateOnly(2026, 3, 1), 900_000, "f4-jf-1");
        Form4(founder, new DateOnly(2026, 9, 2), 800_000, "f4-jf-2");
        var familyInsider = Owner("1100", "Family Office Founder");
        Form4(familyInsider, new DateOnly(2026, 9, 10), 150_000, "f4-fo-1");

        AddFundReport("S000006037", "24238", 108281);
        Fund(
            "S000006037",
            "Fidelity Contrafund",
            new DateOnly(2026, 4, 30),
            60_000,
            isLatest: false
        );
        Fund(
            "S000006037",
            "Fidelity Contrafund",
            new DateOnly(2026, 7, 31),
            70_000,
            isLatest: true
        );

        await DbContext.SaveChangesAsync();
    }

    private InstitutionalHolder Holder(string cik, string name, string fileNumber)
    {
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = name,
            Form13FFileNumber = fileNumber,
        };
        DbContext.Add(holder);
        return holder;
    }

    private void Form13F(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly quarter,
        long shares
    ) =>
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = quarter,
                FilingDate = quarter.AddDays(40),
                Shares = shares,
                Value = shares * 100,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                FilingType = FilingType.Form13F,
                AccessionNumber = $"13f-{holder.Cik}-{quarter:yyyyMMdd}",
            }
        );

    private void Filing(InstitutionalHolder holder, string accession, DateOnly quarter) =>
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = accession,
                InstitutionalHolderId = holder.Id,
                ReportDate = quarter,
                FilingDate = quarter.AddDays(40),
                FilingType = FilingType.Form13F,
                PositionCount = 1,
                TotalValue = 1_000_000L,
            }
        );

    private void OtherManager(string accession, int sequence, string fileNumber) =>
        DbContext.Add(
            new FilingOtherManager
            {
                AccessionNumber = accession,
                Direction = OtherManagerDirection.IncludedInReport,
                SequenceNumber = sequence,
                Form13FFileNumber = fileNumber,
            }
        );

    private void Notice(
        string accession,
        string filerCik,
        DateOnly quarter,
        string crd,
        string[] reporters
    )
    {
        var notice = new Form13FNotice
        {
            AccessionNumber = accession,
            FilerCik = filerCik,
            CrdNumber = crd,
            ReportDate = quarter,
            FilingDate = quarter.AddDays(40),
            DataSetFileName = "01sep2026-30nov2026_form13f.zip",
        };
        for (var ordinal = 0; ordinal < reporters.Length; ordinal++)
            notice.Reporters.Add(
                new Form13FNoticeReporter
                {
                    Form13FNoticeId = notice.Id,
                    Ordinal = ordinal + 1,
                    Cik = reporters[ordinal],
                }
            );
        DbContext.Add(notice);
    }

    private void Schedule(
        EquityIssuer stock,
        InstitutionalHolder holder,
        FilingType type,
        DateOnly eventDate,
        long shares,
        string accession,
        params (string Name, string Cik, ReportingPersonTypes Types, bool Lead)[] persons
    )
    {
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = eventDate,
                FilingDate = eventDate.AddDays(10),
                Shares = shares,
                Value = 0,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                FilingType = type,
                AccessionNumber = accession,
            }
        );
        var filing = new BeneficialOwnershipFiling
        {
            AccessionNumber = accession,
            FilingType = type,
            FilingDate = eventDate.AddDays(10),
            DateOfEvent = eventDate,
            FilerCik = holder.Cik,
            IssuerCik = stock.Cik,
            IssuerCusip = NvdaCusip,
            Rules =
                type == FilingType.Schedule13G
                    ? Schedule13GRules.QualifiedInstitution
                    : Schedule13GRules.None,
        };
        for (var ordinal = 0; ordinal < persons.Length; ordinal++)
            filing.ReportingPersons.Add(
                new BeneficialOwnershipReportingPerson
                {
                    BeneficialOwnershipFilingId = filing.Id,
                    Ordinal = ordinal + 1,
                    Name = persons[ordinal].Name,
                    Cik = persons[ordinal].Cik,
                    Types = persons[ordinal].Types,
                    AggregateAmountOwned = shares,
                    PercentOfClass = 100m * shares / SharesOutstanding,
                    SoleVotingPower = persons[ordinal].Lead ? shares : 0,
                    SharedDispositivePower = shares,
                    IsLead = persons[ordinal].Lead,
                }
            );
        DbContext.Add(filing);
    }

    private InsiderOwner Owner(string cik, string name)
    {
        var owner = new InsiderOwner
        {
            OwnerCik = cik,
            Name = name,
            IsTenPercentOwner = true,
        };
        DbContext.Add(owner);
        return owner;
    }

    private void Form4(InsiderOwner owner, DateOnly date, long ownedAfter, string accession) =>
        DbContext.Add(
            new InsiderTransaction
            {
                EquityIssuerId = _nvda.Id,
                InsiderOwnerId = owner.Id,
                TransactionDate = date,
                FilingDate = date.AddDays(2),
                TransactionCode = TransactionCode.Sale,
                Shares = 100,
                PricePerShare = 50m,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                SharesOwnedAfter = ownedAfter,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Common Stock",
                AccessionNumber = accession,
                SecurityKind = InsiderSecurityKind.NonDerivative,
            }
        );

    private void Fund(string seriesId, string name, DateOnly period, decimal balance, bool isLatest)
    {
        var filing = new NportFiling
        {
            RegistrantCik = "24238",
            RegistrantName = "Fidelity Contrafund Trust",
            AccessionNumber = $"nport-{seriesId}-{period:yyyyMMdd}",
            FilingDate = period.AddDays(55),
            SeriesId = seriesId,
            SeriesName = name,
            ReportPeriodDate = period,
            ReportPeriodEnd = new DateOnly(2026, 12, 31),
            ParserVersion = NportFiling.CurrentParserVersion,
        };
        filing.Holdings.Add(
            new NportHolding
            {
                NportFilingId = filing.Id,
                Name = "NVIDIA Corp",
                Cusip = NvdaCusip,
                Balance = balance,
                Units = FundContributor.SharesUnit,
                ValueUsd = balance * 100,
                PercentValue = 1.5m,
                PayoffProfile = "Long",
            }
        );
        DbContext.Add(filing);
        if (isLatest)
            DbContext.Add(
                new FundSeries
                {
                    IdentityKey = $"rc:24238:{seriesId}",
                    Slug = "fidelity-contrafund",
                    RegistrantCik = "24238",
                    SeriesId = seriesId,
                    SeriesName = name,
                    RegistrantName = filing.RegistrantName,
                    LatestReportPeriodDate = period,
                    LatestFilingDate = filing.FilingDate,
                    LatestNportFilingId = filing.Id,
                }
            );
    }

    private void AddFundReport(string seriesId, string registrantCik, int crd)
    {
        var period = new DateOnly(2025, 12, 31);
        var accession = "ncen-" + seriesId;
        var report = new NCenRegistrantReport
        {
            AccessionNumber = accession,
            RegistrantCik = registrantCik,
            RegistrantName = "Registrant " + registrantCik,
            InvestmentCompanyType = "N-1A",
            FilingDate = period.AddMonths(2),
            ReportEndingPeriod = period,
        };
        var fund = new NCenFundReport
        {
            RegistrantReportId = report.Id,
            FundId = accession + "-F1",
            SeriesId = seriesId,
            FundName = "Fund " + seriesId,
            RegistrantCik = registrantCik,
            AccessionNumber = accession,
            FilingDate = report.FilingDate,
            ReportEndingPeriod = period,
        };
        fund.Advisers.Add(
            new NCenFundAdviser
            {
                FundReportId = fund.Id,
                Role = NCenAdviserRole.InvestmentAdviser,
                Name = "Adviser " + crd,
                Crd = crd,
            }
        );
        DbContext.AddRange(report, fund);
    }

    private sealed class FixedTime(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
