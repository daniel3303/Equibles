using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressMemberLegislatorMatcherTests
{
    private static readonly LegislatorTermCandidate[] Terms =
    [
        House("N000190", "Norman", "SC", 5, 2023, 2025),
        House("Y000033", "Young", "AK", 0, 2021, 1, 3, 2022, 3, 18),
        House("N000147", "Norton", "DC", 0, 2021, 2023),
        House("C001038", "Crowley", "NY", 14, 2017, 2019),
        House("O000172", "Ocasio-Cortez", "NY", 14, 2019, 2021),
        // Redistricting: Smith moves from NC-8 to NC-9, which Jones held before.
        House("Z000001", "Smith", "NC", 8, 2019, 2023),
        House("Z000002", "Jones", "NC", 9, 2019, 2023),
        House("Z000001", "Smith", "NC", 9, 2023, 2025),
        House("B001305", "Budd", "NC", 13, 2019, 2023),
        Senate("B001305", "Budd", "NC", 2023, 2029),
        Senate("T000476", "Tillis", "NC", 2021, 2027),
        Senate("S001217", "Scott", "FL", 2019, 2025),
        Senate("S001184", "Scott", "SC", 2017, 2023),
        Senate("J000293", "Johnson", "WI", 2011, 2017),
        Senate("J000177", "Johnson", "SD", 2009, 2015),
    ];

    private static readonly CongressMemberLegislatorMatcher Matcher = new(Terms);

    [Theory]
    [InlineData("Alexandria Ocasio-Cortez", "Ocasio-Cortez")]
    [InlineData("Alexandria Ocasio Cortez", "Ocasio-Cortez")]
    [InlineData("Alexandria Ocasio-Cortez", "Ocasio Cortez")]
    [InlineData("Chris Van Hollen", "Van Hollen")]
    [InlineData("John McGuire III", "McGuire")]
    [InlineData("John J. McGuire III", "McGuire")]
    [InlineData("Sanford D. Bishop Jr.", "Bishop")]
    [InlineData("Ben Ray Lujan", "Luján")]
    [InlineData("Ben Ray Luján", "Lujan")]
    [InlineData("Beto O'Rourke", "O’Rourke")]
    [InlineData("hon. al GREEN", "Green")]
    public void SurnameMatches_SameSurnameWrittenDifferently_Matches(string filer, string last) =>
        CongressMemberLegislatorMatcher.SurnameMatches(filer, last).Should().BeTrue();

    [Theory]
    [InlineData("Marjorie Taylor Greene", "Green")]
    [InlineData("Al Green", "Greene")]
    [InlineData("Sherrod Brown", "Brownley")]
    [InlineData("Julia Brownley", "Brown")]
    [InlineData("Chris Hollen", "Van Hollen")]
    [InlineData("Brown Sherrod", "Brown")]
    [InlineData("", "Brown")]
    [InlineData("Sherrod Brown", "")]
    public void SurnameMatches_DifferentSurname_DoesNotMatch(string filer, string last) =>
        CongressMemberLegislatorMatcher.SurnameMatches(filer, last).Should().BeFalse();

    [Fact]
    public void SurnameMatches_SuffixLeavingOneToken_IsKept() =>
        CongressMemberLegislatorMatcher.SurnameMatches("Smith Jr", "Jr").Should().BeTrue();

    [Fact]
    public void Match_UniqueSeatHolderWithSurname_ReturnsTheirId() =>
        Matcher.Match(House("Ralph Norman", "SC05", 2024, 3, 1)).Should().Be("N000190");

    [Fact]
    public void Match_SeatHolderWithAnotherSurname_ReturnsNull() =>
        Matcher.Match(House("Someone Else", "SC05", 2024, 3, 1)).Should().BeNull();

    [Fact]
    public void Match_RedistrictedMember_ResolvesFromTheDatesTheyHeldTheStoredSeat() =>
        Matcher
            .Match(House("Jane Smith", "NC09", (2020, 5, 1), (2024, 5, 1)))
            .Should()
            .Be("Z000001");

    [Theory]
    [InlineData("Don Young", "AK00", "Y000033")]
    [InlineData("Eleanor Holmes Norton", "DC00", "N000147")]
    public void Match_AtLargeSeatOrDelegate_Resolves(string name, string seat, string expected) =>
        Matcher.Match(House(name, seat, 2022, 1, 15)).Should().Be(expected);

    [Fact]
    public void Match_OnlyFilingAfterTheTermEnded_ReturnsNull() =>
        Matcher.Match(House("Don Young", "AK00", 2022, 6, 1)).Should().BeNull();

    [Fact]
    public void Match_FilingOnJanuaryThird_BelongsToTheIncomingMember()
    {
        Matcher.Match(House("Joseph Crowley", "NY14", 2019, 1, 3)).Should().BeNull();
        Matcher.Match(House("Alexandria Ocasio-Cortez", "NY14", 2019, 1, 3)).Should().Be("O000172");
    }

    [Fact]
    public void Match_TwoSittingSenatorsShareTheSurname_ReturnsNull() =>
        Matcher.Match(Senator("Rick Scott", 2021, 6, 1)).Should().BeNull();

    [Fact]
    public void Match_OverlappingSenateTermsWithTheSurname_ReturnsNull() =>
        Matcher.Match(Senator("Ron Johnson", 2013, 6, 1)).Should().BeNull();

    [Fact]
    public void Match_SenatorWithUniqueSurname_Resolves() =>
        Matcher.Match(Senator("Thom Tillis", 2024, 6, 1)).Should().Be("T000476");

    [Fact]
    public void Match_NoFilingDates_ReturnsNull() =>
        Matcher
            .Match(
                new CongressMemberMatchSubject(
                    "Ralph Norman",
                    CongressPosition.Representative,
                    "SC05",
                    []
                )
            )
            .Should()
            .BeNull();

    [Theory]
    [InlineData("PUERTO RICO")]
    [InlineData(null)]
    public void Match_RepresentativeWithoutParsableSeat_ReturnsNull(string seat) =>
        Matcher.Match(House("Ralph Norman", seat, 2024, 3, 1)).Should().BeNull();

    [Fact]
    public void Match_MemberWhoServedInBothChambers_ResolvesToOneLegislator() =>
        Matcher
            .Match(
                new CongressMemberMatchSubject(
                    "Ted Budd",
                    CongressPosition.Senator,
                    "NC13",
                    [new DateOnly(2020, 2, 1), new DateOnly(2024, 2, 1)]
                )
            )
            .Should()
            .Be("B001305");

    private static CongressMemberMatchSubject House(
        string name,
        string seat,
        int year,
        int month,
        int day
    ) => House(name, seat, (year, month, day));

    private static CongressMemberMatchSubject House(
        string name,
        string seat,
        params (int Year, int Month, int Day)[] dates
    ) =>
        new(
            name,
            CongressPosition.Representative,
            seat,
            dates.Select(d => new DateOnly(d.Year, d.Month, d.Day)).ToList()
        );

    private static CongressMemberMatchSubject Senator(string name, int year, int month, int day) =>
        new(name, CongressPosition.Senator, null, [new DateOnly(year, month, day)]);

    private static LegislatorTermCandidate House(
        string id,
        string last,
        string state,
        short district,
        int startYear,
        int endYear
    ) => House(id, last, state, district, startYear, 1, 3, endYear, 1, 3);

    private static LegislatorTermCandidate House(
        string id,
        string last,
        string state,
        short district,
        int startYear,
        int startMonth,
        int startDay,
        int endYear,
        int endMonth,
        int endDay
    ) =>
        new(
            id,
            last,
            CongressPosition.Representative,
            state,
            district,
            new DateOnly(startYear, startMonth, startDay),
            new DateOnly(endYear, endMonth, endDay)
        );

    private static LegislatorTermCandidate Senate(
        string id,
        string last,
        string state,
        int startYear,
        int endYear
    ) =>
        new(
            id,
            last,
            CongressPosition.Senator,
            state,
            null,
            new DateOnly(startYear, 1, 3),
            new DateOnly(endYear, 1, 3)
        );
}
