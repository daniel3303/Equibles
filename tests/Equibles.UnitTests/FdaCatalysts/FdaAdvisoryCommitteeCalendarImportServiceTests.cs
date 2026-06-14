using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.FdaCatalysts.Data;
using Equibles.FdaCatalysts.Data.Models;
using Equibles.FdaCatalysts.HostedService.Configuration;
using Equibles.FdaCatalysts.HostedService.Services;
using Equibles.FdaCatalysts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

/// <summary>
/// Contract: <c>FdaAdvisoryCommitteeCalendarImportService.Import</c> renders the
/// FDA.gov advisory-committee calendar through the stealth browser, parses the
/// authoritative columns, and upserts the meetings by their per-meeting slug — so a
/// re-run reconciles in place rather than duplicating, and a row whose calendar fields
/// changed is refreshed. With no stealth engine configured it is a clean no-op.
/// </summary>
public class FdaAdvisoryCommitteeCalendarImportServiceTests
{
    private static string FixtureHtml() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "FdaCatalysts",
                "fda-advisory-committee-calendar.html"
            )
        );

    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new FdaCatalystsModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<FdaCatalystRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static FdaAdvisoryCommitteeCalendarImportService BuildSut(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string html,
        bool stealthEnabled = true
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(stealthEnabled);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(html));

        return new FdaAdvisoryCommitteeCalendarImportService(
            ScopeFactory(options),
            stealth,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<ILogger<FdaAdvisoryCommitteeCalendarImportService>>(),
            Options.Create(new FdaCatalystScraperOptions())
        );
    }

    private static async Task<System.Collections.Generic.List<FdaCatalyst>> AllRows(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        using var ctx = NewContext(options);
        return await ctx.Set<FdaCatalyst>().ToListAsync();
    }

    [Fact]
    public async Task Import_PersistsEveryParsedAdvisoryCommitteeMeeting()
    {
        var options = NewDbOptions();

        await BuildSut(options, FixtureHtml()).Import(CancellationToken.None);

        var rows = await AllRows(options);
        // The fixture has five meeting rows; the stale 2016 row with no Start Date is
        // dropped by the parser, leaving four.
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(c => c.CatalystType == FdaCatalystType.AdvisoryCommittee);
        rows.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.SourceReference));
        rows.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Center));
    }

    [Fact]
    public async Task Import_IsIdempotent_ReRunDoesNotDuplicate()
    {
        var options = NewDbOptions();
        var html = FixtureHtml();

        await BuildSut(options, html).Import(CancellationToken.None);
        await BuildSut(options, html).Import(CancellationToken.None);

        (await AllRows(options)).Should().HaveCount(4);
    }

    [Fact]
    public async Task Import_RefreshesAnExistingRow_FromTheCalendarWithoutDuplicating()
    {
        var options = NewDbOptions();
        var html = FixtureHtml();

        // Seed one row whose slug matches a parsed meeting but whose title is stale, then
        // import: the row must be refreshed in place, not duplicated.
        var parsed = Equibles
            .FdaCatalysts.BusinessLogic.FdaAdvisoryCommitteeCalendarParser.Parse(html)
            .First();
        using (var ctx = NewContext(options))
        {
            ctx.Set<FdaCatalyst>()
                .Add(
                    new FdaCatalyst
                    {
                        CatalystType = FdaCatalystType.AdvisoryCommittee,
                        MeetingDate = new DateOnly(2000, 1, 1),
                        Center = "stale center",
                        Title = "stale title",
                        SourceReference = parsed.SourceReference,
                    }
                );
            await ctx.SaveChangesAsync();
        }

        await BuildSut(options, html).Import(CancellationToken.None);

        var rows = await AllRows(options);
        rows.Should().HaveCount(4);
        var refreshed = rows.Single(c => c.SourceReference == parsed.SourceReference);
        refreshed.Title.Should().Be(parsed.Title);
        refreshed.MeetingDate.Should().Be(parsed.MeetingDate);
        refreshed.Center.Should().Be(parsed.Center);
    }

    [Fact]
    public async Task Import_WithoutAStealthBrowser_DoesNothing()
    {
        var options = NewDbOptions();

        await BuildSut(options, FixtureHtml(), stealthEnabled: false)
            .Import(CancellationToken.None);

        (await AllRows(options)).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_RefreshingARow_PreservesIdResolvedStockAndCreationTime()
    {
        var options = NewDbOptions();
        var html = FixtureHtml();

        // Contract: Apply refreshes only the calendar-sourced fields, preserving the stored
        // Id, the resolved CommonStockId, and the original CreationTime. A parsed row always
        // carries a null CommonStockId and a fresh timestamp, so a refresh must never clobber
        // a previously resolved ticker link or reset the audit timestamp.
        var parsed = Equibles
            .FdaCatalysts.BusinessLogic.FdaAdvisoryCommitteeCalendarParser.Parse(html)
            .First();
        var seededId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var resolvedStockId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var createdAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        using (var ctx = NewContext(options))
        {
            ctx.Set<FdaCatalyst>()
                .Add(
                    new FdaCatalyst
                    {
                        Id = seededId,
                        CommonStockId = resolvedStockId,
                        CreationTime = createdAt,
                        CatalystType = FdaCatalystType.AdvisoryCommittee,
                        MeetingDate = new DateOnly(2000, 1, 1),
                        Center = "stale center",
                        Title = "stale title",
                        SourceReference = parsed.SourceReference,
                    }
                );
            await ctx.SaveChangesAsync();
        }

        await BuildSut(options, html).Import(CancellationToken.None);

        var refreshed = (await AllRows(options)).Single(c =>
            c.SourceReference == parsed.SourceReference
        );
        // The mutable field was refreshed, proving Apply ran...
        refreshed.Title.Should().Be(parsed.Title);
        // ...yet the identity, resolved-stock link, and creation timestamp survived intact.
        refreshed.Id.Should().Be(seededId);
        refreshed.CommonStockId.Should().Be(resolvedStockId);
        refreshed.CreationTime.Should().Be(createdAt);
    }
}
