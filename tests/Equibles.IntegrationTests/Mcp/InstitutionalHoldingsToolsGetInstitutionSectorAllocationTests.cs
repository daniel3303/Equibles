using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins <c>GetInstitutionSectorAllocation</c>. Resolves the holder via name search,
/// pulls the latest-quarter holdings with the Industry navigation, and feeds them to
/// the same <see cref="IndustryAllocationCalculator"/> the web profile uses. Each test
/// exercises one path so a regression in lookup or the Unclassified-bucket handling
/// surfaces as a focused assertion.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionSectorAllocationTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionSectorAllocationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionSectorAllocation_UnknownInstitution_ReportsNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSectorAllocation("Definitely Not A Fund");

        output.Should().Contain("No institution found");
    }

    [Fact]
    public async Task GetInstitutionSectorAllocation_HolderWithNoHoldings_ReportsNoData()
    {
        DbContext.Add(new InstitutionalHolder { Cik = "S00010001", Name = "Empty Allocator" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSectorAllocation("Empty");

        output.Should().Contain("No 13F holdings reported by Empty Allocator");
    }

    [Fact]
    public async Task GetInstitutionSectorAllocation_MixedIndustriesAndUnclassified_RendersTable()
    {
        var software = new Industry { Id = Guid.NewGuid(), Name = "Software" };
        var energy = new Industry { Id = Guid.NewGuid(), Name = "Energy" };
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            IndustryId = software.Id,
        };
        var xom = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil Corp.",
            Cik = "0000034088",
            IndustryId = energy.Id,
        };
        var obscure = new CommonStock
        {
            Ticker = "OBSCURE",
            Name = "Obscure Inc.",
            Cik = "0009999999",
            IndustryId = null,
        };
        var holder = new InstitutionalHolder { Cik = "S00010002", Name = "Allocator LP" };
        DbContext.AddRange(software, energy, aapl, xom, obscure, holder);
        var report = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(aapl, holder, report, value: 2_000_000));
        DbContext.Add(MakeHolding(xom, holder, report, value: 500_000));
        DbContext.Add(MakeHolding(obscure, holder, report, value: 250_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSectorAllocation("Allocator");

        output.Should().Contain("Sector allocation — **Allocator LP** as of 2024-12-31");
        output.Should().Contain("Software");
        output.Should().Contain("Energy");
        output.Should().Contain("Unclassified");
        // Order: Software (2M) first, Energy (500k), Unclassified always last.
        output
            .IndexOf("Software", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("Energy", StringComparison.Ordinal));
        output
            .IndexOf("Energy", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("Unclassified", StringComparison.Ordinal));
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new CommonStockRepository(ctx),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
