using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Converge_FailedSnapshotWriteRollsBackLabelsAndRetries(bool cancel)
    {
        var position = await SeedHolding(ClassBCusip, "BF-B", shares: 8, withManagerLeg: true);
        var failure = new FailSnapshotWrite(cancel);
        var context = _fixture.CreateDbContext(options => options.AddInterceptors(failure));
        _contexts.Add(context);
        var service = CreateService(context);

        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.Converge(CancellationToken.None)
            );
        else
            (await service.Converge(CancellationToken.None)).Should().Be(0);

        failure.Fired.Should().BeTrue();
        var unchanged = await Reload(position);
        unchanged.ListedTicker.Should().Be("BF-B");
        unchanged.Shares.Should().Be(8);
        unchanged.ManagerEntries.Should().ContainSingle();
        await using (var read = FreshContext())
        {
            (await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
                .DirtyAt.Should()
                .BeNull();
            (await read.Set<HoldingLabelConvergenceState>().AnyAsync()).Should().BeFalse();
        }

        (await CreateService().Converge(CancellationToken.None)).Should().Be(1);
        (await Reload(position)).ListedTicker.Should().BeNull();
        await using var verified = FreshContext();
        (await verified.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Quarter))
            .DirtyAt.Should()
            .NotBeNull();
        (await verified.Set<HoldingLabelConvergenceState>().AnyAsync()).Should().BeTrue();
    }

    private sealed class FailSnapshotWrite(bool cancel) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Fired && command.CommandText.Contains("INSERT INTO \"AumQuarterlySnapshot\""))
            {
                Fired = true;
                if (cancel)
                    throw new OperationCanceledException("Injected snapshot cancellation");
                throw new InvalidOperationException("Injected snapshot write failure");
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var context = _fixture.CreateDbContext();
        _contexts.Add(context);
        return context;
    }

    [Fact]
    public async Task Converge_MovesHistoryOntoACoRegisteredRetiredClass()
    {
        await RetireClassA(coRegistered: true);
        var classA = await SeedHolding(ClassACusip, null, shares: 3);
        var classB = await SeedHolding(ClassBCusip, "BF-B", shares: 8);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(2);

        (await Reload(classA)).ListedTicker.Should().Be("BF-A");
        (await Reload(classB)).ListedTicker.Should().BeNull();
    }

    // A retired ticker never registered beside the presentation may be the same security renamed.
    [Fact]
    public async Task Converge_KeepsHistoryOffARetiredTickerNotCoRegistered()
    {
        await RetireClassA(coRegistered: false);
        var classA = await SeedHolding(ClassACusip, null, shares: 3);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(classA)).ListedTicker.Should().BeNull();
    }

    // Co-registration proves a distinct class, not that the CUSIP it holds is its own.
    [Fact]
    public async Task Converge_KeepsHistoryOffARetiredClassUnderACusipNeverStatedForIt()
    {
        await RetireClassA(coRegistered: true, statedCusip: "115637999");
        var classA = await SeedHolding(ClassACusip, null, shares: 3);

        (await CreateService().Converge(CancellationToken.None)).Should().Be(0);

        (await Reload(classA)).ListedTicker.Should().BeNull();
    }

    private async Task RetireClassA(bool coRegistered, string statedCusip = ClassACusip)
    {
        await using var seed = FreshContext();
        var classA = await seed.Set<EquityListing>()
            .SingleAsync(listing => listing.Ticker == "BF-A");
        classA.Active = false;
        classA.IsDirectoryListed = false;
        classA.DelistedOn = Quarter.AddDays(30);
        seed.Set<EquityListingRetirementEvidence>()
            .Add(
                new EquityListingRetirementEvidence
                {
                    EquityIssuerId = _issuerId,
                    ListedTicker = "BF-A",
                    DelistedOn = classA.DelistedOn.Value,
                    Cusip = statedCusip,
                }
            );
        seed.Set<IssuerSecurityRegistration>()
            .AddRange(
                new IssuerSecurityRegistration
                {
                    EquityIssuerId = _issuerId,
                    TradingSymbol = "BFB",
                    Title = "Class B",
                    AccessionNumber = "0000014693-26-000010",
                    FiledDate = Quarter,
                },
                new IssuerSecurityRegistration
                {
                    EquityIssuerId = _issuerId,
                    TradingSymbol = "BFA",
                    Title = "Class A",
                    AccessionNumber = coRegistered
                        ? "0000014693-26-000010"
                        : "0000014693-20-000001",
                    FiledDate = Quarter,
                }
            );
        await seed.SaveChangesAsync();
    }

    private HoldingLabelConvergenceService CreateService(EquiblesFinancialDbContext context = null)
    {
        context ??= FreshContext();
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
