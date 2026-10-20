using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormAdvVersionContentTests
{
    private static readonly DateOnly Roster = new(2020, 1, 1);

    [Fact]
    public void Hash_IgnoresTheFilingDateAndClientTypeOrder()
    {
        var first = Version(Adviser(1_000, new DateOnly(2019, 3, 1)), Corporations, Individuals);
        var refiled = Version(Adviser(1_000, new DateOnly(2019, 9, 1)), Individuals, Corporations);

        refiled.ContentHash.Should().Be(first.ContentHash);
        refiled.LatestFilingDate.Should().Be(new DateOnly(2019, 9, 1));
        refiled
            .ClientTypes.Select(clientType => clientType.ClientType)
            .Should()
            .Equal(FormAdvClientType.Individuals, FormAdvClientType.Corporations);
    }

    [Fact]
    public void Hash_ChangesWithATrackedFigure()
    {
        var first = Version(Adviser(1_000, null), Corporations);

        Version(Adviser(1_001, null), Corporations).ContentHash.Should().NotBe(first.ContentHash);
        Version(Adviser(1_000, null), Individuals).ContentHash.Should().NotBe(first.ContentHash);
        var renamed = Adviser(1_000, null);
        renamed.LegalName = "RENAMED LLC";
        Version(renamed, Corporations).ContentHash.Should().NotBe(first.ContentHash);
    }

    [Fact]
    public void Hash_TellsAMissingFigureFromZero()
    {
        var unknown = Adviser(1_000, null);
        unknown.PrivateFundCount = null;
        var none = Adviser(1_000, null);
        none.PrivateFundCount = 0;

        Version(none, Corporations)
            .ContentHash.Should()
            .NotBe(Version(unknown, Corporations).ContentHash);
    }

    private static readonly FormAdvAdviserClientType Corporations = new()
    {
        Crd = 111,
        ClientType = FormAdvClientType.Corporations,
        ClientCount = 3,
        RegulatoryAum = 600,
    };

    private static readonly FormAdvAdviserClientType Individuals = new()
    {
        Crd = 111,
        ClientType = FormAdvClientType.Individuals,
        ClientCount = 40,
        RegulatoryAum = 400,
    };

    private static FormAdvAdviser Adviser(long aum, DateOnly? filed) =>
        new()
        {
            Crd = 111,
            SecNumber = "801-11111",
            LegalName = "ACME ADVISORS",
            TotalRegulatoryAum = aum,
            LatestFilingDate = filed,
        };

    private static FormAdvAdviserVersion Version(
        FormAdvAdviser adviser,
        params FormAdvAdviserClientType[] clientTypes
    ) => FormAdvVersionContent.From(adviser, clientTypes, FormAdvRegistration.Registered, Roster);
}
