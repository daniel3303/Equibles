using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService;
using Equibles.Congress.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

[Collection(ParadeDbCollection.Name)]
public class CongressLegislatorSyncServiceTests : ParadeDbMcpTestBase
{
    private const int FillerCount = 500;

    public CongressLegislatorSyncServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncAll_FullRoster_StoresLegislatorsAndPartySplitTerms()
    {
        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressLegislator>().CountAsync())
            .Should()
            .Be(FillerCount + 6 + 1 + 3, "six real current, one synthetic, three historical kept");
        var sinema = await verify
            .Set<CongressMemberTerm>()
            .AsNoTracking()
            .Where(term => term.BioguideId == "S001191" && term.Chamber == CongressPosition.Senator)
            .OrderBy(term => term.StartDate)
            .Select(term => new { term.Party, term.EndDate })
            .ToListAsync();
        sinema
            .Select(term => (term.Party, term.EndDate))
            .Should()
            .Equal(
                ("Democrat", new DateOnly(2022, 12, 9)),
                ("Independent", new DateOnly(2025, 1, 3))
            );
        (await verify.Set<CongressLegislator>().AnyAsync(l => l.BioguideId == "Z999901"))
            .Should()
            .BeFalse("a legislator whose party segments leave a gap is rejected whole");
        (await verify.Set<CongressLegislator>().AnyAsync(l => l.BioguideId == "H000323"))
            .Should()
            .BeFalse("a career that ended before 2012 is not stored");
        var error = await verify.Set<Error>().AsNoTracking().SingleAsync();
        error.Context.Should().Be(RejectedErrorContext);
        error.Source.Should().Be(ErrorSource.CongressScraper);
        error.Message.Should().Contain("Z999901");
    }

    [Fact]
    public async Task SyncAll_SecondRun_KeepsEveryTermRow()
    {
        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);
        var firstIds = await TermIds();

        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);

        (await TermIds()).Should().BeEquivalentTo(firstIds);
    }

    [Fact]
    public async Task SyncAll_TermDroppedOrShortened_DeletesAndUpdatesInPlace()
    {
        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);
        var before = await TermsOf("O000172");

        var current = JsonNode.Parse(Current()).AsArray();
        var terms = current
            .Single(node => (string)node["id"]["bioguide"] == "O000172")["terms"]
            .AsArray();
        terms.RemoveAt(terms.Count - 1);
        terms[^1]["end"] = "2024-12-31";
        await CreateSut(current.ToJsonString(), Historical()).SyncAll(CancellationToken.None);

        var after = await TermsOf("O000172");
        after.Should().HaveCount(before.Count - 1);
        after.Select(term => term.Id).Should().BeSubsetOf(before.Select(term => term.Id));
        after[^1].EndDate.Should().Be(new DateOnly(2024, 12, 31));
        after[^1].Id.Should().Be(before[^2].Id);
    }

    [Fact]
    public async Task SyncAll_UndersizedCurrentRoster_WritesNothing()
    {
        var sut = CreateSut(ReadAsset("legislators-current-excerpt.json"), Historical());

        var act = async () => await sut.SyncAll(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressLegislator>().CountAsync()).Should().Be(0);
        (await verify.Set<CongressMemberTerm>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncAll_EmptyHistoricalRoster_KeepsStoredTerms()
    {
        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);
        var stored = await TermIds();

        var act = async () => await CreateSut(Current(), "[]").SyncAll(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await TermIds()).Should().BeEquivalentTo(stored);
    }

    [Fact]
    public async Task SyncAll_IdInBothFiles_KeepsTheCurrentRecord()
    {
        var historical = JsonNode.Parse(Historical()).AsArray();
        historical.Add(
            JsonNode.Parse(
                """
                {"id":{"bioguide":"B001305"},"name":{"first":"Stale","last":"Budd"},
                 "terms":[{"type":"rep","start":"2013-01-03","end":"2015-01-03","state":"NC",
                           "district":1,"party":"Democrat"}]}
                """
            )
        );
        historical.Add(
            JsonNode.Parse(
                """
                {"id":{"bioguide":"Z999901"},"name":{"first":"Valid","last":"Elsewhere"},
                 "terms":[{"type":"rep","start":"2013-01-03","end":"2015-01-03","state":"OH",
                           "district":3,"party":"Democrat"}]}
                """
            )
        );

        await CreateSut(Current(), historical.ToJsonString()).SyncAll(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var budd = await verify
            .Set<CongressLegislator>()
            .AsNoTracking()
            .SingleAsync(l => l.BioguideId == "B001305");
        budd.FirstName.Should().Be("Ted");
        (await TermsOf("B001305")).Should().NotContain(term => term.StartDate.Year == 2013);
        (await verify.Set<CongressLegislator>().AnyAsync(l => l.BioguideId == "Z999901"))
            .Should()
            .BeFalse("the current file rejected that id, so the historical record is not used");
    }

    [Fact]
    public async Task SyncAll_UnresolvedMembers_MatchesThemAndReportsConflictsOnce()
    {
        var bishop = Member("Sanford D. Bishop Jr.", "GA02", null);
        var norton = Member("Eleanor Holmes Norton", "DC00", "X999999");
        DbContext.AddRange(bishop, norton, Trade(bishop), Trade(norton));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await CreateSut(Current(), Historical()).SyncAll(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var members = await verify
            .Set<CongressMember>()
            .AsNoTracking()
            .ToDictionaryAsync(member => member.Name);
        members["Sanford D. Bishop Jr."].BioguideId.Should().Be("B000490");
        members["Sanford D. Bishop Jr."]
            .BioguideIdSource.Should()
            .Be(CongressBioguideIdSource.SeatAndSurnameMatch);
        members["Eleanor Holmes Norton"].BioguideId.Should().Be("X999999");
        var conflict = await verify
            .Set<Error>()
            .AsNoTracking()
            .SingleAsync(error => error.Context == ConflictErrorContext);
        conflict.Message.Should().Contain("Eleanor Holmes Norton").And.Contain("N000147");
    }

    [Fact]
    public async Task DoWork_ResolvesSyncServiceAndRunsOneSync()
    {
        var syncService = CreateSut(Current(), Historical());
        var worker = new CongressLegislatorSyncWorker(
            NullLogger<CongressLegislatorSyncWorker>(),
            ServiceScopeSubstitute.Create((typeof(CongressLegislatorSyncService), syncService)),
            new ErrorReporter(
                Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                NullLogger<ErrorReporter>()
            )
        );
        var doWork = typeof(CongressLegislatorSyncWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        await (Task)doWork.Invoke(worker, [CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressMemberTerm>().AnyAsync(term => term.BioguideId == "B001305"))
            .Should()
            .BeTrue();
    }

    private const string RejectedErrorContext = "CongressLegislators.Rejected";

    private const string ConflictErrorContext = "CongressMembers.LegislatorIdentityConflict";

    private static CongressMember Member(string name, string seat, string bioguideId) =>
        new()
        {
            Name = name,
            Position = CongressPosition.Representative,
            StateDistrict = seat,
            BioguideId = bioguideId,
            BioguideIdSource =
                bioguideId == null ? null : CongressBioguideIdSource.SeatAndSurnameMatch,
        };

    private static CongressionalTrade Trade(CongressMember member) =>
        new()
        {
            CongressMember = member,
            TransactionDate = new DateOnly(2024, 4, 1),
            FilingDate = new DateOnly(2024, 5, 1),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "",
            AssetName = $"Asset for {member.Name}",
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };

    private CongressLegislatorSyncService CreateSut(string currentJson, string historicalJson)
    {
        var client = new CongressLegislatorClient(
            new HttpClient(new RosterHandler(currentJson, historicalJson)),
            NullLogger<CongressLegislatorClient>()
        );
        var errorReporter = new ErrorReporter(
            ServiceScopeSubstitute.Create((typeof(ErrorManager), ErrorManager)),
            NullLogger<ErrorReporter>()
        );
        var context = Fixture.CreateDbContext();
        return new CongressLegislatorSyncService(
            context,
            client,
            new CongressMemberIdentityService(context, NullLogger<CongressMemberIdentityService>()),
            errorReporter,
            NullLogger<CongressLegislatorSyncService>()
        );
    }

    private async Task<List<Guid>> TermIds()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<CongressMemberTerm>().Select(term => term.Id).ToListAsync();
    }

    private async Task<List<CongressMemberTerm>> TermsOf(string bioguideId)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<CongressMemberTerm>()
            .AsNoTracking()
            .Where(term => term.BioguideId == bioguideId)
            .OrderBy(term => term.StartDate)
            .ToListAsync();
    }

    private static string Historical() => ReadAsset("legislators-historical-excerpt.json");

    // The real current excerpt and the synthetic records, padded past the roster-size guard.
    private static string Current()
    {
        var array = JsonNode.Parse(ReadAsset("legislators-current-excerpt.json")).AsArray();
        foreach (var node in JsonNode.Parse(ReadAsset("legislators-synthetic.json")).AsArray())
            array.Add(node.DeepClone());
        for (var i = 0; i < FillerCount; i++)
        {
            array.Add(
                JsonNode.Parse(
                    $$"""
                    {"id":{"bioguide":"X{{i:D6}}"},"name":{"first":"Filler","last":"Member"},
                     "terms":[{"type":"rep","start":"2023-01-03","end":"2025-01-03","state":"WY",
                               "district":{{i % 50 + 1}},"party":"Democrat"}]}
                    """
                )
            );
        }

        return array.ToJsonString();
    }

    private static string ReadAsset(string fileName) =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Congress",
                "Legislators",
                fileName
            )
        );

    private sealed class RosterHandler(string currentJson, string historicalJson)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            var body =
                path.EndsWith("/legislators-current.json") ? currentJson
                : path.EndsWith("/legislators-historical.json") ? historicalJson
                : null;
            return Task.FromResult(
                body == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    }
            );
        }
    }
}
