using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The earnings-release statement lane end to end on CareTrust's release of 5 November 2026:
/// the reading is stored, served only until a periodic report covers the period, and shown by
/// the fact and statement tools as a block of its own.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ReleaseStatementsLaneTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly ReleaseFiled = new(2026, 11, 5);
    private static readonly DateOnly PriorReportFiled = new(2025, 11, 6);
    private static readonly DateOnly Q3Start2026 = new(2026, 7, 1);
    private static readonly DateOnly Q3End2026 = new(2026, 9, 30);

    private readonly Dictionary<string, FinancialConcept> _concepts = [];

    public ReleaseStatementsLaneTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Read_StoresTheFiguresTheReleaseStatesAheadOfThePeriodicReport()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);

        var placed = await Reader().Read(release, CancellationToken.None);

        DbContext.ChangeTracker.Clear();
        var read = await DbContext
            .Set<ReleaseStatementRead>()
            .Include(r => r.Figures)
            .SingleAsync(r => r.DocumentId == release.Id);
        read.MatcherVersion.Should().Be(ReleaseStatementRead.CurrentMatcherVersion);
        read.FigureCount.Should().Be(placed).And.Be(read.Figures.Count).And.BeGreaterThan(10);
        read.Figures.Should()
            .ContainSingle(f =>
                f.FinancialConceptId == _concepts["Revenues"].Id && f.PeriodStart == Q3Start2026
            )
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Value = 176_360_000m,
                    PeriodEnd = Q3End2026,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    SourceLabel = "Total revenues",
                    FiledDate = ReleaseFiled,
                    DocumentId = release.Id,
                }
            );
    }

    [Fact]
    public async Task Read_ReleaseReadAgain_ReplacesItsReading()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        var first = await Reader().Read(release, CancellationToken.None);

        var second = await Reader().Read(release, CancellationToken.None);

        second.Should().Be(first);
        (await DbContext.Set<ReleaseStatementRead>().CountAsync()).Should().Be(1);
        (await DbContext.Set<ReleaseStatementFigure>().CountAsync()).Should().Be(first);
    }

    [Fact]
    public async Task Read_ReleaseWithNoStatement_StoresAnEmptyReading()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, "The company announced a quarterly dividend.");

        var placed = await Reader().Read(release, CancellationToken.None);

        placed.Should().Be(0);
        (await DbContext.Set<ReleaseStatementRead>().SingleAsync()).FigureCount.Should().Be(0);
    }

    [Fact]
    public async Task Unread_OffersAReleaseUntilItIsReadAtTheCurrentVersion()
    {
        var stock = await SeedCompanyOnFile();
        var fresh = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        var readAlready = await SeedRelease(stock, "Read under the current rule.");
        var readUnderAnOlderRule = await SeedRelease(stock, "Read under an older rule.");
        var beforeTheWindow = await SeedRelease(stock, "Too old.", new DateOnly(2026, 5, 5));
        await Reader().Read(readAlready, CancellationToken.None);
        await Reader().Read(readUnderAnOlderRule, CancellationToken.None);
        await DbContext
            .Set<ReleaseStatementRead>()
            .Where(r => r.DocumentId == readUnderAnOlderRule.Id)
            .ExecuteUpdateAsync(r =>
                r.SetProperty(
                    read => read.MatcherVersion,
                    ReleaseStatementRead.CurrentMatcherVersion - 1
                )
            );

        var unread = await ReleaseStatementsWorker
            .Unread(
                new DocumentRepository(DbContext),
                new ReleaseStatementReadRepository(DbContext),
                new DateOnly(2026, 7, 8)
            )
            .Select(d => d.Id)
            .ToListAsync();

        unread.Should().BeEquivalentTo([fresh.Id, readUnderAnOlderRule.Id]);
        unread.Should().NotContain(beforeTheWindow.Id);
    }

    [Fact]
    public async Task GetUncovered_PeriodicReportCoversThePeriod_StopsServingTheRelease()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        await Reader().Read(release, CancellationToken.None);
        var figures = new ReleaseStatementFigureRepository(DbContext);
        var facts = new FinancialFactRepository(DbContext);
        (await figures.GetUncovered(stock.Id, facts)).Should().NotBeEmpty();

        // The 10-Q arrives a day off the release's derived period start and at another value.
        AddFact(
            stock,
            "Revenues",
            Q3Start2026.AddDays(1),
            Q3End2026,
            176_000_000m,
            SecFiscalPeriod.Q3,
            2026,
            new DateOnly(2026, 11, 9)
        );
        await DbContext.SaveChangesAsync();

        (await figures.GetUncovered(stock.Id, facts)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFinancialFact_ReleaseFigureOnFile_ListsItAsPreliminaryUnderTheFacts()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        await Reader().Read(release, CancellationToken.None);

        var result = await Facts().GetFinancialFact("CTRE", "revenue");

        var block = result.IndexOf(
            "Preliminary, from the earnings release",
            StringComparison.Ordinal
        );
        block.Should().BePositive();
        // The filed quarters stay in the tool's own table; the release's figures follow it.
        result[..block].Should().Contain("$132,444,000").And.NotContain("$176,360,000");
        result[block..]
            .Should()
            .Contain(
                "| 2026-07-01 | 2026-09-30 | 2026 | Q3 | $176,360,000 | USD | Total revenues (line 13) | 2026-11-05 |"
            )
            .And.Contain("$480,491,000")
            .And.Contain(release.Id.ToString());
    }

    [Fact]
    public async Task GetFinancialFact_WindowEndingBeforeTheRelease_OmitsItsFigures()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        await Reader().Read(release, CancellationToken.None);

        var result = await Facts().GetFinancialFact("CTRE", "revenue", toDate: "2026-06-30");

        result.Should().NotContain("Preliminary").And.NotContain("176,360,000");
    }

    [Fact]
    public async Task GetFinancialStatement_ReleaseFiguresOnFile_ListsThemByStatementLine()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        await Reader().Read(release, CancellationToken.None);

        var result = await Statements().GetFinancialStatement("CTRE", "income");

        var block = result.IndexOf(
            "Preliminary, from the earnings release",
            StringComparison.Ordinal
        );
        block.Should().BePositive();
        result[..block].Should().NotContain("176,360,000");
        result[block..]
            .Should()
            .Contain(
                "| 2026-07-01 | 2026-09-30 | 2026 | Q3 | $176,360,000 | USD | Total revenues (line 13) |"
            )
            .And.Contain("$83,653,000");
    }

    [Fact]
    public async Task GetFinancialFact_SplitAfterTheRelease_LeavesItsPerShareFigureOut()
    {
        var stock = await SeedCompanyOnFile();
        var release = await SeedRelease(stock, CareTrustRelease.IncomeStatement);
        await Reader().Read(release, CancellationToken.None);
        var read = await DbContext.Set<ReleaseStatementRead>().SingleAsync();
        var eps = Concept("EarningsPerShareDiluted");
        AddFact(
            stock,
            "EarningsPerShareDiluted",
            new(2025, 7, 1),
            new(2025, 9, 30),
            0.35m,
            SecFiscalPeriod.Q3,
            unit: "USD/shares"
        );
        DbContext
            .Set<ReleaseStatementFigure>()
            .Add(
                new ReleaseStatementFigure
                {
                    ReleaseStatementReadId = read.Id,
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = eps.Id,
                    DocumentId = release.Id,
                    FiledDate = ReleaseFiled,
                    Unit = "USD/shares",
                    PeriodStart = Q3Start2026,
                    PeriodEnd = Q3End2026,
                    Value = 0.35m,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    SourceLine = 37,
                    SourceLabel = "Diluted",
                }
            );
        await DbContext.SaveChangesAsync();
        (await Facts().GetFinancialFact("CTRE", "eps-diluted"))
            .Should()
            .Contain("Diluted (line 37)");

        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = stock.Presentation.EquityListingId,
                    PriceSeriesTicker = "CTRE",
                    EffectiveDate = ReleaseFiled.AddDays(4),
                    Numerator = 2,
                    Denominator = 1,
                    Source = StockSplitSource.Yahoo,
                }
            );
        await DbContext.SaveChangesAsync();

        (await Facts().GetFinancialFact("CTRE", "eps-diluted"))
            .Should()
            .NotContain("Diluted (line 37)");
    }

    private ReleaseStatementReadService Reader() =>
        new(
            new FinancialFactRepository(DbContext),
            new ReportedFinancialStatementRepository(DbContext),
            new ReleaseStatementReadRepository(DbContext),
            FileManagerTestFactory.Create(new FileRepository(DbContext))
        );

    private FinancialFactsTools Facts() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>(),
            new DocumentRepository(DbContext),
            new ReleaseStatementFigureRepository(DbContext)
        );

    private FinancialStatementTools Statements() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>(),
            new DocumentRepository(DbContext),
            new ReleaseStatementFigureRepository(DbContext)
        );

    // CareTrust as it stood before the release: the third quarter and nine months of 2025 from
    // that year's 10-Q with its income statement as filed, and the second quarter of 2026.
    private async Task<EquityIssuer> SeedCompanyOnFile()
    {
        var stock = EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CTRE",
            Name: "CareTrust REIT, Inc.",
            Cik: "0001590717"
        );
        DbContext.Set<EquityIssuer>().Add(stock);

        foreach (var (tag, _, quarter, nineMonths) in CareTrustRelease.Flows)
        {
            AddFact(
                stock,
                tag,
                new(2025, 7, 1),
                new(2025, 9, 30),
                quarter * 1_000m,
                SecFiscalPeriod.Q3
            );
            AddFact(
                stock,
                tag,
                new(2025, 1, 1),
                new(2025, 9, 30),
                nineMonths * 1_000m,
                SecFiscalPeriod.Q3
            );
        }
        AddFact(
            stock,
            "Revenues",
            new(2026, 4, 1),
            new(2026, 6, 30),
            160_000_000m,
            SecFiscalPeriod.Q2,
            2026,
            new DateOnly(2026, 8, 6)
        );

        var priorReport = AddDocument(stock, DocumentType.TenQ, PriorReportFiled, "10-Q", null);
        DbContext
            .Set<ReportedFinancialStatement>()
            .Add(
                new ReportedFinancialStatement
                {
                    EquityIssuerId = stock.Id,
                    Document = priorReport,
                    AccessionNumber = "0001590717-25-000101",
                    Kind = ReportedStatementKind.Income,
                    RoleUri = "http://caretrustreit.com/role/ConsolidatedIncomeStatements",
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    PrimaryPeriodEnd = new DateOnly(2025, 9, 30),
                    Form = DocumentType.TenQ,
                    FiledDate = PriorReportFiled,
                    Payload = JsonConvert.SerializeObject(
                        new ReportedStatementPayload
                        {
                            Rows = CareTrustRelease
                                .Flows.Select(line => new ReportedStatementRow
                                {
                                    Label = line.Label,
                                    Taxonomy = line.Tag.Contains(':') ? "ctre" : "us-gaap",
                                    Concept = line.Tag.Split(':')[^1],
                                    Values = [line.Quarter],
                                })
                                .ToList(),
                        }
                    ),
                }
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    private async Task<Document> SeedRelease(
        EquityIssuer stock,
        string text,
        DateOnly? filed = null
    )
    {
        var release = AddDocument(
            stock,
            DocumentType.EightK,
            filed ?? ReleaseFiled,
            text,
            "2.02,9.01"
        );
        await DbContext.SaveChangesAsync();
        return release;
    }

    private Document AddDocument(
        EquityIssuer stock,
        DocumentType type,
        DateOnly filed,
        string text,
        string items
    )
    {
        var content = new FileContent { Bytes = Encoding.UTF8.GetBytes(text) };
        var file = new File
        {
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = content.Bytes.Length,
            FileContent = content,
        };
        content.FileId = file.Id;
        DbContext.Set<File>().Add(file);
        var document = new Document
        {
            EquityIssuerId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = type,
            ReportingDate = filed,
            ReportingForDate = filed,
            Items = items,
            LineCount = text.Split('\n').Length,
        };
        DbContext.Set<Document>().Add(document);
        return document;
    }

    private FinancialConcept Concept(string tag)
    {
        if (_concepts.TryGetValue(tag, out var concept))
            return concept;
        concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = tag.Contains(':') ? FactTaxonomy.Custom : FactTaxonomy.UsGaap,
            Tag = tag,
            Label = tag,
            Balance = ConceptBalance.Debit,
        };
        DbContext.Set<FinancialConcept>().Add(concept);
        return _concepts[tag] = concept;
    }

    private void AddFact(
        EquityIssuer stock,
        string tag,
        DateOnly start,
        DateOnly end,
        decimal value,
        SecFiscalPeriod period,
        int fiscalYear = 2025,
        DateOnly? filed = null,
        string unit = "USD"
    ) =>
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = Concept(tag).Id,
                    Unit = unit,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = start,
                    PeriodEnd = end,
                    Value = value,
                    FiscalYear = fiscalYear,
                    FiscalPeriod = period,
                    Form = DocumentType.TenQ,
                    FiledDate = filed ?? PriorReportFiled,
                    AccessionNumber =
                        $"0001590717-{(filed ?? PriorReportFiled):yyMMdd}-{start:MMdd}",
                }
            );
}
