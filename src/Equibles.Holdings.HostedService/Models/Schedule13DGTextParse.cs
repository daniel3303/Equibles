namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// The outcome of reading one pre-XML Schedule 13D or 13G. <see cref="Filing"/> always carries the
/// header facts that could be read; when <see cref="RefusalReason"/> is set the filing was not read
/// reliably, has no reporting persons, and must never be imported.
/// </summary>
public record Schedule13DGTextParse(Parsed13DGFiling Filing, string RefusalReason)
{
    public bool IsRefused => RefusalReason != null;
}
