using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A holder's classification derived from its <see cref="HolderClassificationEvidence"/>: its
/// entity type with the source that decided it, and the adviser and registered-fund facts behind
/// it. Every holder has one; no evidence leaves <see cref="EntityType"/> Unknown.
/// </summary>
[Index(nameof(InstitutionalHolderId), IsUnique = true)]
[Index(nameof(EntityType))]
public class InstitutionalHolderProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public HolderEntityType EntityType { get; set; }

    /// <summary>The source that decided the type; null when it is Unknown.</summary>
    public HolderEvidenceSource? EntityTypeSource { get; set; }

    [MaxLength(32)]
    public string EntityTypeSourceKey { get; set; }

    public DateOnly? EntityTypeAsOf { get; set; }

    /// <summary>True when current evidence implies more than one type.</summary>
    public bool HasConflictingEvidence { get; set; }

    /// <summary>The holder's own Form ADV adviser, when one is linked by a filed identifier.</summary>
    public int? AdviserCrd { get; set; }

    public DateOnly? AdviserAsOf { get; set; }
    public long? RegulatoryAum { get; set; }

    [MaxLength(20)]
    public string Lei { get; set; }

    public HolderClientBase ClientBase { get; set; }

    /// <summary>The client base's share of regulatory assets, from 0 to 1.</summary>
    public decimal? ClientBaseAumShare { get; set; }

    /// <summary>Pooled investment vehicles' share of regulatory assets, from 0 to 1.</summary>
    public decimal? PooledVehicleAumShare { get; set; }

    public bool? AdvisesPrivateFunds { get; set; }
    public int? PrivateFundCount { get; set; }
    public PrivateFundFocus PrivateFundFocus { get; set; }

    /// <summary>What decided <see cref="PrivateFundFocus"/>; null when there is none.</summary>
    public PrivateFundFocusBasis? PrivateFundFocusBasis { get; set; }

    /// <summary>The roster or Schedule D filing date behind <see cref="PrivateFundFocus"/>.</summary>
    public DateOnly? PrivateFundFocusAsOf { get; set; }

    public long? PrivateFundGrossAssets { get; set; }

    /// <summary>Registered fund series the adviser manages as primary adviser, from their latest N-CEN.</summary>
    public int AdvisedSeriesCount { get; set; }

    public int IndexSeriesCount { get; set; }
    public int EtfSeriesCount { get; set; }
    public decimal? AdvisedNetAssets { get; set; }
    public decimal? IndexNetAssets { get; set; }
    public DateOnly? AdvisedSeriesAsOf { get; set; }

    [MaxLength(8)]
    public string Sic { get; set; }

    public int DerivationVersion { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
