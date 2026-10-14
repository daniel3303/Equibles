using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pins the reprocess half of the one-for-one derivative repair. Stored rows carry no
/// underlying-share evidence, so a re-enrolled filing must take it from the replayed XML: the
/// CoreWeave call (total premium in the per-unit field, 1:1 underlying) is repaired, while a
/// convertible preferred at its $1,000 stated value (10 commons per preferred) stays as filed.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerDerivativeTotalTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerDerivativeTotalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private const string OwnershipXml =
        "<ownershipDocument>"
        + "<documentType>4</documentType>"
        + "<periodOfReport>2026-08-18</periodOfReport>"
        + "<derivativeTable>"
        + "<derivativeTransaction>"
        + "<securityTitle><value>Call option (obligation to sell)</value></securityTitle>"
        + "<conversionOrExercisePrice><value>130</value></conversionOrExercisePrice>"
        + "<transactionDate><value>2026-08-18</value></transactionDate>"
        + "<transactionCoding><transactionCode>S</transactionCode></transactionCoding>"
        + "<transactionAmounts>"
        + "<transactionShares><value>183640</value></transactionShares>"
        + "<transactionPricePerShare><value>3659027</value></transactionPricePerShare>"
        + "<transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>"
        + "</transactionAmounts>"
        + "<underlyingSecurity>"
        + "<underlyingSecurityTitle><value>Class A Common Stock</value></underlyingSecurityTitle>"
        + "<underlyingSecurityShares><value>183640</value></underlyingSecurityShares>"
        + "</underlyingSecurity>"
        + "</derivativeTransaction>"
        + "<derivativeTransaction>"
        + "<securityTitle><value>Series A Convertible Preferred Stock</value></securityTitle>"
        + "<conversionOrExercisePrice><value>100</value></conversionOrExercisePrice>"
        + "<transactionDate><value>2026-08-18</value></transactionDate>"
        + "<transactionCoding><transactionCode>P</transactionCode></transactionCoding>"
        + "<transactionAmounts>"
        + "<transactionShares><value>5000</value></transactionShares>"
        + "<transactionPricePerShare><value>1000</value></transactionPricePerShare>"
        + "<transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>"
        + "</transactionAmounts>"
        + "<underlyingSecurity>"
        + "<underlyingSecurityTitle><value>Class A Common Stock</value></underlyingSecurityTitle>"
        + "<underlyingSecurityShares><value>50000</value></underlyingSecurityShares>"
        + "</underlyingSecurity>"
        + "</derivativeTransaction>"
        + "</derivativeTable>"
        + "</ownershipDocument>";

    [Fact]
    public async Task Run_ReenrolledOneForOneDerivativeTotal_RepairsFromTheReplayedEvidence()
    {
        var date = new DateOnly(2026, 8, 18);
        var accession = "0001104659-26-097430";
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CRWV",
            Name: "CoreWeave, Inc.",
            Cik: "0001769628"
        );
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001352851",
            Name = "Fund Owner",
            City = "Evanston",
            StateOrCountry = "IL",
        };

        InsiderTransaction Stored(int order, string title, long shares, decimal price) =>
            new()
            {
                Id = Guid.NewGuid(),
                EquityIssuerId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = order,
                FilingDate = new DateOnly(2026, 8, 20),
                TransactionDate = date,
                TransactionCode = order == 0 ? TransactionCode.Sale : TransactionCode.Purchase,
                Shares = shares,
                PricePerShare = price,
                ReportedPricePerShare = price,
                IsPriceValid = true,
                AcquiredDisposed =
                    order == 0 ? AcquiredDisposed.Disposed : AcquiredDisposed.Acquired,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = title,
                SecurityKind = InsiderSecurityKind.Derivative,
                FilingForm = InsiderOwnershipForm.Form4,
                ParserVersion = InsiderTransaction.CurrentParserVersion - 1,
            };

        var call = Stored(0, "Call option (obligation to sell)", 183_640, 3_659_027m);
        var preferred = Stored(1, "Series A Convertible Preferred Stock", 5_000, 1_000m);
        var rawBytes = Encoding.UTF8.GetBytes(OwnershipXml);

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = Equibles.TestSupport.NativeListingSeed.ForStock(DbContext, stock, null),
                Date = date,
                Close = 108m,
                Low = 104m,
                High = 112m,
                Volume = 1_000,
            }
        );
        DbContext.Add(call);
        DbContext.Add(preferred);
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

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new EquityDailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            Substitute.For<ISecEdgarClient>(),
            InsiderReprocessTestSupport.NewFileManager(),
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Repaired.Should().Be(1);
        result.Failed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var repairedCall = await verify.Set<InsiderTransaction>().FindAsync(call.Id);
        repairedCall!.PricePerShare.Should().Be(3_659_027m / 183_640m);
        repairedCall.ReportedPricePerShare.Should().Be(3_659_027m);
        repairedCall.PriceWasRepaired.Should().BeTrue();
        repairedCall.IsPriceValid.Should().BeTrue();
        repairedCall.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

        var untouched = await verify.Set<InsiderTransaction>().FindAsync(preferred.Id);
        untouched!.PricePerShare.Should().Be(1_000m);
        untouched.PriceWasRepaired.Should().BeFalse();
        untouched.IsPriceValid.Should().BeTrue();
        untouched.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
