namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The applied marker whose stored price boundary was audited.
/// </summary>
public readonly record struct AppliedSplitMarkerSnapshot(Guid SplitId, DateTime AppliedTime);
