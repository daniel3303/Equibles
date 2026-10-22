namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>
/// What a filing's cover page states about one registrant in its unqualified context: the SEC
/// file number (<c>dei:EntityFileNumber</c>) and the shell-company check box
/// (<c>dei:EntityShellCompany</c>). A value is null when the cover does not state it, states it in
/// a form that cannot be decoded, or states two different values.
/// </summary>
public record ParsedRegistrantCover(string Cik, string FileNumber, bool? IsShellCompany);
