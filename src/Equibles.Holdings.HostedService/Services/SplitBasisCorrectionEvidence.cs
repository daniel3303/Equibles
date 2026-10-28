using System.Text.Json;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Keeps the as-filed figures of every position moved off a straddled split's later basis in
/// <see cref="HoldingsCorrectionEvidence"/>, so a corrected count or value can always be traced
/// back to what the filer wrote. One row per position and kind: a replay re-staging the same
/// correction adds nothing.
/// </summary>
internal static class SplitBasisCorrectionEvidence
{
    internal const string SharesCorrectionKey = "13f-split-basis-shares";
    internal const string ValueCorrectionKey = "13f-split-basis-value";

    // Marks a correction rounding had lost whose filing was queued for recovery; it keeps the
    // damaged figures the replay overwrites.
    internal const string RoundingReplayKey = "13f-split-basis-rounding-replay";
    internal const string SourceTable = nameof(InstitutionalHolding);

    private static readonly string SourceSchema = JsonSerializer.Serialize(
        new[]
        {
            Column("AccessionNumber", "character varying(32)", 1, false),
            Column("Cusip", "character varying(9)", 2, true),
            Column("ListedTicker", "character varying(32)", 3, true),
            Column("ReportDate", "date", 4, false),
            Column("FilingDate", "date", 5, false),
            Column("Shares", "bigint", 6, false),
            Column("FiledValue", "bigint", 7, false),
            Column("Factor", "numeric", 8, false),
            Column("NeighbourShares", "bigint", 9, true),
        }
    );

    internal static string CorrectionKey(SplitBasisShareCountRepairer.Kind kind) =>
        kind == SplitBasisShareCountRepairer.Kind.PostSplitShares
            ? SharesCorrectionKey
            : ValueCorrectionKey;

    // The position's identity in its filing; the stored row's Id is not known before the upsert.
    internal static string SourceRecordId(SplitBasisCorrection correction) =>
        $"{correction.AccessionNumber}|{correction.Cusip}|{correction.ListedTicker}";

    /// <summary>
    /// Adds the evidence rows not already recorded to <paramref name="dbContext"/>; the caller
    /// saves them inside the transaction that writes the corrected positions.
    /// </summary>
    internal static async Task<int> Stage(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<SplitBasisCorrection> corrections,
        CancellationToken cancellationToken
    )
    {
        if (corrections.Count == 0)
            return 0;
        var staged = corrections
            .DistinctBy(correction => (correction.Kind, SourceRecordId(correction)))
            .ToList();
        var recordIds = staged.Select(SourceRecordId).Distinct().ToList();
        var recorded = (
            await dbContext
                .Set<HoldingsCorrectionEvidence>()
                .Where(evidence =>
                    evidence.SourceTable == SourceTable
                    && recordIds.Contains(evidence.SourceRecordId)
                )
                .Select(evidence => new { evidence.CorrectionKey, evidence.SourceRecordId })
                .ToListAsync(cancellationToken)
        )
            .Select(evidence => (evidence.CorrectionKey, evidence.SourceRecordId))
            .ToHashSet();

        var preservedAt = DateTime.UtcNow;
        var added = staged
            .Where(correction =>
                !recorded.Contains((CorrectionKey(correction.Kind), SourceRecordId(correction)))
            )
            .Select(correction => new HoldingsCorrectionEvidence
            {
                CorrectionKey = CorrectionKey(correction.Kind),
                SourceTable = SourceTable,
                SourceRecordId = SourceRecordId(correction),
                SourceSchema = SourceSchema,
                OriginalRow = JsonSerializer.Serialize(
                    new
                    {
                        correction.AccessionNumber,
                        correction.Cusip,
                        correction.ListedTicker,
                        correction.ReportDate,
                        correction.FilingDate,
                        Shares = correction.FiledShares,
                        correction.FiledValue,
                        correction.Factor,
                        correction.NeighbourShares,
                    }
                ),
                MigratedAt = preservedAt,
            })
            .ToList();
        dbContext.Set<HoldingsCorrectionEvidence>().AddRange(added);
        return added.Count;
    }

    private static readonly string RoundingReplaySchema = JsonSerializer.Serialize(
        new[]
        {
            Column("AccessionNumber", "character varying(32)", 1, false),
            Column("Cusip", "character varying(9)", 2, true),
            Column("ListedTicker", "character varying(32)", 3, true),
            Column("Shares", "bigint", 4, false),
            Column("FiledValue", "bigint", 5, false),
            Column("VotingAuthSole", "bigint", 6, false),
            Column("VotingAuthShared", "bigint", 7, false),
            Column("VotingAuthNone", "bigint", 8, false),
        }
    );

    internal sealed record RoundingReplay(
        string AccessionNumber,
        string Cusip,
        string ListedTicker,
        long Shares,
        long FiledValue,
        long VotingAuthSole,
        long VotingAuthShared,
        long VotingAuthNone
    );

    /// <summary>
    /// Adds one marker per position to <paramref name="dbContext"/>; the caller saves them with the
    /// recovery request in one transaction.
    /// </summary>
    internal static void StageRoundingReplays(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<RoundingReplay> replays
    )
    {
        var preservedAt = DateTime.UtcNow;
        dbContext
            .Set<HoldingsCorrectionEvidence>()
            .AddRange(
                replays
                    .DistinctBy(replay =>
                        $"{replay.AccessionNumber}|{replay.Cusip}|{replay.ListedTicker}"
                    )
                    .Select(replay => new HoldingsCorrectionEvidence
                    {
                        CorrectionKey = RoundingReplayKey,
                        SourceTable = SourceTable,
                        SourceRecordId =
                            $"{replay.AccessionNumber}|{replay.Cusip}|{replay.ListedTicker}",
                        SourceSchema = RoundingReplaySchema,
                        OriginalRow = JsonSerializer.Serialize(replay),
                        MigratedAt = preservedAt,
                    })
            );
    }

    private static object Column(string name, string type, int ordinal, bool nullable) =>
        new
        {
            name,
            type,
            ordinal,
            nullable,
        };
}
