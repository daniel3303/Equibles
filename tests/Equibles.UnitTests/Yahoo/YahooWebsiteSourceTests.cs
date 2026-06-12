using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Contract: <c>YahooWebsiteSource</c> looks up each stock's asset profile by
/// ticker and returns the profile website where present; blank websites and
/// ticker-less stocks are absent from the result, and a per-ticker HTTP failure
/// skips that stock without sinking the rest of the batch.
/// </summary>
public class YahooWebsiteSourceTests
{
    private static YahooWebsiteSource BuildSut(IYahooFinanceClient client) =>
        new(client, Substitute.For<ILogger<YahooWebsiteSource>>());

    [Fact]
    public async Task ProfileWebsites_AreKeyedByStockId()
    {
        var withWebsite = new WebsiteSourceStock(Guid.NewGuid(), "AAPL", "320193");
        var blankWebsite = new WebsiteSourceStock(Guid.NewGuid(), "ZZZZ", "999999");
        var client = Substitute.For<IYahooFinanceClient>();
        client
            .GetCompanyProfile("AAPL")
            .Returns(new CompanyProfile { Website = "https://www.apple.com" });
        client.GetCompanyProfile("ZZZZ").Returns(new CompanyProfile { Website = " " });

        var result = await BuildSut(client)
            .FindWebsites([withWebsite, blankWebsite], CancellationToken.None);

        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<Guid, string>(withWebsite.Id, "https://www.apple.com"));
    }

    [Fact]
    public async Task PerTickerHttpFailure_SkipsTheStock_NotTheBatch()
    {
        var failing = new WebsiteSourceStock(Guid.NewGuid(), "DEAD", "111111");
        var healthy = new WebsiteSourceStock(Guid.NewGuid(), "AAPL", "320193");
        var client = Substitute.For<IYahooFinanceClient>();
        client.GetCompanyProfile("DEAD").ThrowsAsync(new HttpRequestException("404"));
        client
            .GetCompanyProfile("AAPL")
            .Returns(new CompanyProfile { Website = "https://www.apple.com" });

        var result = await BuildSut(client)
            .FindWebsites([failing, healthy], CancellationToken.None);

        result.Should().ContainSingle().Which.Key.Should().Be(healthy.Id);
    }

    [Fact]
    public async Task TickerlessStocks_AreNeverLookedUp()
    {
        var noTicker = new WebsiteSourceStock(Guid.NewGuid(), null, "111111");
        var client = Substitute.For<IYahooFinanceClient>();

        var result = await BuildSut(client).FindWebsites([noTicker], CancellationToken.None);

        result.Should().BeEmpty();
        await client.DidNotReceive().GetCompanyProfile(Arg.Any<string>());
    }

    [Fact]
    public async Task NullProfile_LeavesTheStockAbsent()
    {
        var stock = new WebsiteSourceStock(Guid.NewGuid(), "AAPL", "320193");
        var client = Substitute.For<IYahooFinanceClient>();
        client.GetCompanyProfile("AAPL").Returns((CompanyProfile)null);

        var result = await BuildSut(client).FindWebsites([stock], CancellationToken.None);

        result.Should().BeEmpty();
    }
}
