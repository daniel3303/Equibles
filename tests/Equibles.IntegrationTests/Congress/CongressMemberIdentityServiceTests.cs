using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

[Collection(ParadeDbCollection.Name)]
public class CongressMemberIdentityServiceTests : ParadeDbMcpTestBase
{
    public CongressMemberIdentityServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ReconcileMembers_NoNewFilings_MergesReviewedAliases()
    {
        var survivor = new CongressMember { Name = "Scott Franklin" };
        var retired = new CongressMember { Name = "C. Scott Franklin" };
        DbContext.AddRange(survivor, retired);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await CreateSut().ReconcileMembers(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var member = await verify.Set<CongressMember>().AsNoTracking().SingleAsync();
        member.Id.Should().Be(survivor.Id);
        member.BioguideId.Should().Be("F000472");
        var redirect = await verify.Set<CongressMemberRedirect>().AsNoTracking().SingleAsync();
        redirect.Id.Should().Be(retired.Id);
        redirect.MergedIntoId.Should().Be(survivor.Id);
    }

    [Fact]
    public async Task ReconcileMembers_AliasHasConflictingBioguideId_RefusesMerge()
    {
        var member = new CongressMember { Name = "James E. Banks", BioguideId = "X000001" };
        DbContext.Add(member);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var act = async () => await CreateSut().ReconcileMembers(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var verify = Fixture.CreateDbContext();
        var persisted = await verify.Set<CongressMember>().AsNoTracking().SingleAsync();
        persisted.Name.Should().Be("James E. Banks");
        persisted.BioguideId.Should().Be("X000001");
        (await verify.Set<CongressMemberRedirect>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileMembers_SameDayAnnualAliasAmendment_KeepsNewestStoredReport()
    {
        var survivor = new CongressMember { Name = "Scott Franklin" };
        var retired = new CongressMember { Name = "C. Scott Franklin" };
        var filedDate = new DateOnly(2025, 5, 15);
        DbContext.AddRange(survivor, retired);
        DbContext.AddRange(
            new CongressionalAnnualDisclosure
            {
                CongressMember = survivor,
                Year = 2024,
                FiledDate = filedDate,
                ReportId = "original",
                CreationTime = new DateTime(2025, 5, 15, 9, 0, 0, DateTimeKind.Utc),
            },
            new CongressionalAnnualDisclosure
            {
                CongressMember = retired,
                Year = 2024,
                FiledDate = filedDate,
                ReportId = "amendment",
                CreationTime = new DateTime(2025, 5, 15, 10, 0, 0, DateTimeKind.Utc),
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await CreateSut().ReconcileMembers(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var member = await verify.Set<CongressMember>().AsNoTracking().SingleAsync();
        var disclosure = await verify
            .Set<CongressionalAnnualDisclosure>()
            .AsNoTracking()
            .SingleAsync();
        disclosure.CongressMemberId.Should().Be(member.Id);
        disclosure.ReportId.Should().Be("amendment");
    }

    private CongressMemberIdentityService CreateSut() =>
        new(DbContext, Substitute.For<ILogger<CongressMemberIdentityService>>());
}
