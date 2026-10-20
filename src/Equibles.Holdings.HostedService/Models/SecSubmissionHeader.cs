namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// The parts of an EDGAR complete submission's SGML header a pre-XML Schedule 13D or 13G needs:
/// the submission type and the CIKs of its subject company and filer blocks, in filed order.
/// </summary>
public class SecSubmissionHeader
{
    public string AccessionNumber { get; set; }

    /// <summary>The conformed submission type, e.g. <c>SC 13G/A</c>.</summary>
    public string SubmissionType { get; set; }

    public List<SecSubmissionParty> SubjectCompanies { get; set; } = [];
    public List<SecSubmissionParty> FiledBy { get; set; } = [];
}
