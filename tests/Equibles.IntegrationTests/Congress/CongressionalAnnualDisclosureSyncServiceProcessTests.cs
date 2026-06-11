using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Congress.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Pins the annual disclosure persistence flow end-to-end against a real
/// Postgres: a new report upserts the member and stores the disclosure with
/// its band rollup and lines; a later amendment replaces the member-year row
/// in place (same row, new report id, fresh lines); re-running the same
/// report is a no-op.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressionalAnnualDisclosureSyncServiceProcessTests : ParadeDbMcpTestBase
{
    public CongressionalAnnualDisclosureSyncServiceProcessTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly MethodInfo ProcessReportsMethod =
        typeof(CongressionalAnnualDisclosureSyncService).GetMethod(
            "ProcessReports",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    private static Task ProcessReports(
        CongressionalAnnualDisclosureSyncService sut,
        List<AnnualDisclosureReport> reports
    ) => (Task)ProcessReportsMethod.Invoke(sut, [reports, CancellationToken.None]);

    private CongressionalAnnualDisclosureSyncService BuildSut()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(CongressMemberRepository), new CongressMemberRepository(DbContext)),
            (
                typeof(CongressionalAnnualDisclosureRepository),
                new CongressionalAnnualDisclosureRepository(DbContext)
            )
        );
        return new CongressionalAnnualDisclosureSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalAnnualDisclosureSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    private static AnnualDisclosureReport Report(
        string reportId,
        DateOnly filed,
        bool amendment,
        params AnnualDisclosureLineItem[] lines
    ) =>
        new()
        {
            MemberName = "Jane Doe",
            Position = CongressPosition.Representative,
            Year = 2024,
            FiledDate = filed,
            ReportId = reportId,
            IsAmendment = amendment,
            Lines = lines.ToList(),
        };

    private static AnnualDisclosureLineItem Line(
        CongressionalDisclosureLineKind kind,
        string description,
        long minimum,
        long maximum
    ) =>
        new()
        {
            Kind = kind,
            Description = description,
            RangeMinimum = minimum,
            RangeMaximum = maximum,
        };

    [Fact]
    public async Task ProcessReports_NewReport_UpsertsMemberAndStoresBandAndLines()
    {
        var report = Report(
            "10066169",
            new DateOnly(2025, 5, 15),
            amendment: false,
            Line(CongressionalDisclosureLineKind.Asset, "Apple Inc. (AAPL)", 1_000_001, 5_000_000),
            Line(CongressionalDisclosureLineKind.Liability, "Mortgage (Bank)", 250_001, 500_000)
        );

        await ProcessReports(BuildSut(), [report]);

        await using var verify = Fixture.CreateDbContext();
        var member = await verify
            .Set<CongressMember>()
            .AsNoTracking()
            .SingleAsync(m => m.Name == "Jane Doe");
        member.Position.Should().Be(CongressPosition.Representative);

        var disclosure = await verify
            .Set<CongressionalAnnualDisclosure>()
            .AsNoTracking()
            .Include(d => d.Lines)
            .SingleAsync();
        disclosure.CongressMemberId.Should().Be(member.Id);
        disclosure.Year.Should().Be(2024);
        disclosure.ReportId.Should().Be("10066169");
        disclosure.NetWorthMinimum.Should().Be(1_000_001 - 500_000);
        disclosure.NetWorthMaximum.Should().Be(5_000_000 - 250_001);
        disclosure.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessReports_Amendment_ReplacesTheMemberYearRowInPlace()
    {
        var original = Report(
            "10066169",
            new DateOnly(2025, 5, 15),
            amendment: false,
            Line(CongressionalDisclosureLineKind.Asset, "Apple Inc. (AAPL)", 1_000_001, 5_000_000)
        );
        var sut = BuildSut();
        await ProcessReports(sut, [original]);
        var originalId = await DbContext
            .Set<CongressionalAnnualDisclosure>()
            .AsNoTracking()
            .Select(d => d.Id)
            .SingleAsync();
        DbContext.ChangeTracker.Clear();

        var amendment = Report(
            "10074000",
            new DateOnly(2025, 11, 4),
            amendment: true,
            Line(CongressionalDisclosureLineKind.Asset, "NVIDIA (NVDA)", 5_000_001, 25_000_000),
            Line(CongressionalDisclosureLineKind.Asset, "Visa Inc. (V)", 15_001, 50_000)
        );
        await ProcessReports(sut, [amendment]);

        await using var verify = Fixture.CreateDbContext();
        var disclosure = await verify
            .Set<CongressionalAnnualDisclosure>()
            .AsNoTracking()
            .Include(d => d.Lines)
            .SingleAsync();
        disclosure.Id.Should().Be(originalId, "amendments replace the year's report in place");
        disclosure.ReportId.Should().Be("10074000");
        disclosure.FiledDate.Should().Be(new DateOnly(2025, 11, 4));
        disclosure.NetWorthMinimum.Should().Be(5_000_001 + 15_001);
        disclosure.NetWorthMaximum.Should().Be(25_000_000 + 50_000);
        disclosure.Lines.Should().HaveCount(2);
        disclosure.Lines.Should().OnlyContain(l => l.Description != "Apple Inc. (AAPL)");
    }

    [Fact]
    public async Task ProcessReports_SameReportAgain_IsIdempotent()
    {
        var report = Report(
            "10066169",
            new DateOnly(2025, 5, 15),
            amendment: false,
            Line(CongressionalDisclosureLineKind.Asset, "Apple Inc. (AAPL)", 1_000_001, 5_000_000)
        );
        var sut = BuildSut();
        await ProcessReports(sut, [report]);
        DbContext.ChangeTracker.Clear();

        await ProcessReports(sut, [report]);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressionalAnnualDisclosure>().AsNoTracking().CountAsync())
            .Should()
            .Be(1);
        (await verify.Set<CongressionalDisclosureLine>().AsNoTracking().CountAsync())
            .Should()
            .Be(1);
    }
}
