using System;
using System.Threading.Tasks;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.FdaCatalysts.Data;
using Equibles.FdaCatalysts.Data.Models;
using Equibles.FdaCatalysts.Mcp.Tools;
using Equibles.FdaCatalysts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.FdaCatalysts;

/// <summary>
/// Contract: <c>GetFdaCatalysts</c> renders the requested window's meetings with the
/// per-meeting FDA page link, rejects malformed or inverted date arguments instead of
/// silently substituting a different window, signposts truncation, and reports the
/// calendar's covered span when a window turns up nothing.
/// </summary>
public class FdaCatalystToolsTests
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

    private static FdaCatalyst Meeting(
        DateOnly date,
        string title,
        string slug,
        string sourceUrl = "https://www.fda.gov/advisory-committees/meeting"
    ) =>
        new()
        {
            CatalystType = FdaCatalystType.AdvisoryCommittee,
            MeetingDate = date,
            Center = "Center for Drug Evaluation and Research",
            Title = title,
            SourceReference = slug,
            SourceUrl = sourceUrl,
        };

    private static FdaCatalystTools Tools(EquiblesFinancialDbContext ctx) =>
        new(
            new FdaCatalystRepository(ctx),
            new ErrorManager(null!),
            NullLogger<FdaCatalystTools>.Instance
        );

    [Fact]
    public async Task GetFdaCatalysts_RendersTheMeetingWithItsFdaPageLink()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        ctx.Add(
            Meeting(
                date,
                "Oncologic Drugs Advisory Committee",
                "odac-slug",
                "https://www.fda.gov/advisory-committees/odac-meeting"
            )
        );
        await ctx.SaveChangesAsync();

        var result = await Tools(ctx).GetFdaCatalysts();

        result
            .Should()
            .Contain(
                "https://www.fda.gov/advisory-committees/odac-meeting",
                "the per-meeting FDA page is the only way to resolve which product the meeting concerns"
            );
        result.Should().Contain("Oncologic Drugs Advisory Committee");
        result.Should().Contain("| Details |", "the link needs its own column");
    }

    [Fact]
    public async Task GetFdaCatalysts_MalformedStartDate_ReturnsAnErrorInsteadOfSilentlyDefaulting()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);
        ctx.Add(Meeting(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), "Meeting", "slug-1"));
        await ctx.SaveChangesAsync();

        var result = await Tools(ctx).GetFdaCatalysts(startDate: "23/07/2026");

        result.Should().Contain("Unknown startDate '23/07/2026'");
        result
            .Should()
            .NotContain(
                "FDA Catalyst Calendar",
                "a bad argument must never return data for a different window"
            );
    }

    [Fact]
    public async Task GetFdaCatalysts_InvertedRange_ReturnsAnErrorInsteadOfClampingToOneDay()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);

        var result = await Tools(ctx)
            .GetFdaCatalysts(startDate: "2026-09-01", endDate: "2026-08-01");

        result
            .Should()
            .Contain(
                "Invalid date range",
                "the old clamp searched a window the caller never asked for"
            );
        result.Should().Contain("2026-08-01").And.Contain("2026-09-01");
    }

    [Fact]
    public async Task GetFdaCatalysts_EmptyWindow_ReportsTheCalendarsCoveredSpan()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);
        ctx.Add(Meeting(new DateOnly(2025, 11, 6), "Oldest meeting", "slug-old"));
        ctx.Add(Meeting(new DateOnly(2026, 7, 30), "Newest meeting", "slug-new"));
        await ctx.SaveChangesAsync();

        var result = await Tools(ctx)
            .GetFdaCatalysts(startDate: "2024-01-01", endDate: "2024-12-31");

        result
            .Should()
            .Contain("No FDA advisory-committee meetings found between 2024-01-01 and 2024-12-31");
        result
            .Should()
            .Contain(
                "covers 2025-11-06 to 2026-07-30",
                "an empty answer outside coverage must read as a coverage boundary, not as no meetings"
            );
    }

    [Fact]
    public async Task GetFdaCatalysts_MaxResultsClip_AppendsTheTruncationNote()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        for (var i = 0; i < 3; i++)
            ctx.Add(Meeting(start.AddDays(i), $"Meeting {i}", $"slug-{i}"));
        await ctx.SaveChangesAsync();

        var result = await Tools(ctx).GetFdaCatalysts(maxResults: 2);

        result.Should().Contain("Showing first 2 of 3 results");
    }

    [Fact]
    public async Task GetFdaCatalysts_FullResult_HasNoTruncationNote()
    {
        var options = NewDbOptions();
        await using var ctx = NewContext(options);
        ctx.Add(Meeting(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), "Meeting", "slug-1"));
        await ctx.SaveChangesAsync();

        var result = await Tools(ctx).GetFdaCatalysts();

        result.Should().NotContain("Showing first");
    }
}
