using System.Drawing;
using System.Globalization;

namespace CentralWhatsApp.WebView2;

internal enum AgendaCalendarMode { Day, Week, Month }
internal sealed record AgendaCalendarEntry(DateOnly Date, string Time, string Client, string Service,
    string Status, AgendaServiceCategory Category, int Duration = 60, bool AllDay = false,
    string Key = "", DateOnly? ExclusiveEndDate = null)
{
    internal DateTime Start => Date.ToDateTime(TimeOnly.TryParse(Time, out var clock) ? clock : TimeOnly.MinValue);
    internal DateTime End => AllDay || !TimeOnly.TryParse(Time, out _)
        ? (ExclusiveEndDate ?? Date.AddDays(1)).ToDateTime(TimeOnly.MinValue)
        : Start.AddMinutes(Math.Max(1, Duration));
    internal bool IsAllDay => AllDay || !TimeOnly.TryParse(Time, out _);
    internal bool IsPast(DateTime now) => End <= now;
}
internal sealed record AgendaCalendarSpan(DateOnly First, DateOnly Last)
{
    internal bool Contains(DateOnly date) => date >= First && date <= Last;
    internal bool Covers(AgendaCalendarSpan other) => First <= other.First && Last >= other.Last;
}
internal sealed record AgendaTimeSlot(AgendaCalendarEntry Entry, double StartMinute, double EndMinute, int Lane, int Lanes);

internal static class AgendaCalendarLayout
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    internal static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
    internal static AgendaCalendarSpan Range(DateOnly day, AgendaCalendarMode mode)
    {
        var first = mode switch
        {
            AgendaCalendarMode.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            AgendaCalendarMode.Month => new DateOnly(day.Year, day.Month, 1), _ => day
        };
        return new(first, mode switch { AgendaCalendarMode.Week => first.AddDays(6),
            AgendaCalendarMode.Month => first.AddMonths(1).AddDays(-1), _ => first });
    }
    internal static AgendaCalendarSpan MonthEnvelope(DateOnly day)
    {
        var first = Range(new DateOnly(day.Year, day.Month, 1), AgendaCalendarMode.Week).First;
        return new(first, first.AddDays(41));
    }
    internal static Color Fade(Color color, Color surface, bool past)
        => !past ? color : Color.FromArgb((color.R * 38 + surface.R * 62) / 100,
            (color.G * 38 + surface.G * 62) / 100, (color.B * 38 + surface.B * 62) / 100);

    internal static IReadOnlyList<AgendaTimeSlot> Slots(IEnumerable<AgendaCalendarEntry> source, DateOnly day)
    {
        var begin = day.ToDateTime(TimeOnly.MinValue); var end = begin.AddDays(1);
        var items = source.Where(x => !x.IsAllDay && x.Start < end && x.End > begin)
            .Select(x => (Entry: x, Start: Math.Max(0, (x.Start - begin).TotalMinutes),
                End: Math.Min(1440, (x.End - begin).TotalMinutes)))
            .OrderBy(x => x.Start).ThenBy(x => x.End).ThenBy(x => x.Entry.Key, StringComparer.Ordinal).ToArray();
        var result = new List<AgendaTimeSlot>();
        for (var first = 0; first < items.Length;)
        {
            var last = first + 1; var groupEnd = items[first].End;
            while (last < items.Length && items[last].Start < groupEnd)
            { groupEnd = Math.Max(groupEnd, items[last].End); last++; }
            var lanes = new List<double>(); var group = new List<(int Index, int Lane)>();
            for (var i = first; i < last; i++)
            {
                var lane = lanes.FindIndex(value => value <= items[i].Start);
                if (lane < 0) { lane = lanes.Count; lanes.Add(items[i].End); } else lanes[lane] = items[i].End;
                group.Add((i, lane));
            }
            foreach (var (index, lane) in group)
                result.Add(new(items[index].Entry, items[index].Start, items[index].End, lane, lanes.Count));
            first = last;
        }
        return result;
    }
}
