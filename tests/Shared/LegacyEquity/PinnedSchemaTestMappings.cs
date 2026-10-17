using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;
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

        // Added by 20261014150246_SplitShareCountEvidence.
        builder.Entity<StockSplit>().Ignore(split => split.ShareCountEvidence);
        builder.Entity<StockSplit>().Ignore(split => split.ShareCountEvidenceAsOf);
        builder.Entity<StockSplit>().Ignore(split => split.ShareCountEvidenceTime);

        // Added by 20261014214216_DocumentAcceptedAt.
        builder.Entity<Document>().Ignore(document => document.AcceptedAt);

        // Added by 20261010015440_AddListingActivityConcentrationColumns.
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.CurrentValue);
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.CurrentFilerCount);
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.HolderValueSquaredSum);
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.TopOneValue);
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.TopFiveValue);
        builder.Entity<StockQuarterlyListingActivity>().Ignore(row => row.TopTenValue);

        // Added by 20261015133022_DocumentAsFiledPdf.
        builder.Entity<Document>().Ignore(document => document.AsFiledPdfContent);
        builder.Entity<Document>().Ignore(document => document.AsFiledPdfContentId);

        // Added by 20261017121009_AddHolderIdentityNumbers.
        builder.Entity<InstitutionalHolder>().Ignore(holder => holder.SecFileNumber);
        builder.Entity<InstitutionalHolder>().Ignore(holder => holder.IdentityAccession);
    }
}
