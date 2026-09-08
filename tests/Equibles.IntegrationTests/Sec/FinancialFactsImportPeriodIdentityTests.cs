using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

// Pins #982: SEC stamps every value inside a 10-K with the filing's own
// fy/fp identity, even though comparable-year values inside the same filing
// measure different actual periods. The resolver must label each row by
// the period it measures (derived from PeriodStart / PeriodEnd against
// CommonStock.FiscalYearEndMonth/Day) — not by the filing's identity.
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportPeriodIdentityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportPeriodIdentityTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var sharesProvider = Substitute.For<ISharesOutstandingProvider>();
        sharesProvider
            .GetCurrentSharesOutstanding(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<long?>(null));
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(ctx));
                sp.GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(ctx));
                sp.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(ctx));
                sp.GetService(typeof(ISharesOutstandingProvider)).Returns(sharesProvider);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task Import_HistoricalCalendarChange_UsesCapturedQuarterMetadataAndKeepsCurrentYearEnd()
    {
        var stock = new CommonStock
        {
            Ticker = "JHG",
            Name = "Janus Henderson Group",
            Cik = "0001274173",
            FiscalYearEndMonth = 6,
            FiscalYearEndDay = 30,
        };
        var annualEnd = new DateOnly(2025, 12, 31);
        var quarterEnd = new DateOnly(2026, 3, 31);
        const string envelope = """
            <html xmlns:dei="http://xbrl.sec.gov/dei/2026"><body>
            <xbrli:context id="quarter"><xbrli:entity>
            <xbrli:identifier scheme="http://www.sec.gov/CIK">0001274173</xbrli:identifier>
            </xbrli:entity><xbrli:period><xbrli:startDate>2026-01-01</xbrli:startDate>
            <xbrli:endDate>2026-03-31</xbrli:endDate></xbrli:period></xbrli:context>
            <ix:nonNumeric name="dei:CurrentFiscalYearEndDate" contextRef="quarter">--12-31</ix:nonNumeric>
            </body></html>
            """;
        var bytes = System.Text.Encoding.UTF8.GetBytes(envelope);
        Equibles.Media.Data.Models.File File() =>
            new()
            {
                Name = "filing",
                Extension = "txt",
                ContentType = "text/plain",
            };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(stock);
            seed.Add(
                new Document
                {
                    CommonStock = stock,
                    Content = File(),
                    DocumentType = DocumentType.TenK,
                    ReportingForDate = annualEnd,
                    ReportingDate = new DateOnly(2026, 2, 1),
                    AccessionNumber = "0001437749-26-005628",
                }
            );
            seed.Add(
                new Document
                {
                    CommonStock = stock,
                    Content = File(),
                    DocumentType = DocumentType.TenQ,
                    ReportingForDate = quarterEnd,
                    ReportingDate = new DateOnly(2026, 5, 1),
                    AccessionNumber = "0001437749-26-015926",
                    XbrlContent = File(),
                    XbrlStatus = XbrlCaptureStatus.Captured,
                    XbrlType = XbrlType.InlineIxbrl,
                    XbrlUncompressedSize = bytes.Length,
                }
            );
            await seed.SaveChangesAsync(CancellationToken.None);
        }
        CompanyFactValue Value(DateOnly? start, DateOnly end, string form) =>
            new()
            {
                Start = start,
                End = end,
                Val = 123m,
                Form = form,
                Fy = 2026,
                Fp = "Q3",
                Filed = new DateOnly(2026, 5, 1),
                Accn = "0001437749-26-015926",
            };
        var response = new CompanyFactsResponse
        {
            Cik = 1274173,
            EntityName = stock.Name,
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new()
                    {
                        Label = "Revenue",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                Value(new(2025, 1, 1), annualEnd, "10-K"),
                                Value(new(2025, 1, 1), new(2025, 3, 31), "10-Q"),
                                Value(new(2026, 1, 1), quarterEnd, "10-Q"),
                            ],
                        },
                    },
                    ["Assets"] = new()
                    {
                        Label = "Assets",
                        Units = new() { ["USD"] = [Value(null, quarterEnd, "10-Q")] },
                    },
                },
            },
        };
        var client = Substitute.For<ISecEdgarClient>();
        client.GetCompanyFacts(stock.Cik).Returns(response);
        var files = Substitute.For<Equibles.Media.BusinessLogic.IFileManager>();
        files
            .GetContent(Arg.Any<Equibles.Media.Data.Models.File>())
            .Returns(Equibles.Media.BusinessLogic.GzipCompressor.Compress(bytes));
        var scopes = CreateScopeFactory();
        var reader = new FiscalCalendarEvidenceReader(
            scopes,
            files,
            new Equibles.Sec.FinancialFacts.BusinessLogic.Parsers.InlineXbrlParser()
        );
        var sut = new FinancialFactsImportService(
            scopes,
            client,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            reader
        );
        await sut.Import(stock, CancellationToken.None);
        await using var verify = _fixture.CreateDbContext();
        var facts = await verify
            .Set<FinancialFact>()
            .Where(f => f.CommonStockId == stock.Id)
            .ToListAsync();
        facts.Should().HaveCount(4);
        facts
            .Where(f => f.PeriodEnd == quarterEnd)
            .Should()
            .OnlyContain(f =>
                f.FiscalYear == 2026 && f.FiscalPeriod == SecFiscalPeriod.Q1 && f.Value == 123m
            );
        facts
            .Single(f => f.PeriodEnd == new DateOnly(2025, 3, 31))
            .FiscalPeriod.Should()
            .Be(SecFiscalPeriod.Q1);
        var storedStock = await verify.Set<CommonStock>().SingleAsync(s => s.Id == stock.Id);
        storedStock.FiscalYearEndMonth.Should().Be(6);
        var checkpoint = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleAsync(s => s.CommonStockId == stock.Id);
        checkpoint.CalendarEvidenceFingerprint.Should().HaveLength(64);

        // A corrected source calendar must replay even when SEC's newest filed date is unchanged.
        var originalFingerprint = checkpoint.CalendarEvidenceFingerprint;
        var originalIds = facts.Select(f => f.Id).Order().ToArray();
        files
            .GetContent(Arg.Any<Equibles.Media.Data.Models.File>())
            .Returns(
                Equibles.Media.BusinessLogic.GzipCompressor.Compress(
                    System.Text.Encoding.UTF8.GetBytes(envelope.Replace("--12-31", "--06-30"))
                )
            );
        await sut.Import(stock, CancellationToken.None);
        verify.ChangeTracker.Clear();
        var replayed = await verify
            .Set<FinancialFact>()
            .Where(f => f.CommonStockId == stock.Id)
            .ToListAsync();
        replayed.Select(f => f.Id).Order().Should().Equal(originalIds);
        replayed
            .Where(f => f.PeriodEnd == quarterEnd)
            .Should()
            .OnlyContain(f =>
                f.FiscalYear == 2026 && f.FiscalPeriod == SecFiscalPeriod.Q3 && f.Value == 123m
            );
        var replayCheckpoint = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleAsync(s => s.CommonStockId == stock.Id);
        replayCheckpoint.CalendarEvidenceFingerprint.Should().NotBe(originalFingerprint);

        // Unusable source evidence must leave the last successful checkpoint and facts intact.
        files
            .GetContent(Arg.Any<Equibles.Media.Data.Models.File>())
            .Returns(
                Equibles.Media.BusinessLogic.GzipCompressor.Compress(
                    System.Text.Encoding.UTF8.GetBytes(envelope.Replace("0001274173", "0000000001"))
                )
            );
        await sut.Import(stock, CancellationToken.None);
        await verify.Entry(replayCheckpoint).ReloadAsync();
        replayCheckpoint.CalendarEvidenceFingerprint.Should().NotBe(originalFingerprint);
        (await verify.Set<FinancialFact>().CountAsync(f => f.CommonStockId == stock.Id))
            .Should()
            .Be(4);
    }

    [Fact]
    public async Task Import_TenKWithThreeComparableYears_DerivesFiscalYearFromPeriodEndPerCompanyFYE()
    {
        // Apple's FYE is Sept 28 (52/53-week filer, so actual end dates
        // wobble within a few days year over year).
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            FiscalYearEndMonth = 9,
            FiscalYearEndDay = 28,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        // One filing (FY2024 10-K) carrying three comparable-year FullYear
        // values for the same concept, all tagged with the filing's fy=2024
        // / fp=FY. Each value's actual period is encoded in start/end.
        CompanyFactValue Annual(DateOnly start, DateOnly end, decimal val) =>
            new()
            {
                Start = start,
                End = end,
                Val = val,
                Accn = "0000320193-24-000123",
                Fy = 2024,
                Fp = "FY",
                Form = "10-K",
                Filed = new DateOnly(2024, 11, 1),
                Frame = null,
            };
        var values = new[]
        {
            Annual(new DateOnly(2023, 09, 30), new DateOnly(2024, 09, 28), 391_035_000_000m),
            Annual(new DateOnly(2022, 09, 25), new DateOnly(2023, 09, 30), 383_285_000_000m),
            Annual(new DateOnly(2021, 09, 26), new DateOnly(2022, 09, 24), 394_328_000_000m),
        };
        var response = new CompanyFactsResponse
        {
            Cik = 320193,
            EntityName = "Apple Inc.",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["RevenueFromContractWithCustomerExcludingAssessedTax"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new() { ["USD"] = values.ToList() },
                    },
                },
            },
        };

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("0000320193").Returns(response);

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            errorReporter
        );

        await sut.Import(apple, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var facts = await verify
            .Set<FinancialFact>()
            .Where(f => f.CommonStockId == apple.Id)
            .OrderByDescending(f => f.PeriodEnd)
            .ToListAsync(CancellationToken.None);

        facts.Should().HaveCount(3);
        // Each row's (FiscalYear, FiscalPeriod) must reflect the period it
        // actually measures — not the filing's fy/fp.
        facts[0].FiscalYear.Should().Be(2024);
        facts[0].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        facts[1].FiscalYear.Should().Be(2023);
        facts[1].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        facts[2].FiscalYear.Should().Be(2022);
        facts[2].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
    }

    [Fact]
    public async Task Import_CompanyWithoutFye_FallsBackToFilingSuppliedFiscalYear()
    {
        // A company without ingested fiscal-year-end metadata gets the
        // pre-fix behavior — value.Fy ?? value.End.Year — preserved.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "ZZZZ",
            Name = "Unknown FYE Corp.",
            Cik = "0000999999",
            FiscalYearEndMonth = null,
            FiscalYearEndDay = null,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var response = new CompanyFactsResponse
        {
            Cik = 999999,
            EntityName = "Unknown FYE Corp.",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                new CompanyFactValue
                                {
                                    Start = new DateOnly(2023, 1, 1),
                                    End = new DateOnly(2023, 12, 31),
                                    Val = 100m,
                                    Accn = "0000999999-24-000001",
                                    Fy = 2023,
                                    Fp = "FY",
                                    Form = "10-K",
                                    Filed = new DateOnly(2024, 1, 15),
                                },
                            ],
                        },
                    },
                },
            },
        };

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("0000999999").Returns(response);

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            errorReporter
        );

        await sut.Import(stock, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var fact = await verify
            .Set<FinancialFact>()
            .SingleAsync(f => f.CommonStockId == stock.Id, CancellationToken.None);

        fact.FiscalYear.Should().Be(2023);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_OlderImporterVersion_ReplaysAndCorrectsExistingFiscalIdentity(
        bool interimInstant
    )
    {
        var periodStart = interimInstant ? new DateOnly(2020, 5, 3) : new DateOnly(2024, 12, 30);
        var periodEnd = interimInstant ? periodStart : new DateOnly(2025, 12, 28);
        var oldYear = interimInstant ? 2020 : 2026;
        var expectedYear = interimInstant ? 2021 : 2025;
        var period = interimInstant ? SecFiscalPeriod.Q1 : SecFiscalPeriod.FullYear;
        var form = interimInstant ? DocumentType.TenQ : DocumentType.TenK;
        var factId = Guid.NewGuid();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "JNJ",
            Name = "Johnson & Johnson",
            Cik = "0000200406",
            FiscalYearEndMonth = 1,
            FiscalYearEndDay = interimInstant ? 31 : 3,
        };
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        var filed = new DateOnly(2026, 2, 17);
        var accession = "0000200406-26-000010";
        var documentId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<FinancialConcept>().Add(concept);
            var content = new Equibles.Media.Data.Models.File
            {
                Name = "jnj-10-k",
                Extension = "txt",
                ContentType = "text/plain",
            };
            seed.Set<Document>()
                .Add(
                    new Document
                    {
                        Id = documentId,
                        CommonStock = stock,
                        Content = content,
                        DocumentType = form,
                        ReportingDate = filed,
                        ReportingForDate = periodEnd,
                        AccessionNumber = accession,
                    }
                );
            seed.Set<FinancialFact>()
                .Add(
                    new FinancialFact
                    {
                        Id = factId,
                        CommonStockId = stock.Id,
                        FinancialConceptId = concept.Id,
                        Unit = "USD",
                        PeriodType = interimInstant
                            ? FactPeriodType.Instant
                            : FactPeriodType.Duration,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                        Value = 94_200_000_000m,
                        FiscalYear = oldYear,
                        FiscalPeriod = period,
                        Form = form,
                        FiledDate = filed,
                        AccessionNumber = accession,
                    }
                );
            seed.Set<FinancialFactsSyncStatus>()
                .Add(
                    new FinancialFactsSyncStatus
                    {
                        CommonStockId = stock.Id,
                        LastCheckedAt = DateTime.UtcNow,
                        LastFiledDateSeen = filed,
                        ImporterVersion = FinancialFactsImportService.CurrentImporterVersion - 1,
                    }
                );
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var response = new CompanyFactsResponse
        {
            Cik = 200406,
            EntityName = "Johnson & Johnson",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenue",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                new CompanyFactValue
                                {
                                    Start = interimInstant ? null : periodStart,
                                    End = periodEnd,
                                    Val = 94_200_000_000m,
                                    Accn = accession,
                                    Fy = oldYear,
                                    Fp = interimInstant ? "Q1" : "FY",
                                    Form = interimInstant ? "10-Q" : "10-K",
                                    Filed = filed,
                                },
                            ],
                        },
                    },
                },
            },
        };
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts(stock.Cik).Returns(response);
        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(stock, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var fact = await verify
            .Set<FinancialFact>()
            .SingleAsync(f => f.CommonStockId == stock.Id, CancellationToken.None);
        var status = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleAsync(s => s.CommonStockId == stock.Id, CancellationToken.None);
        fact.Id.Should().Be(factId, "replay updates the existing natural-key row");
        fact.FiscalYear.Should().Be(expectedYear);
        fact.FiscalPeriod.Should().Be(period);
        fact.DocumentId.Should().Be(documentId);
        status.ImporterVersion.Should().Be(FinancialFactsImportService.CurrentImporterVersion);
    }

    [Fact]
    public async Task Import_AnnualDocumentPeriodCorrectsHighSecYearWithoutFyeMetadata()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "DOCP",
            Name = "Document Period Corp.",
            Cik = "0000999998",
        };
        var filed = new DateOnly(2026, 2, 17);
        var periodEnd = new DateOnly(2025, 12, 28);
        const string accession = "0000999998-26-000010";
        var documentId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            var content = new Equibles.Media.Data.Models.File
            {
                Name = "annual-filing",
                Extension = "txt",
                ContentType = "text/plain",
            };
            seed.Set<Document>()
                .Add(
                    new Document
                    {
                        Id = documentId,
                        CommonStock = stock,
                        Content = content,
                        DocumentType = DocumentType.TenK,
                        ReportingDate = filed,
                        ReportingForDate = periodEnd,
                        AccessionNumber = accession,
                    }
                );
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var response = new CompanyFactsResponse
        {
            Cik = 999998,
            EntityName = stock.Name,
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenue",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                new CompanyFactValue
                                {
                                    Start = new DateOnly(2024, 12, 30),
                                    End = periodEnd,
                                    Val = 100_000_000m,
                                    Accn = accession,
                                    Fy = 2026,
                                    Fp = "FY",
                                    Form = "10-K",
                                    Filed = filed,
                                },
                            ],
                        },
                    },
                },
            },
        };
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts(stock.Cik).Returns(response);
        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(stock, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var fact = await verify
            .Set<FinancialFact>()
            .SingleAsync(f => f.CommonStockId == stock.Id, CancellationToken.None);
        fact.FiscalYear.Should().Be(2025);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        fact.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task Import_CorroboratedThousandScaleAmendment_RemovesPreviouslyStoredCorruptRow()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "DAWN",
            Name = "Day One Biopharmaceuticals Inc.",
            Cik = "0001845337",
            FiscalYearEndMonth = 12,
            FiscalYearEndDay = 31,
        };
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "NetIncomeLoss",
            Label = "Net loss",
        };
        var originalFiled = new DateOnly(2024, 3, 7);
        var amendmentFiled = new DateOnly(2024, 5, 1);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<FinancialConcept>().Add(concept);
            seed.Set<FinancialFact>()
                .Add(
                    new FinancialFact
                    {
                        CommonStockId = stock.Id,
                        FinancialConceptId = concept.Id,
                        Unit = "USD",
                        PeriodType = FactPeriodType.Duration,
                        PeriodStart = new DateOnly(2023, 1, 1),
                        PeriodEnd = new DateOnly(2023, 12, 31),
                        Value = -107_322_000_000m,
                        FiscalYear = 2023,
                        FiscalPeriod = SecFiscalPeriod.FullYear,
                        Form = DocumentType.TenKa,
                        FiledDate = amendmentFiled,
                        AccessionNumber = "dawn-amendment",
                    }
                );
            seed.Set<FinancialFactsSyncStatus>()
                .Add(
                    new FinancialFactsSyncStatus
                    {
                        CommonStockId = stock.Id,
                        LastCheckedAt = DateTime.UtcNow,
                        LastFiledDateSeen = amendmentFiled,
                        ImporterVersion = FinancialFactsImportService.CurrentImporterVersion - 1,
                    }
                );
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        CompanyFactValue Value(
            DateOnly start,
            DateOnly end,
            decimal value,
            string accession,
            string form,
            DateOnly filed
        ) =>
            new()
            {
                Start = start,
                End = end,
                Val = value,
                Accn = accession,
                Fy = 2023,
                Fp = end == new DateOnly(2023, 12, 31) ? "FY" : "Q3",
                Form = form,
                Filed = filed,
            };
        var response = new CompanyFactsResponse
        {
            Cik = 1845337,
            EntityName = stock.Name,
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["NetIncomeLoss"] = new CompanyFactConcept
                    {
                        Label = "Net loss",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                Value(
                                    new DateOnly(2023, 1, 1),
                                    new DateOnly(2023, 12, 31),
                                    -107_322_000m,
                                    "dawn-original",
                                    "10-K",
                                    originalFiled
                                ),
                                Value(
                                    new DateOnly(2023, 1, 1),
                                    new DateOnly(2023, 12, 31),
                                    -107_322_000_000m,
                                    "dawn-amendment",
                                    "10-K/A",
                                    amendmentFiled
                                ),
                            ],
                        },
                    },
                    ["ProfitLoss"] = new CompanyFactConcept
                    {
                        Label = "Net loss including noncontrolling interests",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                Value(
                                    new DateOnly(2023, 1, 1),
                                    new DateOnly(2023, 3, 31),
                                    -20_000_000m,
                                    "dawn-q1",
                                    "10-Q",
                                    new DateOnly(2023, 5, 1)
                                ),
                                Value(
                                    new DateOnly(2023, 4, 1),
                                    new DateOnly(2023, 6, 30),
                                    -30_000_000m,
                                    "dawn-q2",
                                    "10-Q",
                                    new DateOnly(2023, 8, 1)
                                ),
                                Value(
                                    new DateOnly(2023, 7, 1),
                                    new DateOnly(2023, 9, 30),
                                    -36_044_000m,
                                    "dawn-q3",
                                    "10-Q",
                                    new DateOnly(2023, 11, 1)
                                ),
                            ],
                        },
                    },
                },
            },
        };
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts(stock.Cik).Returns(response);
        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(stock, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var facts = await verify
            .Set<FinancialFact>()
            .Where(f => f.CommonStockId == stock.Id)
            .ToListAsync(CancellationToken.None);
        facts.Should().Contain(f => f.Value == -107_322_000m);
        facts.Should().NotContain(f => f.Value == -107_322_000_000m);
        facts.Should().NotContain(f => f.AccessionNumber == "dawn-amendment");
    }
}
