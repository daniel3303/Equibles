using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Equibles.Migrations.Migrations;

public partial class AddSourceInstrumentIdentifiers
{
    // Instruments without ISINs cannot recover their identity after these bindings are discarded.
    public override IReadOnlyList<MigrationOperation> DownOperations =>
        throw new NotSupportedException("Source instrument identity bindings cannot be removed by rollback.");
}
