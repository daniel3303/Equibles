using Equibles.Congress.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data;

public class CongressModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<CongressMember>();
        // Default value has no data annotation, so it must be Fluent: OwnerType is a required
        // member of the trade upsert unique key, and the '' default lets the migration backfill
        // pre-existing NULL rows when the column tightens to NOT NULL. ValueGeneratedNever is
        // load-bearing: HasDefaultValue alone marks the column ValueGenerated.OnAdd, and the
        // FlexLabs upsert rejects generated columns in its match key (the scraper writes the
        // value on every insert, so nothing is ever DB-generated here).
        builder
            .Entity<CongressionalTrade>()
            .Property(t => t.OwnerType)
            .HasDefaultValue("")
            .ValueGeneratedNever();
        builder.Entity<CongressionalAnnualDisclosure>();
        builder.Entity<CongressionalDisclosureLine>();
        builder.Entity<CongressionalFilingRecord>();
    }
}
