using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pins the v13 backfill: the reprocess is the only path that records the holding vehicle on rows
/// ingested earlier, so it must copy each re-parsed row's nature of ownership and footnote ids.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerOwnershipVehicleTests : ParadeDbMcpTestBase
{
    private const string Accession = "0001197649-26-000012";

    public InsiderFilingReprocessManagerOwnershipVehicleTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_V12RowsOfTheNvidiaCeoForm4_RecordEachRowsVehicle()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "NVDA",
            Name: "NVIDIA Corp",
            Cik: "0001045810"
        );
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001197649",
            Name = "HUANG JEN HSUN",
        };
        var xml = System.IO.File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "InsiderTrading",
                "form4-nvidia-ceo-vehicles.xml"
            )
        );

        // The rows a v12 ingest stored: the same parse, before the vehicle was recorded.
        var stored = InsiderFilingParser.ParseTransactions(
            InsiderFilingParser.TryGetOwnershipRoot(xml),
            owner,
            stock.Id,
            new FilingData
            {
                AccessionNumber = Accession,
                Form = "4",
                FilingDate = new DateOnly(2026, 9, 18),
                ReportDate = new DateOnly(2026, 9, 16),
            },
            false
        );
        foreach (var row in stored)
        {
            row.ReportedPricePerShare = row.PricePerShare;
            row.NatureOfOwnership = null;
            row.OwnershipNatureFootnoteIds = [];
            row.BalanceFootnoteIds = [];
            row.ParserVersion = 12;
        }

        var rawBytes = Encoding.UTF8.GetBytes(xml);
        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.AddRange(stored);
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

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await manager.Run(batchPause: TimeSpan.FromMilliseconds(300));

        result.Processed.Should().Be(1);
        // The worker paces the drain so a parser bump does not rewrite every filing back to back.
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(300));
        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<InsiderTransaction>()
            .Where(t => t.AccessionNumber == Accession)
            .OrderBy(t => t.TransactionOrder)
            .ToListAsync();
        rows.Should().OnlyContain(t => t.ParserVersion == InsiderTransaction.CurrentParserVersion);
        rows.Select(t => (t.NatureOfOwnership, string.Join(",", t.OwnershipNatureFootnoteIds)))
            .Should()
            .Equal(
                (null, ""),
                ("By Trust", "F4"),
                ("By Irrevocable Trust", "F5"),
                ("By Irrevocable Remainder Trust", "F6"),
                ("By Limited Liability Company 1", "F7"),
                ("By Limited Liability Company 2", "F8"),
                ("By Limited Liability Company 3", "F9"),
                ("By Limited Liability Company 4", "F10"),
                ("By Limited Liability Company 5", "F11"),
                ("By Limited Liability Company 6", "F12")
            );
        rows.Select(t => string.Join(",", t.BalanceFootnoteIds))
            .Should()
            .Equal("F2", "", "", "", "", "", "", "", "", "");
    }
}
