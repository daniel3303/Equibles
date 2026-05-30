using System.ComponentModel.DataAnnotations;

namespace Equibles.Search.Abstractions;

/// <summary>
/// How the consumer wants hits ordered within each result group. Applied centrally by the
/// aggregator on the hits providers return, so no provider needs to know about it.
/// </summary>
public enum SearchSort
{
    /// <summary>Keep each provider's own ranking (provider-local <see cref="SearchHit.Score"/>).</summary>
    [Display(Name = "Relevance")]
    Relevance = 0,

    /// <summary>Alphabetical by <see cref="SearchHit.Title"/>, case-insensitive.</summary>
    [Display(Name = "Name (A–Z)")]
    Name = 1,

    /// <summary>
    /// Newest first by <see cref="SearchHit.Date"/>. Hits without a date keep their provider
    /// order and sink below dated hits, so date-less groups (e.g. Stocks) are unaffected.
    /// </summary>
    [Display(Name = "Date (newest)")]
    Date = 2,
}
