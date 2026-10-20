using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services.Signals;

/// <summary>
/// Reads and writes the cost slots of <see cref="HolderPositionCostBasis"/> one quarter at a time.
/// A write sets one slot and one mask bit relative to the stored row, so a concurrent rebuild of
/// another quarter never loses its own slot, and only rows whose slot changes are written.
/// </summary>
internal sealed class HolderCostBasisStore
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public HolderCostBasisStore(EquiblesFinancialDbContext dbContext) => _dbContext = dbContext;

    /// <summary>The filers' rows with their slots at <paramref name="quarter"/> and the one before.</summary>
    public Task<List<CostRow>> Load(
        IReadOnlyCollection<Guid> holderIds,
        int quarter,
        CancellationToken cancellationToken
    )
    {
        var previous = quarter - 1;
        var previousBit = quarter > 0 ? 1L << previous : 0L;
        var bit = 1L << quarter;
        return _dbContext
            .Set<HolderPositionCostBasis>()
            .AsNoTracking()
            .Where(row => holderIds.Contains(row.InstitutionalHolderId))
            .Select(row => new CostRow
            {
                Id = row.Id,
                InstitutionalHolderId = row.InstitutionalHolderId,
                Security = new HolderSecurityKey(row.EquityIssuerId, row.ListedTicker),
                PreviousCost =
                    previous >= 0 && row.CostPerShare.Length > previous
                        ? row.CostPerShare[previous]
                        : 0f,
                PreviousUnpriced = (row.UnpricedMask & previousBit) != 0,
                Cost = row.CostPerShare.Length > quarter ? row.CostPerShare[quarter] : 0f,
                Unpriced = (row.UnpricedMask & bit) != 0,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The slots a filer's rows held the quarter before: a stored cost is an estimate, a set
    /// unpriced bit an unpriced buy; a held position with neither had no known entry.
    /// </summary>
    public static Dictionary<HolderSecurityKey, HolderCostSlot> PreviousSlots(
        IEnumerable<CostRow> rows
    )
    {
        var slots = new Dictionary<HolderSecurityKey, HolderCostSlot>();
        foreach (var row in rows)
        {
            if (row.PreviousCost > 0)
                slots[row.Security] = HolderCostSlot.Estimated((decimal)row.PreviousCost);
            else if (row.PreviousUnpriced)
                slots[row.Security] = HolderCostSlot.Unpriced;
        }
        return slots;
    }

    /// <summary>
    /// The slot writes that move each filer's stored rows to the quarter's slots: a held position
    /// stores its estimate or its unpriced bit, and every other stored row of the filer clears the
    /// quarter. Positions with neither and no row stay rowless.
    /// </summary>
    public static List<CostWrite> Diff(
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<HolderSecurityKey, HolderCostSlot>> slots,
        ILookup<Guid, CostRow> stored
    )
    {
        var writes = new List<CostWrite>();
        foreach (var (holderId, holderSlots) in slots)
        {
            var rows = stored[holderId].ToDictionary(row => row.Security);
            foreach (var (security, slot) in holderSlots)
            {
                var cost =
                    slot.Status == HolderCostStatus.Estimated
                        ? (float)slot.CostPerShare.GetValueOrDefault()
                        : 0f;
                var unpriced = slot.Status == HolderCostStatus.Unpriced;
                if (rows.Remove(security, out var row))
                {
                    if (row.Cost != cost || row.Unpriced != unpriced)
                        writes.Add(new CostWrite(holderId, security, row.Id, cost, unpriced));
                }
                else if (cost > 0 || unpriced)
                    writes.Add(new CostWrite(holderId, security, null, cost, unpriced));
            }
            foreach (var row in rows.Values.Where(row => row.Cost != 0 || row.Unpriced))
                writes.Add(new CostWrite(holderId, row.Security, row.Id, 0f, false));
        }
        return writes;
    }

    /// <summary>
    /// Inserts the missing rows empty (a row a concurrent rebuild inserted first is kept), then
    /// sets each write's slot and bit relative to the stored row.
    /// </summary>
    public async Task Apply(
        List<CostWrite> writes,
        int quarter,
        CancellationToken cancellationToken
    )
    {
        var missing = writes.Where(write => write.Id == null).ToList();
        var ids = writes
            .Where(write => write.Id != null)
            .ToDictionary(write => (write.HolderId, write.Security), write => write.Id.Value);
        if (missing.Count > 0)
        {
            await _dbContext
                .Set<HolderPositionCostBasis>()
                .UpsertRange(
                    missing.Select(write => new HolderPositionCostBasis
                    {
                        InstitutionalHolderId = write.HolderId,
                        EquityIssuerId = write.Security.IssuerId,
                        ListedTicker = write.Security.ListedTicker,
                    })
                )
                .On(row => new
                {
                    row.InstitutionalHolderId,
                    row.EquityIssuerId,
                    row.ListedTicker,
                })
                .NoUpdate()
                .RunAsync(cancellationToken);
            var holderIds = missing.Select(write => write.HolderId).Distinct().ToList();
            foreach (var row in await Load(holderIds, quarter, cancellationToken))
                ids.TryAdd((row.InstitutionalHolderId, row.Security), row.Id);
        }

        var bit = 1L << quarter;
        foreach (var chunk in writes.Chunk(HolderIntelligenceRefreshService.PublishBatchSize))
        {
            var rowIds = chunk.Select(write => ids[(write.HolderId, write.Security)]).ToArray();
            var costs = chunk.Select(write => write.Cost).ToArray();
            var unpriced = chunk.Select(write => write.Unpriced ? bit : 0L).ToArray();
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "HolderPositionCostBasis" AS c
                SET "CostPerShare" = CASE
                        WHEN cardinality(c."CostPerShare") > {quarter}
                            THEN c."CostPerShare"[1:{quarter}] || v.cost || c."CostPerShare"[{quarter
                    + 2}:]
                        ELSE c."CostPerShare"
                            || array_fill(0::real, ARRAY[{quarter} - cardinality(c."CostPerShare")])
                            || v.cost
                    END,
                    "UnpricedMask" = (c."UnpricedMask" & ~{bit}) | v.unpriced
                FROM unnest({rowIds}, {costs}, {unpriced}) AS v(id, cost, unpriced)
                WHERE c."Id" = v.id
                """,
                cancellationToken
            );
        }
    }

    public sealed class CostRow
    {
        public Guid Id { get; init; }
        public Guid InstitutionalHolderId { get; init; }
        public HolderSecurityKey Security { get; init; }
        public float PreviousCost { get; init; }
        public bool PreviousUnpriced { get; init; }
        public float Cost { get; init; }
        public bool Unpriced { get; init; }
    }

    public readonly record struct CostWrite(
        Guid HolderId,
        HolderSecurityKey Security,
        Guid? Id,
        float Cost,
        bool Unpriced
    );
}
