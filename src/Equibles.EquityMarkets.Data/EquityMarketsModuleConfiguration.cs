using Equibles.EquityMarkets.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Data;

public class EquityMarketsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<EquityMarketRegistration>();
        builder.Entity<FirdsInstrumentRecord>();
        builder.Entity<FirdsImportRun>();
    }
}
