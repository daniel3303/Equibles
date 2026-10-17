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
// never guessed. Entries naming the recipient itself (by its own DUNS or UEI) are dropped
// only beside a different current parent. The fixtures mirror profiles verified live
// against USAspending (Sikorsky: RTX + Lockheed; CACI, Inc. - Federal: CACI International +
// its former name Systemware under one DUNS; Kratos SRE: Kratos Defense + itself).
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
        choice.ResolvedByDroppingSelfEntries.Should().BeFalse();
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
        // CACI NSS, LLC (live shape): SAM lists the recipient itself as a parent beside CACI
        // International, and its CURRENT top-level parent is itself too. Self entries are
        // only dropped beside a different current parent, so this stays ambiguous.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "49aa319d-C",
            Duns = "043033294",
            Uei = "Y9CDLHK3A125",
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
    public void ChooseParent_SelfEntryBesideADifferentCurrentParent_ResolvesToTheCurrentParent()
    {
        // KRATOS SRE, INC. (live shape): the current parent is Kratos Defense, but the history
        // also lists the recipient itself under its own DUNS/UEI. Award records show the
        // self era overlapping years of Kratos ownership, so the self entry is SAM noise.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "3b5b3071-54b8-49dc-37a3-9ef0ba5e3e68-C",
            Name = "KRATOS SRE, INC.",
            Duns = "006648179",
            Uei = "GM11ZH81LBY7",
            RecipientLevel = "C",
            ParentId = "6ed33a3b-1910-7875-9717-c4b6701f032b-P",
            ParentName = "KRATOS DEFENSE & SECURITY SOLUTIONS, INC.",
            ParentDuns = "884457813",
            ParentUei = "H6BWMHGJ5AF5",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "6ed33a3b-1910-7875-9717-c4b6701f032b-P",
                    ParentName = "KRATOS DEFENSE & SECURITY SOLUTIONS, INC.",
                    ParentDuns = "884457813",
                    ParentUei = "H6BWMHGJ5AF5",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "3b5b3071-54b8-49dc-37a3-9ef0ba5e3e68-P",
                    ParentName = "KRATOS SRE, INC.",
                    ParentDuns = "006648179",
                    ParentUei = "GM11ZH81LBY7",
                },
            ],
        };

        var choice = GovernmentContractsImportService.ChooseParent(profile);

        choice.Should().NotBeNull();
        choice.ParentId.Should().Be("6ed33a3b-1910-7875-9717-c4b6701f032b-P");
        choice.Names.Should().Equal("KRATOS DEFENSE & SECURITY SOLUTIONS, INC.");
        choice
            .ResolvedByDroppingSelfEntries.Should()
            .BeTrue("the choice exists only because the self entry was set aside");
    }

    [Fact]
    public void ChooseParent_SelfEntryRecognisedByUeiAlone_IsDropped()
    {
        // The profile carries no DUNS of its own (post-migration), so the self entry is
        // recognised by UEI alone.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            Uei = "SELFUEI00001",
            ParentId = "owner-P",
            ParentName = "Owner Holdings Inc",
            ParentUei = "OWNERUEI0001",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "owner-P",
                    ParentName = "Owner Holdings Inc",
                    ParentUei = "OWNERUEI0001",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "child-P",
                    ParentName = "Child Operating LLC",
                    ParentUei = "SELFUEI00001",
                },
            ],
        };

        var choice = GovernmentContractsImportService.ChooseParent(profile);

        choice.Should().NotBeNull();
        choice.ParentId.Should().Be("owner-P");
        choice.Names.Should().Equal("Owner Holdings Inc");
    }

    [Fact]
    public void ChooseParent_TwoNonSelfOwnersBesideASelfEntry_StaysAmbiguous()
    {
        // Dropping the self entry still leaves two different owners: ownership moved, so
        // one link cannot be right for both eras.
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            Duns = "111111111",
            ParentId = "new-owner-P",
            ParentName = "New Owner Corp",
            ParentDuns = "222222222",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "new-owner-P",
                    ParentName = "New Owner Corp",
                    ParentDuns = "222222222",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "old-owner-P",
                    ParentName = "Old Owner Corp",
                    ParentDuns = "333333333",
                },
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "child-P",
                    ParentName = "Child Operating LLC",
                    ParentDuns = "111111111",
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
