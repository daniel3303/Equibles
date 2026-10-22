using Newtonsoft.Json;

namespace Equibles.Integrations.Sec.Models.Responses;

internal class SecAddresses
{
    [JsonProperty("business")]
    public SecAddress Business { get; set; }
}

internal class SecAddress
{
    [JsonProperty("stateOrCountry")]
    public string StateOrCountry { get; set; }

    [JsonProperty("isForeignLocation")]
    public int? IsForeignLocation { get; set; }

    [JsonProperty("countryCode")]
    public string CountryCode { get; set; }

    // EDGAR writes a location two ways: most records carry the code in stateOrCountry, while
    // some foreign ones leave it null, set isForeignLocation and put the code in countryCode.
    public string LocationCode =>
        !string.IsNullOrWhiteSpace(StateOrCountry) ? StateOrCountry.Trim()
        : IsForeignLocation == 1 && !string.IsNullOrWhiteSpace(CountryCode) ? CountryCode.Trim()
        : null;
}
