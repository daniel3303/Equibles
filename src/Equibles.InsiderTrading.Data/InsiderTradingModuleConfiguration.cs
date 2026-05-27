using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data;

public class InsiderTradingModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<InsiderOwner>();
        builder.Entity<InsiderTransaction>(entity =>
        {
            // SQL DEFAULT true so the column add doesn't backfill existing
            // rows to false (which would hide every old row from the
            // dashboard until the maintenance backfill runs). The parser
            // explicitly sets this on new rows, so the default only ever
            // applies to legacy data and to manually-inserted rows.
            entity.Property(t => t.IsPriceValid).HasDefaultValue(true);
        });
    }
}
