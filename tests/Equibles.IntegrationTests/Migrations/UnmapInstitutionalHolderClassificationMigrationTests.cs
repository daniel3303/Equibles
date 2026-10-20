using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

public class UnmapInstitutionalHolderClassificationMigrationTests
{
    // The release before it still mapped the column while the rollout replaced its containers, so
    // the unmap drops only the index; DropInstitutionalHolderClassification drops the column later.
    [Fact]
    public void Up_DropsOnlyTheIndex()
    {
        new UnmapInstitutionalHolderClassification()
            .UpOperations.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<DropIndexOperation>()
            .Which.Name.Should()
            .Be("IX_InstitutionalHolder_Classification");
    }
}
