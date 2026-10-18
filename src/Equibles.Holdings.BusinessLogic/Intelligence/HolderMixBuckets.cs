using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// The bucket a filer falls in for each holder-mix dimension. Every filer lands in exactly one
/// bucket of every dimension, so each dimension's buckets sum to the stock's totals.
/// </summary>
public static class HolderMixBuckets
{
    public sealed record Bucket(string Token, string Label, int Order);

    private const string NotMeasuredToken = "none";
    private const string NotMeasuredLabel = "Not measured";

    private static readonly Bucket[] Turnover =
    [
        new("low", "Low turnover", 0),
        new("med", "Medium turnover", 1),
        new("high", "High turnover", 2),
        new("unk", "Unknown", 3),
        new(NotMeasuredToken, NotMeasuredLabel, 4),
    ];

    private static readonly Bucket[] Horizon =
    [
        new("short", "Short horizon", 0),
        new("med", "Medium horizon", 1),
        new("long", "Long horizon", 2),
        new("unk", "Unknown", 3),
        new(NotMeasuredToken, NotMeasuredLabel, 4),
    ];

    // Thresholds on the filer's 13F equity value in the measured quarter.
    private static readonly (long Below, Bucket Bucket)[] BookSizes =
    [
        (1_000_000_000L, new("under1b", "Under $1B", 0)),
        (10_000_000_000L, new("1to10b", "$1B to $10B", 1)),
        (100_000_000_000L, new("10to100b", "$10B to $100B", 2)),
        (long.MaxValue, new("over100b", "$100B or more", 3)),
    ];

    private static readonly Dictionary<HolderClientBase, Bucket> ClientBases = new()
    {
        [HolderClientBase.Individuals] = new("ind", "Individuals and high net worth", 0),
        [HolderClientBase.PooledInvestmentVehicles] = new(
            "pooled",
            "Pooled investment vehicles",
            1
        ),
        [HolderClientBase.RegisteredFunds] = new("funds", "Registered funds and BDCs", 2),
        [HolderClientBase.PensionPlans] = new("pension", "Pension and profit sharing plans", 3),
        [HolderClientBase.InsuranceCompanies] = new("ins", "Insurance companies", 4),
        [HolderClientBase.CharitableOrganizations] = new("charity", "Charitable organizations", 5),
        [HolderClientBase.GovernmentAndSovereign] = new(
            "gov",
            "Governments and sovereign funds",
            6
        ),
        [HolderClientBase.BanksAndThrifts] = new("bank", "Banks and thrifts", 7),
        [HolderClientBase.Corporations] = new("corp", "Corporations", 8),
        [HolderClientBase.OtherInvestmentAdvisers] = new(
            "advisers",
            "Other investment advisers",
            9
        ),
        [HolderClientBase.Other] = new("other", "Other", 10),
        [HolderClientBase.Diversified] = new("div", "Diversified", 11),
        [HolderClientBase.Unknown] = new("unk", "Unknown", 12),
    };

    private static readonly Dictionary<PrivateFundFocus, Bucket> PrivateFundFocuses = new()
    {
        [PrivateFundFocus.Hedge] = new("hedge", "Hedge funds", 0),
        [PrivateFundFocus.PrivateEquity] = new("pe", "Private equity funds", 1),
        [PrivateFundFocus.VentureCapital] = new("vc", "Venture capital funds", 2),
        [PrivateFundFocus.RealEstate] = new("re", "Real estate funds", 3),
        [PrivateFundFocus.Liquidity] = new("liq", "Liquidity funds", 4),
        [PrivateFundFocus.Securitized] = new("sec", "Securitized asset funds", 5),
        [PrivateFundFocus.Other] = new("other", "Other private funds", 6),
        [PrivateFundFocus.Mixed] = new("mixed", "Mixed private funds", 7),
        [PrivateFundFocus.None] = new("unstated", "Private funds, type not stated", 8),
    };

    private static readonly Bucket NoPrivateFunds = new("nopf", "No private funds", 9);
    private static readonly Bucket PrivateFundsUnknown = new("unk", "Unknown", 10);

    private static readonly Bucket UnitedStates = new("us", "United States", 0);
    private static readonly Bucket OutsideUs = new("nonus", "Outside the US", 1);
    private static readonly Bucket LocationUnknown = new("unk", "Unknown", 2);

    public static Bucket Of(HolderMixDimension dimension, HolderMixHolder holder) =>
        dimension switch
        {
            HolderMixDimension.EntityType => EntityType(holder),
            HolderMixDimension.Style => Style(holder.StyleBand),
            HolderMixDimension.Turnover => holder.TurnoverBand switch
            {
                null => Turnover[4],
                HolderTurnoverBand.Low => Turnover[0],
                HolderTurnoverBand.Medium => Turnover[1],
                HolderTurnoverBand.High => Turnover[2],
                _ => Turnover[3],
            },
            HolderMixDimension.Horizon => holder.HorizonBand switch
            {
                null => Horizon[4],
                HolderHorizonBand.Short => Horizon[0],
                HolderHorizonBand.Medium => Horizon[1],
                HolderHorizonBand.Long => Horizon[2],
                _ => Horizon[3],
            },
            HolderMixDimension.BookSize => holder.EquityAum is { } aum
                ? BookSizes.First(size => aum < size.Below).Bucket
                : new(NotMeasuredToken, NotMeasuredLabel, BookSizes.Length),
            HolderMixDimension.ClientBase => ClientBases[
                holder.Profiled ? holder.ClientBase : HolderClientBase.Unknown
            ],
            HolderMixDimension.PrivateFunds => PrivateFunds(holder),
            HolderMixDimension.Location => Location(holder.StateOrCountry),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
        };

    // An unprofiled filer reads Unknown, as the unk holder-type filter matches it.
    private static Bucket EntityType(HolderMixHolder holder)
    {
        var type = holder.Profiled ? holder.EntityType : HolderEntityType.Unknown;
        // Unknown sorts last, as it does in every other dimension.
        var order = type == HolderEntityType.Unknown ? int.MaxValue : (int)type;
        return new(HolderBucketTokens.Token(type), HolderBucketTokens.Label(type), order);
    }

    // The computed Unknown band stays apart from a filer not measured that quarter.
    private static Bucket Style(HolderStyleBand? band) =>
        band is { } value
            ? new(
                HolderBucketTokens.StyleToken(value),
                HolderBucketTokens.StyleLabel(value),
                StyleOrder(value)
            )
            : new(NotMeasuredToken, NotMeasuredLabel, 5);

    private static int StyleOrder(HolderStyleBand band) =>
        band switch
        {
            HolderStyleBand.IndexLike => 0,
            HolderStyleBand.SystematicLike => 1,
            HolderStyleBand.Concentrated => 2,
            HolderStyleBand.DiversifiedActive => 3,
            _ => 4,
        };

    private static Bucket PrivateFunds(HolderMixHolder holder) =>
        !holder.Profiled || holder.AdvisesPrivateFunds == null ? PrivateFundsUnknown
        : holder.AdvisesPrivateFunds == false ? NoPrivateFunds
        : PrivateFundFocuses[holder.PrivateFundFocus];

    // EDGAR gives US states and territories as letter codes and other countries a code with a
    // digit ("X0" is the United Kingdom); "X1" is the United States given as a country.
    private const string UnitedStatesCountryCode = "X1";

    private static Bucket Location(string stateOrCountry) =>
        string.IsNullOrWhiteSpace(stateOrCountry) ? LocationUnknown
        : string.Equals(
            stateOrCountry.Trim(),
            UnitedStatesCountryCode,
            StringComparison.OrdinalIgnoreCase
        )
            ? UnitedStates
        : stateOrCountry.Any(char.IsDigit) ? OutsideUs
        : UnitedStates;
}
