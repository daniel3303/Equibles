namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Optional second-pass confirmation for a candidate page that has already cleared the cheap
/// keyword prefilter (<see cref="InvestorRelationsPageValidator"/>). The keyword check only asks
/// "does this look IR-ish?" — it counts keyword presence, so a link-dense page (a sitemap, an A–Z
/// index, a mega-menu homepage) or a single press release stuffed with IR boilerplate passes it
/// while not actually being the company's IR landing/events hub. A confirmer is the place to apply
/// a stronger, possibly expensive check (e.g. an LLM classification) that the OSS core deliberately
/// does not depend on.
///
/// OSS ships NO implementation, so a standalone build resolves an empty set and discovery stays
/// keyword-only — exactly as before. A downstream build (the commercial product) registers an
/// implementation, and the probe persists a discovered URL only when every registered confirmer
/// agrees the page is a real IR page. This mirrors how <see cref="IStealthBrowserClient"/> keeps the
/// third-party render engine out of the OSS core: the extension point lives in OSS, the heavy
/// dependency lives downstream.
/// </summary>
public interface IInvestorRelationsPageConfirmer
{
    /// <summary>
    /// True when <paramref name="html"/> (rendered from <paramref name="url"/>) is genuinely an
    /// investor-relations landing/events page, false when it is something the keyword prefilter
    /// can't tell apart (a sitemap, a press release, a generic homepage, a soft-404).
    ///
    /// A rejection is treated as a CONCLUSIVE assessment of that candidate (assessed, not an IR
    /// page) — the probe moves on to the next candidate. So an implementation that cannot reach a
    /// verdict (its backing engine timed out or errored) MUST return true ("fail open"): a confirmer
    /// outage then degrades discovery to keyword-only behavior rather than wrongly writing a real IR
    /// page off as absent and exiling the stock on the miss back-off.
    /// </summary>
    Task<bool> IsInvestorRelationsPage(
        string url,
        string html,
        CancellationToken cancellationToken
    );
}
