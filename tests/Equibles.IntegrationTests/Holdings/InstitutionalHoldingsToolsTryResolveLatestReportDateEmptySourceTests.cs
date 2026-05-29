using System.Reflection;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the failure contract of the Try-pattern resolver TryResolveLatestReportDate,
/// the sibling of ResolveReportDate. Unlike ResolveReportDate (which always returns a
/// date, falling back to validDates[0]), this overload reports a Found bool whose only
/// false signal is the `latest != default` guard after FirstOrDefaultAsync. When the
/// input does not parse AND the date source is empty, there is genuinely no date to
/// resolve, so Found must be false — otherwise callers would treat DateOnly default
/// (0001-01-01) as a real "latest" filing. Oracle derived from the Try-pattern name +
/// signature, not the body.
/// </summary>
public class InstitutionalHoldingsToolsTryResolveLatestReportDateEmptySourceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext = TestDbContextFactory.Create(
        new HoldingsModuleConfiguration()
    );

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task TryResolveLatestReportDate_UnparseableInputEmptySource_ReportsNotFound()
    {
        // Empty EF-backed IQueryable<DateOnly> (no holdings seeded) so FirstOrDefaultAsync
        // runs through a real async provider and yields default(DateOnly).
        IQueryable<DateOnly> emptyDates = _dbContext
            .Set<InstitutionalHolding>()
            .Select(h => h.ReportDate);

        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "TryResolveLatestReportDate",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var (date, found) = await (Task<(DateOnly Date, bool Found)>)
            method.Invoke(null, ["not-a-date", emptyDates])!;

        found.Should().BeFalse();
        date.Should().Be(default);
    }
}
