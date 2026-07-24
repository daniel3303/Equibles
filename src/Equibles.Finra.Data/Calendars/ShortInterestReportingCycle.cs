namespace Equibles.Finra.Data.Calendars;

/// <summary>
/// One row of FINRA's short interest reporting calendar: the settlement date positions are
/// measured as of, the deadline broker-dealers must file by, and the date FINRA disseminates
/// the file publicly. See <see cref="ShortInterestCalendar"/> for how these are derived.
/// </summary>
/// <param name="SettlementDate">The date short positions are measured as of.</param>
/// <param name="DueDate">Broker-dealer filing deadline, 6:00 p.m. ET on this date.</param>
/// <param name="PublicationDate">The date FINRA publishes the file (data becomes fetchable).</param>
public sealed record ShortInterestReportingCycle(
    DateOnly SettlementDate,
    DateOnly DueDate,
    DateOnly PublicationDate
);
