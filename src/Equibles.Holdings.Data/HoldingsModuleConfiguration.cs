using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data;

public class HoldingsModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        // Holdings unique index: include OptionType with NULLS NOT DISTINCT (cannot be expressed via attributes)
        builder.Entity<InstitutionalHolding>()
            .HasIndex(h => new { h.CommonStockId, h.InstitutionalHolderId, h.ReportDate, h.ShareType, h.OptionType })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.Entity<ProcessedDataSet>();
    }
}
