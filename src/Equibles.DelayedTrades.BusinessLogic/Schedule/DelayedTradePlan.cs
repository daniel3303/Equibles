namespace Equibles.DelayedTrades.BusinessLogic.Schedule;

public readonly record struct DelayedTradePlan(bool Intraday, bool Settle, bool Recheck)
{
    public bool IsEmpty => !Intraday && !Settle && !Recheck;
}
