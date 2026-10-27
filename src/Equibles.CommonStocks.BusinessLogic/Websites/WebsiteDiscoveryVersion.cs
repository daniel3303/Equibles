namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// Version of the website discovery rules. Bump it when a source's rules change in a way that
/// makes websites it stored earlier suspect: every stored website below it is re-checked once
/// through <see cref="IWebsiteSource.FindReplacements"/>.
/// </summary>
public static class WebsiteDiscoveryVersion
{
    // 1: filing website extraction stopped taking a third party's site named next to the
    // company's own ("our website and Amazon.com", a transfer agent's "Its website is").
    public const int Current = 1;
}
