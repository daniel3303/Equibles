using Equibles.GovernmentContracts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.GovernmentContracts.Data;

public class GovernmentContractsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<GovernmentContract>();
        builder.Entity<GovernmentContractsScanState>();
    }
}
