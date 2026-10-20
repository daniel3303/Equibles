using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

/// <summary>Reads the registered-fund ownership the fund-ownership drain publishes.</summary>
public class FundOwnershipRepository : BaseRepository<StockQuarterlyFundOwnership>
{
    public FundOwnershipRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>Every quarter whose rows are published, newest first.</summary>
    public IQueryable<DateOnly> GetComputedQuarters() =>
        DbContext
            .Set<FundOwnershipQuarterState>()
            .Where(state => state.ComputedAt != null)
            .OrderByDescending(state => state.ReportDate)
            .Select(state => state.ReportDate);

    public IQueryable<FundOwnershipQuarterState> GetQuarterStates() =>
        DbContext.Set<FundOwnershipQuarterState>();

    /// <summary>One issuer's rows for the given quarters, every listing.</summary>
    public IQueryable<StockQuarterlyFundOwnership> GetByIssuer(
        Guid issuerId,
        IReadOnlyCollection<DateOnly> reportDates
    ) =>
        GetAll()
            .Where(row => row.EquityIssuerId == issuerId && reportDates.Contains(row.ReportDate));

    /// <summary>Every row of one quarter, for whole-market readers such as a screener.</summary>
    public IQueryable<StockQuarterlyFundOwnership> GetByQuarter(DateOnly reportDate) =>
        GetAll().Where(row => row.ReportDate == reportDate);

    /// <summary>The advised registered-fund shares of the given 13F filers in one issuer and quarter.</summary>
    public IQueryable<HolderStockAdvisedFundShares> GetAdvisedFundShares(
        Guid issuerId,
        DateOnly reportDate,
        IReadOnlyCollection<Guid> holderIds
    ) =>
        DbContext
            .Set<HolderStockAdvisedFundShares>()
            .Where(row =>
                row.EquityIssuerId == issuerId
                && row.ReportDate == reportDate
                && holderIds.Contains(row.InstitutionalHolderId)
            );

    /// <summary>The given issuers' splits effective after <paramref name="after"/> and by <paramref name="asOf"/>.</summary>
    public IQueryable<StockSplit> GetSplitsBetween(
        IReadOnlyCollection<Guid> issuerIds,
        DateOnly after,
        DateOnly asOf
    ) =>
        StockSplitQueries
            .ForIssuers(DbContext, issuerIds)
            .Where(split => split.EffectiveDate > after && split.EffectiveDate <= asOf);

    /// <summary>The given issuers' US listings, flagged when one is the issuer's presentation listing.</summary>
    public IQueryable<FundOwnershipListing> GetUsListings(IReadOnlyCollection<Guid> issuerIds) =>
        DbContext
            .Set<EquityListing>()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            )
            .Select(listing => new FundOwnershipListing
            {
                EquityIssuerId = listing.Security.EquityIssuerId,
                ListingId = listing.Id,
                Ticker = listing.Ticker,
                IsPresentation = listing.Security.Issuer.Presentation.EquityListingId == listing.Id,
                IsReferenceListed = listing.IsReferenceListed,
            });
}

/// <summary>A US listing of an issuer with fund ownership, for restating its counts per series.</summary>
public class FundOwnershipListing
{
    public Guid EquityIssuerId { get; set; }
    public Guid ListingId { get; set; }
    public string Ticker { get; set; }
    public bool IsPresentation { get; set; }

    /// <summary>An exchange-traded product's listing, from the security-type reference feed.</summary>
    public bool IsReferenceListed { get; set; }
}
