using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>A press release parsed from an IR site's news RSS feed.</summary>
public sealed record ParsedIrNewsItem(
    string Title,
    string Url,
    string Summary,
    DateTime PublishedAtUtc
);

/// <summary>An event parsed from an IR site's events RSS feed.</summary>
public sealed record ParsedIrEvent(
    string Title,
    string Url,
    DateTime StartDateTimeUtc,
    IrEventType Type
);
