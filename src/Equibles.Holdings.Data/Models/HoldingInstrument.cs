namespace Equibles.Holdings.Data.Models;

// The display label for a 13F position leg: common shares, a put/call notional, or principal.
public static class HoldingInstrument
{
    public static string Label(OptionType? optionType, ShareType shareType) =>
        optionType switch
        {
            OptionType.Put => "Put",
            OptionType.Call => "Call",
            _ => shareType == ShareType.Principal ? "Principal" : "Common",
        };
}
