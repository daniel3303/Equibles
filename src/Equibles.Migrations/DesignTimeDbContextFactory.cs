using Equibles.CommonStocks.Data;
using Equibles.Congress.Data;
using Equibles.Data;
using Equibles.Errors.Data;
using Equibles.Holdings.Data;
using Equibles.InsiderTrading.Data;
using Equibles.Media.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data;
using Equibles.Finra.Data;
using Equibles.Fred.Data;
using Equibles.Yahoo.Data;
using Equibles.Cftc.Data;
using Equibles.Cboe.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Equibles.Migrations;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EquiblesDbContext> {
    public EquiblesDbContext CreateDbContext(string[] args) {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("designsettings.json", optional: false)
            .AddJsonFile("designsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<EquiblesDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        optionsBuilder.UseNpgsql(connectionString, options => {
            options.UseVector();
            options.UseParadeDb();
            options.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly);
        });

        IModuleConfiguration[] modules = [
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new FinraModuleConfiguration(),
            new FredModuleConfiguration(),
            new YahooModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration(),
            new SecModuleConfiguration(),
            new MediaModuleConfiguration(),
            new ErrorsModuleConfiguration(),
        ];

        return new EquiblesDbContext(optionsBuilder.Options, modules);
    }
}
