using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// <see cref="FredImportServiceTests"/> pins the happy import. This pins
/// <c>Import</c>'s per-series catch arms: with an empty DB every curated series
/// resolves no stored metadata and calls the FRED client, so a throwing client
/// drives the HttpRequestException (skip) and generic-Exception (report) arms —
/// one bad series can never abort the whole curated sweep.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredImportServiceImportCatchTests : ParadeDbMcpTestBase
{
    public FredImportServiceImportCatchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FredImportService BuildService(IFredClient fredClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FredSeriesRepository), new FredSeriesRepository(DbContext))
        );
        return new FredImportService(
            scopeFactory,
            Substitute.For<ILogger<FredImportService>>(),
            fredClient,
            Options.Create(new WorkerOptions()),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    [Fact]
    public async Task Import_FredClientThrowsHttpRequestException_SkipsSeriesAndCompletes()
    {
        var fredClient = Substitute.For<IFredClient>();
        fredClient
            .GetSeriesMetadata(Arg.Any<string>())
            .Returns<Task<Equibles.Integrations.Fred.Models.FredSeriesRecord>>(_ =>
                throw new HttpRequestException("FRED unavailable")
            );

        // Must complete — every curated series 's fetch fails but is caught.
        await BuildService(fredClient).Import(CancellationToken.None);
    }

    [Fact]
    public async Task Import_FredClientThrowsUnexpected_ReportsErrorAndCompletes()
    {
        var fredClient = Substitute.For<IFredClient>();
        fredClient
            .GetSeriesMetadata(Arg.Any<string>())
            .Returns<Task<Equibles.Integrations.Fred.Models.FredSeriesRecord>>(_ =>
                throw new InvalidOperationException("unexpected")
            );

        await BuildService(fredClient).Import(CancellationToken.None);
    }
}
