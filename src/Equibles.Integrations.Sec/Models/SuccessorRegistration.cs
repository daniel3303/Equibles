namespace Equibles.Integrations.Sec.Models;

/// <summary>
/// A registrant's newest successor-issuer report: Form 8-K12B, 8-K12G3 or 8-K15D5, which a company
/// formed in a reorganization files to inherit its predecessor's Exchange Act registration under
/// Rule 12g-3. SEC's ticker directory moves the predecessor's ticker to the new CIK within days.
/// </summary>
public sealed record SuccessorRegistration(string Form, DateOnly FiledOn, string AccessionNumber);
