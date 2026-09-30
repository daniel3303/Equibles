using Equibles.Core.Identity;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

internal static class EquityDirectoryListingInputValidator
{
    public static void Validate(EquityDirectoryListingInput input)
    {
        if (input == null)
            throw Invalid();
        if (!Source(input) || !Instruments(input) || !LegalIdentity(input))
            throw Invalid();
        if (!Listing(input) || !Quotation(input) || !Evidence(input))
            throw Invalid();
    }

    private static bool Source(EquityDirectoryListingInput input) =>
        Text(input.Source, 64)
        && Text(input.SourceIssuerIdentifier, 128)
        && Text(input.IssuerName, 500);

    private static bool Instruments(EquityDirectoryListingInput input)
    {
        if (
            input.SecurityType.HasValue
            && (!Enum.IsDefined(input.SecurityType.Value) || input.SecurityType.Value == 0)
        )
            return false;
        if (input.ListedOn == DateOnly.MinValue)
            return false;
        if (input.Isin != null && !InternationalSecurityIdentifiers.IsValidIsin(input.Isin))
            return false;
        if (input.Isin == null && input.SourceSecurityIdentifier == null)
            return false;
        if ((input.SourceSecurityIdentifier == null) != (input.SourceListingIdentifier == null))
            return false;
        return input.SourceSecurityIdentifier == null
            || SourceIdentifier(input.SourceSecurityIdentifier)
                && SourceIdentifier(input.SourceListingIdentifier);
    }

    private static bool LegalIdentity(EquityDirectoryListingInput input)
    {
        if (
            input.RelatedIsins == null
            || input.RelatedIsins.Any(isin => !InternationalSecurityIdentifiers.IsValidIsin(isin))
        )
            return false;
        if (input.LegalEntityIdentifier == null)
            return true;
        if (!InternationalSecurityIdentifiers.IsValidLei(input.LegalEntityIdentifier))
            return false;
        return input.Isin == null || input.RelatedIsins.Contains(input.Isin);
    }

    private static bool Listing(EquityDirectoryListingInput input)
    {
        if (!Identifier(input.MarketIdentifierCode, 4) || !Identifier(input.MarketCountryCode, 2))
            return false;
        return Text(input.Ticker, 32) && input.Ticker == input.Ticker.Trim();
    }

    private static bool Quotation(EquityDirectoryListingInput input) =>
        input.TradingCurrency == null
            ? input.QuoteUnitMultiplier == null
            : Identifier(input.TradingCurrency, 3) && input.QuoteUnitMultiplier > 0;

    private static bool Evidence(EquityDirectoryListingInput input)
    {
        if (
            string.IsNullOrWhiteSpace(input.PayloadJson)
            || !Uri.TryCreate(input.SourceUrl, UriKind.Absolute, out var url)
        )
            return false;
        return url.Scheme == "https" && input.SourceUrl.Length <= 256;
    }

    private static bool Text(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private static bool Identifier(string value, int length) =>
        value?.Length == length
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static bool SourceIdentifier(string value) => Text(value, 128) && value == value.Trim();

    private static InvalidDataException Invalid() =>
        new("Directory listing input lacks complete source identity.");
}
