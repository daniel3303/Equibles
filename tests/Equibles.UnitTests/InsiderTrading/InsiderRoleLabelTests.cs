using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;

namespace Equibles.UnitTests.InsiderTrading;

public class InsiderRoleLabelTests
{
    // Elliott Robinson was first stored as a director and 10% owner; his Hinge Health Form 4
    // (0001193125-26-459184) ticks the director box only.
    private static readonly InsiderOwner FirstSeenDirectorAndTenPercentOwner = new()
    {
        Name = "Robinson Elliott",
        IsDirector = true,
        IsTenPercentOwner = true,
    };

    [Fact]
    public void Of_FilingStatesItsBoxes_LabelsTheFiledRelationship()
    {
        InsiderRoleLabel
            .Of(FirstSeenDirectorAndTenPercentOwner, InsiderRelationship.Director)
            .Should()
            .Be("Director");
    }

    [Fact]
    public void Of_FilingTicksOfficer_UsesTheOwnersTitleOrThePlainWord()
    {
        var titled = new InsiderOwner
        {
            IsOfficer = true,
            OfficerTitle = "Chief Financial Officer",
        };

        InsiderRoleLabel
            .Of(titled, InsiderRelationship.Director | InsiderRelationship.Officer)
            .Should()
            .Be("Director, Chief Financial Officer");
        InsiderRoleLabel
            .Of(FirstSeenDirectorAndTenPercentOwner, InsiderRelationship.Officer)
            .Should()
            .Be("Officer");
    }

    [Fact]
    public void Of_RowPredatesTheRecordedBoxes_FallsBackToTheOwnersFirstSeenFlags()
    {
        InsiderRoleLabel
            .Of(FirstSeenDirectorAndTenPercentOwner, filed: null)
            .Should()
            .Be("Director, 10% Owner");
    }

    [Fact]
    public void Of_FilingTicksOnlyOther_ReadsAsInsider()
    {
        InsiderRoleLabel
            .Of(FirstSeenDirectorAndTenPercentOwner, InsiderRelationship.Other)
            .Should()
            .Be("Insider");
    }
}
