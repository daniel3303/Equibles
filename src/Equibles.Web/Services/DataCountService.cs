using System.Data;
using Equibles.Cboe.Repositories;
using Equibles.Cftc.Repositories;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class DataCountService
{
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly FredObservationRepository _fredObservationRepository;
    private readonly EquityDailyStockPriceRepository _dailyStockPriceRepository;
    private readonly CftcPositionReportRepository _cftcPositionReportRepository;
    private readonly CboePutCallRatioRepository _cboePutCallRatioRepository;
    private readonly CboeVixDailyRepository _cboeVixDailyRepository;

    public DataCountService(
        EquityIssuerRepository commonStockRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        FailToDeliverRepository failToDeliverRepository,
        FredObservationRepository fredObservationRepository,
        EquityDailyStockPriceRepository dailyStockPriceRepository,
        CftcPositionReportRepository cftcPositionReportRepository,
        CboePutCallRatioRepository cboePutCallRatioRepository,
        CboeVixDailyRepository cboeVixDailyRepository
    )
    {
        _commonStockRepository = commonStockRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _fredObservationRepository = fredObservationRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _cftcPositionReportRepository = cftcPositionReportRepository;
        _cboePutCallRatioRepository = cboePutCallRatioRepository;
        _cboeVixDailyRepository = cboeVixDailyRepository;
    }

    public Task<int> GetStockCount() => CountAll(_commonStockRepository);

    public Task<int> GetDocumentCount() => CountAll(_documentRepository);

    public Task<int> GetInsiderTransactionCount() => CountAll(_insiderTransactionRepository);

    public Task<int> GetCongressionalTradeCount() => CountAll(_congressionalTradeRepository);

    public Task<int> GetInstitutionalHoldingCount() => CountAll(_institutionalHoldingRepository);

    public Task<int> GetFailToDeliverCount() => CountAll(_failToDeliverRepository);

    public Task<int> GetFredObservationCount() => CountAll(_fredObservationRepository);

    public Task<int> GetDailyStockPriceCount() => CountAll(_dailyStockPriceRepository);

    public Task<int> GetCftcPositionReportCount() => CountAll(_cftcPositionReportRepository);

    public Task<int> GetCboePutCallRatioCount() => CountAll(_cboePutCallRatioRepository);

    public Task<int> GetCboeVixDailyCount() => CountAll(_cboeVixDailyRepository);

    // These counts feed the /status dashboard. Upstream counted rows with
    // COUNT(*) once per table - thirteen times on every request - which is a full
    // scan of each table. On a production-sized dataset that is unusable: two of
    // these tables reach 20M and 44M rows, and a COUNT(*) on each did not finish
    // within 240 s (measured 2026-10-20), so the page could never render
    // regardless of the client timeout. Even the small tables cost ~14 s each
    // while the disk was busy.
    //
    // The number on a status page is an indicator, not an accounting figure -
    // nobody acts on the last digit - so read the planner's estimate from
    // pg_class.reltuples instead. That turns thirteen table scans into one
    // catalogue lookup per table at microseconds, and it stays fast under load,
    // which matters most when someone opens this page because the box is unwell.
    //
    // The estimate is as fresh as the last ANALYZE (autovacuum does this), and
    // reltuples is -1 for a table that has never been analyzed - in that case we
    // fall back to the exact count rather than report a wrong zero. That path is
    // rare in practice, so it does not reintroduce the slow behaviour.
    private async Task<int> CountAll<T>(BaseRepository<T> repository)
        where T : class
    {
        var entityType = repository.Context.Model.FindEntityType(typeof(T));
        var table = entityType?.GetTableName();
        if (table is null)
            return await repository.GetAll().CountAsync();

        var schema = entityType!.GetSchema() ?? "public";
        var connection = repository.Context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync();
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT reltuples::bigint FROM pg_class c "
                + "JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE c.relname = @table AND n.nspname = @schema";
            var pTable = cmd.CreateParameter();
            pTable.ParameterName = "@table";
            pTable.Value = table;
            cmd.Parameters.Add(pTable);
            var pSchema = cmd.CreateParameter();
            pSchema.ParameterName = "@schema";
            pSchema.Value = schema;
            cmd.Parameters.Add(pSchema);

            if (await cmd.ExecuteScalarAsync() is long estimate && estimate >= 0)
                return (int)Math.Min(estimate, int.MaxValue);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        // reltuples < 0 means never analyzed. Report the real number then, rather
        // than a wrong zero - this is rare (autovacuum analyzes after the first
        // insert batch) so it does not reintroduce the slow path in practice.
        return await repository.GetAll().CountAsync();
    }
}
