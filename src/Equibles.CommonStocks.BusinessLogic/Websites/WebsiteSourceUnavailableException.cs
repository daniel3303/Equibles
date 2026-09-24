namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// Thrown by an <see cref="IWebsiteSource"/> whose backend is temporarily unavailable
/// (throttled or overloaded). Discovery treats it as "no answer this batch" and retries the
/// stocks next cycle; it is an expected outage, not a fault worth an Errors row.
/// </summary>
public class WebsiteSourceUnavailableException : Exception
{
    public WebsiteSourceUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
