using Equibles.CommonStocks.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>Where a Schedule 13D or 13G stake stands today, judged from the filer's latest filing on the security.</summary>
public enum Schedule13DGStakeStatus
{
    /// <summary>At or above the reporting threshold and filed within the last two years.</summary>
    Current = 0,

    /// <summary>
    /// No filing in two years. A 13D or 13G stays in force until amended and no amendment is due
    /// without a material change, so the stake stays shown and summed; its event date is the last
    /// confirmed date.
    /// </summary>
    Unconfirmed = 1,

    /// <summary>
    /// The latest filing reports under 5% of the class: the filer owes no further amendments,
    /// and the shares may still be held.
    /// </summary>
    BelowThreshold = 2,

    /// <summary>The latest filing reports no shares.</summary>
    Exited = 3,

    /// <summary>The issuer no longer has a listing trading in the US (delisted or merged away).</summary>
    SecurityRetired = 4,
}

/// <summary>
/// The one status rule for Schedule 13D and 13G stakes, shared by the stock-side holder list and
/// record and the holder-side stake list, so every surface labels a stake the same way.
/// </summary>
public static class Schedule13DGStatus
{
    /// <summary>A stake with no filing this long is unconfirmed, never exited: silence is not a sale.</summary>
    public const int UnconfirmedMonths = 24;

    /// <summary>The percent of class at which Schedule 13D and 13G reporting begins and ends.</summary>
    public const decimal ReportingThresholdPercent = 5m;

    /// <summary>
    /// Judged in this order: Exited (no shares), SecurityRetired (the issuer does not trade in the
    /// US), BelowThreshold (under 5% of the class as filed), Unconfirmed (older than
    /// <see cref="UnconfirmedMonths"/>), else Current. A filing with no percent is never below the
    /// threshold.
    /// </summary>
    public static Schedule13DGStakeStatus Of(
        long shares,
        decimal? percentOfClass,
        DateOnly eventDate,
        DateOnly today,
        bool issuerListed
    )
    {
        if (shares <= 0)
            return Schedule13DGStakeStatus.Exited;
        if (!issuerListed)
            return Schedule13DGStakeStatus.SecurityRetired;
        if (percentOfClass is { } percent && percent < ReportingThresholdPercent)
            return Schedule13DGStakeStatus.BelowThreshold;
        if (IsUnconfirmed(eventDate, today))
            return Schedule13DGStakeStatus.Unconfirmed;
        return Schedule13DGStakeStatus.Current;
    }

    public static bool IsUnconfirmed(DateOnly eventDate, DateOnly today) =>
        eventDate < today.AddMonths(-UnconfirmedMonths);

    /// <summary>
    /// True when the issuer's presentation line trades in the US. A stake filed on a class or unit
    /// that was since renamed or converted follows the issuer, so an inactive secondary listing
    /// alone never retires it.
    /// </summary>
    public static bool IsListed(EquityIssuer issuer) =>
        issuer?.Presentation?.Listing is { MarketCountryCode: "US", Active: true };

    /// <summary>The statuses a default view keeps: a stake still held at or above the threshold.</summary>
    public static bool IsHeld(Schedule13DGStakeStatus status) =>
        status is Schedule13DGStakeStatus.Current or Schedule13DGStakeStatus.Unconfirmed;
}
