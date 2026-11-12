namespace Equibles.InsiderTrading.BusinessLogic;

public static class InsiderAmendmentPairing
{
    /// <summary>
    /// EDGAR indexes an after-17:30 submission on the next business day, so an original's feed
    /// date can trail the date its amendment states for it by up to a long weekend.
    /// </summary>
    public const int OriginalDateShiftToleranceDays = 4;
}
