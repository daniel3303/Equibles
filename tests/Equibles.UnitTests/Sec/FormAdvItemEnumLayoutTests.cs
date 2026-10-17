using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The importer casts the parser's item bitmasks (item (n) = bit n-1) straight onto these
/// enums, so each value must sit on its Form ADV item's bit.
/// </summary>
public class FormAdvItemEnumLayoutTests
{
    private static int Item(int number) => 1 << (number - 1);

    [Fact]
    public void RegistrationBasis_FollowsItem2A()
    {
        ((int)FormAdvRegistrationBasis.LargeAdvisoryFirm).Should().Be(Item(1));
        ((int)FormAdvRegistrationBasis.PrincipalOfficeOutsideUnitedStates).Should().Be(Item(4));
        ((int)FormAdvRegistrationBasis.RegisteredInvestmentCompanyAdviser).Should().Be(Item(5));
        ((int)FormAdvRegistrationBasis.BusinessDevelopmentCompanyAdviser).Should().Be(Item(6));
        ((int)FormAdvRegistrationBasis.NoLongerEligible).Should().Be(Item(13));
    }

    [Fact]
    public void ExemptionBasis_FollowsItem2B()
    {
        ((int)FormAdvExemptionBasis.VentureCapitalFundAdviser).Should().Be(Item(1));
        ((int)FormAdvExemptionBasis.PrivateFundAdviserUnder150Million).Should().Be(Item(2));
        ((int)FormAdvExemptionBasis.PrivateFundAdviserAtOrAbove150Million).Should().Be(Item(3));
    }

    [Fact]
    public void OtherBusinessAndRelatedPersons_FollowItems6AAnd7A()
    {
        ((int)FormAdvOtherBusiness.BrokerDealer).Should().Be(Item(1));
        ((int)FormAdvOtherBusiness.Bank).Should().Be(Item(7));
        ((int)FormAdvOtherBusiness.OtherFinancialProductSalesperson).Should().Be(Item(14));
        ((int)FormAdvRelatedPerson.BankOrThrift).Should().Be(Item(8));
        ((int)FormAdvRelatedPerson.InsuranceCompanyOrAgency).Should().Be(Item(12));
        ((int)FormAdvRelatedPerson.PooledVehicleSponsor).Should().Be(Item(16));
    }

    [Fact]
    public void ClientTypes_FollowItem5DLetters()
    {
        ((int)FormAdvClientType.Individuals).Should().Be("abcdefghijklmn".IndexOf('a'));
        ((int)FormAdvClientType.InvestmentCompanies).Should().Be("abcdefghijklmn".IndexOf('d'));
        ((int)FormAdvClientType.PooledInvestmentVehicles)
            .Should()
            .Be("abcdefghijklmn".IndexOf('f'));
        ((int)FormAdvClientType.SovereignWealthFunds).Should().Be("abcdefghijklmn".IndexOf('l'));
        ((int)FormAdvClientType.Other).Should().Be("abcdefghijklmn".IndexOf('n'));
    }
}
