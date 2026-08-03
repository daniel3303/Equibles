namespace Equibles.Worker;

internal sealed class ScraperLeasePoolUnavailableException : Exception
{
    internal ScraperLeasePoolUnavailableException()
        : base("The dedicated scraper lease connection pool is at capacity.") { }
}
