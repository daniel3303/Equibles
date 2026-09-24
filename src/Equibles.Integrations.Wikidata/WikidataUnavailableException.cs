using System.Net;

namespace Equibles.Integrations.Wikidata;

/// <summary>
/// The Wikidata query service kept answering with a throttle (429) or a server error (5xx)
/// after the bounded retries. Transient by nature: callers should try again on a later cycle.
/// </summary>
public class WikidataUnavailableException : Exception
{
    public WikidataUnavailableException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
