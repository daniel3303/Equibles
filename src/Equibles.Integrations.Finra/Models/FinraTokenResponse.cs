using Newtonsoft.Json;

namespace Equibles.Integrations.Finra.Models;

public class FinraTokenResponse {
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
}
