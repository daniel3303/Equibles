using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// ProfilesController (issue #888 — navigable search hits for institutions,
/// insiders, congress members) had 0% coverage: no test drove its three routes
/// through routing → controller → repository projection → Razor view. These pin
/// the populated happy path of each route, including the row-projection lambdas
/// that only execute when a related record exists.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesControllerViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesControllerViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInstitution_KnownCikWithHolding_RendersHolderAndHoldingRow()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
            var holder = new InstitutionalHolder
            {
                Cik = "0001067983",
                Name = "BERKSHIRE HATHAWAY INC",
                City = "Omaha",
                StateOrCountry = "NE",
            };
            db.Add(stock);
            db.Add(holder);
            db.Add(
                new InstitutionalHolding
                {
                    InstitutionalHolder = holder,
                    CommonStock = stock,
                    ReportDate = new DateOnly(2024, 9, 30),
                    FilingDate = new DateOnly(2024, 11, 14),
                    Shares = 1000,
                    Value = 250000,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Institutions/0001067983");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("BERKSHIRE HATHAWAY INC").And.Contain("AAPL");
    }

    [Fact]
    public async Task GetInsider_KnownOwnerCikWithTransaction_RendersOwnerRoleAndTradeRow()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock { Ticker = "MSFT", Name = "Microsoft Corp" };
            var owner = new InsiderOwner
            {
                OwnerCik = "0001214156",
                Name = "NADELLA SATYA",
                OfficerTitle = "Chief Executive Officer",
                IsDirector = true,
            };
            db.Add(stock);
            db.Add(owner);
            db.Add(
                new InsiderTransaction
                {
                    InsiderOwner = owner,
                    CommonStock = stock,
                    TransactionDate = new DateOnly(2024, 6, 3),
                    FilingDate = new DateOnly(2024, 6, 5),
                    Shares = 500,
                    PricePerShare = 415.20m,
                    SecurityTitle = "Common Stock",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Insiders/0001214156");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("NADELLA SATYA").And.Contain("Chief Executive Officer");
    }

    [Fact]
    public async Task GetMember_KnownIdWithTrade_RendersMemberAndTradeRow()
    {
        var memberId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock { Ticker = "NVDA", Name = "NVIDIA Corp" };
            var member = new CongressMember
            {
                Id = memberId,
                Name = "Jane Representative",
                Position = CongressPosition.Representative,
            };
            db.Add(stock);
            db.Add(member);
            db.Add(
                new CongressionalTrade
                {
                    CongressMember = member,
                    CommonStock = stock,
                    TransactionDate = new DateOnly(2024, 7, 1),
                    FilingDate = new DateOnly(2024, 7, 20),
                    TransactionType = CongressTransactionType.Purchase,
                    AssetName = "NVIDIA Corporation",
                    OwnerType = "Self",
                    AmountFrom = 1001,
                    AmountTo = 15000,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Congress/{memberId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Jane Representative").And.Contain("NVDA");
    }
}
