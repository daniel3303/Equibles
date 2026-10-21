using System.Net;

namespace Equibles.Integrations.Fred;

/// <summary>
/// FRED answered with a success status but an HTML page instead of JSON, which is how its
/// outage and maintenance pages arrive. The page says nothing about the series or release that
/// was asked for, so a caller sweeping many of them should stop the cycle instead of fanning the
/// same failure out across every remaining request. Derives from
/// <see cref="HttpRequestException"/> so callers that already skip HTTP failures keep doing so.
/// </summary>
public class FredUnavailableException : HttpRequestException
{
    public FredUnavailableException(string message, HttpStatusCode statusCode)
        : base(message, null, statusCode) { }
}
