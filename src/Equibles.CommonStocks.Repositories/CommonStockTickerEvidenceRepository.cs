using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class CommonStockTickerEvidenceRepository : BaseRepository<CommonStockTickerEvidence>
{
    public CommonStockTickerEvidenceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CommonStockTickerEvidence> GetByTickers(IEnumerable<string> tickers)
    {
        return GetAll().Where(evidence => tickers.Contains(evidence.Ticker));
    }

    public Task UpsertRange(
        IEnumerable<CommonStockTickerEvidence> evidence,
        CancellationToken cancellationToken = default
    ) =>
        GetDbSet()
            .UpsertRange(evidence)
            .On(row => new
            {
                row.CommonStockId,
                row.Ticker,
                row.SourceDocumentId,
            })
            .NoUpdate()
            .RunAsync(cancellationToken);
}
