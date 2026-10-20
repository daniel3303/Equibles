using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.InsiderTrading.Repositories;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// The insider lane: each insider's actual shares from their newest Form 3, 4 or 5, read through
/// <see cref="InsiderOwnershipPositionCalculator"/> as GetInsiderOwnership reads them, compared with
/// the insider's previous filing. Forms 3, 4 and 5 report an issuer-wide balance across share
/// classes, so the lane serves the primary listing only and every row is flagged issuer-wide.
/// </summary>
public class InsiderContributor : IHolderSourceContributor
{
    private readonly InsiderTransactionRepository _transactionRepository;

    public InsiderContributor(InsiderTransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<IReadOnlyList<StockHolderRow>> Read(
        StockOwnershipScope scope,
        CancellationToken cancellationToken = default
    )
    {
        if (scope.ExactListing || !scope.IsPrimaryListing)
            return [];
        var splits = scope.SplitsFor(scope.PrimaryTicker);
        var calculator = new InsiderOwnershipPositionCalculator(_transactionRepository);
        var current = await calculator.GetCurrentPositions(scope.Stock, splits, cancellationToken);
        if (current.Count == 0)
            return [];
        var previous = await calculator.GetPreviousPositions(
            scope.Stock,
            current,
            splits,
            cancellationToken
        );
        return current
            .Select(position =>
            {
                var owner = position.Anchor.InsiderOwner;
                var cik = CikNormalizer.Canonicalize(owner.OwnerCik);
                // Several balances on one indirect line may be separate vehicles, so neither
                // filing's closing balance compares with the other's.
                var prior = previous.GetValueOrDefault(position.Anchor.InsiderOwnerId);
                if (
                    prior != null
                    && (position.IndirectBalancesAmbiguous || prior.IndirectBalancesAmbiguous)
                )
                    prior = null;
                return new StockHolderRow
                {
                    HolderKey = new StockHolderKey(
                        cik == null ? StockHolderKeyKind.Opaque : StockHolderKeyKind.Cik,
                        cik ?? owner.Id.ToString()
                    ),
                    Source = StockHolderSource.Insider,
                    Name = owner.Name,
                    Cik = cik,
                    Detail = InsiderRoleLabel.Of(owner),
                    Shares = position.TotalShares,
                    FiledShares = position.TotalShares,
                    ChangeShares = prior == null ? null : position.TotalShares - prior.TotalShares,
                    PriorAsOf = prior?.Anchor.TransactionDate,
                    AsOf = position.Anchor.TransactionDate,
                    FilingDate = position.Anchor.FilingDate,
                    Accession = position.Anchor.AccessionNumber,
                    Country = owner.StateOrCountry,
                    Flags =
                        StockHolderRowFlags.IssuerWide
                        | (
                            scope.SplitUnresolved(position.Anchor.TransactionDate, null)
                                ? StockHolderRowFlags.SplitUnresolved
                                : StockHolderRowFlags.None
                        ),
                };
            })
            .ToList();
    }
}
