using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FundPortfolioSnapshotTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task SnapshotPagesAtTheDatabaseAndKeepsHeaderBeyondTheLastRow()
    {
        var filing = Filing(4);
        for (var i = 1; i <= 4; i++)
            filing.Holdings.Add(
                new NportHolding
                {
                    Id = new Guid(i, 0, 0, new byte[8]),
                    Name = $"Position {i}",
                    ValueUsd = 10,
                }
            );
        DbContext.Add(filing);
        await DbContext.SaveChangesAsync();
        var counter = new ReadCounter();
        await using var db = Fixture.CreateDbContext(options => options.AddInterceptors(counter));
        var repository = new NportFilingRepository(db);

        var page = await repository.GetPortfolioSnapshot(filing.Id, 1, 2);

        page.Holdings.Select(h => h.Name).Should().Equal("Position 2", "Position 3");
        page.TotalHoldings.Should().Be(4);
        page.ReportedHoldingCount.Should().Be(4);
        page.NetAssets.Should().Be(1_000_000);
        page.HoldingsCoverage.Should().Be("fullPortfolio");
        counter.Commands.Should().ContainSingle();
        counter
            .ReadCount.Should()
            .BeInRange(2, 3, "only the requested rows, plus EOF, cross the database boundary");
        db.ChangeTracker.Entries<NportHolding>().Should().BeEmpty();

        counter.Commands.Clear();
        var pastEnd = await repository.GetPortfolioSnapshot(filing.Id, 4, 2);
        pastEnd.Holdings.Should().BeEmpty();
        pastEnd.TotalHoldings.Should().Be(4);
        pastEnd.ReportPeriodDate.Should().Be(filing.ReportPeriodDate);
        pastEnd.HoldingsCoverage.Should().Be("fullPortfolio");
        counter.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, "fullPortfolio")]
    [InlineData(5, "trackedEquitiesOnly")]
    [InlineData(null, "unknown")]
    public async Task EmptyReportRetainsItsFactsAndDiffersFromAMissingReport(
        int? reported,
        string coverage
    )
    {
        var filing = Filing(reported);
        DbContext.Add(filing);
        await DbContext.SaveChangesAsync();
        var repository = new NportFilingRepository(DbContext);

        var snapshot = await repository.GetPortfolioSnapshot(filing.Id, 0, 10);

        snapshot.TotalHoldings.Should().Be(0);
        snapshot.ReportedHoldingCount.Should().Be(reported);
        snapshot.HoldingsCoverage.Should().Be(coverage);
        snapshot.NetAssets.Should().Be(filing.NetAssets);
        (await repository.GetPortfolioSnapshot(Guid.NewGuid(), 0, 10)).Should().BeNull();
    }

    [Theory]
    [InlineData(false, 1, "full reported portfolio")]
    [InlineData(true, 2, "partial portfolio")]
    [InlineData(false, null, "unknown")]
    [InlineData(true, 0, "unknown")]
    public async Task ProfileUsesSelectedReportAndCountsRegardlessOfIngestionRoute(
        bool trackedIssuer,
        int? reported,
        string coverage
    )
    {
        var filing = Filing(reported);
        if (trackedIssuer)
        {
            filing.Issuer = new EquityIssuer { Name = "Filed registrant", Cik = "0000000991" };
            filing.RegistrantCik = null;
        }
        filing.Holdings.Add(new NportHolding { Name = "Selected position", ValueUsd = 10 });
        var series = new FundSeries
        {
            IdentityKey = trackedIssuer
                ? $"cs:{filing.Issuer.Id}:S000000001"
                : "rc:0000000991:S000000001",
            Issuer = filing.Issuer,
            Slug = "fund-s000000001",
            SeriesId = filing.SeriesId,
            RegistrantCik = filing.RegistrantCik,
            SeriesName = "Selected fund",
            RegistrantName = "Filed registrant",
            LatestNportFilingId = filing.Id,
            LatestReportPeriodDate = new(2020, 1, 1),
            PositionCount = 99,
            ReportedHoldingCount = 100,
        };
        var newer = Filing(1);
        newer.AccessionNumber = "newer-unselected";
        newer.ReportPeriodDate = filing.ReportPeriodDate.AddMonths(1);
        newer.Holdings.Add(new NportHolding { Name = "Unselected position", ValueUsd = 999 });
        DbContext.AddRange(filing, series, newer);
        await DbContext.SaveChangesAsync();
        var tools = new FundDirectoryTools(
            new FundSeriesRepository(DbContext),
            new NportFilingRepository(DbContext),
            ErrorManager,
            NullLogger<FundDirectoryTools>()
        );

        var result = await tools.GetFundProfile(series.Slug);

        result
            .Should()
            .Contain("Selected position")
            .And.NotContain("Unselected position")
            .And.Contain("reported 2026-07-31")
            .And.Contain("1 stored holdings")
            .And.Contain($"coverage: {coverage}");
    }

    [Fact]
    public async Task CanceledSnapshotPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new NportFilingRepository(DbContext);

        Func<Task> read = () =>
            repository.GetPortfolioSnapshot(Guid.NewGuid(), 0, 10, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static NportFiling Filing(int? reported) =>
        new()
        {
            RegistrantCik = "0000000991",
            SeriesId = "S000000001",
            AccessionNumber = "selected",
            ReportPeriodDate = new(2026, 7, 31),
            FilingDate = new(2026, 8, 15),
            NetAssets = 1_000_000,
            ReportedHoldingCount = reported,
        };

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public int ReadCount { get; private set; }

        public override ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command,
            DataReaderClosingEventData eventData,
            InterceptionResult result
        )
        {
            ReadCount = eventData.ReadCount;
            return base.DataReaderClosingAsync(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
