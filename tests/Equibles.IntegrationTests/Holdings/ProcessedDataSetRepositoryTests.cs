using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Holdings;

public class ProcessedDataSetRepositoryTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly ProcessedDataSetRepository _repository;

    public ProcessedDataSetRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(new HoldingsModuleConfiguration());
        _repository = new ProcessedDataSetRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Exists_DifferentFileName_ReturnsFalse()
    {
        _repository.Add(
            new ProcessedDataSet { FileName = "2025q3_form13f.zip", SubmissionCount = 1234 }
        );
        await _repository.SaveChanges();

        var result = await _repository.Exists("2025q4_form13f.zip");

        result.Should().BeFalse();
    }
}
