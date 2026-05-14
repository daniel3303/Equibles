using System.ComponentModel.DataAnnotations;
using BenchmarkDotNet.Attributes;
using Equibles.Core.Extensions;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-call cost of <see cref="EnumExtensions.NameForHumans"/>. Every page that renders an
/// enum-backed label (put/call ratio types, holdings categories, transaction codes, etc.)
/// goes through this method, once per displayed value. The implementation does an uncached
/// reflection walk on each call — <c>Type.GetMember</c> + <c>GetCustomAttribute</c> — which
/// allocates a fresh <c>MemberInfo[]</c> and (on a hit) instantiates the attribute. Captures
/// both the "has Display" path and the fallback for callers without the attribute.
/// </summary>
[MemoryDiagnoser]
public class EnumExtensionsBenchmarks
{
    private enum Decorated
    {
        [Display(Name = "Total Exchange")]
        TotalExchange,

        [Display(Name = "Equity")]
        Equity,

        [Display(Name = "Index")]
        Index,

        [Display(Name = "VIX")]
        Vix,

        [Display(Name = "ETP")]
        Etp,
    }

    private enum Plain
    {
        Buy,
        Sell,
        Hold,
        Other,
    }

    private static readonly Decorated[] DecoratedValues = Enum.GetValues<Decorated>();
    private static readonly Plain[] PlainValues = Enum.GetValues<Plain>();

    [Benchmark]
    public int RenderDecoratedEnumNames()
    {
        var total = 0;
        for (var i = 0; i < DecoratedValues.Length; i++)
        {
            total += DecoratedValues[i].NameForHumans().Length;
        }
        return total;
    }

    [Benchmark]
    public int RenderPlainEnumNames()
    {
        // Fallback path — no [Display] attribute, returns ToString(). Still pays the reflection
        // cost because the attribute lookup happens before the null-coalesce.
        var total = 0;
        for (var i = 0; i < PlainValues.Length; i++)
        {
            total += PlainValues[i].NameForHumans().Length;
        }
        return total;
    }
}
