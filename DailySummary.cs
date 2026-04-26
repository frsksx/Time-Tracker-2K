namespace TimeTracker2K;

internal sealed record DailySummary(
    DateOnly Date,
    string? FirstLoginTime,
    TimeSpan OnTime,
    TimeSpan LoggedIn,
    TimeSpan Away,
    TimeSpan LoggedOut,
    TimeSpan Correction);
