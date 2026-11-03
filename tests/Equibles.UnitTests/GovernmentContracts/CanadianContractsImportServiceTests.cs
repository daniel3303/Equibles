using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.GovernmentContracts.Data;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Canada;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.GovernmentContracts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// The recorded KBL quarters hold a contract (CW2439844) let in Q1 and amended twice in Q2, and one (EZ11350885)
// reported only through amendments. One row per contract keeps the latest report's value, and the period cursor
// only moves over quarters actually read.
public class CanadianContractsImportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly LastQuarterOfFiscal2025 = new(2026, 3, 31);

    [Fact]
    public async Task Import_KeepsOneRowPerContractAtItsLatestAmendment_AndARereadNeverRollsItBack()
    {
        var options = NewDbOptions();
        Seed(options, LastQuarterOfFiscal2025);
        var service = Service(options, Answers());

        await service.Import(CancellationToken.None);
        await service.Import(CancellationToken.None);

        using var context = NewContext(options);
        var contracts = context
            .Set<GovernmentContract>()
            .AsNoTracking()
            .OrderBy(c => c.AwardUniqueKey)
            .ToList();
        contracts.Should().HaveCount(2);
        contracts[0]
            .Should()
            .BeEquivalentTo(
                new
                {
                    AwardUniqueKey = "CA:pwgsc-tpsgc:CW2439844",
                    CountryCode = "CA",
                    Amount = 1495407.84m,
                    AwardType = GovernmentContractAwardType.Contract,
                    ActionDate = (DateOnly?)new DateOnly(2026, 6, 11),
                    LastModifiedDate = (DateOnly?)new DateOnly(2026, 9, 30),
                    AwardingAgency = "Public Services and Procurement Canada",
                    RecipientName = "KBL PROJECTS LTD.",
                }
            );
        contracts[1]
            .Should()
            .BeEquivalentTo(
                new
                {
                    AwardUniqueKey = "CA:pwgsc-tpsgc:EZ11350885",
                    Amount = 23419020.89m,
                    AwardType = GovernmentContractAwardType.Unknown,
                    ActionDate = (DateOnly?)new DateOnly(2025, 4, 4),
                }
            );
        ReadCursor(options).Should().Be(new DateOnly(2026, 12, 31));
        service.BacklogRemains.Should().BeFalse();
    }

    [Fact]
    public async Task Import_ReadingTheRecentQuartersNeverCarriesTheCursorPastUnreadBacklog()
    {
        var options = NewDbOptions();
        Seed(options, cursor: null);
        var service = Service(options, Answers());

        await service.Import(CancellationToken.None);

        // Eight backlog quarters from fiscal 2004-2005 were read; the recent quarters were too, but the cursor stops at the backlog.
        ReadCursor(options).Should().Be(new DateOnly(2006, 3, 31));
        service.BacklogRemains.Should().BeTrue();
    }

    [Fact]
    public async Task Import_ARefusedQuarterEndsTheCycleWithoutMovingTheCursor()
    {
        var options = NewDbOptions();
        Seed(options, LastQuarterOfFiscal2025);
        var answers = Answers();
        answers["2026-2027-Q1"] = Fixture("open-canada-rejected.html");
        var service = Service(options, answers);

        var import = () => service.Import(CancellationToken.None);

        await import.Should().ThrowAsync<InvalidDataException>();
        ReadCursor(options).Should().Be(LastQuarterOfFiscal2025);
    }

    private static Dictionary<string, string> Answers() =>
        new()
        {
            ["2026-2027-Q1"] = Fixture("open-canada-contracts-kbl-2026-2027-Q1.json"),
            ["2026-2027-Q2"] = Fixture("open-canada-contracts-kbl-2026-2027-Q2.json"),
        };

    private static CanadianContractsImportService Service(
        DbContextOptions<EquiblesFinancialDbContext> options,
        Dictionary<string, string> answers
    )
    {
        var scopes = ScopeFactory(options);
        return new CanadianContractsImportService(
            scopes,
            new Clients(new QuarterHandler(answers)),
            new RecipientResolver(scopes),
            new FixedClock(Now),
            NullLogger<CanadianContractsImportService>.Instance
        );
    }

    private static void Seed(DbContextOptions<EquiblesFinancialDbContext> options, DateOnly? cursor)
    {
        using var seed = NewContext(options);
        seed.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "KBL",
                Name: "KBL Projects Ltd.",
                MarketCountryCode: "CA",
                MarketIdentifierCode: "XTSX",
                IdentityState: EquityIdentityState.Verified
            )
        );
        if (cursor != null)
            seed.Add(
                new GovernmentContractsScanState
                {
                    Name = "canada-contract-periods",
                    LastCompletedWindowEnd = cursor,
                    MatchingVersion = 1,
                }
            );
        seed.SaveChanges();
    }

    private static DateOnly? ReadCursor(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var context = NewContext(options);
        return context
            .Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .Single(s => s.Name == "canada-contract-periods")
            .LastCompletedWindowEnd;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "GovernmentContracts", "Fixtures", name)
        );

    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new GovernmentContractsModuleConfiguration(),
            }
        );
        context.Database.EnsureCreated();
        return context;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<EquityIssuerRepository>();
        services.AddScoped<GovernmentContractRepository>();
        services.AddScoped<GovernmentContractsScanStateRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // Answers each quarter's request with its recorded page, and any other quarter with an empty one.
    private sealed class QuarterHandler(Dictionary<string, string> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            var body =
                answers
                    .FirstOrDefault(answer =>
                        query.Contains($"\"reporting_period\":\"{answer.Key}\"")
                    )
                    .Value
                ?? """{"success": true, "result": {"records": []}}""";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
