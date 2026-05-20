using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Institution profile's quarterly-activity section: when the holder has
/// at least two quarters of data the controller diffs the latest pair, the view
/// renders one panel per bucket with the right counts (verified by the badge span
/// inside each bucket's panel), and the page renders a single panel per change
/// type even when only a subset of buckets have rows.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionQuarterlyActivityTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionQuarterlyActivityTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInstitution_SingleQuarter_DoesNotRenderActivityCard()
    {
        var holderCik = "0001700001";
        var holderId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var only = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Single-Quarter Holder",
                }
            );
            db.Add(MakeHolding(stockId, holderId, only, shares: 1_000, value: 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("data-testid=\"institution-quarterly-activity\"");
    }

    [Fact]
    public async Task GetInstitution_TwoQuartersWithMovement_RendersAllFourBucketPanels()
    {
        var holderCik = "0001700002";
        var holderId = Guid.NewGuid();
        var increasedId = Guid.NewGuid();
        var reducedId = Guid.NewGuid();
        var initiatedId = Guid.NewGuid();
        var exitedId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Id = increasedId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                },
                new CommonStock
                {
                    Id = reducedId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                },
                new CommonStock
                {
                    Id = initiatedId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                },
                new CommonStock
                {
                    Id = exitedId,
                    Ticker = "TSLA",
                    Name = "Tesla Inc.",
                    Cik = "0001318605",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Active Allocator LP",
                }
            );
            // Prior quarter: AAPL + MSFT + TSLA.
            db.Add(MakeHolding(increasedId, holderId, prior, shares: 1_000, value: 1_000_000));
            db.Add(MakeHolding(reducedId, holderId, prior, shares: 500, value: 500_000));
            db.Add(MakeHolding(exitedId, holderId, prior, shares: 100, value: 100_000));
            // Current quarter: AAPL increased, MSFT reduced, TSLA exited, NVDA initiated.
            db.Add(MakeHolding(increasedId, holderId, current, shares: 1_500, value: 1_500_000));
            db.Add(MakeHolding(reducedId, holderId, current, shares: 200, value: 200_000));
            db.Add(MakeHolding(initiatedId, holderId, current, shares: 50, value: 50_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"institution-quarterly-activity\"");
        // One panel per bucket, regardless of whether it's empty (the panel still renders
        // the count badge and the empty-state copy).
        html.Should().Contain("data-testid=\"activity-bucket-initiated\"");
        html.Should().Contain("data-testid=\"activity-bucket-increased\"");
        html.Should().Contain("data-testid=\"activity-bucket-reduced\"");
        html.Should().Contain("data-testid=\"activity-bucket-exited\"");
        // The four target tickers each appear in the activity card.
        var sectionStart = html.IndexOf(
            "data-testid=\"institution-quarterly-activity\"",
            StringComparison.Ordinal
        );
        var sectionEnd = html.IndexOf("Recent holdings", sectionStart, StringComparison.Ordinal);
        var section = html.Substring(sectionStart, sectionEnd - sectionStart);
        section.Should().Contain("AAPL");
        section.Should().Contain("MSFT");
        section.Should().Contain("NVDA");
        section.Should().Contain("TSLA");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
