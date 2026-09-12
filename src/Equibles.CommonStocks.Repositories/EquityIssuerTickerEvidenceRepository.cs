using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class EquityIssuerTickerEvidenceRepository : BaseRepository<EquityIssuerTickerEvidence>
{
    public EquityIssuerTickerEvidenceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityIssuerTickerEvidence> GetByTickers(IEnumerable<string> tickers)
    {
        return GetAll().Where(evidence => tickers.Contains(evidence.Ticker));
    }

    public Task UpsertRange(
        IEnumerable<EquityIssuerTickerEvidence> evidence,
        CancellationToken cancellationToken = default
    ) =>
        GetDbSet()
            .UpsertRange(evidence)
            .On(row => new
            {
                row.EquityIssuerId,
                row.Ticker,
                row.SourceDocumentId,
            })
            .NoUpdate()
            .RunAsync(cancellationToken);
}
