using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsSourceIdentityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsSourceIdentityTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Builds an <see cref="IServiceScopeFactory"/> whose every <c>CreateScope()</c> call
    /// yields a fresh <see cref="EquiblesFinancialDbContext"/> bound to the same ParadeDB instance
    /// — mirroring production DI's scoped-DbContext lifetime. Each repository the
    /// importer pulls out of a scope therefore gets its own context, so saves don't
    /// fight for the same change-tracker.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                sp.GetService(typeof(HoldingsImportFailureRepository))
                    .Returns(new HoldingsImportFailureRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider)
    {
        return new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
        );
    }

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries)
    {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static IStockPriceProvider PriceProviderReturning(
        Dictionary<(Guid, string, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(false, true, false, false, 0)]
    [InlineData(true, false, false, false, 0)]
    [InlineData(true, true, false, false, 0)]
    [InlineData(false, false, true, false, 0)]
    [InlineData(false, true, true, false, 0)]
    [InlineData(true, false, true, false, 0)]
    [InlineData(true, true, true, false, 0)]
    [InlineData(true, false, false, true, 0)]
    [InlineData(true, false, true, false, 1)]
    [InlineData(true, false, true, false, 2)]
    public async Task ImportDataSet_SourceGroup_RetainsEveryExistingSecurityBeforeReplacingBook(
        bool amendment,
        bool reversed,
        bool twoSecurities,
        bool failWrite,
        int corrupt
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GROUP",
            Name: "Source group",
            Cik: "123",
            Cusip: "530307107"
        );
        var holder = new InstitutionalHolder { Cik = "456", Name = "Source holder" };
        InstitutionalHolding Position(string cusip, string label, long shares) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                InstitutionalHolderId = holder.Id,
                ReportDate = new DateOnly(2026, 6, 30),
                FilingDate = new DateOnly(2026, 8, 1),
                FilingType = FilingType.Form13F,
                ShareType = ShareType.Shares,
                Cusip = cusip,
                ListedTicker = label,
                Shares = shares,
                AccessionNumber = "old",
                ManagerEntries = [new HoldingManagerEntry { Shares = shares }],
            };
        var retained = Position("530307305", "RETAINED-B", 222);
        using (var db = FreshContext())
        {
            db.AddRange(issuer, holder, retained);
            db.Add(new EquityIssuerCusipAlias { EquityIssuerId = issuer.Id, Cusip = "530307305" });
            if (twoSecurities)
                db.Add(Position("530307107", "RETAINED-A", 111));
            await db.SaveChangesAsync();
        }
        var sourceRows = new[] { "new\t530307107\t111\tSH\n", "new\t530307305\t222\tSH\n" };
        if (corrupt > 0)
            sourceRows =
            [
                $"new\t530307107\t111\tSH\t111\t{(corrupt == 1 ? 111 : 0)}\n",
                "new\t530307305\t222\tSH\t222\t0\n",
                "new\t530307107\t111\tSH\t111\t0\n",
                "new\t530307107\t111\tSH\t111\t0\n",
                "new\t530307107\t111\tSH\t111\t0\n",
            ];
        if (reversed)
            Array.Reverse(sourceRows);
        using var archive = BuildArchive(
            (
                "SUBMISSION.tsv",
                "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                    + $"{(amendment ? "13F-HR/A" : "13F-HR")}\tnew\t2026-08-15\t2026-06-30\t456\n"
            ),
            (
                "COVERPAGE.tsv",
                "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n"
                    + $"new\t{(amendment ? "Y" : "N")}\tRESTATEMENT\tSource holder\n"
            ),
            (
                "INFOTABLE.tsv",
                "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tVALUE\tVOTING_AUTH_SOLE\n"
                    + string.Concat(sourceRows)
            )
        );
        if (failWrite)
        {
            using var constraint = FreshContext();
            await constraint.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"InstitutionalHolding\" ADD CONSTRAINT test_restatement_write CHECK (\"AccessionNumber\" <> 'new')"
            );
            try
            {
                var import = () =>
                    CreateImporter(PriceProviderReturning([]))
                        .ImportDataSet(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
                await import
                    .Should()
                    .ThrowAsync<Npgsql.PostgresException>()
                    .Where(error => error.SqlState == "23514");
                using var rollback = FreshContext();
                var original = await rollback
                    .Set<InstitutionalHolding>()
                    .Include(row => row.ManagerEntries)
                    .SingleAsync();
                original.Id.Should().Be(retained.Id);
                original.Shares.Should().Be(222);
                original.ManagerEntries.Single().Shares.Should().Be(222);
            }
            finally
            {
                await constraint.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE \"InstitutionalHolding\" DROP CONSTRAINT test_restatement_write"
                );
            }
            return;
        }
        var result = await CreateImporter(PriceProviderReturning([]))
            .ImportDataSet(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
        using var verify = FreshContext();
        var positions = await verify
            .Set<InstitutionalHolding>()
            .Include(row => row.ManagerEntries)
            .ToListAsync();
        if (twoSecurities)
        {
            result.ConflictedFilings.Should().ContainSingle().Which.Should().Be("new");
            positions.Should().HaveCount(2);
            positions.Sum(row => row.Shares).Should().Be(333);
            positions.Should().OnlyContain(row => row.AccessionNumber == "old");
            positions
                .Single(row => row.Id == retained.Id)
                .ManagerEntries.Single()
                .Shares.Should()
                .Be(222);
            (await verify.Set<HoldingsImportFailure>().SingleAsync())
                .Reason.Should()
                .Be(HoldingsImportFailureReason.IdentityConflict);
        }
        else
        {
            result.ConflictedFilings.Should().BeEmpty();
            var position = positions.Should().ContainSingle().Subject;
            position.Cusip.Should().Be("530307305");
            position.ListedTicker.Should().Be("RETAINED-B");
            position.Shares.Should().Be(333);
            position.ManagerEntries.Sum(row => row.Shares).Should().Be(333);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImportDataSet_RestatementAndAdditions_FollowMetadataOrder(
        bool emptyBase,
        bool additionFirst
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "EMPTY",
            Name: "Empty base",
            Cik: "123",
            Cusip: "530307107"
        );
        var addedIssuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ADDITION",
            Name: "Addition",
            Cik: "789",
            Cusip: "037833100"
        );
        var holder = new InstitutionalHolder { Cik = "456", Name = "Source holder" };
        using (var db = FreshContext())
        {
            db.AddRange(issuer, addedIssuer, holder);
            db.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = issuer.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = new DateOnly(2026, 6, 30),
                    FilingDate = new DateOnly(2026, 8, 1),
                    FilingType = FilingType.Form13F,
                    ShareType = ShareType.Shares,
                    Cusip = "530307107",
                    ListedTicker = "STALE",
                    Shares = 999,
                    AccessionNumber = "old",
                }
            );
            await db.SaveChangesAsync();
        }
        var baseRows = emptyBase ? "" : "base\t530307107\t100\tSH\n";
        var additionRows = "addition\t037833100\t200\tSH\n";
        using var archive = BuildArchive(
            (
                "SUBMISSION.tsv",
                "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
                    + "13F-HR/A\tbase\t2026-08-15\t2026-06-30\t456\n13F-HR/A\taddition\t2026-08-16\t2026-06-30\t456\n"
            ),
            (
                "COVERPAGE.tsv",
                "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n"
                    + "base\tY\tRESTATEMENT\tSource holder\naddition\tY\tNEW HOLDINGS\tSource holder\n"
            ),
            (
                "INFOTABLE.tsv",
                "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\n"
                    + (additionFirst ? additionRows + baseRows : baseRows + additionRows)
            )
        );
        await CreateImporter(PriceProviderReturning([]))
            .ImportDataSet(archive, new DateOnly(2020, 1, 1), CancellationToken.None);
        using var verify = FreshContext();
        var positions = await verify.Set<InstitutionalHolding>().ToListAsync();
        positions.Should().HaveCount(emptyBase ? 1 : 2);
        positions.Single(row => row.AccessionNumber == "addition").Shares.Should().Be(200);
        if (!emptyBase)
            positions.Single(row => row.AccessionNumber == "base").Shares.Should().Be(100);
    }
}
