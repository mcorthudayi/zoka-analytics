namespace ZokaAnalytics.Models;

public enum SubscriptionTier
{
    Free = 0,
    Basic = 1,
    Pro = 2,
    Elite = 3
}

public enum MatchStatus
{
    Scheduled = 0,
    Live = 1,
    HalfTime = 2,
    Finished = 3,
    Postponed = 4,
    Cancelled = 5,
    Abandoned = 6
}

public enum MatchOutcome
{
    HomeWin = 0,
    Draw = 1,
    AwayWin = 2
}

public enum BetType
{
    HomeWin = 0,
    Draw = 1,
    AwayWin = 2,
    DoubleChance1X = 3,
    DoubleChanceX2 = 4,
    DoubleChance12 = 5,
    Over25 = 6,
    Under25 = 7,
    BttsYes = 8,
    BttsNo = 9,
    Over15 = 10,
    Under15 = 11,
    Over35 = 12,
    Under35 = 13
}

public enum SelectionStatus
{
    Pending = 0,
    Won = 1,
    Lost = 2,
    Void = 3
}

public enum CouponStatus
{
    Open = 0,
    Won = 1,
    Lost = 2,
    Cancelled = 3
}

public enum SyncStatus
{
    Running = 0,
    Success = 1,
    Failed = 2,
    Skipped = 3
}