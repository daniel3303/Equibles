using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.Integrations.GovernmentContracts.Models;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: a recipient profile yields at most ONE parent to match through. The top-level
// parent pair is USAspending's current SAM linkage and wins; the parents[] history only
// substitutes when it names a single distinct parent — several distinct parents means
// ownership moved, and guessing between them could assert a wrong link. Self-references
// (parent-level recipients list themselves) carry no new name and count as no parent.
public class GovernmentContractsImportServiceChooseParentTests
{
    [Fact]
    public void ChooseParent_TopLevelParent_Wins()
    {
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            ParentId = "parent-P",
            ParentName = "CACI International Inc",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "old-P",
                    ParentName = "Former Owner Corp",
                },
            ],
        };

        var parent = GovernmentContractsImportService.ChooseParent(profile, "child-C");

        parent.Should().NotBeNull();
        parent.ParentId.Should().Be("parent-P");
        parent.ParentName.Should().Be("CACI International Inc");
    }

    [Fact]
    public void ChooseParent_NoTopLevel_SingleHistoryParent_IsUsed()
    {
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            Parents =
            [
                new UsaSpendingRecipientParentRef
                {
                    ParentId = "parent-P",
                    ParentName = "CACI International Inc",
                },
            ],
        };

        GovernmentContractsImportService
            .ChooseParent(profile, "child-C")
            .ParentId.Should()
            .Be("parent-P");
    }

    [Fact]
    public void ChooseParent_NoTopLevel_MultipleDistinctHistoryParents_IsAmbiguous()
    {
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "child-C",
            Parents =
            [
                new UsaSpendingRecipientParentRef { ParentId = "a-P", ParentName = "Owner A" },
                new UsaSpendingRecipientParentRef { ParentId = "b-P", ParentName = "Owner B" },
            ],
        };

        GovernmentContractsImportService.ChooseParent(profile, "child-C").Should().BeNull();
    }

    [Fact]
    public void ChooseParent_SelfReference_IsNoParent()
    {
        var profile = new UsaSpendingRecipientProfile
        {
            RecipientId = "parent-P",
            ParentId = "parent-P",
            ParentName = "CACI International Inc",
        };

        GovernmentContractsImportService.ChooseParent(profile, "parent-P").Should().BeNull();
    }

    [Fact]
    public void ChooseParent_NullProfile_IsNoParent()
    {
        GovernmentContractsImportService.ChooseParent(null, "child-C").Should().BeNull();
    }
}
