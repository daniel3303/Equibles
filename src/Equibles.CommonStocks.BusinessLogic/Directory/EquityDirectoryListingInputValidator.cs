using Equibles.Core.Identity;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

internal static class EquityDirectoryListingInputValidator
{
    public static void Validate(EquityDirectoryListingInput input)
    {
        if (
            input == null
            || string.IsNullOrWhiteSpace(input.Source)
            || input.Source.Length > 64
            || string.IsNullOrWhiteSpace(input.SourceIssuerIdentifier)
            || input.SourceIssuerIdentifier.Length > 128
            || string.IsNullOrWhiteSpace(input.IssuerName)
            || input.IssuerName.Length > 500
            || input.Isin != null && !InternationalSecurityIdentifiers.IsValidIsin(input.Isin)
            || input.Isin == null && input.SourceSecurityIdentifier == null
            || (input.SourceSecurityIdentifier == null) != (input.SourceListingIdentifier == null)
            || input.SourceSecurityIdentifier != null
                && (
                    !SourceIdentifier(input.SourceSecurityIdentifier)
                    || !SourceIdentifier(input.SourceListingIdentifier)
                )
            || input.LegalEntityIdentifier != null
                && !InternationalSecurityIdentifiers.IsValidLei(input.LegalEntityIdentifier)
            || input.RelatedIsins == null
            || input.RelatedIsins.Any(isin => !InternationalSecurityIdentifiers.IsValidIsin(isin))
            || input.LegalEntityIdentifier != null
                && input.Isin != null
                && !input.RelatedIsins.Contains(input.Isin)
            || !Identifier(input.MarketIdentifierCode, 4)
            || !Identifier(input.MarketCountryCode, 2)
            || string.IsNullOrWhiteSpace(input.Ticker)
            || input.Ticker.Length > 32
            || input.Ticker != input.Ticker.Trim()
            || input.TradingCurrency != null && !Identifier(input.TradingCurrency, 3)
            || input.QuoteUnitMultiplier is <= 0
            || (input.TradingCurrency == null) != (input.QuoteUnitMultiplier == null)
            || !Uri.TryCreate(input.SourceUrl, UriKind.Absolute, out var url)
            || url.Scheme != "https"
            || input.SourceUrl.Length > 256
            || string.IsNullOrWhiteSpace(input.PayloadJson)
        )
            throw new InvalidDataException(
                "Directory listing input lacks complete source identity."
            );
    }

    private static bool Identifier(string value, int length) =>
        value?.Length == length
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static bool SourceIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim();
}
