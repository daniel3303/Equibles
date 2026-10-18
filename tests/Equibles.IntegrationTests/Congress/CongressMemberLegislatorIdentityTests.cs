using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

[Collection(ParadeDbCollection.Name)]
public class CongressMemberLegislatorIdentityTests : ParadeDbMcpTestBase
{
    public CongressMemberLegislatorIdentityTests(ParadeDbFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        DbContext.AddRange(
            Legislator("N000190", "Norman"),
            Legislator("B001305", "Budd"),
            Term("N000190", CongressPosition.Representative, "SC", 5, 2023, 2025),
            Term("B001305", CongressPosition.Representative, "NC", 13, 2019, 2023)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_UniqueSeatAndSurname_AssignsWithMatchSource()
    {
        var member = await SeedMember("Ralph Norman", "SC05", new DateOnly(2024, 3, 1));
        await SeedAnnualReport(member, new DateOnly(2024, 5, 15));

        var result = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        result.Assigned.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        var stored = await Reload(member);
        stored.BioguideId.Should().Be("N000190");
        stored.BioguideIdSource.Should().Be(CongressBioguideIdSource.SeatAndSurnameMatch);
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_AnnualReportOnly_UsesItsFiledDate()
    {
        var member = await SeedMember("Ralph Norman", "SC05", filingDate: null);
        await SeedAnnualReport(member, new DateOnly(2024, 5, 15));

        await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        (await Reload(member)).BioguideId.Should().Be("N000190");
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_MemberHasAnotherId_KeepsItAndReportsConflict()
    {
        var member = await SeedMember(
            "Ralph Norman",
            "SC05",
            new DateOnly(2024, 3, 1),
            bioguideId: "X000009",
            source: CongressBioguideIdSource.SeatAndSurnameMatch
        );

        var result = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        result.Assigned.Should().Be(0);
        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Should()
            .Match<Equibles.Congress.HostedService.Models.LegislatorIdentityConflict>(c =>
                c.ExistingBioguideId == "X000009" && c.DerivedBioguideId == "N000190"
            );
        (await Reload(member)).BioguideId.Should().Be("X000009");
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_IdHeldByAnotherMember_LeavesTheMemberUnresolved()
    {
        var holder = await SeedMember(
            "Norman Holder",
            null,
            filingDate: null,
            bioguideId: "N000190",
            source: CongressBioguideIdSource.SeatAndSurnameMatch
        );
        var member = await SeedMember("Ralph Norman", "SC05", new DateOnly(2024, 3, 1));

        var result = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        result.Assigned.Should().Be(0);
        result.Conflicts.Should().ContainSingle().Which.MemberName.Should().Be("Ralph Norman");
        (await Reload(member)).BioguideId.Should().BeNull();
        (await Reload(holder)).BioguideId.Should().Be("N000190");
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_TwoUnresolvedMembersDeriveOneId_AssignsNeither()
    {
        var first = await SeedMember("Ralph Norman", "SC05", new DateOnly(2024, 3, 1));
        var second = await SeedMember("Ralph W. Norman", "SC05", new DateOnly(2024, 4, 1));

        var result = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        result.Assigned.Should().Be(0);
        result.Conflicts.Should().HaveCount(2);
        (await Reload(first)).BioguideId.Should().BeNull();
        (await Reload(second)).BioguideId.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_CatalogAliasRow_GetsTheCatalogIdentityNotAMatch()
    {
        var alias = await SeedMember("Theodore P. Budd", "NC13", new DateOnly(2020, 3, 1));

        var result = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        result.Assigned.Should().Be(0);
        var stored = await Reload(alias);
        stored.Name.Should().Be("Ted Budd");
        stored.BioguideId.Should().Be("B001305");
        stored.BioguideIdSource.Should().Be(CongressBioguideIdSource.Catalog);
    }

    [Fact]
    public async Task ReconcileMembers_CatalogAliasCarriesAMatchedId_CatalogReplacesIt()
    {
        var alias = await SeedMember(
            "Ted Budd",
            "NC13",
            filingDate: null,
            bioguideId: "Z000003",
            source: CongressBioguideIdSource.SeatAndSurnameMatch
        );

        await CreateSut().ReconcileMembers(CancellationToken.None);

        var stored = await Reload(alias);
        stored.BioguideId.Should().Be("B001305");
        stored.BioguideIdSource.Should().Be(CongressBioguideIdSource.Catalog);
    }

    [Fact]
    public async Task ReconcileMembers_CatalogAliasCarriesAnotherCatalogId_Throws()
    {
        var alias = await SeedMember(
            "Ted Budd",
            "NC13",
            filingDate: null,
            bioguideId: "Z000003",
            source: CongressBioguideIdSource.Catalog
        );

        var act = async () => await CreateSut().ReconcileMembers(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await Reload(alias)).BioguideId.Should().Be("Z000003");
    }

    [Fact]
    public async Task ReconcileLegislatorIdentities_SecondRun_ChangesNothing()
    {
        var member = await SeedMember("Ralph Norman", "SC05", new DateOnly(2024, 3, 1));
        await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        var second = await CreateSut().ReconcileLegislatorIdentities(CancellationToken.None);

        second.Assigned.Should().Be(0);
        second.Conflicts.Should().BeEmpty();
        var stored = await Reload(member);
        stored.BioguideId.Should().Be("N000190");
        stored.BioguideIdSource.Should().Be(CongressBioguideIdSource.SeatAndSurnameMatch);
    }

    private CongressMemberIdentityService CreateSut() =>
        new(Fixture.CreateDbContext(), NullLogger<CongressMemberIdentityService>());

    private async Task<CongressMember> SeedMember(
        string name,
        string seat,
        DateOnly? filingDate,
        string bioguideId = null,
        CongressBioguideIdSource? source = null
    )
    {
        var member = new CongressMember
        {
            Name = name,
            Position = CongressPosition.Representative,
            StateDistrict = seat,
            BioguideId = bioguideId,
            BioguideIdSource = source,
        };
        DbContext.Add(member);
        if (filingDate != null)
        {
            DbContext.Add(
                new CongressionalTrade
                {
                    CongressMember = member,
                    TransactionDate = filingDate.Value.AddDays(-20),
                    FilingDate = filingDate.Value,
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "",
                    AssetName = $"Asset for {name}",
                    AmountFrom = 1_001,
                    AmountTo = 15_000,
                }
            );
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return member;
    }

    private async Task SeedAnnualReport(CongressMember member, DateOnly filedDate)
    {
        DbContext.Add(
            new CongressionalAnnualDisclosure
            {
                CongressMemberId = member.Id,
                Year = filedDate.Year - 1,
                FiledDate = filedDate,
                ReportId = $"report-{member.Id:N}",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CongressMember> Reload(CongressMember member)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<CongressMember>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == member.Id);
    }

    private static CongressLegislator Legislator(string bioguideId, string lastName) =>
        new()
        {
            BioguideId = bioguideId,
            FirstName = "First",
            LastName = lastName,
        };

    private static CongressMemberTerm Term(
        string bioguideId,
        CongressPosition chamber,
        string state,
        short? district,
        int startYear,
        int endYear
    ) =>
        new()
        {
            BioguideId = bioguideId,
            Chamber = chamber,
            State = state,
            District = district,
            Party = "Republican",
            StartDate = new DateOnly(startYear, 1, 3),
            EndDate = new DateOnly(endYear, 1, 3),
        };
}
