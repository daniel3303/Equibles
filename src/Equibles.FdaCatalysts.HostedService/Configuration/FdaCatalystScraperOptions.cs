using Equibles.Worker;

namespace Equibles.FdaCatalysts.HostedService.Configuration;

public class FdaCatalystScraperOptions : ScraperOptions
{
    /// <summary>
    /// The FDA.gov advisory-committee calendar page. Its meeting rows are rendered
    /// client-side (a Drupal "views" DataTable), so the worker fetches it through the
    /// headless stealth browser rather than plain HTTP — a plain GET returns only the
    /// page chrome with no rows.
    /// </summary>
    public string CalendarUrl { get; set; } =
        "https://www.fda.gov/advisory-committees/advisory-committee-calendar";
}
