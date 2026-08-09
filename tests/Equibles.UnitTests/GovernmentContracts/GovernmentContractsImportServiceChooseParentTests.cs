using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: a recipient profile yields at most ONE parent REGISTRANT, judged over the
// union of the top-level parent and the parents[] history grouped by DUNS (then UEI, then
// hash). One registrant under several names (legal renames) collapses to a single choice
// carrying every name; several registrants means ownership moved over the recipient's
// history, and one link cannot be right for awards spanning both eras — always dropped,
// never guessed. The fixtures mirror profiles verified live against USAspending
// (Sikorsky: RTX + Lockheed; CACI, Inc. - Federal: CACI International + its former name
// Systemware under one DUNS).
public class GovernmentContractsImportServiceChooseParentTests
{
    [Fact]
    public void ChooseParent_OneRegistrantRenamedOverHistory_CollapsesToAllItsNames()
    {
        // CACI, INC. - FEDERAL (live shape): top-level CACI INTERNATIONAL INC; history
        // carries CACI INTERNATIONAL INC and SYSTEMWARE, INC. under the SAME DUNS.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "28eee911-C",
            ParentId = "5eb76328-P",
            ParentName = "CACI INTERNATIONAL INC",
            ParentDuns = "045534641",
            ParentUei = "QSRTXLFKV857",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "5eb76328-P",
                    ParentName = "CACI INTERNATIONAL INC",
                    ParentDuns = "045534641",
                    ParentUei = "QSRTXLFKV857",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "697f7757-P",
                    ParentName = "SYSTEMWARE, INC.",
                    ParentDuns = "045534641",
                    ParentUei = "ZWJ2TPLX7MS2",
                },
            ],
        };

        var choice = GovernmentContractsImportService.ChooseParent(profile);

        choice.Should().NotBeNull();
        choice.ParentId.Should().Be("5eb76328-P");
        choice
            .Names.Should()
            .BeEquivalentTo(
                ["CACI INTERNATIONAL INC", "SYSTEMWARE, INC."],
                "every name the registrant has carried may be the one the stock universe stores"
            );
    }

    [Fact]
    public void ChooseParent_OwnershipMovedBetweenRegistrants_IsAmbiguousEvenWithATopLevelParent()
    {
        // SIKORSKY AIRCRAFT CORPORATION (live shape): top-level says RTX CORP, but the
        // history also carries Lockheed Martin under a DIFFERENT DUNS (twice, under two
        // hashes — the DUNS→UEI migration). Sikorsky was UTC/RTX before Nov 2015 and
        // Lockheed after; one link cannot be right for both eras, so the top-level pair
        // must NOT short-circuit the ambiguity check.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "d64537c9-C",
            ParentId = "bb947c1e-P",
            ParentName = "RTX CORP",
            ParentDuns = "001344142",
            ParentUei = "PPLZG8J3N9D4",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "bb947c1e-P",
                    ParentName = "RTX CORP",
                    ParentDuns = "001344142",
                    ParentUei = "PPLZG8J3N9D4",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "8e2862a3-P",
                    ParentName = "LOCKHEED MARTIN CORPORATION",
                    ParentDuns = "834951691",
                    ParentUei = "JSQTW5L2SSM1",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "b97d19b0-P",
                    ParentName = "LOCKHEED MARTIN CORP",
                    ParentDuns = "834951691",
                    ParentUei = "ZFN2JJXBLZT3",
                },
            ],
        };

        GovernmentContractsImportService.ChooseParent(profile).Should().BeNull();
    }

    [Fact]
    public void ChooseParent_SelfRegistrationEraBesideALaterOwner_IsAmbiguous()
    {
        // CACI NSS, LLC (live shape): SAM lists the recipient itself as a parent (its
        // independent era, under its own registrant DUNS) beside CACI International. The
        // independent era's awards must not be attributed to the later owner, so the
        // self-group counts toward ambiguity by design.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "49aa319d-C",
            ParentId = "d644a583-P",
            ParentName = "CACI NSS, LLC",
            ParentDuns = "043033294",
            ParentUei = "Y9CDLHK3A125",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "d644a583-P",
                    ParentName = "CACI NSS, LLC",
                    ParentDuns = "043033294",
                    ParentUei = "Y9CDLHK3A125",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "5eb76328-P",
                    ParentName = "CACI INTERNATIONAL INC",
                    ParentDuns = "045534641",
                    ParentUei = "QSRTXLFKV857",
                },
            ],
        };

        GovernmentContractsImportService.ChooseParent(profile).Should().BeNull();
    }

    [Fact]
    public void ChooseParent_SameRegistrantMissingDuns_GroupsByUei()
    {
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "a-P",
                    ParentName = "Post-Migration Name Inc",
                    ParentUei = "UEI111111111",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "a-P",
                    ParentName = "Post-Migration Name, Inc.",
                    ParentUei = "UEI111111111",
                },
            ],
        };

        var choice = GovernmentContractsImportService.ChooseParent(profile);

        choice.Should().NotBeNull();
        choice.Names.Should().HaveCount(2);
    }

    [Fact]
    public void ChooseParent_NoParents_IsNull()
    {
        GovernmentContractsImportService
            .ChooseParent(new UsaSpendingRecipientProfile { RecipientId = "solo-R" })
            .Should()
            .BeNull();
    }

    [Fact]
    public void ChooseParent_NullProfile_IsNull()
    {
        GovernmentContractsImportService.ChooseParent(null).Should().BeNull();
    }

    [Fact]
    public void JoinParentNames_RoundTrips_AndNeverCutsANameMidway()
    {
        GovernmentContractsImportService
            .SplitParentNames(
                GovernmentContractsImportService.JoinParentNames(["Alpha Corp", "Beta Inc"])
            )
            .Should()
            .BeEquivalentTo(["Alpha Corp", "Beta Inc"]);

        // A name that would cross the column bound is dropped whole — a truncated name
        // could exact-match the wrong company.
        var oversized = new string('A', 2000);
        GovernmentContractsImportService
            .JoinParentNames([oversized, "Beta Inc"])
            .Should()
            .Be("Beta Inc");

        GovernmentContractsImportService.JoinParentNames([]).Should().BeNull();
        GovernmentContractsImportService.JoinParentNames(null).Should().BeNull();
        GovernmentContractsImportService.SplitParentNames(null).Should().BeEmpty();
    }
}
