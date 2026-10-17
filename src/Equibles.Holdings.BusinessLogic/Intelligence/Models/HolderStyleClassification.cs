using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

public sealed record HolderStyleClassification(
    HolderStyleBand Style,
    HolderIndexLikeEvidence IndexLikeEvidence,
    HolderTurnoverBand TurnoverBand,
    HolderHorizonBand HorizonBand
);
