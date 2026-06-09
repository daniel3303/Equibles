using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>A press release parsed from a Nasdaq IR Insight news RSS feed.</summary>
public sealed record ParsedIrNewsItem(
    string Title,
    string Url,
    string Summary,
    DateTime PublishedAtUtc
);

/// <summary>An event parsed from a Nasdaq IR Insight events RSS feed.</summary>
public sealed record ParsedIrEvent(
    string Title,
    string Url,
    DateTime StartDateTimeUtc,
    IrEventType Type
);
