using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the stored-label convergence on Brown-Forman's shape: BF-B became the presentation, so
/// rows stored as 'BF-B' must read as primary and primary rows filed under BF-A's CUSIP must carry
/// 'BF-A', without ever merging two positions or relabelling a CUSIP whose identity is contested.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingLabelConvergenceServiceTests : IAsyncLifetime
{
    private const string ClassBCusip = "115637209";
    private const string ClassACusip = "115637100";
    private static readonly DateOnly Quarter = new(2026, 3, 31);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private Guid _issuerId;
    private Guid _holderId;

    public HoldingLabelConvergenceServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BF-B",
            Name: "Brown Forman Corp",
            Cik: "14693",
            Cusip: ClassBCusip,
            SecondaryTickers: ["BF-A"]
        );
        _issuerId = issuer.Id;
        var holder = new InstitutionalHolder { Cik = "0001000001", Name = "Sample Capital" };
        _holderId = holder.Id;
        await using var seed = FreshContext();
        seed.Set<EquityIssuer>().Add(issuer);
        seed.Set<InstitutionalHolder>().Add(holder);
        seed.Set<EquityListingCusipEvidence>()
            .Add(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = issuer.Id,
                    ListedTicker = "BF-A",
                    Cusip = ClassACusip,
                }
            );
        seed.Set<AumQuarterlySnapshot>().Add(new AumQuarterlySnapshot { ReportDate = Quarter });
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Converge_MovesBothClassesOfOnePositionOntoTheirCurrentLabels()
    {
        // One filer, one quarter, both classes: the primary row must vacate its key first.
        var classA = await SeedHolding(ClassACusip, null, shares: 3);
        var classB = await SeedHolding(ClassBCusip, "BF-B", shares: 8, withManagerLeg: true);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(2);

        (await Reload(classA)).ListedTicker.Should().Be("BF-A");
        var converged = await Reload(classB);
        converged.ListedTicker.Should().BeNull();
        converged.ManagerEntries.Should().ContainSingle("the row keeps its id and its legs");
        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Converge_LeavesAMoveWhoseTargetPositionAlreadyExists()
    {
        var labelled = await SeedHolding(ClassBCusip, "BF-B", shares: 8);
        var primary = await SeedHolding("999999999", null, shares: 5);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(labelled)).ListedTicker.Should().Be("BF-B");
        (await Reload(primary)).ListedTicker.Should().BeNull();
    }

    [Fact]
    public async Task Converge_LeavesAContestedCusipAsStored()
    {
        await using (var seed = FreshContext())
        {
            seed.Set<EquityIssuerCusipAlias>()
                .Add(
                    new EquityIssuerCusipAlias { EquityIssuerId = _issuerId, Cusip = ClassACusip }
                );
            await seed.SaveChangesAsync();
        }
        var primary = await SeedHolding(ClassACusip, null, shares: 3);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(primary)).ListedTicker.Should().BeNull();
    }

    [Fact]
    public async Task Converge_LeavesAPresentationLabelWhoseCusipNamesAnotherClass()
    {
        var disputed = await SeedHolding(ClassACusip, "BF-B", shares: 3);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(disputed)).ListedTicker.Should().Be("BF-B");
    }

    [Fact]
    public async Task Converge_LeavesPrimaryRowsWhenThePresentationHasNoCusipOfItsOwn()
    {
        await using (var seed = FreshContext())
        {
            var security = await seed.Set<EquitySecurity>()
                .SingleAsync(s => s.Cusip == ClassBCusip);
            security.Cusip = null;
            await seed.SaveChangesAsync();
        }
        var primary = await SeedHolding(ClassACusip, null, shares: 3);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(primary)).ListedTicker.Should().BeNull();
    }

    [Fact]
    public async Task Converge_RereadsAnIssuerOnlyAfterItsIdentityChanges()
    {
        // Converged with no positions; a row written later under an unchanged identity is not
        // reread, because an import under that identity writes the converged label itself.
        (await CreateService().Converge(CancellationToken.None))
            .Should()
            .Be(0);
        var classA = await SeedHolding(ClassACusip, null, shares: 3);
        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        await using (var seed = FreshContext())
        {
            seed.Set<EquityIssuerCusipAlias>()
                .Add(
                    new EquityIssuerCusipAlias { EquityIssuerId = _issuerId, Cusip = "115637308" }
                );
            await seed.SaveChangesAsync();
        }
        (await CreateService().Converge(CancellationToken.None)).Should().Be(1);

        (await Reload(classA)).ListedTicker.Should().Be("BF-A");
    }

    [Fact]
    public async Task Converge_MarksTheMovedQuarterDirty()
    {
        await SeedHolding(ClassBCusip, "BF-B", shares: 8);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(1);

        await using var read = FreshContext();
        (await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .DirtyAt.Should()
            .NotBeNull();
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var context = _fixture.CreateDbContext();
        _contexts.Add(context);
        return context;
    }

    private HoldingLabelConvergenceService CreateService()
    {
        var context = FreshContext();
        return new HoldingLabelConvergenceService(
            ServiceScopeSubstitute.Create(
                (typeof(EquiblesFinancialDbContext), context),
                (typeof(EquityIssuerRepository), new EquityIssuerRepository(context))
            ),
            Substitute.For<ILogger<HoldingLabelConvergenceService>>()
        );
    }

    private async Task<Guid> SeedHolding(
        string cusip,
        string listedTicker,
        long shares,
        bool withManagerLeg = false
    )
    {
        var holding = new InstitutionalHolding
        {
            InstitutionalHolderId = _holderId,
            EquityIssuerId = _issuerId,
            FilingDate = Quarter.AddDays(40),
            ReportDate = Quarter,
            Shares = shares,
            Value = shares * 40,
            ShareType = ShareType.Shares,
            FilingType = FilingType.Form13F,
            Cusip = cusip,
            ListedTicker = listedTicker,
            AccessionNumber = "0001000001-26-000001",
        };
        if (withManagerLeg)
            holding.ManagerEntries.Add(
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Sample Adviser",
                    Shares = shares,
                    Value = shares * 40,
                }
            );
        await using var seed = FreshContext();
        seed.Set<InstitutionalHolding>().Add(holding);
        await seed.SaveChangesAsync();
        return holding.Id;
    }

    private async Task<InstitutionalHolding> Reload(Guid id)
    {
        await using var read = FreshContext();
        return await read.Set<InstitutionalHolding>().AsNoTracking().SingleAsync(h => h.Id == id);
    }
}
