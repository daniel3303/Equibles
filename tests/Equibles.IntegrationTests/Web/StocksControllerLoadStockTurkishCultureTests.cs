using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Regression for GH-2591. <c>StocksController.LoadStock</c> calls
/// <c>ticker.ToUpper()</c>, which routes through the thread CurrentCulture's
/// <c>TextInfo</c>. In Turkish (tr-TR), lowercase <c>"i"</c> uppercases to
/// <c>"İ"</c> (U+0130, dotted capital I), not <c>"I"</c>. The other ticker
/// normalizers in this codebase (<c>SearchController.ResolveExactTicker</c>,
/// <c>HoldingsExportController.Holders</c>) all use <c>ToUpperInvariant()</c>,
/// so DB rows are keyed by the invariant uppercase form. A web host running
/// under <c>tr-TR</c> would therefore 404 every ticker containing a lowercase
/// <c>i</c> — the repository's <c>GetByTicker("İBM")</c> lookup misses the
/// row stored as <c>"IBM"</c>.
/// </summary>
public class StocksControllerLoadStockTurkishCultureTests
{
    [Fact]
    public async Task LoadStock_TurkishCultureLowercaseI_NormalizesViaInvariantAndFindsRow()
    {
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        var ibm = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "IBM",
            Name = "International Business Machines",
            Cik = "0000051143",
        };
        ctx.Set<CommonStock>().Add(ibm);
        await ctx.SaveChangesAsync();

        var sut = new StocksController(
            new CommonStockRepository(ctx),
            institutionalHolderRepository: null!,
            institutionalHoldingRepository: null!,
            new DocumentRepository(ctx),
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        );

        var loadStock = typeof(StocksController).GetMethod(
            "LoadStock",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var original = CultureInfo.CurrentCulture;
        CommonStock resolved;
        try
        {
            // tr-TR maps lowercase 'i' → 'İ' (U+0130) under ToUpper(), but
            // leaves it as 'I' under ToUpperInvariant(). Row is stored as
            // primary ticker "IBM"; an "İBM" lookup misses entirely.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var task = (Task<CommonStock>)loadStock!.Invoke(sut, ["ibm"]);
            resolved = await task;
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        resolved
            .Should()
            .NotBeNull(
                "LoadStock must normalize the ticker culture-invariantly so a host running under tr-TR (or any other non-invariant locale where ToUpper differs from ToUpperInvariant) still resolves the row stored as IBM; otherwise every /stocks/<ticker> URL containing a lowercase 'i' 404s in production"
            );
        resolved.Ticker.Should().Be("IBM");
    }
}
