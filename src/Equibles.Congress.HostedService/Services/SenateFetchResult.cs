namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Outcome of a single browser-mediated HTTP request to Senate eFD: the HTTP
/// status code and the raw response body.
/// </summary>
public class SenateFetchResult
{
    public int Status { get; set; }

    public string Body { get; set; }
}
