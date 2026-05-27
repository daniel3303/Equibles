namespace Equibles.Data;

/// <summary>
/// Marker for modules holding public, re-scrapable financial data. These map to
/// the financial context / database. Auto-discovery selects them via
/// <c>EquiblesModuleBuilder.AddAllModulesOfType&lt;IFinancialModule&gt;()</c>.
/// </summary>
public interface IFinancialModule : IModuleConfiguration { }
