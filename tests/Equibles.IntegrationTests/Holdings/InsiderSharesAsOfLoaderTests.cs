using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.HostedService.Services.Signals;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Insiders' shares as of a quarter end, read on the production context shape (Postgres with
/// lazy-loading proxies, which validate every type a raw SQL query materialises): each insider's
/// newest filing by the date counts at its closing balance, a stale insider and a 13F filer do not.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderSharesAsOfLoaderTests : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 3, 31);

    private readonly ParadeDbFixture _fixture;
    private EquityIssuer _issuer;

    public InsiderSharesAsOfLoaderTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        await using var seed = _fixture.CreateDbContext();
        _issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "NVDA", Name: "NVIDIA Corp");
        var chief = Owner("0001197649");
        var stale = Owner("0001000001");
        var fund = Owner("0000315066");
        seed.AddRange(_issuer, chief, stale, fund);
        seed.AddRange(
            // The newest filing by the quarter end closes the count; the later one is after it.
            Row(chief, new DateOnly(2025, 6, 2), "0001197649-25-000001", ownedAfter: 900_000),
            Row(chief, new DateOnly(2026, 2, 10), "0001197649-26-000001", ownedAfter: 1_000_000),
            Row(chief, new DateOnly(2026, 4, 15), "0001197649-26-000002", ownedAfter: 1_200_000),
            // Silent for more than two years before the quarter end.
            Row(stale, new DateOnly(2023, 12, 1), "0001000001-23-000001", ownedAfter: 50_000),
            // An insider that files 13F books is an institution, not float.
            Row(fund, new DateOnly(2026, 1, 20), "0000315066-26-000001", ownedAfter: 7_000)
        );
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Load_CountsEachInsidersNewestFilingByTheQuarterEnd()
    {
        await using var context = _fixture.CreateDbContext();

        var shares = await InsiderSharesAsOfLoader.Load(
            context,
            Quarter,
            [_issuer.Id],
            new HashSet<string> { "315066" },
            new Dictionary<Guid, List<Equibles.CorporateActions.Data.Models.StockSplit>>(),
            CancellationToken.None
        );

        shares.Should().ContainSingle().Which.Value.Should().Be(1_000_000);
    }

    private static InsiderOwner Owner(string cik) =>
        new()
        {
            OwnerCik = cik,
            Name = $"Owner {cik}",
            City = "Santa Clara",
            StateOrCountry = "CA",
            IsOfficer = true,
        };

    private InsiderTransaction Row(
        InsiderOwner owner,
        DateOnly date,
        string accession,
        long ownedAfter
    ) =>
        new()
        {
            EquityIssuerId = _issuer.Id,
            InsiderOwnerId = owner.Id,
            FilingDate = date.AddDays(2),
            TransactionDate = date,
            TransactionCode = TransactionCode.Purchase,
            Shares = 1_000,
            PricePerShare = 100m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = ownedAfter,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            AccessionNumber = accession,
            FilingForm = InsiderOwnershipForm.Form4,
        };
}
