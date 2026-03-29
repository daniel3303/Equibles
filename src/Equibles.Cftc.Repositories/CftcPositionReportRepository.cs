using Equibles.Cftc.Data.Models;
using Equibles.Data;

namespace Equibles.Cftc.Repositories;

public class CftcPositionReportRepository : BaseRepository<CftcPositionReport> {
    public CftcPositionReportRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CftcPositionReport> GetByContract(CftcContract contract) {
        return GetAll().Where(r => r.CftcContractId == contract.Id);
    }

    public IQueryable<CftcPositionReport> GetByContract(CftcContract contract, DateOnly startDate, DateOnly endDate) {
        return GetAll().Where(r =>
            r.CftcContractId == contract.Id &&
            r.ReportDate >= startDate &&
            r.ReportDate <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate(CftcContract contract) {
        return GetAll()
            .Where(r => r.CftcContractId == contract.Id)
            .Select(r => r.ReportDate)
            .OrderByDescending(d => d)
            .Take(1);
    }

    public IQueryable<CftcPositionReport> GetLatestPerContract() {
        return GetAll()
            .GroupBy(r => r.CftcContractId)
            .Select(g => g.OrderByDescending(r => r.ReportDate).First());
    }

    public IQueryable<DateOnly> GetGlobalLatestDate() {
        return GetAll()
            .Select(r => r.ReportDate)
            .OrderByDescending(d => d)
            .Take(1);
    }
}
