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
            ActionDate = ParseDate(record.StartDate),
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

    private static DateOnly? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
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
