using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.TestSupport;

// A context that runs the current model on a schema pinned before a later migration must not map the
// columns that migration adds, or every write to the table fails on the pinned database.
internal sealed class PinnedSchemaTestMappings : IModuleConfiguration
{
    internal PinnedSchemaTestMappings() { }

    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<EquityIssuer>().Ignore(issuer => issuer.NameAliases);

        // Added by 20260916043012_AddDelayedTrades.
        builder.Entity<EquityListing>().Ignore(listing => listing.YahooPriceSyncAttemptedAt);

        // Added by 20260928022742_AddDurableImportRecovery.
        builder.Entity<FinancialFactsSyncStatus>().Ignore(status => status.ImportAttemptId);
        builder.Entity<FinancialFactsSyncStatus>().Ignore(status => status.ImportAttempts);
        builder.Entity<FinancialFactsSyncStatus>().Ignore(status => status.NextAttemptAt);
    }
}
