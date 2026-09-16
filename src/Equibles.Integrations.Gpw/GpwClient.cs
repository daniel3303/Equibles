using Equibles.Core.Identity;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.Gpw.Models;

namespace Equibles.Integrations.Gpw;

// Plain GETs only: the exchange's firewall resets a browser user agent without a browser behind it and rejects
// every POST to its table loader, while these addresses answer any client that asks plainly.
public class GpwClient(HttpClient httpClient)
{
    private static readonly Uri Origin = new("https://www.gpw.pl");
    private const int MaxTableBytes = 8_000_000;
    private const int MaxPageBytes = 2_000_000;

    public const string ContinuousTradingSystem = "continuous";

    // The continuous-trading table and the two single-price auction tables together list the main market.
    public static readonly IReadOnlyList<string> TradingSystems =
    [
        ContinuousTradingSystem,
        "fix1",
        "fix2",
    ];

    public static Uri TableUrl(string tradingSystem) =>
        tradingSystem == ContinuousTradingSystem
            ? new Uri(
                Origin,
                "/ajaxindex.php?action=GPWQuotations&start=showTable&tab=all&lang=EN&type=&full=1&format=html"
            )
            : new Uri(
                Origin,
                $"/ajaxindex.php?action=GPWQuotations&start=listSingle&tab=all&lang=EN&type={tradingSystem}&full=1&format=html"
            );

    public static Uri FactsheetUrl(string isin) => new(Origin, $"/company-factsheet?isin={isin}");

    public async Task<GpwQuotationList> GetQuotations(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var list = new GpwQuotationList { CapturedAt = DateTime.UtcNow };
        foreach (var tradingSystem in TradingSystems)
        {
            var url = TableUrl(tradingSystem);
            var html = await SameOriginTextReader.Read(
                httpClient,
                Origin,
                url,
                MaxTableBytes,
                timeout.Token
            );
            list.Tables.Add(
                new GpwQuotationTable
                {
                    TradingSystem = tradingSystem,
                    SourceUrl = url,
                    Quotations = GpwParser.ReadTable(html),
                    Html = html,
                }
            );
        }
        return list;
    }

    public async Task<GpwCompanyFactsheet> GetFactsheet(
        string isin,
        CancellationToken cancellationToken = default
    )
    {
        if (!InternationalSecurityIdentifiers.IsValidIsin(isin))
            throw new InvalidDataException("GPW company page needs a valid ISIN.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var url = FactsheetUrl(isin);
        var html = await SameOriginTextReader.Read(
            httpClient,
            Origin,
            url,
            MaxPageBytes,
            timeout.Token
        );
        var factsheet = GpwParser.ReadFactsheet(html);
        if (factsheet.Isin != isin)
            throw new InvalidDataException("GPW company page answers for another security.");
        factsheet.SourceUrl = url;
        return factsheet;
    }
}
