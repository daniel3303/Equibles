using System.ComponentModel.DataAnnotations.Schema;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// A retired <see cref="CongressMember"/> id and the member it was merged into.
///
/// Filings spell one person several ways, so the same member could be created more than once
/// before <c>NormalizeMemberName</c> canonicalised the upsert key. Collapsing those duplicates
/// deletes rows whose ids are live, indexed URLs (<c>/congress/members/{id}/{slug}</c>), so the
/// portal keeps this map to 301 a retired id to the survivor instead of 404ing it.
///
/// This is deliberately a separate map rather than a "merged into" column on the member itself:
/// a tombstone column would oblige every member query — leaderboard, rankings, sitemap, ingest
/// resolution — to filter merged rows, and a single missed call site puts a ghost member back on
/// a public page. A map is consulted only on the miss path, so no read can accidentally see it.
/// Same shape and reasoning as the article slug alias on the content side.
/// </summary>
public class CongressMemberRedirect
{
    /// <summary>
    /// The RETIRED member's id. The redirect IS that old identity, so the key is always supplied
    /// by the caller and never generated.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    /// <summary>The surviving member this id now resolves to.</summary>
    public Guid MergedIntoId { get; set; }

    public virtual CongressMember MergedInto { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
