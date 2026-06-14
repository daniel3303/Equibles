using System;
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
/// Contract: a render that yields zero parsed rows is treated as a probable markup
/// change, not as "the calendar is genuinely empty" — the import surfaces an error and
/// returns. Crucially, it reconciles by upsert only and never by deletion, so a failed
/// or empty render must leave previously stored catalysts intact rather than wiping the
/// calendar.
/// </summary>
public class FdaAdvisoryCommitteeCalendarImportServiceZeroRowsTests
{
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
        string html
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
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

    [Fact]
    public async Task Import_WhenRenderYieldsZeroRows_LeavesExistingCatalystsUntouched()
    {
        var options = NewDbOptions();
        // Seed two previously imported meetings, then import a non-empty but tableless
        // render that parses to zero rows: the stored rows must survive unchanged.
        using (var ctx = NewContext(options))
        {
            ctx.Set<FdaCatalyst>()
                .AddRange(
                    new FdaCatalyst
                    {
                        CatalystType = FdaCatalystType.AdvisoryCommittee,
                        MeetingDate = new DateOnly(2026, 7, 1),
                        Center = "CDER",
                        Title = "First committee",
                        SourceReference = "meeting-one",
                    },
                    new FdaCatalyst
                    {
                        CatalystType = FdaCatalystType.AdvisoryCommittee,
                        MeetingDate = new DateOnly(2026, 8, 1),
                        Center = "CBER",
                        Title = "Second committee",
                        SourceReference = "meeting-two",
                    }
                );
            await ctx.SaveChangesAsync();
        }

        await BuildSut(options, "<html><body><p>calendar temporarily unavailable</p></body></html>")
            .Import(CancellationToken.None);

        using var verify = NewContext(options);
        var rows = await verify.Set<FdaCatalyst>().OrderBy(c => c.MeetingDate).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(c => c.SourceReference).Should().Equal("meeting-one", "meeting-two");
    }
}
