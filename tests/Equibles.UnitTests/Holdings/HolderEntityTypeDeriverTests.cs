using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderEntityTypeDeriverTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly Roster = new(2026, 10, 1);
    private static readonly HolderEvidenceContext Context = new(Today, Roster, Roster);

    [Fact]
    public void NoEvidence_IsUnknown()
    {
        var decision = HolderEntityTypeDeriver.Derive([], Context);

        decision.Should().Be(HolderEntityTypeDecision.Unknown);
    }

    [Theory]
    [InlineData(HolderEvidenceFields.AdvRegistered, HolderEntityType.RegisteredAdviser)]
    [InlineData(HolderEvidenceFields.AdvExemptReporting, HolderEntityType.ExemptReportingAdviser)]
    public void CurrentFormAdvListing_DecidesTheAdviserType(
        string registration,
        HolderEntityType expected
    )
    {
        var decision = HolderEntityTypeDeriver.Derive([Adv(registration)], Context);

        decision.EntityType.Should().Be(expected);
        decision.Source.Should().Be(HolderEvidenceSource.FormAdvRoster);
        decision.SourceKey.Should().Be("160108");
        decision.AsOf.Should().Be(Roster);
        decision.HasConflictingEvidence.Should().BeFalse();
    }

    [Theory]
    [InlineData("6021", HolderEntityType.Bank)]
    [InlineData("6022", HolderEntityType.Bank)]
    [InlineData("6712", HolderEntityType.Bank)]
    [InlineData("6311", HolderEntityType.InsuranceCompany)]
    [InlineData("6411", HolderEntityType.InsuranceCompany)]
    [InlineData("6722", HolderEntityType.RegisteredFund)]
    [InlineData("6282", HolderEntityType.InvestmentAdviser)]
    // Bank and insurance holding companies file under 6211 too, so it decides nothing.
    [InlineData("6211", HolderEntityType.Unknown)]
    [InlineData("6199", HolderEntityType.Unknown)]
    [InlineData("7372", HolderEntityType.Unknown)]
    public void SicCode_DecidesOnlyUnambiguousInstitutionTypes(
        string sic,
        HolderEntityType expected
    )
    {
        HolderEntityTypeDeriver.Derive([Sic(sic)], Context).EntityType.Should().Be(expected);
    }

    [Theory]
    [InlineData(ReportingPersonTypes.Bank, HolderEntityType.Bank)]
    [InlineData(ReportingPersonTypes.SavingsAssociation, HolderEntityType.Bank)]
    [InlineData(ReportingPersonTypes.InsuranceCompany, HolderEntityType.InsuranceCompany)]
    [InlineData(ReportingPersonTypes.InvestmentCompany, HolderEntityType.RegisteredFund)]
    [InlineData(ReportingPersonTypes.BrokerDealer, HolderEntityType.BrokerDealer)]
    [InlineData(ReportingPersonTypes.InvestmentAdviser, HolderEntityType.InvestmentAdviser)]
    [InlineData(ReportingPersonTypes.EmployeeBenefitPlan, HolderEntityType.BenefitPlan)]
    [InlineData(ReportingPersonTypes.ChurchPlan, HolderEntityType.BenefitPlan)]
    [InlineData(ReportingPersonTypes.NonUsInstitution, HolderEntityType.NonUsInstitution)]
    [InlineData(ReportingPersonTypes.ParentHoldingCompany, HolderEntityType.Unknown)]
    [InlineData(ReportingPersonTypes.Corporation, HolderEntityType.Unknown)]
    [InlineData(ReportingPersonTypes.Partnership, HolderEntityType.Unknown)]
    [InlineData(ReportingPersonTypes.Individual, HolderEntityType.Unknown)]
    [InlineData(ReportingPersonTypes.Other, HolderEntityType.Unknown)]
    public void SelfReportedTypeCode_DecidesItsInstitutionType(
        ReportingPersonTypes code,
        HolderEntityType expected
    )
    {
        var decision = HolderEntityTypeDeriver.Derive([SelfReport(code, Today)], Context);

        decision.EntityType.Should().Be(expected);
    }

    [Fact]
    public void NCenRegistrant_IsARegisteredFund()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [Registrant("N-1A", Today.AddMonths(-3))],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.RegisteredFund);
        decision.Source.Should().Be(HolderEvidenceSource.NCenRegistrant);
    }

    [Fact]
    public void FormAdvListing_OutranksSicAndFlagsTheDisagreement()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [Sic("6022"), Adv(HolderEvidenceFields.AdvRegistered)],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.RegisteredAdviser);
        decision.HasConflictingEvidence.Should().BeTrue();
    }

    [Fact]
    public void Sic_OutranksASelfReport()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [SelfReport(ReportingPersonTypes.InvestmentAdviser, Today), Sic("6022")],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.Bank);
        decision.Source.Should().Be(HolderEvidenceSource.SecFilerProfile);
        decision.HasConflictingEvidence.Should().BeTrue();
    }

    [Fact]
    public void SelfReport_OutranksAnNCenRegistrant()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [Registrant("N-1A", Today), SelfReport(ReportingPersonTypes.BrokerDealer, Today)],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.BrokerDealer);
    }

    [Theory]
    [InlineData(HolderEvidenceFields.AdvRegistered)]
    [InlineData(HolderEvidenceFields.AdvExemptReporting)]
    public void AdviserBySicOrSelfReport_AgreesWithEitherFormAdvRoster(string registration)
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [
                Adv(registration),
                Sic("6282"),
                SelfReport(ReportingPersonTypes.InvestmentAdviser, Today),
            ],
            Context
        );

        decision.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void FormAdvRowOlderThanItsRoster_IsAFormerListingAndDecidesNothing()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [Adv(HolderEvidenceFields.AdvRegistered, Roster.AddMonths(-1)), Sic("6022")],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.Bank);
        decision.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void FormAdvRow_IsJudgedAgainstItsOwnRoster()
    {
        var exemptRosterLagging = new HolderEvidenceContext(Today, Roster, Roster.AddMonths(-1));

        var decision = HolderEntityTypeDeriver.Derive(
            [Adv(HolderEvidenceFields.AdvExemptReporting, Roster.AddMonths(-1))],
            exemptRosterLagging
        );

        decision.EntityType.Should().Be(HolderEntityType.ExemptReportingAdviser);
    }

    [Fact]
    public void FormAdvRow_WithoutAnyRosterOnRecord_DecidesNothing()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [Adv(HolderEvidenceFields.AdvRegistered)],
            new HolderEvidenceContext(Today, null, null)
        );

        decision.EntityType.Should().Be(HolderEntityType.Unknown);
    }

    [Fact]
    public void SelfReport_CountsForThirtySixMonths()
    {
        var oldest = Today.AddMonths(-HolderEntityTypeDeriver.BeneficialOwnershipLookbackMonths);

        HolderEntityTypeDeriver
            .Derive([SelfReport(ReportingPersonTypes.Bank, oldest)], Context)
            .EntityType.Should()
            .Be(HolderEntityType.Bank);
        HolderEntityTypeDeriver
            .Derive([SelfReport(ReportingPersonTypes.Bank, oldest.AddDays(-1))], Context)
            .EntityType.Should()
            .Be(HolderEntityType.Unknown);
    }

    [Fact]
    public void NCenRegistrant_CountsForTwentyFourMonths()
    {
        var oldest = Today.AddMonths(-HolderEntityTypeDeriver.NCenLookbackMonths);

        HolderEntityTypeDeriver
            .Derive([Registrant("N-2", oldest)], Context)
            .EntityType.Should()
            .Be(HolderEntityType.RegisteredFund);
        HolderEntityTypeDeriver
            .Derive([Registrant("N-2", oldest.AddDays(-1))], Context)
            .EntityType.Should()
            .Be(HolderEntityType.Unknown);
    }

    [Fact]
    public void PersonStatingSeveralCodes_TakesTheStrongestAndIsFlagged()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [
                SelfReport(
                    ReportingPersonTypes.InvestmentAdviser | ReportingPersonTypes.BrokerDealer,
                    Today
                ),
            ],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.BrokerDealer);
        decision.HasConflictingEvidence.Should().BeTrue();
    }

    [Fact]
    public void ThirteenDAndThirteenG_RankTogetherAndTheNewestDecides()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [
                SelfReport(ReportingPersonTypes.Bank, Today.AddMonths(-6), accession: "a-1"),
                SelfReport(
                    ReportingPersonTypes.InvestmentAdviser,
                    Today.AddMonths(-1),
                    HolderEvidenceSource.Schedule13G,
                    "a-2"
                ),
            ],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.InvestmentAdviser);
        decision.Source.Should().Be(HolderEvidenceSource.Schedule13G);
        decision.SourceKey.Should().Be("a-2");
    }

    [Fact]
    public void ListingLinkedByTheCoverCrd_OutranksOneLinkedByCik()
    {
        var decision = HolderEntityTypeDeriver.Derive(
            [
                Adv(
                    HolderEvidenceFields.AdvExemptReporting,
                    match: HolderEvidenceMatch.Cik,
                    crd: "2"
                ),
                Adv(HolderEvidenceFields.AdvRegistered, crd: "1"),
            ],
            Context
        );

        decision.EntityType.Should().Be(HolderEntityType.RegisteredAdviser);
        decision.SourceKey.Should().Be("1");
        // Two current listings tied to one holder are flagged even when both agree it is an adviser.
        decision.HasConflictingEvidence.Should().BeTrue();
    }

    [Fact]
    public void UnparseableSelfReport_DecidesNothing()
    {
        var fact = SelfReport(ReportingPersonTypes.Bank, Today);
        fact.Value = "XX";

        HolderEntityTypeDeriver
            .Derive([fact], Context)
            .EntityType.Should()
            .Be(HolderEntityType.Unknown);
    }

    [Theory]
    [InlineData(typeof(HolderClassificationEvidence))]
    [InlineData(typeof(InstitutionalHolderProfile))]
    public void ClassificationTypes_CarryNoName(Type type)
    {
        // A name on these rows would invite classifying a holder by it.
        type.GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(name => name.Contains("Name", StringComparison.OrdinalIgnoreCase));
    }

    private static HolderClassificationEvidence Adv(
        string registration,
        DateOnly? asOf = null,
        HolderEvidenceMatch match = HolderEvidenceMatch.CoverCrd,
        string crd = "160108"
    ) =>
        new()
        {
            Source = HolderEvidenceSource.FormAdvRoster,
            MatchedOn = match,
            Field = HolderEvidenceFields.AdvRegistration,
            Value = registration,
            SourceKey = crd,
            AsOf = asOf ?? Roster,
        };

    private static HolderClassificationEvidence Sic(string sic) =>
        new()
        {
            Source = HolderEvidenceSource.SecFilerProfile,
            MatchedOn = HolderEvidenceMatch.Cik,
            Field = HolderEvidenceFields.Sic,
            Value = sic,
            SourceKey = "19617",
            AsOf = Today,
        };

    private static HolderClassificationEvidence SelfReport(
        ReportingPersonTypes types,
        DateOnly filed,
        HolderEvidenceSource source = HolderEvidenceSource.Schedule13D,
        string accession = "0000950170-26-000001"
    ) =>
        new()
        {
            Source = source,
            MatchedOn = HolderEvidenceMatch.Cik,
            Field = HolderEvidenceFields.TypeOfReportingPerson,
            Value = types.ToString(),
            SourceKey = accession,
            AsOf = filed,
        };

    private static HolderClassificationEvidence Registrant(string type, DateOnly period) =>
        new()
        {
            Source = HolderEvidenceSource.NCenRegistrant,
            MatchedOn = HolderEvidenceMatch.Cik,
            Field = HolderEvidenceFields.InvestmentCompanyType,
            Value = type,
            SourceKey = "0001145549-26-000001",
            AsOf = period,
        };
}
