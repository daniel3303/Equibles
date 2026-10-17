using System.Net;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The EDGAR profile refresh keeps SEC's entity type, SIC and filer category for every 13F filer:
/// new filers first, stale profiles after the refresh age, one row per canonical CIK, a 404 kept
/// as "not found", and an outage or a null reply never stored as absence.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderFilerProfileRefreshServiceTests : ParadeDbMcpTestBase
{
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();

    public HolderFilerProfileRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task NewFiler_StoresItsEdgarHeaderUnderTheCanonicalCik()
    {
        await Seed13FFiler("0000019617");
        _edgar
            .GetCompanyMetadata("19617")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "19617",
                    FilerCik = "0000019617",
                    Name = "JPMORGAN CHASE & CO",
                    EntityType = "operating",
                    Sic = "6021",
                    SicDescription = "National Commercial Banks",
                    Category = "Large accelerated filer",
                    StateOfIncorporation = "DE",
                }
            );

        (await Sut().Run(100, CancellationToken.None)).Should().Be(1);

        var profile = await Profile("19617");
        profile.Found.Should().BeTrue();
        profile.Sic.Should().Be("6021");
        profile.SicDescription.Should().Be("National Commercial Banks");
        profile.EntityType.Should().Be("operating");
        profile.Category.Should().Be("Large accelerated filer");
        profile.StateOfIncorporation.Should().Be("DE");
    }

    [Fact]
    public async Task BlankEdgarFields_AreStoredAsAbsent()
    {
        await Seed13FFiler("102909");
        _edgar
            .GetCompanyMetadata("102909")
            .Returns(
                new CompanyMetadata
                {
                    FilerCik = "0000102909",
                    Name = "VANGUARD GROUP INC",
                    EntityType = "other",
                    Sic = "",
                    SicDescription = "",
                    Category = "",
                }
            );

        await Sut().Run(100, CancellationToken.None);

        var profile = await Profile("102909");
        profile.Sic.Should().BeNull();
        profile.SicDescription.Should().BeNull();
        profile.Category.Should().BeNull();
    }

    [Fact]
    public async Task FreshProfile_IsNotRefetched_AndAStaleOneIs()
    {
        await Seed13FFiler("111");
        await Seed13FFiler("222");
        await SeedProfile("111", DateTime.UtcNow.AddDays(-10));
        await SeedProfile(
            "222",
            DateTime.UtcNow - HolderFilerProfileRefreshService.RefreshAge - TimeSpan.FromDays(1)
        );
        _edgar
            .GetCompanyMetadata("222")
            .Returns(
                new CompanyMetadata
                {
                    FilerCik = "222",
                    Name = "RENAMED",
                    Sic = "6211",
                }
            );

        await Sut().Run(100, CancellationToken.None);

        await _edgar.DidNotReceive().GetCompanyMetadata("111");
        (await Profile("222")).Sic.Should().Be("6211");
    }

    [Fact]
    public async Task HolderWithoutA13F_IsNotProfiled()
    {
        DbContext.Add(new InstitutionalHolder { Cik = "333", Name = "13G only" });
        await DbContext.SaveChangesAsync();

        await Sut().Run(100, CancellationToken.None);

        await _edgar.DidNotReceive().GetCompanyMetadata(Arg.Any<string>());
    }

    [Fact]
    public async Task UnknownCik_IsStoredAsNotFound()
    {
        await Seed13FFiler("444");
        _edgar
            .GetCompanyMetadata("444")
            .ThrowsAsync(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));

        await Sut().Run(100, CancellationToken.None);

        var profile = await Profile("444");
        profile.Found.Should().BeFalse();
        profile.Name.Should().BeNull();
    }

    [Fact]
    public async Task Outage_StopsTheRunAndStoresNothing()
    {
        await Seed13FFiler("555");
        await Seed13FFiler("556");
        _edgar
            .GetCompanyMetadata(Arg.Any<string>())
            .ThrowsAsync(
                new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable)
            );

        (await Sut().Run(100, CancellationToken.None)).Should().Be(0);

        await _edgar.Received(1).GetCompanyMetadata(Arg.Any<string>());
        (await DbContext.Set<SecFilerProfile>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NullReply_IsNeverStoredAsAbsence()
    {
        // The client returns null for a timeout too, so only a 404 may record "not found".
        await Seed13FFiler("666");
        _edgar.GetCompanyMetadata("666").Returns((CompanyMetadata)null);

        await Sut().Run(100, CancellationToken.None);

        (await DbContext.Set<SecFilerProfile>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ThreeUnreadableRepliesInARow_StopTheRun()
    {
        foreach (var cik in new[] { "701", "702", "703", "704" })
            await Seed13FFiler(cik);
        _edgar.GetCompanyMetadata(Arg.Any<string>()).Returns((CompanyMetadata)null);

        await Sut().Run(100, CancellationToken.None);

        await _edgar.Received(3).GetCompanyMetadata(Arg.Any<string>());
    }

    [Fact]
    public async Task TwoPaddingsOfOneCik_AreFetchedAndStoredOnce()
    {
        await Seed13FFiler("0000019617");
        await Seed13FFiler("19617");
        _edgar
            .GetCompanyMetadata("19617")
            .Returns(new CompanyMetadata { FilerCik = "0000019617", Name = "JPMORGAN CHASE & CO" });

        await Sut().Run(100, CancellationToken.None);

        await _edgar.Received(1).GetCompanyMetadata(Arg.Any<string>());
        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<SecFilerProfile>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task NewFiler_IsFetchedBeforeAStaleProfile()
    {
        await Seed13FFiler("801");
        await Seed13FFiler("802");
        await SeedProfile(
            "801",
            DateTime.UtcNow - HolderFilerProfileRefreshService.RefreshAge - TimeSpan.FromDays(1)
        );
        _edgar
            .GetCompanyMetadata(Arg.Any<string>())
            .Returns(call => new CompanyMetadata { FilerCik = call.Arg<string>(), Name = "X" });

        await Sut().Run(1, CancellationToken.None);

        await _edgar.Received(1).GetCompanyMetadata("802");
        await _edgar.DidNotReceive().GetCompanyMetadata("801");
    }

    [Fact]
    public async Task ReplyNamingAnotherCik_IsNotStored()
    {
        await Seed13FFiler("901");
        _edgar
            .GetCompanyMetadata("901")
            .Returns(new CompanyMetadata { FilerCik = "0000000902", Name = "SOMEONE ELSE" });

        await Sut().Run(100, CancellationToken.None);

        (await DbContext.Set<SecFilerProfile>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task StopRequested_EndsTheRunBeforeTheNextRequest()
    {
        await Seed13FFiler("951");
        await Seed13FFiler("952");
        using var stop = new CancellationTokenSource();
        _edgar
            .GetCompanyMetadata(Arg.Any<string>())
            .Returns(call =>
            {
                stop.Cancel();
                return new CompanyMetadata { FilerCik = call.Arg<string>(), Name = "X" };
            });

        var act = () => Sut().Run(100, stop.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _edgar.Received(1).GetCompanyMetadata(Arg.Any<string>());
    }

    private HolderFilerProfileRefreshService Sut() =>
        new(
            _edgar,
            new InstitutionalHolderRepository(DbContext),
            new SecFilerProfileRepository(DbContext),
            NullLogger<HolderFilerProfileRefreshService>()
        );

    private async Task Seed13FFiler(string cik)
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = "Filer " + cik };
        DbContext.Add(holder);
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = cik + "-26-000001",
                InstitutionalHolderId = holder.Id,
                ReportDate = new DateOnly(2026, 6, 30),
                FilingDate = new DateOnly(2026, 8, 14),
                FilingType = FilingType.Form13F,
                PositionCount = 1,
                TotalValue = 1_000_000L,
            }
        );
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedProfile(string cik, DateTime fetchedAt)
    {
        DbContext.Add(
            new SecFilerProfile
            {
                Cik = cik,
                Found = true,
                Name = "Stored",
                FetchedAt = fetchedAt,
            }
        );
        await DbContext.SaveChangesAsync();
    }

    private async Task<SecFilerProfile> Profile(string cik)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<SecFilerProfile>().AsNoTracking().SingleAsync(p => p.Cik == cik);
    }
}
