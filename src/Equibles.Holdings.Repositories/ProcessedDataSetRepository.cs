using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class ProcessedDataSetRepository : BaseRepository<ProcessedDataSet> {
    public ProcessedDataSetRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public async Task<bool> Exists(string fileName) {
        return await GetAll().AnyAsync(p => p.FileName == fileName);
    }
}
