using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class ReportedFinancialStatementRepository : BaseRepository<ReportedFinancialStatement>
{
    public ReportedFinancialStatementRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>Statements reconstructed from a specific filing.</summary>
    public IQueryable<ReportedFinancialStatement> GetByDocument(Document document) =>
        GetAll().Where(s => s.DocumentId == document.Id);

    /// <summary>Statements of a given kind for a company, newest filing first.</summary>
    public IQueryable<ReportedFinancialStatement> GetByStockAndKind(
        CommonStock stock,
        ReportedStatementKind kind
    ) =>
        GetAll()
            .Where(s => s.CommonStockId == stock.Id && s.Kind == kind)
            .OrderByDescending(s => s.FiscalYear)
            .ThenByDescending(s => s.PrimaryPeriodEnd)
            .ThenByDescending(s => s.FiledDate);

    /// <summary>All statements a company has, used to discover the available kinds and periods.</summary>
    public IQueryable<ReportedFinancialStatement> GetByStock(CommonStock stock) =>
        GetAll().Where(s => s.CommonStockId == stock.Id);
}
