namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Outcome of an IR-page probe: the <see cref="Outcome"/> and, when an IR page was found, the
/// validated <see cref="Page"/> (null otherwise).
/// </summary>
public sealed record IrProbeResult(IrProbeOutcome Outcome, IrDiscoveryResult Page)
{
    public static IrProbeResult Found(IrDiscoveryResult page) => new(IrProbeOutcome.Found, page);

    public static readonly IrProbeResult NoIrPage = new(IrProbeOutcome.NoIrPageFound, null);

    public static readonly IrProbeResult Inconclusive = new(IrProbeOutcome.Inconclusive, null);
}
