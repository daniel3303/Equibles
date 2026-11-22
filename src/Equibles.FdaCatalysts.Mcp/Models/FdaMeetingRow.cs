namespace Equibles.FdaCatalysts.Mcp.Models;

// One meeting of a GetFdaAdvisoryCommitteeMeetings answer, every cell as printed; Url is null where it printed a dash.
public sealed record FdaMeetingRow(
    DateOnly MeetingDate,
    string Title,
    string Center,
    string Type,
    string Through,
    string Url
);
