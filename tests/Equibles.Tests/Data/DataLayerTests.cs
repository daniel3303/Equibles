using Equibles.Cboe.Data;
using Equibles.Cboe.Data.Extensions;
using Equibles.Cboe.Data.Models;
using Equibles.Cftc.Data;
using Equibles.Cftc.Data.Extensions;
using Equibles.Cftc.Data.Models;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Extensions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Extensions;
using Equibles.Congress.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Extensions;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Extensions;
using Equibles.Finra.Data.Models;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.Data.Models;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Media.Data;
using Equibles.Media.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Tests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Extensions;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Tests.Data;

public class ServiceCollectionExtensionsTests {
    [Fact]
    public void AddEquiblesDbContext_RegistersDbContextInServiceProvider() {
        var services = new ServiceCollection();

        services.AddEquiblesDbContext(
            "Host=localhost;Database=test",
            modules => modules.AddCommonStocks());

        var provider = services.BuildServiceProvider();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(EquiblesDbContext));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddEquiblesDbContext_RegistersModuleConfigurationsAsSingletons() {
        var services = new ServiceCollection();

        services.AddEquiblesDbContext(
            "Host=localhost;Database=test",
            modules => {
                modules.AddCommonStocks();
                modules.AddErrors();
                modules.AddMedia();
            });

        var moduleDescriptors = services
            .Where(d => d.ServiceType == typeof(IModuleConfiguration))
            .ToList();

        moduleDescriptors.Should().HaveCount(3);
        moduleDescriptors.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddEquiblesDbContext_WithMultipleModules_RegistersAllModuleConfigurations() {
        var services = new ServiceCollection();

        services.AddEquiblesDbContext(
            "Host=localhost;Database=test",
            modules => {
                modules.AddCommonStocks();
                modules.AddHoldings(); // also adds CommonStocks, but dedup prevents double
                modules.AddCongress(); // also adds CommonStocks
                modules.AddErrors();
                modules.AddFred();
            });

        var moduleDescriptors = services
            .Where(d => d.ServiceType == typeof(IModuleConfiguration))
            .ToList();

        var moduleTypes = moduleDescriptors
            .Select(d => d.ImplementationInstance!.GetType())
            .ToList();

        moduleTypes.Should().Contain(typeof(CommonStocksModuleConfiguration));
        moduleTypes.Should().Contain(typeof(HoldingsModuleConfiguration));
        moduleTypes.Should().Contain(typeof(CongressModuleConfiguration));
        moduleTypes.Should().Contain(typeof(ErrorsModuleConfiguration));
        moduleTypes.Should().Contain(typeof(FredModuleConfiguration));
        // CommonStocks added only once despite being a dependency of Holdings and Congress
        moduleTypes.Count(t => t == typeof(CommonStocksModuleConfiguration)).Should().Be(1);
    }

    [Fact]
    public void AddEquiblesDbContext_ReturnsServiceCollection_ForFluentChaining() {
        var services = new ServiceCollection();

        var result = services.AddEquiblesDbContext(
            "Host=localhost;Database=test",
            modules => modules.AddCommonStocks());

        result.Should().BeSameAs(services);
    }
}

public class ModuleBuilderExtensionTests {
    [Fact]
    public void AddCommonStocks_RegistersCommonStocksModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCommonStocks();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<CommonStocksModuleConfiguration>();
    }

    [Fact]
    public void AddHoldings_RegistersBothHoldingsAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddHoldings();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is HoldingsModuleConfiguration);
    }

    [Fact]
    public void AddInsiderTrading_RegistersBothInsiderTradingAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddInsiderTrading();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is InsiderTradingModuleConfiguration);
    }

    [Fact]
    public void AddCongress_RegistersBothCongressAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCongress();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is CongressModuleConfiguration);
    }

    [Fact]
    public void AddSec_RegistersSecMediaAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddSec();

        builder.Modules.Should().HaveCount(3);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is MediaModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is Equibles.Sec.Data.SecModuleConfiguration);
    }

    [Fact]
    public void AddMedia_RegistersMediaModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddMedia();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<MediaModuleConfiguration>();
    }

    [Fact]
    public void AddErrors_RegistersErrorsModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddErrors();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<ErrorsModuleConfiguration>();
    }

    [Fact]
    public void AddFred_RegistersFredModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddFred();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<FredModuleConfiguration>();
    }

    [Fact]
    public void AddFinra_RegistersBothFinraAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddFinra();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is FinraModuleConfiguration);
    }

    [Fact]
    public void AddYahoo_RegistersBothYahooAndCommonStocksModules() {
        var builder = new EquiblesModuleBuilder();

        builder.AddYahoo();

        builder.Modules.Should().HaveCount(2);
        builder.Modules.Should().ContainSingle(m => m is CommonStocksModuleConfiguration);
        builder.Modules.Should().ContainSingle(m => m is YahooModuleConfiguration);
    }

    [Fact]
    public void AddCftc_RegistersCftcModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCftc();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<CftcModuleConfiguration>();
    }

    [Fact]
    public void AddCboe_RegistersCboeModuleConfiguration() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCboe();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<CboeModuleConfiguration>();
    }

    [Fact]
    public void AllModuleExtensions_WithDependencyOverlap_DoNotDuplicateCommonStocks() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCommonStocks();
        builder.AddHoldings();
        builder.AddInsiderTrading();
        builder.AddCongress();
        builder.AddFinra();
        builder.AddYahoo();

        builder.Modules.Count(m => m is CommonStocksModuleConfiguration).Should().Be(1);
    }

    [Fact]
    public void AllModuleExtensions_ReturnBuilder_ForFluentChaining() {
        var builder = new EquiblesModuleBuilder();

        builder.AddCommonStocks()
            .AddHoldings()
            .AddInsiderTrading()
            .AddCongress()
            .AddMedia()
            .AddErrors()
            .AddFred()
            .AddFinra()
            .AddYahoo()
            .AddCftc()
            .AddCboe()
            .Should().BeSameAs(builder);
    }
}

/// <summary>
/// Tests that each ModuleConfiguration.ConfigureEntities does not throw when applied
/// to a ModelBuilder (using InMemory provider), and that entities are discoverable
/// after module configuration.
/// SEC module uses <see cref="SecTestModuleConfiguration"/> to avoid pgvector types
/// that are incompatible with the InMemory provider.
/// </summary>
public class ModuleConfigurationTests : IDisposable {
    private EquiblesDbContext _dbContext;

    public void Dispose() {
        _dbContext?.Dispose();
    }

    private EquiblesDbContext CreateContext(params IModuleConfiguration[] modules) {
        _dbContext = TestDbContextFactory.Create(modules);
        return _dbContext;
    }

    [Fact]
    public void CommonStocksModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new CommonStocksModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void CommonStocksModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new CommonStocksModuleConfiguration());

        context.Set<CommonStock>().Should().NotBeNull();
        context.Set<Industry>().Should().NotBeNull();
    }

    [Fact]
    public void HoldingsModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void HoldingsModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration());

        context.Set<InstitutionalHolder>().Should().NotBeNull();
        context.Set<InstitutionalHolding>().Should().NotBeNull();
    }

    [Fact]
    public void InsiderTradingModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void InsiderTradingModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration());

        context.Set<InsiderOwner>().Should().NotBeNull();
        context.Set<InsiderTransaction>().Should().NotBeNull();
    }

    [Fact]
    public void CongressModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new CongressModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void CongressModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new CongressModuleConfiguration());

        context.Set<CongressMember>().Should().NotBeNull();
        context.Set<CongressionalTrade>().Should().NotBeNull();
    }

    [Fact]
    public void SecTestModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void MediaModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new MediaModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void MediaModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new MediaModuleConfiguration());

        context.Set<Equibles.Media.Data.Models.File>().Should().NotBeNull();
        context.Set<Equibles.Media.Data.Models.FileContent>().Should().NotBeNull();
        context.Set<Equibles.Media.Data.Models.Image>().Should().NotBeNull();
    }

    [Fact]
    public void ErrorsModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new ErrorsModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void ErrorsModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new ErrorsModuleConfiguration());

        context.Set<Error>().Should().NotBeNull();
    }

    [Fact]
    public void FredModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new FredModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void FredModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new FredModuleConfiguration());

        context.Set<FredSeries>().Should().NotBeNull();
        context.Set<FredObservation>().Should().NotBeNull();
    }

    [Fact]
    public void FinraModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void FinraModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration());

        context.Set<DailyShortVolume>().Should().NotBeNull();
        context.Set<ShortInterest>().Should().NotBeNull();
    }

    [Fact]
    public void YahooModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void YahooModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration());

        context.Set<DailyStockPrice>().Should().NotBeNull();
    }

    [Fact]
    public void CftcModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new CftcModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void CftcModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new CftcModuleConfiguration());

        context.Set<CftcContract>().Should().NotBeNull();
        context.Set<CftcPositionReport>().Should().NotBeNull();
    }

    [Fact]
    public void CboeModuleConfiguration_ConfigureEntities_DoesNotThrow() {
        var act = () => CreateContext(new CboeModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void CboeModuleConfiguration_EntitiesAreDiscoverable() {
        var context = CreateContext(new CboeModuleConfiguration());

        context.Set<CboePutCallRatio>().Should().NotBeNull();
        context.Set<CboeVixDaily>().Should().NotBeNull();
    }

    [Fact]
    public void AllModulesComposed_ConfigureEntities_DoesNotThrow() {
        // Uses SecTestModuleConfiguration instead of SecModuleConfiguration
        // because Chunk/Embedding use pgvector types unsupported by InMemory provider
        var act = () => CreateContext(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new MediaModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new FredModuleConfiguration(),
            new FinraModuleConfiguration(),
            new YahooModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration());

        act.Should().NotThrow();
    }

    [Fact]
    public void AllModulesComposed_CanAddAndQueryEntities() {
        var context = CreateContext(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new MediaModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new FredModuleConfiguration(),
            new FinraModuleConfiguration(),
            new YahooModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration());

        var stock = new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = "TEST",
            Name = "Test Corp",
            Cik = "0000000001"
        };
        context.Set<CommonStock>().Add(stock);
        context.SaveChanges();

        context.Set<CommonStock>().Should().ContainSingle(s => s.Ticker == "TEST");
    }
}
