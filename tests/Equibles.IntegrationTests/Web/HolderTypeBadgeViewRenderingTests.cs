using System.Net;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// The holder-type badge comes from the holder's regulatory profile, never its name: a profiled
/// bank shows "Bank" on the holdings tab, its holder page and its institution page, while an
/// unprofiled holder or one whose records decide no type shows no badge, whatever its name says.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HolderTypeBadgeViewRenderingTests
{
    private const string BankCik = "0000000901";
    private const string UnprofiledCik = "0000000902";

    private readonly WebHostFixture _fixture;

    public HolderTypeBadgeViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetHoldings_ProfiledAndUnprofiledHolders_BadgesOnlyTheProfiledType()
    {
        await SeedAsync();

        var html = await GetHtml("/Stocks/AAPL/Holdings");

        html.Should()
            .Contain("Example Trust Co")
            .And.Contain("Example ETF Fund LLC")
            .And.Contain("Example Insurance Pension Plan");
        Badges(html).Should().NotBeEmpty().And.OnlyContain(label => label == "Bank");
    }

    [Fact]
    public async Task GetHoldingsCombined_ProfiledAndUnprofiledHolders_BadgesOnlyTheProfiledType()
    {
        await SeedAsync();

        var html = await GetHtml("/Stocks/AAPL/Holdings?combined=true");

        html.Should().Contain("Example Trust Co").And.Contain("Example ETF Fund LLC");
        Badges(html).Should().NotBeEmpty().And.OnlyContain(label => label == "Bank");
    }

    [Fact]
    public async Task GetShowHolder_ProfiledBank_RendersBankBadge()
    {
        await SeedAsync();

        var html = await GetHtml($"/Stocks/AAPL/Holders/{BankCik}");

        Badges(html).Should().Equal("Bank");
    }

    [Fact]
    public async Task GetInstitution_ProfiledBank_RendersBankBadge()
    {
        await SeedAsync();

        var html = await GetHtml($"/Institutions/{BankCik}");

        Badges(html).Should().Equal("Bank");
    }

    [Fact]
    public async Task GetInstitution_UnprofiledHolderWithFundName_RendersNoBadge()
    {
        await SeedAsync();

        var html = await GetHtml($"/Institutions/{UnprofiledCik}");

        html.Should().Contain("Example ETF Fund LLC");
        Badges(html).Should().BeEmpty();
    }

    private async Task<string> GetHtml(string url)
    {
        var response = await _fixture.Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static List<string> Badges(string html) =>
        Regex
            .Matches(html, "data-testid=\"holder-type\"[^>]*>([^<]*)<")
            .Select(match => match.Groups[1].Value.Trim())
            .ToList();

    private Task SeedAsync() =>
        _fixture.ResetAndSeedAsync(async db =>
        {
            EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "AAPL",
                Name: "Apple Inc.",
                Cik: "0000320193"
            );
            var bank = new InstitutionalHolder { Cik = BankCik, Name = "Example Trust Co" };
            var unprofiled = new InstitutionalHolder
            {
                Cik = UnprofiledCik,
                Name = "Example ETF Fund LLC",
            };
            var undecided = new InstitutionalHolder
            {
                Cik = "0000000903",
                Name = "Example Insurance Pension Plan",
            };
            db.AddRange(stock, bank, unprofiled, undecided);
            db.Add(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolder = undecided,
                    EntityType = HolderEntityType.Unknown,
                }
            );
            db.Add(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolder = bank,
                    EntityType = HolderEntityType.Bank,
                    EntityTypeSource = HolderEvidenceSource.SecFilerProfile,
                    EntityTypeAsOf = new DateOnly(2024, 11, 14),
                }
            );
            var reportDate = new DateOnly(2024, 12, 31);
            db.Add(MakeHolding(stock.Id, bank, reportDate.AddMonths(-3), 1_500));
            db.Add(MakeHolding(stock.Id, bank, reportDate, 2_000));
            db.Add(MakeHolding(stock.Id, unprofiled, reportDate, 1_000));
            db.Add(MakeHolding(stock.Id, undecided, reportDate, 500));
            await Task.CompletedTask;
        });

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stockId,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = shares * 10,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
