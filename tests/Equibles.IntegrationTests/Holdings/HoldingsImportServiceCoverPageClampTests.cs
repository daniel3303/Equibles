using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Import contract from the 22001 batch aborts: every cover-page string mapped onto a new
/// InstitutionalHolder must fit its column, because one over-length value rejects the whole
/// batch flush and the filing's rows are silently discarded. The City column is
/// varchar(128) and the cover page is unbounded.
/// </summary>
public class HoldingsImportServiceCoverPageClampTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public HoldingsImportServiceCoverPageClampTests()
    {
        _dbContext = TestDbContextFactory.Create(new HoldingsModuleConfiguration());
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void CreateMissingHolders_OverlongCoverPageCity_IsClampedToTheColumnBound()
    {
        var service = new HoldingsImportService(
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<IBus>()
        );

        var longCity = new string('X', 200);
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["ACC-CLAMP-1"] = new SubmissionRow
                {
                    AccessionNumber = "ACC-CLAMP-1",
                    Cik = "0009990001",
                    FormType = "13F-HR",
                },
            },
            CoverPages = new Dictionary<string, CoverPageRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["ACC-CLAMP-1"] = new CoverPageRow
                {
                    AccessionNumber = "ACC-CLAMP-1",
                    CompanyName = "Clamp Test Advisors LP",
                    City = longCity,
                },
            },
        };

        var holderRepo = new InstitutionalHolderRepository(_dbContext);
        var cikToHolderId = new Dictionary<string, Guid>();

        service.CreateMissingHolders(context, [], holderRepo, cikToHolderId);

        var added = _dbContext.ChangeTracker.Entries<InstitutionalHolder>().Single().Entity;
        added.City.Should().NotBeNull();
        added
            .City.Length.Should()
            .BeLessThanOrEqualTo(
                128,
                "an over-length cover-page city must be clamped to the varchar(128) column or the batch flush aborts with 22001"
            );
    }
}
