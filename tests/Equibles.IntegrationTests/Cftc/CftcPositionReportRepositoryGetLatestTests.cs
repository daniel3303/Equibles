using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Pins <see cref="CftcPositionReportRepository.GetLatestPerContract"/> — the
/// query that powers the CFTC dashboard's "most recent position per contract"
/// tile. Uses GroupBy + OrderByDescending(ReportDate).First() per group — a
/// pattern EF Core translates differently per provider, so this only round-
/// trips correctly against the real Postgres fixture.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CftcPositionReportRepositoryGetLatestTests : ParadeDbMcpTestBase
{
    public CftcPositionReportRepositoryGetLatestTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLatestPerContract_TwoContractsWithMultipleReports_ReturnsNewestPerContract()
    {
        var contractA = new CftcContract
        {
            MarketCode = "13874+",
            MarketName = "E-MINI S&P 500",
            Category = CftcContractCategory.EquityIndices,
        };
        var contractB = new CftcContract
        {
            MarketCode = "06765A",
            MarketName = "Crude Oil",
            Category = CftcContractCategory.Energy,
        };
        DbContext.Add(contractA);
        DbContext.Add(contractB);

        DbContext.Add(
            new CftcPositionReport
            {
                CftcContractId = contractA.Id,
                ReportDate = new DateOnly(2024, 11, 5),
                OpenInterest = 2_400_000,
            }
        );
        DbContext.Add(
            new CftcPositionReport
            {
                CftcContractId = contractA.Id,
                ReportDate = new DateOnly(2024, 12, 24),
                OpenInterest = 2_500_000,
            }
        );
        DbContext.Add(
            new CftcPositionReport
            {
                CftcContractId = contractB.Id,
                ReportDate = new DateOnly(2024, 12, 10),
                OpenInterest = 1_800_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CftcPositionReportRepository(verify);

        var latest = (await sut.GetLatestPerContract().AsNoTracking().ToListAsync()).ToDictionary(
            r => r.CftcContractId
        );

        latest.Should().HaveCount(2);
        latest[contractA.Id].ReportDate.Should().Be(new DateOnly(2024, 12, 24));
        latest[contractA.Id].OpenInterest.Should().Be(2_500_000);
        latest[contractB.Id].ReportDate.Should().Be(new DateOnly(2024, 12, 10));
    }
}
