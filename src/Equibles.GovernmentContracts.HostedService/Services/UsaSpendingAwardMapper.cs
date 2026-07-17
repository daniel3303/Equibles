using System.Globalization;
using Equibles.Core.Extensions;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.Integrations.GovernmentContracts.Models;

namespace Equibles.GovernmentContracts.HostedService.Services;

/// <summary>
/// Maps a USAspending wire record onto a <see cref="GovernmentContract"/> entity for an
/// already-resolved public company. Pure and side-effect free so it can be unit tested.
/// </summary>
public static class UsaSpendingAwardMapper
{
    /// <summary>
    /// Builds the entity, or returns null when the award carries no stable unique
    /// identifier (neither a generated id nor a PIID) and therefore can't be stored idempotently.
    /// </summary>
    public static GovernmentContract Map(UsaSpendingAwardRecord record, Guid commonStockId)
    {
        var uniqueKey = FirstNonEmpty(record.GeneratedInternalId, record.AwardId);
        if (uniqueKey == null)
            return null;

        return new GovernmentContract
        {
            CommonStockId = commonStockId,
            AwardUniqueKey = Truncate(uniqueKey, 128),
            AwardId = Truncate(record.AwardId, 128),
            RecipientName = Truncate(record.RecipientName, 512),
            RecipientId = Truncate(record.RecipientId, 64),
            AwardType = MapAwardType(record.ContractAwardType),
            AwardingAgency = Truncate(record.AwardingAgency, 256),
            Amount = record.Amount ?? 0m,
            TotalOutlays = record.TotalOutlays,
            // The award action date must come from Base Obligation Date (the date the base
            // award was signed), never from Start Date: the period-of-performance start can
            // sit years in the future, and a future ActionDate freezes the incremental
            // import cursor (max(ActionDate)+1 overshoots today) — this stalled prod
            // ingestion at Feb 2024.
            ActionDate = ParseDate(record.BaseObligationDate),
            EndDate = ParseDate(record.EndDate),
            LastModifiedDate = ParseDate(record.LastModifiedDate),
            NaicsCode = LeadingToken(record.Naics, 8),
            PscCode = LeadingToken(record.Psc, 8),
            Description = record.Description,
        };
    }

    public static GovernmentContractAwardType MapAwardType(string contractAwardType)
    {
        if (string.IsNullOrWhiteSpace(contractAwardType))
            return GovernmentContractAwardType.Unknown;

        return contractAwardType.Trim().ToUpperInvariant() switch
        {
            "BPA CALL" => GovernmentContractAwardType.BpaCall,
            "PURCHASE ORDER" => GovernmentContractAwardType.PurchaseOrder,
            "DELIVERY ORDER" => GovernmentContractAwardType.DeliveryOrder,
            "DEFINITIVE CONTRACT" => GovernmentContractAwardType.DefinitiveContract,
            _ => GovernmentContractAwardType.Unknown,
        };
    }

    // Date fields arrive as bare ISO dates, except Last Modified Date which carries a
    // timestamp ("2026-07-07 17:57:06") — the date-only strict parse silently nulled it
    // on every row, so both formats are accepted.
    private static readonly string[] WireDateFormats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"];

    // DateTime rather than DateOnly parsing: DateOnly.TryParseExact rejects any format
    // that carries a time component, which would re-null the timestamped variant.
    private static DateOnly? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParseExact(
            value.Trim(),
            WireDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? DateOnly.FromDateTime(date)
            : null;
    }

    private static string FirstNonEmpty(params string[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    // USAspending NAICS/PSC fields carry the bare code; guard against an unexpectedly
    // long value (e.g. "code | description") by keeping only the leading token.
    private static string LeadingToken(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var token = value.Trim().Split(' ', '|')[0];
        return Truncate(token, maxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().TruncateToFit(maxLength);
    }
}
