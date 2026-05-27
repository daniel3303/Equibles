using Microsoft.EntityFrameworkCore;

namespace Equibles.Data;

/// <summary>
/// Context for the public financial database (re-scrapable market data; the
/// source for the Snowflake / Delta data-share product). Enables the pgvector
/// extension for embeddings; ParadeDB (pg_search) is wired through the Npgsql
/// options in <c>AddEquiblesDbContext</c>, not the model.
/// </summary>
public class EquiblesFinancialDbContext : EquiblesDbContextBase
{
    public EquiblesFinancialDbContext(
        DbContextOptions<EquiblesFinancialDbContext> options,
        ModuleConfigurationSet<EquiblesFinancialDbContext> modules
    )
        : base(options, modules.Modules) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasPostgresExtension("vector");
        base.OnModelCreating(builder);
    }
}
