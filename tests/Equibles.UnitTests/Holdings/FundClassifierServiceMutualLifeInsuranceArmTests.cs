using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FundClassifierServiceMutualLifeInsuranceArmTests
{
    // Adversarial Lane A. The 30+ existing FundClassifier pins cover most
    // rules in the table but skip THIS one: the "MUTUAL LIFE" entry that
    // sits IMMEDIATELY BELOW "MUTUAL FUND" and routes to InsuranceCompany
    // (not MutualFund — a deliberate exception for legacy mutual-insurance
    // 13F filers like Mass Mutual Life and Northwestern Mutual Life).
    //
    // The risks this pin uniquely catches:
    //
    //   • Rule-order inversion. The pair sits in a specific order:
    //         ("MUTUAL FUND", MutualFund),     // index 17
    //         ("MUTUAL LIFE", InsuranceCompany), // index 18
    //     Both contain "MUTUAL " and a maintainer who alphabetized the
    //     table (FUND vs LIFE) would compile cleanly — but for any name
    //     containing BOTH "MUTUAL FUND" and "MUTUAL LIFE" the priority
    //     would flip. Real edge case: "MASS MUTUAL FUND OF LIFE GROUP"
    //     should classify as MutualFund (first match wins). Inversion
    //     would route it to InsuranceCompany.
    //
    //   • Rule deletion under "consolidation" pressure. The "MUTUAL
    //     FUND" rule alone would catch every mutual-FUND filer; a
    //     maintainer might delete "MUTUAL LIFE" thinking it's redundant.
    //     Doing so silently flips every legacy-mutual-insurance filer
    //     from InsuranceCompany to Unknown — the analyst export then
    //     misses an entire institutional-investor category.
    //
    //   • Wrong target classification. A maintainer who reroutes
    //     "MUTUAL LIFE" to MutualFund (a plausible "all MUTUAL is fund"
    //     misreading) would compile and pass every existing pin (none
    //     of which exercises this rule).
    //
    // Adversarial input: a name that hits ONLY this rule.
    // "BERKSHIRE MUTUAL LIFE" deliberately avoids every upstream
    // InsuranceCompany rule (no INSURANCE / ASSURANCE / REINSURANCE
    // / UNDERWRITER / "LIFE INS" substring — note the trailing-space
    // requirement on "LIFE INS") and the MUTUAL FUND rule (no
    // "MUTUAL FUND" substring). So the match has to come from
    // "MUTUAL LIFE" specifically. Pin: input → InsuranceCompany.
    [Fact]
    public void Classify_MutualLifeWithoutUpstreamInsuranceMarkers_ReturnsInsuranceCompany()
    {
        FundClassifierService
            .Classify("BERKSHIRE MUTUAL LIFE")
            .Should()
            .Be(FundClassification.InsuranceCompany);
    }
}
