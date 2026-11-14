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

    // The owner-only ladder every surface shared before rows kept their filing's boxes: roles join
    // in box order, a blank title reads "Officer", and no box at all reads "Insider".
    [Theory]
    [InlineData(true, true, null, true, "Director, Officer, 10% Owner")]
    [InlineData(false, true, "Chief Executive Officer", false, "Chief Executive Officer")]
    [InlineData(false, true, "", false, "Officer")]
    [InlineData(false, true, "   ", false, "Officer")]
    [InlineData(false, false, null, false, "Insider")]
    public void Of_OwnerFlagsOnly_JoinsTheRolesInBoxOrder(
        bool director,
        bool officer,
        string title,
        bool tenPercentOwner,
        string expected
    )
    {
        var owner = new InsiderOwner
        {
            IsDirector = director,
            IsOfficer = officer,
            OfficerTitle = title,
            IsTenPercentOwner = tenPercentOwner,
        };

        InsiderRoleLabel.Of(owner).Should().Be(expected);
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
