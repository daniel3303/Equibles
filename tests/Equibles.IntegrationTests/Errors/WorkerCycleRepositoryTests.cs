using Equibles.Data;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;

namespace Equibles.IntegrationTests.Errors;

public class WorkerCycleRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly WorkerCycleRepository _repository;

    public WorkerCycleRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(new ErrorsModuleConfiguration());
        _repository = new WorkerCycleRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetCompletedAt_UnknownWorker_IsNull()
    {
        (await _repository.GetCompletedAt("Never ran")).Should().BeNull();
    }

    [Fact]
    public async Task MarkCompleted_KeepsOneRowPerWorkerWithTheLatestCompletion()
    {
        var first = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(6);

        await _repository.MarkCompleted("Index composition", first);
        await _repository.MarkCompleted("Index composition", second);
        await _repository.MarkCompleted("Other lane", first);

        _repository.GetAll().Should().HaveCount(2);
        (await _repository.GetCompletedAt("Index composition")).Should().Be(second);
        (await _repository.GetCompletedAt("Other lane")).Should().Be(first);
    }
}
