using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Extensions;

/// <summary>
/// Maps raw SEC submission/form-type strings (as they appear in the EDGAR daily
/// index and in a filing's <c>submissionType</c> element) to the
/// <see cref="FilingType"/> the holdings model records.
/// </summary>
public static class HoldingsFilingTypeExtensions
{
    /// <summary>
    /// Returns the <see cref="FilingType"/> for a SEC form type, or null when the
    /// form is not one the holdings pipeline ingests. The amendment marker
    /// (<c>/A</c>) is ignored — an amendment maps to the same type as its base
    /// form.
    /// </summary>
    public static FilingType? ToHoldingsFilingType(this string formType)
    {
        if (string.IsNullOrWhiteSpace(formType))
            return null;

        var normalized = formType
            .Replace("/A", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();

        return normalized switch
        {
            "13F-HR" => FilingType.Form13F,
            "SCHEDULE 13D" or "SC 13D" => FilingType.Schedule13D,
            "SCHEDULE 13G" or "SC 13G" => FilingType.Schedule13G,
            _ => null,
        };
    }

    /// <summary>True when the form type is an amendment (its type carries "/A").</summary>
    public static bool IsAmendmentFormType(this string formType) =>
        formType?.Contains("/A", StringComparison.OrdinalIgnoreCase) == true;
}
