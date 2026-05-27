namespace Equibles.Data;

/// <summary>
/// Marker for modules holding customer state (PII, billing, support). These map
/// to the customer context / database. The OSS foundation defines the marker so
/// the shared registration helpers can select by it; the OSS tree itself ships
/// no customer modules — they live in the commercial deployment.
/// </summary>
public interface ICustomerModule : IModuleConfiguration { }
