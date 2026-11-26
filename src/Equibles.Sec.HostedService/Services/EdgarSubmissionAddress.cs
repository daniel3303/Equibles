using System.Text.RegularExpressions;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// The CIK a stored filing's submission is fetched under. EDGAR serves a submission only under a
/// CIK that filed it, and an issuer that takes a new CIK (a holding-company reorganization, a
/// successor) keeps its earlier filings under the old one, which the address the filing was
/// stored from still names.
/// </summary>
public static class EdgarSubmissionAddress
{
    private static readonly Regex Submission = new(
        @"/Archives/edgar/data/(\d+)/(\d{10}-\d{2}-\d{6})\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// The issuer's CIK, unless the stored address names this filing under another one: then
    /// that one, without leading zeros. An address of another accession than the filing's own
    /// says nothing about it.
    /// </summary>
    public static string Cik(Document document)
    {
        var issuerCik = document.Issuer?.Cik;
        var match = Submission.Match(document.SourceUrl ?? string.Empty);
        if (
            !match.Success
            || (
                !string.IsNullOrEmpty(document.AccessionNumber)
                && !string.Equals(
                    match.Groups[2].Value,
                    document.AccessionNumber,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return issuerCik;
        }

        var filedUnder = match.Groups[1].Value.TrimStart('0');
        return filedUnder.Length == 0 || filedUnder == issuerCik?.TrimStart('0')
            ? issuerCik
            : filedUnder;
    }
}
