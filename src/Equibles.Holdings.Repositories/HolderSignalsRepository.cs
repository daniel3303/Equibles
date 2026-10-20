using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

/// <summary>
/// Reads the holder-intelligence families published per security and per position: stock
/// signals, holder-overlap peers, estimated cost slots and presence masks.
/// </summary>
public class HolderSignalsRepository : BaseRepository<StockQuarterlyHolderSignals>
{
    public HolderSignalsRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The closed quarters computed by a version that publishes signals, newest first.</summary>
    public IQueryable<HolderIntelligenceQuarterState> GetSignalQuarters(int firstVersion) =>
        DbContext
            .Set<HolderIntelligenceQuarterState>()
            .Where(state => state.ComputedAt != null && state.CalculationVersion >= firstVersion)
            .OrderByDescending(state => state.ReportDate);

    /// <summary>One listing's signal rows; a null ticker is the issuer's primary listing.</summary>
    public IQueryable<StockQuarterlyHolderSignals> GetByListing(
        Guid issuerId,
        string listedTicker
    ) =>
        GetAll()
            .Where(row =>
                row.EquityIssuerId == issuerId
                && (
                    listedTicker == null
                        ? row.ListedTicker == null
                        : row.ListedTicker == listedTicker
                )
            );

    /// <summary>Every primary listing's signal row in one quarter.</summary>
    public IQueryable<StockQuarterlyHolderSignals> GetPrimaryListings(DateOnly reportDate) =>
        GetAll().Where(row => row.ReportDate == reportDate && row.ListedTicker == null);

    /// <summary>One listing's overlap peers, closest first.</summary>
    public IQueryable<StockQuarterlyHolderOverlap> GetOverlap(Guid issuerId, string listedTicker) =>
        DbContext
            .Set<StockQuarterlyHolderOverlap>()
            .Where(row =>
                row.EquityIssuerId == issuerId
                && (
                    listedTicker == null
                        ? row.ListedTicker == null
                        : row.ListedTicker == listedTicker
                )
            );

    /// <summary>The filer's estimated-cost rows, one per position ever costed or unpriced.</summary>
    public IQueryable<HolderPositionCostBasis> GetCostBasis(Guid holderId) =>
        DbContext
            .Set<HolderPositionCostBasis>()
            .Where(row => row.InstitutionalHolderId == holderId);

    /// <summary>The filer's estimated-cost row for one listing, if it was ever costed.</summary>
    public IQueryable<HolderPositionCostBasis> GetCostBasis(
        Guid holderId,
        Guid issuerId,
        string listedTicker
    ) =>
        GetCostBasis(holderId)
            .Where(row =>
                row.EquityIssuerId == issuerId
                && (
                    listedTicker == null
                        ? row.ListedTicker == null
                        : row.ListedTicker == listedTicker
                )
            );

    /// <summary>The filer's presence masks, one per security it ever held.</summary>
    public IQueryable<HolderStockPresence> GetPresence(Guid holderId) =>
        DbContext.Set<HolderStockPresence>().Where(row => row.InstitutionalHolderId == holderId);

    /// <summary>The filers whose presence mask has the quarter's bit set on any listing of an issuer.</summary>
    public IQueryable<Guid> GetHoldersOfIssuer(Guid issuerId, long quarterBit) =>
        DbContext
            .Set<HolderStockPresence>()
            .Where(row => row.EquityIssuerId == issuerId && (row.HeldMask & quarterBit) != 0)
            .Select(row => row.InstitutionalHolderId)
            .Distinct();
}
