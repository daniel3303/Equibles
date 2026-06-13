using Equibles.FdaCatalysts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.FdaCatalysts.Data;

public class FdaCatalystsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<FdaCatalyst>();
    }
}
