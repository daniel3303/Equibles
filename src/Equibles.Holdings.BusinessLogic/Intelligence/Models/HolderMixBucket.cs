namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One bucket of a stock's holders in one quarter. Shares and value are common stock; the
/// like-for-like pair is null when the quarter has no comparable previous quarter.
/// </summary>
public sealed class HolderMixBucket
{
    public string Token { get; init; }
    public string Label { get; init; }
    public long Shares { get; set; }
    public long Value { get; set; }
    public int Filers { get; set; }
    public long CallShares { get; set; }
    public long CallValue { get; set; }
    public long PutShares { get; set; }
    public long PutValue { get; set; }
    public long? LikeForLikeCurrent { get; set; }
    public long? LikeForLikePrevious { get; set; }

    // Shares of filers with no observed 13F on the other side, left out of the like-for-like pair.
    public long ExcludedCurrentShares { get; set; }
    public long ExcludedPreviousShares { get; set; }

    // Compared filers that opened or closed their common-share position this quarter.
    public int NewFilers { get; set; }
    public int ExitedFilers { get; set; }
}
