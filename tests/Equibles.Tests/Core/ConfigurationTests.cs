using Equibles.Core.Configuration;
using Equibles.Data.Contracts;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Integrations.Finra.Configuration;
using Equibles.Integrations.Fred.Configuration;
using Equibles.Mcp;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Configuration;
using NSubstitute;

namespace Equibles.Tests.Core;

public class ConfigurationTests {
    // ── WorkerOptions ────────────────────────────────────────────────

    [Fact]
    public void WorkerOptions_MinSyncDate_DefaultsToNull() {
        var options = new WorkerOptions();
        options.MinSyncDate.Should().BeNull();
    }

    [Fact]
    public void WorkerOptions_TickersToSync_DefaultsToEmptyList() {
        var options = new WorkerOptions();
        options.TickersToSync.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void WorkerOptions_PropertiesCanBeSet() {
        var date = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var tickers = new List<string> { "AAPL", "MSFT", "GOOG" };

        var options = new WorkerOptions {
            MinSyncDate = date,
            TickersToSync = tickers
        };

        options.MinSyncDate.Should().Be(date);
        options.TickersToSync.Should().BeEquivalentTo(tickers);
    }

    // ── McpToolContext ───────────────────────────────────────────────

    [Fact]
    public void McpToolContext_Arguments_DefaultsToEmptyDictionary() {
        var context = new McpToolContext();
        context.Arguments.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void McpToolContext_AllPropertiesCanBeSetAndRead() {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var arguments = new Dictionary<string, object> {
            ["ticker"] = "AAPL",
            ["limit"] = 10
        };

        var context = new McpToolContext {
            ToolName = "get-holdings",
            Arguments = arguments,
            ServiceProvider = serviceProvider
        };

        context.ToolName.Should().Be("get-holdings");
        context.Arguments.Should().BeSameAs(arguments);
        context.ServiceProvider.Should().BeSameAs(serviceProvider);
    }

    // ── FredScraperOptions ───────────────────────────────────────────

    [Fact]
    public void FredScraperOptions_SleepIntervalHours_DefaultsTo24() {
        var options = new FredScraperOptions();
        options.SleepIntervalHours.Should().Be(24);
    }

    [Fact]
    public void FredScraperOptions_SleepIntervalHours_CanBeSet() {
        var options = new FredScraperOptions { SleepIntervalHours = 12 };
        options.SleepIntervalHours.Should().Be(12);
    }

    // ── FinraScraperOptions ──────────────────────────────────────────

    [Fact]
    public void FinraScraperOptions_SleepIntervalHours_DefaultsTo24() {
        var options = new FinraScraperOptions();
        options.SleepIntervalHours.Should().Be(24);
    }

    [Fact]
    public void FinraScraperOptions_SleepIntervalHours_CanBeSet() {
        var options = new FinraScraperOptions { SleepIntervalHours = 6 };
        options.SleepIntervalHours.Should().Be(6);
    }

    // ── FtdScraperOptions ────────────────────────────────────────────

    [Fact]
    public void FtdScraperOptions_SleepIntervalHours_DefaultsTo24() {
        var options = new FtdScraperOptions();
        options.SleepIntervalHours.Should().Be(24);
    }

    [Fact]
    public void FtdScraperOptions_SleepIntervalHours_CanBeSet() {
        var options = new FtdScraperOptions { SleepIntervalHours = 48 };
        options.SleepIntervalHours.Should().Be(48);
    }

    // ── YahooPriceScraperOptions ─────────────────────────────────────

    [Fact]
    public void YahooPriceScraperOptions_SleepIntervalHours_DefaultsTo24() {
        var options = new YahooPriceScraperOptions();
        options.SleepIntervalHours.Should().Be(24);
    }

    [Fact]
    public void YahooPriceScraperOptions_SleepIntervalHours_CanBeSet() {
        var options = new YahooPriceScraperOptions { SleepIntervalHours = 1 };
        options.SleepIntervalHours.Should().Be(1);
    }

    // ── DocumentScraperOptions ───────────────────────────────────────

    [Fact]
    public void DocumentScraperOptions_DocumentTypesToSync_DefaultsToExpectedTypes() {
        var options = new DocumentScraperOptions();

        options.DocumentTypesToSync.Should().NotBeNull()
            .And.HaveCount(5)
            .And.ContainInOrder(
                DocumentType.TenK,
                DocumentType.TenQ,
                DocumentType.EightK,
                DocumentType.FormFour,
                DocumentType.FormThree);
    }

    [Fact]
    public void DocumentScraperOptions_DocumentTypesToSync_CanBeSet() {
        var custom = new List<DocumentType> { DocumentType.EightK };
        var options = new DocumentScraperOptions { DocumentTypesToSync = custom };

        options.DocumentTypesToSync.Should().ContainSingle()
            .Which.Should().Be(DocumentType.EightK);
    }

    // ── FinraOptions (integration) ───────────────────────────────────

    [Fact]
    public void FinraOptions_PropertiesDefaultToNull() {
        var options = new FinraOptions();
        options.ClientId.Should().BeNull();
        options.ClientSecret.Should().BeNull();
    }

    [Fact]
    public void FinraOptions_PropertiesCanBeSet() {
        var options = new FinraOptions {
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret"
        };

        options.ClientId.Should().Be("my-client-id");
        options.ClientSecret.Should().Be("my-client-secret");
    }

    // ── FredOptions (integration) ────────────────────────────────────

    [Fact]
    public void FredOptions_ApiKey_DefaultsToNull() {
        var options = new FredOptions();
        options.ApiKey.Should().BeNull();
    }

    [Fact]
    public void FredOptions_ApiKey_CanBeSet() {
        var options = new FredOptions { ApiKey = "test-api-key" };
        options.ApiKey.Should().Be("test-api-key");
    }

    // ── EmbeddingConfig ──────────────────────────────────────────────

    [Fact]
    public void EmbeddingConfig_DefaultValues() {
        var config = new EmbeddingConfig();

        config.Enabled.Should().BeFalse();
        config.ModelName.Should().BeNull();
        config.BaseUrl.Should().BeNull();
        config.ApiKey.Should().BeNull();
        config.BatchSize.Should().Be(10);
    }

    [Fact]
    public void EmbeddingConfig_IsConfigured_ReturnsFalse_WhenDefaults() {
        var config = new EmbeddingConfig();
        config.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void EmbeddingConfig_IsConfigured_ReturnsFalse_WhenEnabledButMissingFields() {
        var config = new EmbeddingConfig { Enabled = true };
        config.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void EmbeddingConfig_IsConfigured_ReturnsTrue_WhenFullyConfigured() {
        var config = new EmbeddingConfig {
            Enabled = true,
            ModelName = "all-MiniLM-L6-v2",
            BaseUrl = "http://localhost:11434",
            ApiKey = "key"
        };

        config.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void EmbeddingConfig_PropertiesCanBeSet() {
        var config = new EmbeddingConfig {
            Enabled = true,
            ModelName = "text-embedding-3-small",
            BaseUrl = "https://api.openai.com",
            ApiKey = "sk-test",
            BatchSize = 50
        };

        config.Enabled.Should().BeTrue();
        config.ModelName.Should().Be("text-embedding-3-small");
        config.BaseUrl.Should().Be("https://api.openai.com");
        config.ApiKey.Should().Be("sk-test");
        config.BatchSize.Should().Be(50);
    }

    // ── IActivable / ISortable (compile-time verification) ───────────

    [Fact]
    public void IActivable_InterfaceDefinesExpectedMembers() {
        var mock = Substitute.For<IActivable>();
        mock.Active = true;

        mock.Active.Should().BeTrue();
        mock.Id.Should().BeEmpty(); // Guid default
    }

    [Fact]
    public void ISortable_InterfaceDefinesExpectedMembers() {
        var mock = Substitute.For<ISortable>();
        mock.Order = 5;

        mock.Order.Should().Be(5);
        mock.Id.Should().BeEmpty(); // Guid default
    }
}
