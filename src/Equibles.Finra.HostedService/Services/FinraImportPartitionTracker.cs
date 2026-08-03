using Equibles.Core.AutoWiring;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.HostedService.Services;

[Service]
public class FinraImportPartitionTracker
{
    private readonly FinraImportPartitionRepository _repository;

    public FinraImportPartitionTracker(FinraImportPartitionRepository repository)
    {
        _repository = repository;
    }

    public Task<Dictionary<DateOnly, FinraImportPartition>> GetCompleted(
        string dataset,
        string scopeKey,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken
    )
    {
        return _repository
            .GetRange(dataset, scopeKey, startDate, endDate)
            .ToDictionaryAsync(partition => partition.PartitionDate, cancellationToken);
    }

    public async Task MarkImported(
        string dataset,
        string scopeKey,
        DateOnly partitionDate,
        DateTime importedAt,
        CancellationToken cancellationToken
    )
    {
        var partition = await _repository
            .GetPartition(dataset, scopeKey, partitionDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (partition == null)
        {
            _repository.Add(
                new FinraImportPartition
                {
                    Dataset = dataset,
                    ScopeKey = scopeKey,
                    PartitionDate = partitionDate,
                    ImportedAt = importedAt,
                }
            );
        }
        else
        {
            partition.ImportedAt = importedAt;
        }

        await _repository.SaveChanges();
    }
}
