using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Outcome of a successful investor-relations probe: the validated IR page URL and
/// the platform classified from that page's HTML.
/// </summary>
public sealed record IrDiscoveryResult(string Url, IrPlatformType Platform);
