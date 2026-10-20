using System.IO.Compression;
using System.Text;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The Schedule D 7.B.(1) archive import against Postgres: tables are read from SEC's two-part
/// archive by byte range, funds are summed by type per filing and joined to the filing's CRD and
/// date, repeats collapse, and each part is recorded in its ledger so it is read once.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvPrivateFundArchiveImporterTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FormAdvPrivateFundArchiveImporterTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportNext_ReadsEachPartOnce_AndKeepsOnlyChangedFilings()
    {
        var (client, requests) = ArchiveClient();
        var sut = new FormAdvPrivateFundArchiveImporter(
            ScopeFactory(),
            client,
            Substitute.For<ILogger<FormAdvPrivateFundArchiveImporter>>()
        );

        (await sut.ImportNext(CancellationToken.None))
            .Should()
            .BeTrue("the registered part remains");
        (await sut.ImportNext(CancellationToken.None)).Should().BeFalse();
        var used = requests.Count;
        (await sut.ImportNext(CancellationToken.None)).Should().BeFalse();
        requests.Count.Should().Be(used, "an imported archive is never fetched again");

        await using var verify = _fixture.CreateDbContext();
        var registered = await verify
            .Set<FormAdvPrivateFundFiling>()
            .Where(filing => filing.Registration == FormAdvRegistration.Registered)
            .OrderBy(filing => filing.FiledOn)
            .ToListAsync();
        // Filing 11 repeats filing 10, so only 10, the changed 12 and the empty 13 remain.
        registered.Select(filing => filing.FilingId).Should().Equal(10, 12, 13);
        registered[0].Crd.Should().Be(105958);
        registered[0].FiledOn.Should().Be(new DateOnly(2023, 3, 28));
        registered[0].FundCount.Should().Be(3);
        registered[0].GrossAssets.Should().Be(1_150);
        registered[0].HedgeFundCount.Should().Be(2);
        registered[0].HedgeFundGrossAssets.Should().Be(1_000);
        registered[0].PrivateEquityFundCount.Should().Be(1);
        registered[1].HedgeFundGrossAssets.Should().Be(1_500);
        registered[2].FundCount.Should().Be(0, "a filing after funds stopped records it");

        var exempt = await verify
            .Set<FormAdvPrivateFundFiling>()
            .SingleAsync(filing => filing.Registration == FormAdvRegistration.ExemptReporting);
        exempt.Crd.Should().Be(165476);
        exempt.FiledOn.Should().Be(new DateOnly(2012, 11, 14));
        exempt.VentureCapitalFundGrossAssets.Should().Be(70);
        exempt.VentureCapitalFundCount.Should().Be(2);
        exempt.FundCount.Should().Be(3, "a fund without a recognized type still counts");
        exempt.GrossAssets.Should().Be(75);

        var ledger = await verify.Set<FormAdvFilingArchiveImport>().ToListAsync();
        ledger.Should().HaveCount(2);
        ledger
            .Should()
            .OnlyContain(import =>
                import.ParserVersion == FormAdvPrivateFundArchiveImporter.ParserVersion
                && import.ArchiveEndDate == new DateOnly(2024, 12, 31)
            );
    }

    private static (
        ISecEdgarClient Client,
        List<(string Url, long Offset)> Requests
    ) ArchiveClient()
    {
        var parts = new Dictionary<string, byte[]>
        {
            [FormAdvPrivateFundArchiveImporter.ArchiveUrls[0]] = Zip(
                "adv-part1/adv-part1/",
                ("ADV_Filing_Types_20111105_20241231.csv", "FilingID\n1\n"),
                (
                    "ERA_ADV_Base_20111105_20241231.csv",
                    "FilingID,FormVersion,DateSubmitted,1A,1E1\n"
                        + "20,Oct-12,11/14/2012,EQT CREDIT II GP LP,165476\n"
                        + "21,Oct-12,11/14/2012,OTHER GP,999999\n"
                ),
                (
                    "ERA_Schedule_D_7B1_20111105_20241231.csv",
                    "\"FilingID\",\"Fund Name\",\"Fund Type\",\"Gross Asset Value\"\n"
                        + "20,\"FUND, A\",\"Venture Capital Fund\",70\n"
                        + "20,\"FUND B\",\"\",5\n"
                        // A keyed-in $757 trillion counts the fund without its assets.
                        + "20,\"FUND \"C\" LP\",\"Venture Capital Fund\",757742557721008\n"
                ),
                (
                    "IA_ADV_Base_A_20111105_20241231.csv",
                    "\"FilingID\",\"DateSubmitted\",\"1A\",\"1E1\"\n"
                        + "10,\"03/28/2023 01:39:54 PM\",\"VANGUARD\",105958\n"
                        + "11,\"06/02/2023 09:00:00 AM\",\"VANGUARD\",105958\n"
                        + "12,\"03/27/2024 10:00:00 AM\",\"VANGUARD\",105958\n"
                        + "13,\"03/26/2025 10:00:00 AM\",\"VANGUARD\",105958\n"
                        + "14,\"03/26/2025 10:00:00 AM\",\"NO FUNDS EVER\",42\n"
                )
            ),
            [FormAdvPrivateFundArchiveImporter.ArchiveUrls[1]] = Zip(
                "adv-part2/",
                ("IA_Schedule_D_7B1A17b_20111105_20241231.csv", "FilingID\n10\n"),
                (
                    "IA_Schedule_D_7B1_20111105_20241231.csv",
                    "\"FilingID\",\"Fund Type\",\"Gross Asset Value\"\n"
                        + "10,\"Hedge Fund\",400\n10,\"Hedge Fund\",600\n10,\"Private Equity Fund\",150\n"
                        + "11,\"Hedge Fund\",600\n11,\"Hedge Fund\",400\n11,\"Private Equity Fund\",150\n"
                        + "12,\"Hedge Fund\",1500\n12,\"Private Equity Fund\",150\n"
                )
            ),
        };
        var requests = new List<(string, long)>();
        var client = Substitute.For<ISecEdgarClient>();
        client
            .DownloadRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var url = call.ArgAt<string>(0);
                var offset = call.ArgAt<long>(1);
                var bytes = parts[url];
                var count = (int)Math.Min(call.ArgAt<int>(2), bytes.Length - offset);
                requests.Add((url, offset));
                return Task.FromResult(
                    new SecByteRange(bytes.AsSpan((int)offset, count).ToArray(), bytes.Length)
                );
            });
        return (client, requests);
    }

    private static byte[] Zip(string folder, params (string Name, string Csv)[] tables)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, csv) in tables)
            {
                using var stream = archive.CreateEntry(folder + name).Open();
                var bytes = Encoding.Latin1.GetBytes(csv);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return buffer.ToArray();
    }

    private IServiceScopeFactory ScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = _fixture.CreateDbContext();
                _contexts.Add(context);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }
}
