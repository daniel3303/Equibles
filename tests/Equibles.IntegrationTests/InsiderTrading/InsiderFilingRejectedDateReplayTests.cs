using System.Text;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

[Collection(ParadeDbCollection.Name)]
public class InsiderFilingRejectedDateReplayTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Replay_RestoresSourceRowsAndQuarantinesUnusableDates()
    {
        const string accession = "0000913760-24-000032";
        var xml = System.IO.File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "snex-invalid-dates.xml")
        );
        var root = XElement.Parse(xml);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SNEX",
            Name: "StoneX",
            Cik: "913760"
        );
        var owner = new InsiderOwner { Name = "Source owner", OwnerCik = "1" };
        var filing = new FilingData
        {
            AccessionNumber = accession,
            FilingDate = new(2024, 2, 14),
            ReportDate = InsiderFilingParser.ParsePeriodOfReport(root).Value,
            Form = "4",
        };
        var source = InsiderFilingParser.ParseTransactionsForReplay(
            root,
            owner,
            stock.Id,
            filing,
            false
        );
        var rows = InsiderFilingParser.ParseTransactionsForReplay(
            root,
            owner,
            stock.Id,
            filing,
            false
        );
        var oldCompacted = source.Where(t => t.TransactionDate.Year >= 1900).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].ReportedPricePerShare = rows[i].PricePerShare;
            rows[i].ParserVersion = 9;
            if (i < oldCompacted.Count)
            {
                rows[i].TransactionDate = oldCompacted[i].TransactionDate;
                rows[i].TransactionCode = oldCompacted[i].TransactionCode;
                rows[i].SecurityKind = oldCompacted[i].SecurityKind;
            }
        }
        var bytes = Encoding.UTF8.GetBytes(xml);
        DbContext.AddRange(
            stock,
            owner,
            new InsiderFiling
            {
                AccessionNumber = accession,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
                UncompressedSize = bytes.Length,
                Content = new File
                {
                    Name = accession,
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = GzipCompressor.Compress(bytes) },
                },
            }
        );
        DbContext.AddRange(rows);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var edgar = Substitute.For<ISecEdgarClient>();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(DbContext),
            new InsiderFilingRepository(DbContext),
            new EquityDailyStockPriceRepository(DbContext),
            new StockSplitRepository(DbContext),
            new InsiderTransactionPriceValidator(),
            edgar,
            InsiderReprocessTestSupport.NewFileManager(),
            DbContext,
            NullLogger<InsiderFilingReprocessManager>()
        );
        var result = await manager.Run();
        Assert.Equal(0, result.Failed);
        DbContext.ChangeTracker.Clear();
        var raw = await DbContext
            .Set<InsiderTransaction>()
            .IgnoreQueryFilters()
            .OrderBy(t => t.TransactionOrder)
            .ToListAsync();
        Assert.Equal(6, raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            Assert.Equal(source[i].TransactionDate, raw[i].TransactionDate);
            Assert.Equal(source[i].TransactionCode, raw[i].TransactionCode);
            Assert.Equal(source[i].SecurityKind, raw[i].SecurityKind);
            Assert.Equal(InsiderTransaction.CurrentParserVersion, raw[i].ParserVersion);
        }
        Assert.Equal(
            new[] { 1, 2, 3, 5 },
            await DbContext
                .Set<InsiderTransaction>()
                .OrderBy(t => t.TransactionOrder)
                .Select(t => t.TransactionOrder)
                .ToArrayAsync()
        );
        Assert.Equal(0, (await manager.Run()).Total);
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllInvalidFiling_RemainsKnownAcrossRepeatedIngestion(bool alreadyStored)
    {
        const string accession = "0001628280-24-006020";
        var xml = System.IO.File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "oprx-invalid-dates.xml")
        );
        var root = XElement.Parse(xml);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OPRX",
            Name: "OptimizeRx",
            Cik: "1448431"
        );
        var filing = new FilingData
        {
            AccessionNumber = accession,
            FilingDate = new(2024, 2, 21),
            ReportDate = new(23, 12, 19),
            Form = "4",
            Cik = stock.Cik,
        };
        DbContext.Add(stock);
        if (alreadyStored)
        {
            var owner = new InsiderOwner
            {
                Name = "Source owner",
                OwnerCik = root.Descendants("rptOwnerCik").First().Value,
            };
            DbContext.Add(owner);
            DbContext.AddRange(
                InsiderFilingParser.ParseTransactionsForReplay(root, owner, stock.Id, filing, false)
            );
        }
        await DbContext.SaveChangesAsync();
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetDocumentContent(Arg.Any<FilingData>()).Returns(xml);
        var processor = Processor(edgar);
        Assert.Equal(!alreadyStored, await processor.Process(filing, stock));
        Assert.False(await processor.Process(filing, stock));
        Assert.Contains(accession, await processor.FilterKnownAccessions([accession]));
        Assert.Equal(
            alreadyStored ? 2 : 1,
            await DbContext.Set<InsiderTransaction>().IgnoreQueryFilters().CountAsync()
        );
        Assert.Empty(await DbContext.Set<InsiderTransaction>().ToListAsync());
    }

    [Fact]
    public async Task Amendment_SeesRejectedOriginalBeforeConvertingRemainingRowsToMarker()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var owner = new InsiderOwner { Name = "Owner", OwnerCik = "1234567" };
        const string original = "0000000001-24-000001";
        const string amended = "0000000001-24-000002";
        DbContext.AddRange(stock, owner);
        for (var i = 0; i < 2; i++)
            DbContext.Add(
                new InsiderTransaction
                {
                    EquityIssuerId = stock.Id,
                    InsiderOwnerId = owner.Id,
                    AccessionNumber = original,
                    TransactionOrder = i,
                    FilingDate = new(2024, 3, 16),
                    TransactionDate = new(i == 0 ? 24 : 2024, 3, 15),
                    FilingForm = InsiderOwnershipForm.Form4,
                    TransactionCode = TransactionCode.Purchase,
                    SecurityKind = InsiderSecurityKind.NonDerivative,
                    SecurityTitle = "Common Stock",
                    Shares = 10 + i,
                }
            );
        await DbContext.SaveChangesAsync();
        const string xml = """
            <ownershipDocument><documentType>4/A</documentType><periodOfReport>2024-03-15</periodOfReport>
            <dateOfOriginalSubmission>2024-03-16</dateOfOriginalSubmission><issuer><issuerCik>320193</issuerCik></issuer>
            <reportingOwner><reportingOwnerId><rptOwnerCik>1234567</rptOwnerCik><rptOwnerName>Owner</rptOwnerName></reportingOwnerId></reportingOwner>
            <nonDerivativeTable><nonDerivativeTransaction><securityTitle><value>Common Stock</value></securityTitle>
            <transactionDate><value>2024-03-15</value></transactionDate><transactionCoding><transactionCode>P</transactionCode></transactionCoding>
            <transactionAmounts><transactionShares><value>30</value></transactionShares><transactionPricePerShare><value>0</value></transactionPricePerShare></transactionAmounts>
            </nonDerivativeTransaction></nonDerivativeTable></ownershipDocument>
            """;
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetDocumentContent(Arg.Any<FilingData>()).Returns(xml);
        var processor = Processor(edgar);
        Assert.True(
            await processor.Process(
                new FilingData
                {
                    AccessionNumber = amended,
                    FilingDate = new(2024, 3, 18),
                    ReportDate = new(2024, 3, 15),
                    Form = "4/A",
                    Cik = stock.Cik,
                },
                stock
            )
        );
        var marker = await DbContext
            .Set<InsiderTransaction>()
            .IgnoreQueryFilters()
            .SingleAsync(t => t.AccessionNumber == original);
        Assert.Equal(TransactionCode.IngestMarker, marker.TransactionCode);
        Assert.Equal(0, marker.TransactionOrder);
        Assert.Single(await DbContext.Set<InsiderTransaction>().ToListAsync());
    }

    private InsiderTradingFilingProcessor Processor(ISecEdgarClient edgar)
    {
        var files = Substitute.For<IFileManager>();
        files
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(call =>
            {
                var file = new File
                {
                    Name = call.ArgAt<string>(1),
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = call.ArgAt<byte[]>(0) },
                };
                DbContext.Add(file);
                return file;
            });
        var scopes = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), edgar),
            (typeof(InsiderTransactionRepository), new InsiderTransactionRepository(DbContext)),
            (typeof(InsiderOwnerRepository), new InsiderOwnerRepository(DbContext)),
            (typeof(InsiderFilingRepository), new InsiderFilingRepository(DbContext)),
            (typeof(FailedFilingIngestRepository), new FailedFilingIngestRepository(DbContext)),
            (typeof(IFileManager), files),
            (
                typeof(EquityDailyStockPriceRepository),
                new EquityDailyStockPriceRepository(DbContext)
            ),
            (typeof(StockSplitRepository), new StockSplitRepository(DbContext)),
            (typeof(InsiderTransactionPriceValidator), new InsiderTransactionPriceValidator())
        );
        return new InsiderTradingFilingProcessor(
            scopes,
            NullLogger<InsiderTradingFilingProcessor>(),
            null
        );
    }
}
