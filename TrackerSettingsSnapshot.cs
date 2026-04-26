namespace TimeTracker2K;

internal sealed record TrackerSettingsSnapshot(
    decimal StandardWeeklyHours,
    decimal OvertimeTolerancePercent,
    decimal HourlyRate,
    string CurrencyCode,
    string BackupFolderPath,
    string BackupInterval);
