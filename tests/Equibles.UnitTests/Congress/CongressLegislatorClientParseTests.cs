using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Replays trimmed excerpts of the congress-legislators roster (real records, unused fields
/// dropped) plus two synthetic records for the rejection and overlap paths.
/// </summary>
public class CongressLegislatorClientParseTests
{
    private static async Task<LegislatorRosterParseResult> Parse(string fileName)
    {
        await using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Congress",
                "Legislators",
                fileName
            )
        );
        return await CongressLegislatorClient.ParseLegislators(stream);
    }

    private static Task<LegislatorRosterParseResult> Current() =>
        Parse("legislators-current-excerpt.json");

    private static Task<LegislatorRosterParseResult> Historical() =>
        Parse("legislators-historical-excerpt.json");

    private static Task<LegislatorRosterParseResult> Synthetic() =>
        Parse("legislators-synthetic.json");

    private static CongressLegislator Find(LegislatorRosterParseResult result, string id) =>
        result.Legislators.Single(legislator => legislator.BioguideId == id);

    [Fact]
    public async Task ParseLegislators_HouseSwitchWithInclusiveEnd_SplitsAtTheNextPartysStart()
    {
        var vanDrew = Find(await Current(), "V000133");

        vanDrew
            .Terms.Where(term => term.StartDate.Year <= 2019)
            .Select(term => (term.Party, term.StartDate, term.EndDate))
            .Should()
            .Equal(
                ("Democrat", new DateOnly(2019, 1, 3), new DateOnly(2019, 12, 19)),
                ("Republican", new DateOnly(2019, 12, 19), new DateOnly(2021, 1, 3))
            );
        vanDrew.Terms.Should().HaveCount(5);
    }

    [Fact]
    public async Task ParseLegislators_SenateSwitch_SplitsTermByParty()
    {
        var sinema = Find(await Historical(), "S001191");

        sinema
            .Terms.Where(term => term.Chamber == CongressPosition.Senator)
            .Select(term => (term.Party, term.StartDate, term.EndDate, term.District))
            .Should()
            .Equal(
                ("Democrat", new DateOnly(2019, 1, 3), new DateOnly(2022, 12, 9), (short?)null),
                ("Independent", new DateOnly(2022, 12, 9), new DateOnly(2025, 1, 3), null)
            );
    }

    [Fact]
    public async Task ParseLegislators_SwitchWithSharedBoundary_SplitsOnThatDate()
    {
        var manchin = Find(await Historical(), "M001183");

        manchin
            .Terms.Where(term => term.StartDate.Year >= 2019)
            .Select(term => (term.Party, term.StartDate, term.EndDate))
            .Should()
            .Equal(
                ("Democrat", new DateOnly(2019, 1, 3), new DateOnly(2024, 5, 31)),
                ("Independent", new DateOnly(2024, 5, 31), new DateOnly(2025, 1, 3))
            );
        manchin.Suffix.Should().Be("III");
    }

    [Fact]
    public async Task ParseLegislators_AtLargeSeat_IsDistrictZero()
    {
        var young = Find(await Historical(), "Y000033");

        young.Terms.Should().HaveCount(25);
        young
            .Terms.Should()
            .OnlyContain(term =>
                term.Chamber == CongressPosition.Representative
                && term.State == "AK"
                && term.District == 0
            );
        young.Terms[^1].EndDate.Should().Be(new DateOnly(2022, 3, 18));
    }

    [Fact]
    public async Task ParseLegislators_Delegate_IsDistrictZero()
    {
        var norton = Find(await Current(), "N000147");

        norton.Terms.Should().HaveCount(18);
        norton.Terms.Should().OnlyContain(term => term.State == "DC" && term.District == 0);
        norton.OfficialFullName.Should().Be("Eleanor Holmes Norton");
    }

    [Fact]
    public async Task ParseLegislators_HouseThenSenate_TermsMeetWithoutOverlap()
    {
        var budd = Find(await Current(), "B001305");

        budd.Terms.Select(term => (term.Chamber, term.StartDate, term.EndDate, term.District))
            .Should()
            .Equal(
                (
                    CongressPosition.Representative,
                    new DateOnly(2017, 1, 3),
                    new DateOnly(2019, 1, 3),
                    (short?)13
                ),
                (
                    CongressPosition.Representative,
                    new DateOnly(2019, 1, 3),
                    new DateOnly(2021, 1, 3),
                    13
                ),
                (
                    CongressPosition.Representative,
                    new DateOnly(2021, 1, 3),
                    new DateOnly(2023, 1, 3),
                    13
                ),
                (CongressPosition.Senator, new DateOnly(2023, 1, 3), new DateOnly(2029, 1, 3), null)
            );
    }

    [Fact]
    public async Task ParseLegislators_Representative_KeepsNameStateDistrictAndParty()
    {
        var ocasioCortez = Find(await Current(), "O000172");

        ocasioCortez.FirstName.Should().Be("Alexandria");
        ocasioCortez.LastName.Should().Be("Ocasio-Cortez");
        ocasioCortez.Suffix.Should().BeNull();
        ocasioCortez
            .Terms.Should()
            .OnlyContain(term =>
                term.State == "NY" && term.District == 14 && term.Party == "Democrat"
            );
        ocasioCortez.Terms[0].StartDate.Should().Be(new DateOnly(2019, 1, 3));
    }

    [Fact]
    public async Task ParseLegislators_Suffix_IsKept()
    {
        var bishop = Find(await Current(), "B000490");

        bishop.LastName.Should().Be("Bishop");
        bishop.Suffix.Should().Be("Jr.");
        bishop.OfficialFullName.Should().Be("Sanford D. Bishop, Jr.");
    }

    [Fact]
    public async Task ParseLegislators_IndependentSenator_KeepsPartyAndCaucus()
    {
        var sanders = Find(await Current(), "S000033");

        var latest = sanders.Terms[^1];
        latest.Chamber.Should().Be(CongressPosition.Senator);
        latest.District.Should().BeNull();
        latest.Party.Should().Be("Independent");
        latest.Caucus.Should().Be("Democrat");
    }

    [Fact]
    public async Task ParseLegislators_ServiceEndedBefore2012_IsSkippedButCounted()
    {
        var historical = await Historical();

        historical.RecordCount.Should().Be(4);
        historical
            .Legislators.Select(legislator => legislator.BioguideId)
            .Should()
            .BeEquivalentTo("S001191", "Y000033", "M001183");
        historical.Rejections.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseLegislators_EveryLegislator_HasNoOverlappingTerms()
    {
        var all = (await Current()).Legislators.Concat((await Historical()).Legislators);

        foreach (var legislator in all)
        {
            legislator
                .Terms.Zip(legislator.Terms.Skip(1))
                .Should()
                .OnlyContain(pair => pair.First.EndDate <= pair.Second.StartDate);
            legislator.Terms.Should().OnlyContain(term => term.EndDate > term.StartDate);
        }
    }

    [Fact]
    public async Task ParseLegislators_PartySegmentsLeaveAGap_RejectsTheLegislator()
    {
        var synthetic = await Synthetic();

        synthetic.Legislators.Should().NotContain(legislator => legislator.BioguideId == "Z999901");
        synthetic.Rejections.Should().ContainSingle().Which.BioguideId.Should().Be("Z999901");
    }

    [Fact]
    public async Task ParseLegislators_ChamberTermsOverlap_TrimsTheEarlierTerm()
    {
        var switcher = Find(await Synthetic(), "Z999902");

        switcher
            .Terms.Select(term => (term.Chamber, term.StartDate, term.EndDate))
            .Should()
            .Equal(
                (
                    CongressPosition.Representative,
                    new DateOnly(2021, 1, 3),
                    new DateOnly(2022, 11, 8)
                ),
                (CongressPosition.Senator, new DateOnly(2022, 11, 8), new DateOnly(2029, 1, 3))
            );
    }

    [Fact]
    public async Task ParseLegislators_TermEndsTheDayBeforeTheNext_MeetsTheNextTerm()
    {
        const string json = """
            [{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[
              {"type":"rep","start":"2021-01-03","end":"2022-12-21","state":"OH","district":1,"party":"Democrat"},
              {"type":"rep","start":"2022-12-22","end":"2025-01-03","state":"OH","district":1,"party":"Democrat"}]}]
            """;
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await CongressLegislatorClient.ParseLegislators(stream);

        result
            .Legislators.Single()
            .Terms.Select(term => (term.StartDate, term.EndDate))
            .Should()
            .Equal(
                (new DateOnly(2021, 1, 3), new DateOnly(2022, 12, 22)),
                (new DateOnly(2022, 12, 22), new DateOnly(2025, 1, 3))
            );
    }

    [Theory]
    [InlineData("""[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":null}]""")]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH","party":"Democrat","party_affiliations":null}]}]"""
    )]
    public async Task ParseLegislators_NullCollections_DoNotAbortTheRead(string json)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await CongressLegislatorClient.ParseLegislators(stream);

        result.RecordCount.Should().Be(1);
        result.Rejections.Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"rep","start":"2019-01-03","end":"2021-01-03","state":"OH","party":"Democrat"}]}]""",
        "House term without a district"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH","party":"Democrat","party_affiliations":[{"start":"2019-01-03","end":"2020-01-03","party":"Democrat"},{"start":"2020-01-05","end":"2025-01-03","party":"Independent"}]}]}]""",
        "party segments two days apart"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"del","start":"2019-01-03","end":"2021-01-03","state":"OH","district":1,"party":"Democrat"}]}]""",
        "unknown term type"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2021-01-03","end":"2021-01-03","state":"OH","party":"Democrat"}]}]""",
        "empty term"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH"}]}]""",
        "missing party"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH","party":"Democrat","party_affiliations":[{"start":"2019-01-03","end":"2020-01-03","party":"Democrat"},{"start":"2020-01-01","end":"2025-01-03","party":"Independent"}]}]}]""",
        "overlapping party segments"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH","party":"Democrat","party_affiliations":[{"start":"2019-01-03","end":"2020-01-03","party":"Democrat"},{"start":"2020-01-03","end":"2024-01-03","party":"Independent"}]}]}]""",
        "segments stopping short of the term end"
    )]
    [InlineData(
        """[{"id":{"bioguide":"Z1"},"name":{"first":"A","last":"B"},"terms":[{"type":"rep","start":"2019-01-03","end":"2021-01-03","state":"OH","district":1,"party":"Democrat"},{"type":"sen","start":"2019-01-03","end":"2025-01-03","state":"OH","party":"Democrat"}]}]""",
        "two terms starting the same day"
    )]
    public async Task ParseLegislators_UnstorableRecord_RejectsIt(string json, string because)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await CongressLegislatorClient.ParseLegislators(stream);

        result.Legislators.Should().BeEmpty(because);
        result.Rejections.Should().ContainSingle(because).Which.BioguideId.Should().Be("Z1");
        result.RecordCount.Should().Be(1);
    }
}
