using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class BeneficialOwnershipDetailMappingTests
{
    [Fact]
    public void ToEntity_MapsEveryPersonAndMarksTheLeadTheHoldingsRowsCarry()
    {
        var filing = new Parsed13DGFiling
        {
            AccessionNumber = "0000895421-26-000285",
            FilingType = FilingType.Schedule13G,
            IsAmendment = true,
            FilingDate = new DateOnly(2026, 10, 9),
            DateOfEvent = new DateOnly(2026, 8, 31),
            FilerCik = "895421",
            IssuerCik = "1678130",
            IssuerCusip = "76882G404",
            RuleDesignations = ["Rule 13d-1(b)"],
            FilingPersonTypeCodes = ["BD", "IA", "HC"],
            ReportingPersons =
            [
                new Parsed13DGReportingPerson
                {
                    Name = "Morgan Stanley",
                    TypeCodes = ["HC", "CO"],
                    AggregateAmountOwned = 245_955,
                    PercentOfClass = 10.2m,
                },
                new Parsed13DGReportingPerson
                {
                    Name = "Morgan Stanley Smith Barney LLC",
                    TypeCodes = ["BD", "IA", "CO", "ZZ"],
                    AggregateAmountOwned = 245_955,
                },
            ],
        };

        var entity = BeneficialOwnershipDetailWriter.ToEntity(filing);

        entity.Rules.Should().Be(Schedule13GRules.QualifiedInstitution);
        entity
            .FilingPersonTypes.Should()
            .Be(
                ReportingPersonTypes.BrokerDealer
                    | ReportingPersonTypes.InvestmentAdviser
                    | ReportingPersonTypes.ParentHoldingCompany
            );
        entity.DateOfEvent.Should().Be(new DateOnly(2026, 8, 31));
        entity
            .ReportingPersons.Select(p => (p.Ordinal, p.Types, p.UnrecognizedTypeCodes, p.IsLead))
            .Should()
            .Equal(
                (
                    0,
                    ReportingPersonTypes.ParentHoldingCompany | ReportingPersonTypes.Corporation,
                    (string)null,
                    true
                ),
                (
                    1,
                    ReportingPersonTypes.BrokerDealer
                        | ReportingPersonTypes.InvestmentAdviser
                        | ReportingPersonTypes.Corporation,
                    "ZZ",
                    false
                )
            );
    }

    [Fact]
    public void ToEntity_UnreadableEventDate_IsNull()
    {
        var entity = BeneficialOwnershipDetailWriter.ToEntity(
            new Parsed13DGFiling
            {
                AccessionNumber = "0000000000-26-000001",
                DateOfEvent = DateOnly.MinValue,
            }
        );

        entity.DateOfEvent.Should().BeNull();
        entity.ReportingPersons.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Rule 13d-1(b)", Schedule13GRules.QualifiedInstitution)]
    [InlineData("Rule 13d-1(c)", Schedule13GRules.PassiveInvestor)]
    [InlineData("rule 13D-1 ( d )", Schedule13GRules.ExemptInvestor)]
    [InlineData("Rule 13d-2(b)", Schedule13GRules.None)]
    [InlineData("", Schedule13GRules.None)]
    public void ParseRules_ReadsEachDesignation(string designation, Schedule13GRules expected)
    {
        BeneficialOwnershipDetailWriter.ParseRules([designation]).Should().Be(expected);
    }

    [Fact]
    public void ParseRules_SeveralDesignations_Combine()
    {
        BeneficialOwnershipDetailWriter
            .ParseRules(["Rule 13d-1(b)", "Rule 13d-1(d)"])
            .Should()
            .Be(Schedule13GRules.QualifiedInstitution | Schedule13GRules.ExemptInvestor);
    }

    [Fact]
    public void ReportingPersonTypeCodes_CoverSecsFullList()
    {
        var types = ReportingPersonTypeCodes.Parse(
            ["BD", "BK", "IC", "IV", "IA", "EP", "HC", "SA", "CP", "CO", "PN", "IN", "FI", "OO"],
            out var unrecognized
        );

        unrecognized.Should().BeNull();
        types.Should().Be(Enum.GetValues<ReportingPersonTypes>().Aggregate((a, b) => a | b));
    }
}
