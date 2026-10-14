using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the ingest half of the one-for-one derivative repair: the parsed underlying-share and
/// strike evidence must reach the validator, so the CoreWeave call (accession
/// 0001104659-26-097430, total premium in the per-unit field) lands repaired instead of valid at
/// $3,659,027 per option.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderTradingFilingProcessorDerivativeTotalTests : ParadeDbMcpTestBase
{
    public InsiderTradingFilingProcessorDerivativeTotalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private const string CallForm4Xml = """
        <ownershipDocument>
            <documentType>4</documentType>
            <periodOfReport>2026-08-18</periodOfReport>
            <reportingOwner>
                <reportingOwnerId>
                    <rptOwnerCik>0001352851</rptOwnerCik>
                    <rptOwnerName>Fund Owner</rptOwnerName>
                </reportingOwnerId>
                <reportingOwnerRelationship>
                    <isTenPercentOwner>1</isTenPercentOwner>
                </reportingOwnerRelationship>
            </reportingOwner>
            <derivativeTable>
                <derivativeTransaction>
                    <securityTitle><value>Call option (obligation to sell)</value></securityTitle>
                    <conversionOrExercisePrice><value>130</value></conversionOrExercisePrice>
                    <transactionDate><value>2026-08-18</value></transactionDate>
                    <transactionCoding><transactionCode>S</transactionCode></transactionCoding>
                    <transactionAmounts>
                        <transactionShares><value>183640</value></transactionShares>
                        <transactionPricePerShare><value>3659027</value></transactionPricePerShare>
                        <transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>
                    </transactionAmounts>
                    <underlyingSecurity>
                        <underlyingSecurityTitle><value>Class A Common Stock</value></underlyingSecurityTitle>
                        <underlyingSecurityShares><value>183640</value></underlyingSecurityShares>
                    </underlyingSecurity>
                    <postTransactionAmounts>
                        <sharesOwnedFollowingTransaction><value>183640</value></sharesOwnedFollowingTransaction>
                    </postTransactionAmounts>
                    <ownershipNature>
                        <directOrIndirectOwnership><value>I</value></directOrIndirectOwnership>
                    </ownershipNature>
                </derivativeTransaction>
            </derivativeTable>
        </ownershipDocument>
        """;

    [Fact]
    public async Task Process_OneForOneCallWithTotalPremium_PersistsTheRepairedUnitPrice()
    {
        var date = new DateOnly(2026, 8, 18);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CRWV",
            Name: "CoreWeave, Inc.",
            Cik: "0001769628"
        );
        DbContext.Add(stock);
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
        await DbContext.SaveChangesAsync();

        var secClient = Substitute.For<ISecEdgarClient>();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(CallForm4Xml);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(InsiderOwnerRepository), new InsiderOwnerRepository(DbContext)),
            (typeof(InsiderTransactionRepository), new InsiderTransactionRepository(DbContext)),
            (typeof(InsiderFilingRepository), new InsiderFilingRepository(DbContext)),
            (typeof(FailedFilingIngestRepository), new FailedFilingIngestRepository(DbContext)),
            (typeof(IFileManager), Substitute.For<IFileManager>()),
            (typeof(ErrorManager), new ErrorManager(new ErrorRepository(DbContext))),
            (
                typeof(EquityDailyStockPriceRepository),
                new EquityDailyStockPriceRepository(DbContext)
            ),
            (typeof(InsiderTransactionPriceValidator), new InsiderTransactionPriceValidator()),
            (typeof(StockSplitRepository), new StockSplitRepository(DbContext))
        );
        var processor = new InsiderTradingFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );
        var filing = new FilingData
        {
            AccessionNumber = "0001104659-26-097430",
            Form = "4",
            FilingDate = new DateOnly(2026, 8, 20),
            ReportDate = date,
            Cik = stock.Cik,
        };

        var processed = await processor.Process(filing, stock);

        processed.Should().BeTrue();
        await using var verify = Fixture.CreateDbContext();
        var row = await verify
            .Set<InsiderTransaction>()
            .SingleAsync(t => t.AccessionNumber == filing.AccessionNumber);
        row.SecurityKind.Should().Be(InsiderSecurityKind.Derivative);
        row.ReportedPricePerShare.Should().Be(3_659_027m);
        row.PricePerShare.Should().Be(3_659_027m / 183_640m);
        row.PriceWasRepaired.Should().BeTrue();
        row.IsPriceValid.Should().BeTrue();
    }
}
