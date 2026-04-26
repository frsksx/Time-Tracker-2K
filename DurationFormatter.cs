namespace TimeTracker2K;

internal static class DurationFormatter
{
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return duration.TotalSeconds < 1 ? "0m" : $"{Math.Max(1, duration.Seconds)}s";
        }

        var totalMinutes = (long)Math.Floor(duration.TotalMinutes);
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            return $"{days}d {hours}h {minutes}m";
        }

        if (hours > 0)
        {
            return $"{hours}h {minutes}m";
        }

        return $"{minutes}m";
    }
}
