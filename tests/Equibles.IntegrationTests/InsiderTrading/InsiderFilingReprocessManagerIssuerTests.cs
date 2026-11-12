using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pins the v15 pass: EDGAR lists an ownership filing under every company it names, so before the
/// import checked the issuer, a company that reported its own stake in another issuer took that
/// issuer's rows. The shape is Shopify's: a subsidiary's Klaviyo trades, filed jointly
/// with Shopify Inc., stored as Shopify insider trades.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerIssuerTests : ParadeDbMcpTestBase
{
    private const string Accession = "0001993562-26-000004";
    private const string HolderCik = "1594805";
    private const string IssuerCik = "1835830";
    private const decimal ReportedPrice = 31.5m;
    private static readonly DateOnly TradeDate = new(2026, 4, 28);
    private static readonly DateOnly FilingDate = new(2026, 4, 29);

    public InsiderFilingReprocessManagerIssuerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_RowsHeldUnderAReportingOwner_MoveToTheIssuerTheDocumentNames()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        var issuer = Issuer("KVYO", "Klaviyo, Inc.", IssuerCik);
        await Seed(holder, Document(IssuerCik, "1993562", HolderCik), issuer);

        var result = await Run();

        result.Reattributed.Should().Be(1);
        var row = await DbContext.Set<InsiderTransaction>().AsNoTracking().SingleAsync();
        row.EquityIssuerId.Should().Be(issuer.Id);
        row.IsPriceValid.Should().BeTrue();
        row.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }

    [Fact]
    public async Task Run_IssuerOutsideTheDirectory_RemovesTheRowsHeldUnderTheReportingOwner()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        await Seed(holder, Document(IssuerCik, "1993562", HolderCik));

        var result = await Run();

        result.ForeignIssuerRowsRemoved.Should().Be(1);
        (await DbContext.Set<InsiderTransaction>().AsNoTracking().CountAsync()).Should().Be(0);
    }

    // The issuer's feed took the amendment while the holder's took the original, so the two were
    // never paired. Moved beside it the trade would be listed twice, so the original stays.
    [Fact]
    public async Task Run_IssuerAlreadyHoldsTheUnpairedAmendment_LeavesTheRowsWithTheHolder()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        var issuer = Issuer("KVYO", "Klaviyo, Inc.", IssuerCik);
        await Seed(holder, Document(IssuerCik, "1993562", HolderCik), issuer);
        await AddAtIssuer(issuer, "0001993562-26-000009", amendsFilingOf: FilingDate);

        var result = await Run();

        result.HeldForPairing.Should().Be(1);
        result.Reattributed.Should().Be(0);
        var original = await DbContext
            .Set<InsiderTransaction>()
            .AsNoTracking()
            .SingleAsync(row => row.AccessionNumber == Accession);
        original.EquityIssuerId.Should().Be(holder.Id);
        original.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }

    // A batch saves once: the original it has just moved to the issuer is not stored there yet when
    // its unpaired amendment, held by the same company, is judged.
    [Fact]
    public async Task Run_OriginalMovedEarlierInTheBatch_LeavesItsUnpairedAmendmentWithTheHolder()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        var issuer = Issuer("KVYO", "Klaviyo, Inc.", IssuerCik);
        await Seed(holder, Document(IssuerCik, "1993562", HolderCik), issuer);
        await AddUnpairedAmendmentAtHolder(holder, "0001993562-26-000009");

        var result = await Run();

        result.Reattributed.Should().Be(1);
        result.HeldForPairing.Should().Be(1);
        var rows = await DbContext.Set<InsiderTransaction>().AsNoTracking().ToListAsync();
        rows.Single(row => row.AccessionNumber == Accession).EquityIssuerId.Should().Be(issuer.Id);
        rows.Single(row => row.AccessionNumber == "0001993562-26-000009")
            .EquityIssuerId.Should()
            .Be(holder.Id);
    }

    // An amendment of another filing of the same owner pairs with nothing here.
    [Fact]
    public async Task Run_IssuerHoldsAnAmendmentOfAnotherFiling_MovesTheRows()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        var issuer = Issuer("KVYO", "Klaviyo, Inc.", IssuerCik);
        await Seed(holder, Document(IssuerCik, "1993562", HolderCik), issuer);
        await AddAtIssuer(issuer, "0001993562-26-000009", amendsFilingOf: FilingDate.AddDays(-30));

        var result = await Run();

        result.Reattributed.Should().Be(1);
        result.HeldForPairing.Should().Be(0);
        (
            await DbContext
                .Set<InsiderTransaction>()
                .AsNoTracking()
                .CountAsync(row => row.EquityIssuerId == issuer.Id)
        )
            .Should()
            .Be(2);
    }

    // An issuer merged into its successor keeps rows whose documents name the retired CIK; the
    // company is not a reporting owner there, so nothing proves the rows are another issuer's.
    [Fact]
    public async Task Run_DocumentNamesAnotherCikButTheHolderIsNoReportingOwner_KeepsTheRows()
    {
        var holder = Issuer("SHOP", "Shopify Inc.", HolderCik);
        var other = Issuer("KVYO", "Klaviyo, Inc.", IssuerCik);
        await Seed(holder, Document(IssuerCik, "1993562"), other);

        var result = await Run();

        result.Reattributed.Should().Be(0);
        result.ForeignIssuerRowsRemoved.Should().Be(0);
        var row = await DbContext.Set<InsiderTransaction>().AsNoTracking().SingleAsync();
        row.EquityIssuerId.Should().Be(holder.Id);
        row.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }

    private static EquityIssuer Issuer(string ticker, string name, string cik) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: cik
        );

    private static string Document(string issuerCik, params string[] reportingOwnerCiks) =>
        Document(issuerCik, amends: null, reportingOwnerCiks);

    private static string Document(
        string issuerCik,
        DateOnly? amends,
        params string[] reportingOwnerCiks
    ) =>
        "<ownershipDocument>"
        + (
            amends is { } original
                ? $"<documentType>4/A</documentType><dateOfOriginalSubmission>{original:yyyy-MM-dd}</dateOfOriginalSubmission>"
                : "<documentType>4</documentType>"
        )
        + "<periodOfReport>2026-04-28</periodOfReport>"
        + $"<issuer><issuerCik>{issuerCik.PadLeft(10, '0')}</issuerCik></issuer>"
        + string.Concat(
            reportingOwnerCiks.Select(cik =>
                $"<reportingOwner><reportingOwnerId><rptOwnerCik>{cik.PadLeft(10, '0')}</rptOwnerCik></reportingOwnerId>"
                + "<reportingOwnerRelationship><isTenPercentOwner>1</isTenPercentOwner></reportingOwnerRelationship>"
                + "</reportingOwner>"
            )
        )
        + "<nonDerivativeTable><nonDerivativeTransaction>"
        + "<securityTitle><value>Series B Common Stock</value></securityTitle>"
        + "<transactionDate><value>2026-04-28</value></transactionDate>"
        + "<transactionCoding><transactionCode>S</transactionCode></transactionCoding>"
        + "<transactionAmounts>"
        + "<transactionShares><value>344381</value></transactionShares>"
        + "<transactionPricePerShare><value>31.5</value></transactionPricePerShare>"
        + "<transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>"
        + "</transactionAmounts>"
        + "<postTransactionAmounts><sharesOwnedFollowingTransaction><value>16973108</value>"
        + "</sharesOwnedFollowingTransaction></postTransactionAmounts>"
        + "</nonDerivativeTransaction></nonDerivativeTable>"
        + "</ownershipDocument>";

    private async Task Seed(EquityIssuer holder, string ownershipXml, params EquityIssuer[] others)
    {
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "1993562",
            Name = "Shopify Strategic Holdings 3 LLC",
            IsTenPercentOwner = true,
        };
        var rawBytes = Encoding.UTF8.GetBytes(ownershipXml);
        DbContext.Add(holder);
        DbContext.AddRange(others);
        // The stored verdict was reached against the holder's prices; each other issuer closed at
        // the reported price.
        foreach (var other in others)
        {
            DbContext.Add(
                new EquityDailyStockPrice
                {
                    Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                        DbContext,
                        other,
                        null
                    ),
                    Date = TradeDate,
                    Close = ReportedPrice,
                    Volume = 1_000_000,
                }
            );
        }
        DbContext.Add(owner);
        DbContext.Add(
            new InsiderTransaction
            {
                Id = Guid.NewGuid(),
                EquityIssuerId = holder.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = Accession,
                TransactionOrder = 0,
                FilingDate = FilingDate,
                TransactionDate = TradeDate,
                TransactionCode = TransactionCode.Sale,
                Shares = 344381,
                PricePerShare = ReportedPrice,
                ReportedPricePerShare = ReportedPrice,
                IsPriceValid = false,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                SharesOwnedAfter = 16973108,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Series B Common Stock",
                SecurityKind = InsiderSecurityKind.NonDerivative,
                ParserVersion = 14,
            }
        );
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = Accession,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = Accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    // A Form 4/A of the seeded filing that the holder also took, never paired with it.
    private async Task AddUnpairedAmendmentAtHolder(EquityIssuer holder, string accession)
    {
        var ownerId = await DbContext.Set<InsiderOwner>().Select(owner => owner.Id).SingleAsync();
        var rawBytes = Encoding.UTF8.GetBytes(
            Document(IssuerCik, amends: FilingDate, "1993562", HolderCik)
        );
        DbContext.Add(
            new InsiderTransaction
            {
                Id = Guid.NewGuid(),
                EquityIssuerId = holder.Id,
                InsiderOwnerId = ownerId,
                AccessionNumber = accession,
                TransactionOrder = 0,
                FilingDate = FilingDate.AddDays(2),
                TransactionDate = TradeDate,
                TransactionCode = TransactionCode.Sale,
                Shares = 344381,
                PricePerShare = ReportedPrice,
                ReportedPricePerShare = ReportedPrice,
                IsPriceValid = false,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                SharesOwnedAfter = 16973108,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Series B Common Stock",
                SecurityKind = InsiderSecurityKind.NonDerivative,
                FilingForm = InsiderOwnershipForm.Form4,
                IsAmendment = true,
                OriginalFilingDate = FilingDate,
                ParserVersion = 14,
            }
        );
        DbContext.Add(
            new InsiderFiling
            {
                AccessionNumber = accession,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = rawBytes.Length,
                Content = new File
                {
                    Name = accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
                },
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    // A current-version Form 4/A the issuer's own feed stored, not yet paired with an original.
    private async Task AddAtIssuer(EquityIssuer issuer, string accession, DateOnly amendsFilingOf)
    {
        var ownerId = await DbContext.Set<InsiderOwner>().Select(owner => owner.Id).SingleAsync();
        DbContext.Add(
            new InsiderTransaction
            {
                Id = Guid.NewGuid(),
                EquityIssuerId = issuer.Id,
                InsiderOwnerId = ownerId,
                AccessionNumber = accession,
                TransactionOrder = 0,
                FilingDate = FilingDate.AddDays(2),
                TransactionDate = TradeDate,
                TransactionCode = TransactionCode.Sale,
                Shares = 344381,
                PricePerShare = ReportedPrice,
                ReportedPricePerShare = ReportedPrice,
                IsPriceValid = true,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                SharesOwnedAfter = 16973108,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = "Series B Common Stock",
                SecurityKind = InsiderSecurityKind.NonDerivative,
                FilingForm = InsiderOwnershipForm.Form4,
                IsAmendment = true,
                OriginalFilingDate = amendsFilingOf,
                ParserVersion = InsiderTransaction.CurrentParserVersion,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Equibles.InsiderTrading.BusinessLogic.Models.InsiderFilingReprocessResult> Run()
    {
        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new EquityDailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new EquityIssuerRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );
        return await manager.Run();
    }
}
