using System.Net.Http;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.Sec;

public class RawXbrlArtifactCaptureServiceBestEffortTests
{
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();

    private static EquiblesFinancialDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new RawFilingArtifactOnlyModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static CommonStock SeedStock(EquiblesFinancialDbContext db)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        db.Set<CommonStock>().Add(stock);
        db.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task Capture_WhenClientThrows_SwallowsExceptionAndStoresNothing()
    {
        // Contract (doc-comment + issue #1118): capture is best-effort and must never
        // interrupt ingest — a fetch failure is logged and swallowed, not propagated,
        // and leaves no partial row behind.
        using var db = NewDbContext();
        var stock = SeedStock(db);
        var filing = new FilingData
        {
            Cik = "0000320193",
            AccessionNumber = "0000320193-18-000145",
            FilingDate = new DateOnly(2018, 11, 5),
            Form = "10-K",
            PrimaryDocument = "aapl-20180929.htm",
        };
        _secEdgarClient
            .GetDocumentFileBytes(filing.Cik, filing.AccessionNumber, filing.PrimaryDocument)
            .ThrowsAsync(new HttpRequestException("SEC unavailable"));
        var service = new RawXbrlArtifactCaptureService(
            _secEdgarClient,
            new RawFilingArtifactRepository(db),
            Options.Create(
                new RawXbrlArtifactOptions { Enabled = true, CaptureInlineIxbrl = true }
            ),
            Substitute.For<ILogger<RawXbrlArtifactCaptureService>>()
        );

        var act = async () => await service.Capture(stock, filing);

        await act.Should().NotThrowAsync();
        db.Set<RawFilingArtifact>().Should().BeEmpty();
    }
}
