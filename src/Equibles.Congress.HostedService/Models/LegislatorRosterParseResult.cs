using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// One roster file read: how many records it held, the legislators kept (each with its terms
/// split by party and free of overlaps), and the legislators rejected.
/// </summary>
public class LegislatorRosterParseResult
{
    public int RecordCount { get; set; }
    public List<CongressLegislator> Legislators { get; set; } = [];
    public List<LegislatorRejection> Rejections { get; set; } = [];
}
