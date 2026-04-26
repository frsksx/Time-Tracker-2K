using System.Diagnostics.Eventing.Reader;

namespace TimeTracker2K;

internal static class ComputerRunningTime
{
    private static readonly HashSet<int> InactiveStartEventIds = [13, 42, 6006];
    private static readonly HashSet<int> InactiveEndEventIds = [1, 12, 107, 6005];

    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> GetRunningSpans(
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (end <= start)
        {
            return [];
        }

        try
        {
            var inactiveSpans = ReadInactiveSpans(start, end);
            return SubtractInactiveSpans(start, end, inactiveSpans);
        }
        catch
        {
            return [(start, end)];
        }
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> ReadInactiveSpans(
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var events = new List<(DateTimeOffset Time, int Id)>();
        const string queryText = "*[System[(EventID=1 or EventID=12 or EventID=13 or EventID=42 or EventID=107 or EventID=6005 or EventID=6006)]]";
        var query = new EventLogQuery("System", PathType.LogName, queryText)
        {
            ReverseDirection = false
        };

        using var reader = new EventLogReader(query);
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (record.TimeCreated is not { } timeCreated)
                {
                    continue;
                }

                var time = new DateTimeOffset(timeCreated);
                if (time < start || time > end)
                {
                    continue;
                }

                events.Add((time, record.Id));
            }
        }

        events.Sort((left, right) => left.Time.CompareTo(right.Time));

        var spans = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        DateTimeOffset? inactiveStart = null;
        foreach (var entry in events)
        {
            if (InactiveStartEventIds.Contains(entry.Id))
            {
                inactiveStart ??= entry.Time;
            }
            else if (inactiveStart is { } currentStart && InactiveEndEventIds.Contains(entry.Id))
            {
                if (entry.Time > currentStart)
                {
                    spans.Add((currentStart, entry.Time));
                }

                inactiveStart = null;
            }
        }

        if (inactiveStart is { } openStart && end > openStart)
        {
            spans.Add((openStart, end));
        }

        return spans;
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> SubtractInactiveSpans(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> inactiveSpans)
    {
        var runningSpans = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var cursor = start;

        foreach (var inactive in inactiveSpans)
        {
            var inactiveStart = Max(inactive.Start, start);
            var inactiveEnd = Min(inactive.End, end);
            if (inactiveEnd <= inactiveStart)
            {
                continue;
            }

            if (inactiveStart > cursor)
            {
                runningSpans.Add((cursor, inactiveStart));
            }

            if (inactiveEnd > cursor)
            {
                cursor = inactiveEnd;
            }
        }

        if (cursor < end)
        {
            runningSpans.Add((cursor, end));
        }

        return runningSpans;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }
}
