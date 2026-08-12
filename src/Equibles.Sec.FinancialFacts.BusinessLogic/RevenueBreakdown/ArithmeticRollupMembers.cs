using System.Numerics;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

// Drops arithmetic ROLLUP members from a pivoted breakdown axis — members that duplicate
// other members' revenue as a subtotal (INTC tags a combined "CCG + DCAI + NEX" member
// ALONGSIDE its components; AAPL tags the parent us-gaap:ProductMember alongside its four
// product lines), so every consumer that sums the axis double-counts.
// CollapseOverlappingSchemes can't catch this shape: the rollup plus the leftover members
// never partition into two full-total subsets, so the period is left untouched and the
// subtotal lingers.
//
// Detection is ARITHMETIC PROOF, never name matching:
// a member is a rollup only when
//   - EVERY period it reports is EXACTLY equal — on the raw tagged values — to the sum
//     of two or more other members' same-period values, across at least
//     MinimumEvidencePeriods periods (a single exactly-matching period is not evidence;
//     round-figure coincidence is real), AND
//   - at least one component SUBSET recurs across MinimumEvidencePeriods periods. The
//     recurring subtotal is what an issuer actually tags; two periods each explained
//     only by a DIFFERENT ad-hoc subset is the signature of coincidence on a
//     round-valued axis, not of a rollup. Other periods may still match a different
//     subset — reorganisations move components between years (Intel's combined member
//     equals CCG+DCAI+NEX in FY2022 and CCG+DCAI from FY2023 on, exact in all four
//     years, so {CCG, DCAI} recurs three times).
// One unexplained period keeps the member (a real segment that coincidentally matched
// once). A member with any non-positive reported period is never a rollup, and
// non-positive values never count as subset components — segment subtotals are sums of
// positive revenue, and admitting zeros would flag any member that merely duplicates
// another's value. Conservative consequence: an axis carrying an eliminations member
// inside the subtotal's component set is never collapsed.
public static class ArithmeticRollupMembers
{
    // How many exactly-explained periods — and recurrences of one component subset —
    // a member needs before it is dropped as a rollup.
    public const int MinimumEvidencePeriods = 2;

    // Combinatorial guard: the subset search is exhaustive over the other members, so
    // past this many members the axis is returned unfiltered. Breakdown axes carry well
    // under this in practice (filers tag 3–10 members).
    public const int MaxSearchMembers = 12;

    // The axis's members minus arithmetic rollups. Order preserved; the input list is
    // never mutated. Below three members no subtotal-plus-two-components shape exists.
    public static List<AxisMemberSeries> Filter(List<AxisMemberSeries> members)
    {
        if (members.Count < 3 || members.Count > MaxSearchMembers)
        {
            return members;
        }
        return members.Where(m => !IsArithmeticRollup(m, members)).ToList();
    }

    private static bool IsArithmeticRollup(
        AxisMemberSeries member,
        IReadOnlyList<AxisMemberSeries> allMembers
    )
    {
        var reported = Enumerable
            .Range(0, member.Values.Count)
            .Where(i => member.Values[i].HasValue)
            .ToList();
        if (reported.Count < MinimumEvidencePeriods)
        {
            return false;
        }

        // Component subsets are identified by a bitmask over the axis's member list, so
        // the same subset found in different periods counts as one recurring subtotal.
        var subsetPeriods = new Dictionary<int, int>();
        foreach (var index in reported)
        {
            var target = member.Values[index].Value;
            if (target <= 0)
            {
                return false;
            }

            var components = new List<(int MemberIndex, decimal Value)>();
            for (var other = 0; other < allMembers.Count; other++)
            {
                if (ReferenceEquals(allMembers[other], member))
                {
                    continue;
                }
                var value =
                    index < allMembers[other].Values.Count ? allMembers[other].Values[index] : null;
                if (value is > 0)
                {
                    components.Add((other, value.Value));
                }
            }

            var matches = ExactSubsetMasks(components, target);
            if (matches.Count == 0)
            {
                return false;
            }
            foreach (var mask in matches)
            {
                subsetPeriods[mask] = subsetPeriods.GetValueOrDefault(mask) + 1;
            }
        }

        return subsetPeriods.Values.Any(periods => periods >= MinimumEvidencePeriods);
    }

    // Every subset of two or more values summing exactly to the target, each returned
    // as a bitmask over the axis's member indexes. Exhaustive bitmask search — a few
    // thousand combinations at most under MaxSearchMembers. The local bound makes the
    // shift safety self-contained: C# masks shift counts, so without it a raised
    // MaxSearchMembers would silently turn the loop off instead of overflowing loudly.
    private static List<int> ExactSubsetMasks(
        IReadOnlyList<(int MemberIndex, decimal Value)> components,
        decimal target
    )
    {
        var masks = new List<int>();
        if (components.Count < 2 || components.Count >= MaxSearchMembers)
        {
            return masks;
        }
        for (var subset = 1; subset < 1 << components.Count; subset++)
        {
            if (BitOperations.PopCount((uint)subset) < 2)
            {
                continue;
            }
            var sum = 0m;
            var memberMask = 0;
            for (var bit = 0; bit < components.Count; bit++)
            {
                if ((subset & (1 << bit)) != 0)
                {
                    sum += components[bit].Value;
                    memberMask |= 1 << components[bit].MemberIndex;
                }
            }
            if (sum == target)
            {
                masks.Add(memberMask);
            }
        }
        return masks;
    }
}
