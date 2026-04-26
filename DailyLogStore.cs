using System.Globalization;
using System.Text.Json;

namespace TimeTracker2K;

internal sealed class DailyLogStore
{
    public const int DailyDisplayDays = 30;
    public const int RetentionDays = 730;

    private static readonly TimeSpan AwayThreshold = TimeSpan.FromMinutes(5);
    private const string AppDataFolderName = "TimeTracker2K";
    private const string LegacyAppDataFolderName = "LoginDurationTracker";
    private const string DataFileName = "work-log.json";
    private const string BackupFileName = "work-log-backup.json";
    public const string DailyBackupInterval = "Daily";
    public const string WeeklyBackupInterval = "Weekly";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _dataFilePath;
    private readonly string _legacyAppDataDirectory;
    private TrackerData _data;
    private DateTimeOffset _lastCheckpoint;
    private DateTimeOffset _lastInputAt;
    private TrackingMode _mode = TrackingMode.LoggedIn;
    private TrackingMode _modeBeforeSuspend = TrackingMode.LoggedIn;

    public DailyLogStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataDirectory = Path.Combine(
            localAppData,
            AppDataFolderName);
        _legacyAppDataDirectory = Path.Combine(
            localAppData,
            LegacyAppDataFolderName);
        _dataFilePath = Path.Combine(AppDataDirectory, DataFileName);
        _data = Load();
        MigrateOldData();

        var now = DateTimeOffset.Now;
        CompletePendingLogout(now);
        _lastCheckpoint = now;
        _lastInputAt = IdleTime.GetLastInputTime();
        TrimOldEntries();
        Save();
    }

    public string AppDataDirectory { get; }

    public string DataFilePath => _dataFilePath;

    public TrackerSettingsSnapshot GetSettings()
    {
        lock (_gate)
        {
            EnsureSettings();
            return new TrackerSettingsSnapshot(
                (decimal)_data.StandardWeeklyHours,
                (decimal)_data.OvertimeTolerancePercent,
                (decimal)_data.HourlyRate,
                _data.CurrencyCode,
                _data.BackupFolderPath,
                _data.BackupInterval);
        }
    }

    public void UpdateSettings(
        decimal standardWeeklyHours,
        decimal overtimeTolerancePercent,
        decimal hourlyRate,
        string currencyCode,
        string backupFolderPath,
        string backupInterval)
    {
        lock (_gate)
        {
            _data.StandardWeeklyHours = (double)Math.Clamp(standardWeeklyHours, 1, 100);
            _data.OvertimeTolerancePercent = (double)Math.Clamp(overtimeTolerancePercent, 0, 100);
            _data.HourlyRate = (double)Math.Clamp(hourlyRate, 0, 10000);
            _data.CurrencyCode = NormalizeCurrencyCode(currencyCode);
            _data.BackupFolderPath = NormalizeBackupFolderPath(backupFolderPath);
            _data.BackupInterval = NormalizeBackupInterval(backupInterval);
            Save();
        }
    }

    public TimeSpan GetWeeklyCorrection(DateOnly weekStart)
    {
        lock (_gate)
        {
            return TimeSpan.FromSeconds(ReadWeeklyCorrectionSeconds(weekStart));
        }
    }

    public void SetWeeklyCorrection(DateOnly weekStart, TimeSpan correction)
    {
        lock (_gate)
        {
            var key = ToKey(GetWeekStart(weekStart));
            var seconds = (long)Math.Round(correction.TotalSeconds);
            if (seconds == 0)
            {
                _data.WeeklyCorrectionSeconds.Remove(key);
            }
            else
            {
                _data.WeeklyCorrectionSeconds[key] = seconds;
            }

            TrimOldEntries();
            Save();
        }
    }

    public void RestoreFromBackup(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Backup path is empty.", nameof(backupPath));
        }

        backupPath = Environment.ExpandEnvironmentVariables(backupPath.Trim());
        if (!TryLoadData(backupPath, out var restoredData))
        {
            throw new InvalidDataException("The selected file is not a readable Time Tracker 2K backup.");
        }

        lock (_gate)
        {
            if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(DateTimeOffset.Now);
            }

            BackupCurrentDataBeforeRestore();
            _data = restoredData;
            _data.PendingLogoutStartedAt = null;
            MigrateOldData();
            TrimOldEntries();
            _lastCheckpoint = DateTimeOffset.Now;
            _lastInputAt = IdleTime.GetLastInputTime();
            _mode = TrackingMode.LoggedIn;
            Save();
        }
    }

    public void Checkpoint()
    {
        lock (_gate)
        {
            if (_mode == TrackingMode.Suspended)
            {
                return;
            }

            CheckpointCore(DateTimeOffset.Now);
            Save();
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_mode != TrackingMode.Suspended)
            {
                _modeBeforeSuspend = _mode;
                CheckpointCore(DateTimeOffset.Now);
                _mode = TrackingMode.Suspended;
                Save();
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _lastCheckpoint = DateTimeOffset.Now;
            _lastInputAt = IdleTime.GetLastInputTime();
            _mode = _modeBeforeSuspend == TrackingMode.Suspended ? TrackingMode.LoggedIn : _modeBeforeSuspend;
            Save();
        }
    }

    public void MarkLoggedOutStarted(bool persistAcrossRestart)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(now);
            }

            _mode = TrackingMode.LoggedOut;
            _lastCheckpoint = now;
            if (persistAcrossRestart)
            {
                _data.PendingLogoutStartedAt ??= now;
            }

            Save();
        }
    }

    public void MarkLoggedInAgain()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (_mode == TrackingMode.LoggedOut)
            {
                CheckpointCore(now);
                _data.PendingLogoutStartedAt = null;
            }
            else if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(now);
                CompletePendingLogout(now);
            }
            else
            {
                CompletePendingLogout(now);
            }

            _lastCheckpoint = now;
            _lastInputAt = IdleTime.GetLastInputTime();
            _mode = TrackingMode.LoggedIn;
            Save();
        }
    }

    public IReadOnlyList<DailySummary> GetLastDays(int dayCount)
    {
        lock (_gate)
        {
            if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(DateTimeOffset.Now);
                Save();
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var days = new List<DailySummary>(dayCount);
            for (var i = 0; i < dayCount; i++)
            {
                var date = today.AddDays(-i);
                days.Add(BuildSummary(date));
            }

            return days;
        }
    }

    public IReadOnlyList<DailySummary> GetLastDaysWithFullOldestWeek(int dayCount)
    {
        lock (_gate)
        {
            if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(DateTimeOffset.Now);
                Save();
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var startDate = GetFullWeekHistoryStart(today, dayCount);
            var days = new List<DailySummary>();
            for (var date = today; date >= startDate; date = date.AddDays(-1))
            {
                days.Add(BuildSummary(date));
            }

            return days;
        }
    }

    public IReadOnlyList<DailySummary> GetRetainedDaysWithFullOldestWeek()
    {
        return GetLastDaysWithFullOldestWeek(RetentionDays);
    }

    public DailySummary GetTodaySummary()
    {
        lock (_gate)
        {
            if (_mode != TrackingMode.Suspended)
            {
                CheckpointCore(DateTimeOffset.Now);
                Save();
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            return BuildSummary(today);
        }
    }

    private DailySummary BuildSummary(DateOnly date)
    {
        var record = ReadRecord(date);
        return new DailySummary(
            date,
            record.FirstLoginTime,
            TimeSpan.FromSeconds(record.LoggedInSeconds + record.LoggedOutSeconds),
            TimeSpan.FromSeconds(record.LoggedInSeconds),
            TimeSpan.FromSeconds(record.AwaySeconds),
            TimeSpan.FromSeconds(record.LoggedOutSeconds),
            TimeSpan.Zero);
    }

    private void CheckpointCore(DateTimeOffset now)
    {
        if (now <= _lastCheckpoint)
        {
            _lastCheckpoint = now;
            _lastInputAt = IdleTime.GetLastInputTime();
            return;
        }

        var currentLastInputAt = IdleTime.GetLastInputTime();
        if (_mode == TrackingMode.LoggedOut)
        {
            AddDuration(_lastCheckpoint, now, MetricKind.LoggedOut);
            if (_data.PendingLogoutStartedAt is not null)
            {
                _data.PendingLogoutStartedAt = now;
            }
        }
        else
        {
            AddDuration(_lastCheckpoint, now, MetricKind.LoggedIn);
            AddAwayDuration(_lastCheckpoint, now, _lastInputAt, currentLastInputAt);
        }

        _lastCheckpoint = now;
        _lastInputAt = currentLastInputAt;
        TrimOldEntries();
    }

    private void CompletePendingLogout(DateTimeOffset now)
    {
        if (_data.PendingLogoutStartedAt is not { } start || now <= start)
        {
            _data.PendingLogoutStartedAt = null;
            return;
        }

        foreach (var runningSpan in ComputerRunningTime.GetRunningSpans(start, now))
        {
            AddDuration(runningSpan.Start, runningSpan.End, MetricKind.LoggedOut);
        }

        _data.PendingLogoutStartedAt = null;
        TrimOldEntries();
    }

    private void AddAwayDuration(
        DateTimeOffset intervalStart,
        DateTimeOffset intervalEnd,
        DateTimeOffset previousLastInputAt,
        DateTimeOffset currentLastInputAt)
    {
        currentLastInputAt = Clamp(currentLastInputAt, intervalStart, intervalEnd);
        previousLastInputAt = previousLastInputAt > currentLastInputAt ? currentLastInputAt : previousLastInputAt;

        if (currentLastInputAt > previousLastInputAt)
        {
            AddAwayPart(intervalStart, intervalEnd, previousLastInputAt + AwayThreshold, currentLastInputAt);
            AddAwayPart(intervalStart, intervalEnd, currentLastInputAt + AwayThreshold, intervalEnd);
            return;
        }

        AddAwayPart(intervalStart, intervalEnd, currentLastInputAt + AwayThreshold, intervalEnd);
    }

    private void AddAwayPart(
        DateTimeOffset intervalStart,
        DateTimeOffset intervalEnd,
        DateTimeOffset awayStart,
        DateTimeOffset awayEnd)
    {
        var start = Max(intervalStart, awayStart);
        var end = Min(intervalEnd, awayEnd);
        if (end > start)
        {
            AddDuration(start, end, MetricKind.Away);
        }
    }

    private void AddDuration(DateTimeOffset start, DateTimeOffset end, MetricKind metric)
    {
        var cursor = start;
        while (DateOnly.FromDateTime(cursor.LocalDateTime) < DateOnly.FromDateTime(end.LocalDateTime))
        {
            var currentDate = DateOnly.FromDateTime(cursor.LocalDateTime);
            var nextMidnight = ToLocalDateTimeOffset(currentDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
            if (nextMidnight <= cursor || nextMidnight >= end)
            {
                break;
            }

            AddSeconds(currentDate, nextMidnight - cursor, metric, cursor);
            cursor = nextMidnight;
        }

        AddSeconds(DateOnly.FromDateTime(cursor.LocalDateTime), end - cursor, metric, cursor);
    }

    private void AddSeconds(DateOnly date, TimeSpan duration, MetricKind metric, DateTimeOffset metricStart)
    {
        var seconds = Math.Max(0, (long)Math.Round(duration.TotalSeconds));
        if (seconds == 0)
        {
            return;
        }

        var record = GetRecord(date);
        if (metric == MetricKind.LoggedIn
            && record.LoggedInSeconds == 0
            && string.IsNullOrWhiteSpace(record.FirstLoginTime))
        {
            record.FirstLoginTime = metricStart.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        switch (metric)
        {
            case MetricKind.LoggedIn:
                record.LoggedInSeconds += seconds;
                break;
            case MetricKind.Away:
                record.AwaySeconds += seconds;
                break;
            case MetricKind.LoggedOut:
                record.LoggedOutSeconds += seconds;
                break;
        }
    }

    private DayRecord GetRecord(DateOnly date)
    {
        var key = ToKey(date);
        if (!_data.Days.TryGetValue(key, out var record))
        {
            record = new DayRecord();
            _data.Days[key] = record;
        }

        return record;
    }

    private DayRecord ReadRecord(DateOnly date)
    {
        return _data.Days.TryGetValue(ToKey(date), out var record) ? record : new DayRecord();
    }

    private void TrimOldEntries()
    {
        var cutoff = GetFullWeekHistoryStart(DateOnly.FromDateTime(DateTime.Today), RetentionDays);
        foreach (var key in _data.Days.Keys.ToArray())
        {
            if (DateOnly.TryParseExact(key, "yyyy-MM-dd", out var date) && date < cutoff)
            {
                _data.Days.Remove(key);
            }
        }

        foreach (var key in _data.WeeklyCorrectionSeconds.Keys.ToArray())
        {
            if (DateOnly.TryParseExact(key, "yyyy-MM-dd", out var date) && date < cutoff)
            {
                _data.WeeklyCorrectionSeconds.Remove(key);
            }
        }
    }

    private TrackerData Load()
    {
        Directory.CreateDirectory(AppDataDirectory);
        if (TryLoadData(_dataFilePath, out var data))
        {
            return data;
        }

        if (File.Exists(_dataFilePath))
        {
            var corruptPath = Path.Combine(
                AppDataDirectory,
                $"work-log.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_dataFilePath, corruptPath, overwrite: true);
        }

        if (TryLoadData(GetBackupPath(), out var backupData))
        {
            return backupData;
        }

        var legacyDataPath = Path.Combine(_legacyAppDataDirectory, DataFileName);
        if (TryLoadData(legacyDataPath, out var legacyData))
        {
            return legacyData;
        }

        if (TryLoadData(legacyDataPath + ".bak", out var legacyBackupData))
        {
            return legacyBackupData;
        }

        return new TrackerData();
    }

    private void MigrateOldData()
    {
        EnsureSettings();
        MigrateDailyCorrectionsToWeekly();

        if (_data.DailySeconds.Count == 0)
        {
            return;
        }

        foreach (var entry in _data.DailySeconds)
        {
            if (!_data.Days.TryGetValue(entry.Key, out var record))
            {
                record = new DayRecord();
                _data.Days[entry.Key] = record;
            }

            if (record.LoggedInSeconds == 0)
            {
                record.LoggedInSeconds = entry.Value;
            }
        }

        _data.DailySeconds.Clear();
    }

    private void MigrateDailyCorrectionsToWeekly()
    {
        foreach (var entry in _data.Days)
        {
            if (!DateOnly.TryParseExact(entry.Key, "yyyy-MM-dd", out var date) || entry.Value.CorrectionSeconds == 0)
            {
                continue;
            }

            var weekKey = ToKey(GetWeekStart(date));
            _data.WeeklyCorrectionSeconds.TryGetValue(weekKey, out var current);
            _data.WeeklyCorrectionSeconds[weekKey] = current + entry.Value.CorrectionSeconds;
            entry.Value.CorrectionSeconds = 0;
        }
    }

    private void EnsureSettings()
    {
        if (_data.StandardWeeklyHours <= 0)
        {
            _data.StandardWeeklyHours = 37.5;
        }

        if (_data.OvertimeTolerancePercent < 0)
        {
            _data.OvertimeTolerancePercent = 10;
        }

        if (_data.HourlyRate <= 0)
        {
            _data.HourlyRate = 20;
        }

        _data.CurrencyCode = NormalizeCurrencyCode(_data.CurrencyCode);
        var backupFolderPath = NormalizeBackupFolderPath(_data.BackupFolderPath);
        _data.BackupFolderPath = IsLegacyDefaultBackupFolder(backupFolderPath)
            ? GetDefaultBackupFolderPath()
            : backupFolderPath;
        _data.BackupInterval = NormalizeBackupInterval(_data.BackupInterval);
    }

    private void Save()
    {
        Directory.CreateDirectory(AppDataDirectory);
        var json = JsonSerializer.Serialize(_data, JsonOptions);
        var tempPath = _dataFilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_dataFilePath))
        {
            ReplaceDataFile(tempPath);
        }
        else
        {
            File.Move(tempPath, _dataFilePath);
        }

        TryUpdateConfiguredBackup();
    }

    private void ReplaceDataFile(string tempPath)
    {
        var backupPath = GetBackupPath();
        try
        {
            File.Replace(tempPath, _dataFilePath, backupPath, ignoreMetadataErrors: true);
            return;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            File.Copy(_dataFilePath, backupPath, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        File.Move(tempPath, _dataFilePath, overwrite: true);
    }

    private void BackupCurrentDataBeforeRestore()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var restoreBackupPath = Path.Combine(
                AppDataDirectory,
                $"work-log-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(restoreBackupPath, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch
        {
            // Restore should still be possible if this safety copy cannot be written.
        }
    }

    private static string ToKey(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    private static string NormalizeCurrencyCode(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return "EUR";
        }

        var normalized = currencyCode.Trim().ToUpperInvariant();
        return normalized.Length > 8 ? normalized[..8] : normalized;
    }

    private string NormalizeBackupFolderPath(string? backupFolderPath)
    {
        if (string.IsNullOrWhiteSpace(backupFolderPath))
        {
            return GetDefaultBackupFolderPath();
        }

        return Environment.ExpandEnvironmentVariables(backupFolderPath.Trim());
    }

    private string GetDefaultBackupFolderPath()
    {
        return Path.Combine(AppDataDirectory, "Backups");
    }

    private bool IsLegacyDefaultBackupFolder(string backupFolderPath)
    {
        return PathsEqual(backupFolderPath, Path.Combine(_legacyAppDataDirectory, "Backups"));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeBackupInterval(string? backupInterval)
    {
        return string.Equals(backupInterval, WeeklyBackupInterval, StringComparison.OrdinalIgnoreCase)
            ? WeeklyBackupInterval
            : DailyBackupInterval;
    }

    private void TryUpdateConfiguredBackup()
    {
        try
        {
            var backupFolder = NormalizeBackupFolderPath(_data.BackupFolderPath);
            var backupInterval = NormalizeBackupInterval(_data.BackupInterval);
            Directory.CreateDirectory(backupFolder);

            var backupPath = Path.Combine(backupFolder, BackupFileName);
            if (!IsBackupDue(backupPath, backupInterval, DateTime.Now))
            {
                return;
            }

            File.Copy(_dataFilePath, backupPath, overwrite: true);
        }
        catch
        {
            // Backup failures must not interrupt tracking or corrupt the primary data file.
        }
    }

    private static bool IsBackupDue(string backupPath, string backupInterval, DateTime now)
    {
        if (!File.Exists(backupPath))
        {
            return true;
        }

        var lastBackupDate = File.GetLastWriteTime(backupPath).Date;
        if (backupInterval == WeeklyBackupInterval)
        {
            return lastBackupDate < GetWeekStart(DateOnly.FromDateTime(now)).ToDateTime(TimeOnly.MinValue);
        }

        return lastBackupDate < now.Date;
    }

    private static DateOnly GetFullWeekHistoryStart(DateOnly today, int dayCount)
    {
        var oldestDailyDate = today.AddDays(-(dayCount - 1));
        var offset = ((int)oldestDailyDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return oldestDailyDate.AddDays(-offset);
    }

    private string GetBackupPath()
    {
        return _dataFilePath + ".bak";
    }

    private static bool TryLoadData(string path, out TrackerData data)
    {
        data = new TrackerData();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            data = JsonSerializer.Deserialize<TrackerData>(json) ?? new TrackerData();
            return true;
        }
        catch
        {
            data = new TrackerData();
            return false;
        }
    }

    private static DateTimeOffset ToLocalDateTimeOffset(DateTime localDateTime)
    {
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }

    private long ReadWeeklyCorrectionSeconds(DateOnly weekStart)
    {
        return _data.WeeklyCorrectionSeconds.TryGetValue(ToKey(GetWeekStart(weekStart)), out var seconds) ? seconds : 0;
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }

    private static DateTimeOffset Clamp(DateTimeOffset value, DateTimeOffset min, DateTimeOffset max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    private enum MetricKind
    {
        LoggedIn,
        Away,
        LoggedOut
    }

    private enum TrackingMode
    {
        LoggedIn,
        LoggedOut,
        Suspended
    }

    private sealed class TrackerData
    {
        public Dictionary<string, DayRecord> Days { get; set; } = new();

        public Dictionary<string, long> WeeklyCorrectionSeconds { get; set; } = new();

        public Dictionary<string, long> DailySeconds { get; set; } = new();

        public DateTimeOffset? PendingLogoutStartedAt { get; set; }

        public double StandardWeeklyHours { get; set; } = 37.5;

        public double OvertimeTolerancePercent { get; set; } = 10;

        public double HourlyRate { get; set; } = 20;

        public string CurrencyCode { get; set; } = "EUR";

        public string BackupFolderPath { get; set; } = string.Empty;

        public string BackupInterval { get; set; } = DailyBackupInterval;
    }

    private sealed class DayRecord
    {
        public long LoggedInSeconds { get; set; }

        public long AwaySeconds { get; set; }

        public long LoggedOutSeconds { get; set; }

        public long CorrectionSeconds { get; set; }

        public string? FirstLoginTime { get; set; }
    }
}
