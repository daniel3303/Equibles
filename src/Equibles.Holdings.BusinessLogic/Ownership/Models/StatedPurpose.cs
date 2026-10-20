namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>Whether a Schedule 13D's Item 4 states a purpose, as a deployment reads it.</summary>
public enum StatedPurposeState
{
    /// <summary>Item 4 states one or more of the deployment's purpose categories.</summary>
    Stated = 0,

    /// <summary>Item 4 states none of the deployment's purpose categories.</summary>
    NoneStated = 1,

    /// <summary>An amendment that does not restate Item 4, so the earlier 13D's purpose stands.</summary>
    NotRestated = 2,
}

/// <summary>One purpose a 13D states: the deployment's category token and name, and the filing's own sentence.</summary>
public sealed record StatedPurposeCategory(string Token, string Name, string Quote);

/// <summary>The purpose a Schedule 13D states in Item 4, as an <see cref="IBeneficialOwnershipPurposeSource"/> reads it.</summary>
public sealed record StatedPurpose(
    StatedPurposeState State,
    IReadOnlyList<StatedPurposeCategory> Categories
)
{
    /// <summary>The purposes' names, or why there are none.</summary>
    public string Summary =>
        State switch
        {
            StatedPurposeState.Stated => string.Join(", ", Categories.Select(c => c.Name)),
            StatedPurposeState.NoneStated => "none of the classified purposes",
            _ => "not restated by this amendment, the earlier 13D's purpose stands",
        };
}
