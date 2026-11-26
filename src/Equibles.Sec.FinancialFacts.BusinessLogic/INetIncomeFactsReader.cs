using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// A host that proves an issuer's net income from evidence this module does not hold (a foreign
/// filer's owners' profit, proven from its annual reports) states the figures the net income alias
/// reads for that issuer, so the fact tools show the line the host's other surfaces show.
/// </summary>
public interface INetIncomeFactsReader
{
    /// <summary>
    /// The issuer's net income figures, consolidated and one per period, each with the concept it
    /// is read as; null where the host has no reading of its own and the alias reads its tags. An
    /// empty reading means the host shows no net income for the issuer.
    /// </summary>
    Task<NetIncomeReading> Read(EquityIssuer issuer, CancellationToken cancellationToken = default);
}

/// <summary>
/// The figures an <see cref="INetIncomeFactsReader"/> states, with the concept each carries.
/// <paramref name="Derived"/> holds the quarters of <paramref name="Facts"/> the host worked out
/// itself: the tools mark them and work out none of their own, so they show the host's quarters.
/// </summary>
public sealed record NetIncomeReading(
    IReadOnlyList<FinancialFact> Facts,
    IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> Concepts,
    IReadOnlySet<FinancialFact> Derived
);
