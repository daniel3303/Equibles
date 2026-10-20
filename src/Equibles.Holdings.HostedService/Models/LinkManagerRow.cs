using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>One other manager a filer's 13F report lists, on its summary page or its cover.</summary>
public record LinkManagerRow(
    string AccessionNumber,
    Guid FilerHolderId,
    DateOnly ReportDate,
    DateOnly FilingDate,
    OtherManagerDirection Direction,
    ManagerReference Manager
);
