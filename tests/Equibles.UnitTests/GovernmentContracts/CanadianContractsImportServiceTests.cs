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

        // Eight backlog quarters from fiscal 2017-2018 were read; the recent quarters were too, but the cursor stops at the backlog.
        ReadCursor(options).Should().Be(new DateOnly(2019, 3, 31));
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

    // The portal answers 504 on a share of pages for hours at a stretch; a page that fails that way is asked
    // again, and only a gateway that never recovers ends the cycle.
    [Fact]
    public async Task Import_AGatewayTimeoutOnAPageIsRetriedBeforeTheCycleIsGivenUp()
    {
        var options = NewDbOptions();
        Seed(options, LastQuarterOfFiscal2025);
        var flaky = new FlakyHandler(new QuarterHandler(Answers()), failures: 2);
        var service = Service(options, flaky);

        await service.Import(CancellationToken.None);

        using var context = NewContext(options);
        context.Set<GovernmentContract>().Should().HaveCount(2);
        ReadCursor(options).Should().Be(new DateOnly(2026, 12, 31));

        var down = Service(
            NewSeeded(),
            new FlakyHandler(new QuarterHandler(Answers()), int.MaxValue)
        );
        var import = () => down.Import(CancellationToken.None);
        (await import.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.GatewayTimeout);
    }

    // National Defence dates an amendment by its own signing: call-up W0046-26-154-0 was let on 2025-11-26 for
    // C$23,995 (Q3) and restated on 2026-01-16 at C$44,642.70 (Q4). The amended value wins, the award date stays,
    // whichever quarter is read first.
    [Fact]
    public async Task Import_AnAmendmentRestatesTheValueButNeverMovesTheAwardDate()
    {
        var options = NewDbOptions();
        Seed(options, new DateOnly(2025, 12, 31), "Calian Ltd", "CGY", "XTSE");
        var answers = new Dictionary<string, string>
        {
            ["2025-2026-Q3"] = Fixture("open-canada-contracts-calian-dnd-2025-2026-Q3.json"),
            ["2025-2026-Q4"] = Fixture("open-canada-contracts-calian-dnd-2025-2026-Q4.json"),
        };
        var service = Service(options, answers);

        // The cursor sits past Q3, so only Q4 (recent) is read first; then the cursor is rewound and Q3 is read after it.
        await service.Import(CancellationToken.None);
        var afterAmendment = Contract(options, "CA:dnd-mdn:W0046-26-154-0");
        RewindCursor(options, new DateOnly(2025, 9, 30));
        await service.Import(CancellationToken.None);

        afterAmendment.ActionDate.Should().Be(new DateOnly(2026, 1, 16));
        Contract(options, "CA:dnd-mdn:W0046-26-154-0")
            .Should()
            .BeEquivalentTo(
                new
                {
                    Amount = 44642.70m,
                    ActionDate = (DateOnly?)new DateOnly(2025, 11, 26),
                    LastModifiedDate = (DateOnly?)new DateOnly(2026, 3, 31),
                }
            );
    }

    private static GovernmentContract Contract(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string key
    )
    {
        using var context = NewContext(options);
        return context
            .Set<GovernmentContract>()
            .AsNoTracking()
            .Single(c => c.AwardUniqueKey == key);
    }

    private static void RewindCursor(
        DbContextOptions<EquiblesFinancialDbContext> options,
        DateOnly cursor
    )
    {
        using var context = NewContext(options);
        context
            .Set<GovernmentContractsScanState>()
            .Single(s => s.Name == "canada-contract-periods")
            .LastCompletedWindowEnd = cursor;
        context.SaveChanges();
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
    ) => Service(options, new QuarterHandler(answers));

    private static CanadianContractsImportService Service(
        DbContextOptions<EquiblesFinancialDbContext> options,
        HttpMessageHandler handler
    )
    {
        var scopes = ScopeFactory(options);
        return new CanadianContractsImportService(
            scopes,
            new Clients(handler),
            new RecipientResolver(scopes),
            new FixedClock(Now),
            NullLogger<CanadianContractsImportService>.Instance
        )
        {
            PageRetryPause = TimeSpan.Zero,
        };
    }

    private static DbContextOptions<EquiblesFinancialDbContext> NewSeeded()
    {
        var options = NewDbOptions();
        Seed(options, LastQuarterOfFiscal2025);
        return options;
    }

    private static void Seed(
        DbContextOptions<EquiblesFinancialDbContext> options,
        DateOnly? cursor,
        string name = "KBL Projects Ltd.",
        string ticker = "KBL",
        string market = "XTSX"
    )
    {
        using var seed = NewContext(options);
        seed.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: ticker,
                Name: name,
                MarketCountryCode: "CA",
                MarketIdentifierCode: market,
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

    // Answers 504 to the first requests it sees, then hands every later one to the recorded answers.
    private sealed class FlakyHandler(HttpMessageHandler inner, int failures)
        : DelegatingHandler(inner)
    {
        private int _seen;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            _seen++ < failures
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout))
                : base.SendAsync(request, cancellationToken);
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
