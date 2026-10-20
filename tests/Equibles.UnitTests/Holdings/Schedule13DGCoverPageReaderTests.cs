using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Schedule13DGCoverPageReaderTests
{
    private static string Cover(string citizenshipRow) =>
        "1 NAMES OF REPORTING PERSONS Acme Capital LLC 2 CHECK THE APPROPRIATE BOX IF A MEMBER OF A GROUP (a) [ ] (b) [ ] 3 SEC USE ONLY "
        + citizenshipRow
        + " 5 SOLE VOTING POWER 100 6 SHARED VOTING POWER 0 7 SOLE DISPOSITIVE POWER 100 8 SHARED DISPOSITIVE POWER 0"
        + " 9 AGGREGATE AMOUNT BENEFICIALLY OWNED BY EACH REPORTING PERSON 100"
        + " 10 CHECK IF THE AGGREGATE AMOUNT IN ROW (9) EXCLUDES CERTAIN SHARES [ ]"
        + " 11 PERCENT OF CLASS REPRESENTED BY AMOUNT IN ROW (9) 5.1% 12 TYPE OF REPORTING PERSON IA";

    // Filed variants of the citizenship caption and of the side heading that follows its answer.
    [Theory]
    [InlineData(
        "4 CITIZENSHIP OR PLACE OF ORGANIZATION Ontario, Canada NUMBER OF SHARES BENEFICIALLY OWNED BY EACH REPORTING PERSON WITH"
    )]
    [InlineData(
        "4 CITIZENSHIP OR PLACE OF ORGANIZATION Ontario, Canada NUMBER OFSHARES BENEFICIALLY OWNED BY EACH REPORTING PERSON WITH"
    )]
    [InlineData(
        "4 Citizenship or place or organization Ontario, Canada Number of Shares Beneficially Owned By Reporting Person With"
    )]
    public void Read_CitizenshipAnswer_LeavesTheCaptionsOut(string citizenshipRow)
    {
        var (persons, _, failure) = Schedule13DGCoverPageReader.Read(Cover(citizenshipRow));

        failure.Should().BeNull();
        var person = persons.Should().ContainSingle().Subject;
        person.Name.Should().Be("Acme Capital LLC");
        person.CitizenshipOrOrganization.Should().Be("Ontario, Canada");
        person.SoleVotingPower.Should().Be(100);
        person.AggregateAmountOwned.Should().Be(100);
        person.PercentOfClass.Should().Be(5.1m);
        person.TypeCodes.Should().Equal("IA");
    }

    [Fact]
    public void Read_AggregateNoPowerAccountsFor_IsRefused()
    {
        var flat = Cover("4 CITIZENSHIP OR PLACE OF ORGANIZATION Delaware")
            .Replace("SOLE VOTING POWER 100", "SOLE VOTING POWER 0")
            .Replace("SOLE DISPOSITIVE POWER 100", "SOLE DISPOSITIVE POWER 0");

        var (_, _, failure) = Schedule13DGCoverPageReader.Read(flat);

        failure
            .Should()
            .Be(
                new Schedule13DGCoverPageReader.Failure(
                    1,
                    "no power accounts for the aggregate amount"
                )
            );
    }
}
