using System.Text;
using Equibles.Integrations.Sec.FormAdv;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Record-replay pins for the roster detail the parser reads beyond identity and fees, against
/// real rows of SEC's October 2026 registered and exempt reporting adviser rosters: CIK, LEI,
/// filing date, Items 2, 3, 6 and 7, private fund counts and the Item 5.D client types.
/// </summary>
public class FormAdvCsvParserRosterDetailTests
{
    private static Dictionary<int, FormAdvAdviserData> Parse(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "FormAdv", file);
        using var reader = new StreamReader(path, Encoding.Latin1);
        return FormAdvCsvParser.Parse(reader).ToDictionary(adviser => adviser.Crd);
    }

    private static int Items(params int[] numbers) =>
        numbers.Aggregate(0, (items, number) => items | 1 << (number - 1));

    [Fact]
    public void RegisteredFundManager_ReadsIdentityItemsAndItsSingleClientType()
    {
        var vanguard = Parse("ria-sample-2026-10.csv")[105958];

        vanguard.FirmType.Should().Be("Registered");
        vanguard.Cik.Should().Be("102909");
        vanguard.CikCount.Should().Be(1);
        vanguard.Lei.Should().Be("5493002789CX3L0CJP65");
        vanguard.LatestFilingDate.Should().Be(new DateOnly(2026, 9, 21));
        vanguard.OrganizationForm.Should().Be("Corporation");
        vanguard.RegistrationBasisItems.Should().Be(Items(1, 5));
        vanguard.OtherBusinessItems.Should().Be(Items(3));
        vanguard.RelatedPersonItems.Should().Be(Items(1, 2, 3, 6, 8, 9, 12, 13, 16));
        vanguard.AdvisesPrivateFunds.Should().BeFalse();
        vanguard.PrivateFundCount.Should().Be(0);
        vanguard.HedgeFundCount.Should().Be(0, "an \"N\" to Any Hedge Funds is a reported zero");
        vanguard.PrivateFundGrossAssets.Should().Be(0);
        // Every other type is a reported 0 with .00 assets, so only investment companies remain.
        vanguard
            .ClientTypes.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new FormAdvClientTypeData
                {
                    Index = 3,
                    ClientCount = 228,
                    RegulatoryAum = 11_092_665_107_962L,
                }
            );
    }

    [Fact]
    public void FewerThanFiveClients_StoresTheFlagInsteadOfTheFiledZeroCount()
    {
        var quint = Parse("ria-sample-2026-10.csv")[26586];

        quint.ClientTypes.Select(type => type.Index).Should().Equal(0, 1, 6);
        quint.ClientTypes[0].FewerThanFiveClients.Should().BeTrue();
        quint.ClientTypes[0].ClientCount.Should().BeNull();
        quint.ClientTypes[0].RegulatoryAum.Should().BeNull();
        quint.ClientTypes[1].ClientCount.Should().Be(60);
        quint.ClientTypes[1].RegulatoryAum.Should().Be(750_000_000L);
        quint.ClientTypes[2].ClientCount.Should().Be(20);
        quint.AdvisesPrivateFunds.Should().BeTrue();
        quint.PrivateFundCount.Should().Be(1);
        quint.HedgeFundCount.Should().Be(1);
        quint.PrivateEquityFundCount.Should().Be(0);
        quint.PrivateFundGrossAssets.Should().Be(1_480_751L);
        quint.RegistrationBasisItems.Should().Be(Items(1, 10));
    }

    [Fact]
    public void FreeTextItems_AreReturnedAsFiled()
    {
        var advisers = Parse("ria-sample-2026-10.csv");

        advisers[39550].Lei.Should().Be("N/A", "placeholders are filtered by the importer");
        advisers[39550].Cik.Should().BeNull();
        advisers[14553].OrganizationForm.Should().Be("Other (specify)");
    }

    [Fact]
    public void ExemptReportingAdvisers_ReadItem2BAndPrivateFundsWithoutClientTypes()
    {
        var advisers = Parse("era-sample-2026-10.csv");

        var ventureCapital = advisers[289165];
        ventureCapital.FirmType.Should().Be("ERA");
        ventureCapital.ExemptionBasisItems.Should().Be(Items(1));
        ventureCapital.RegistrationBasisItems.Should().Be(0);
        ventureCapital.VentureCapitalFundCount.Should().Be(7);
        ventureCapital.PrivateFundGrossAssets.Should().Be(226_090_000L);
        ventureCapital.TotalRegulatoryAum.Should().BeNull();
        ventureCapital.ClientTypes.Should().BeEmpty();

        var privateFund = advisers[328555];
        privateFund.ExemptionBasisItems.Should().Be(Items(2));
        privateFund.HedgeFundCount.Should().Be(1);
    }
}
