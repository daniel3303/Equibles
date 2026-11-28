using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Comparison;

// Each issuer's compared fact, keyed by issuer id; the issuers whose gross profit pick equals their operating income
// while revenue less cost of revenue states another; and the concept each fact was reported under, which decides how a
// split restates it.
public sealed record FinancialFactComparison(
    IReadOnlyDictionary<Guid, FinancialFact> BestByIssuer,
    IReadOnlySet<Guid> MistaggedIssuerIds,
    IReadOnlyDictionary<Guid, FinancialConceptAliases.ConceptRef> ConceptsById
);
