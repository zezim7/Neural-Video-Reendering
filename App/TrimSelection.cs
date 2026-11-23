using System.Globalization;

namespace VideoNR;

internal readonly record struct TrimSelection(double Start, double End)
{
    // The worker uses zero for an unbounded end. Keep that sentinel for full-length exports.
    internal double EndAt(double duration) => End == 0 ? duration : End;

    internal TrimSelection MoveEdge(bool startEdge, double position, double duration)
    {
        if (duration <= 0 || !double.IsFinite(position)) return this;
        var minimum = Math.Min(.001, duration);
        if (startEdge)
            return this with { Start = Math.Clamp(position, 0, Math.Max(0, EndAt(duration) - minimum)) };
        var end = Math.Clamp(position, Math.Min(Start + minimum, duration), duration);
        return this with { End = duration - end <= .0005 ? 0 : end };
    }

    internal static string Format(double seconds, bool milliseconds = false)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var hours = (long)time.TotalHours;
        var baseTime = FormattableString.Invariant($"{hours:00}:{time.Minutes:00}:{time.Seconds:00}");
        return milliseconds ? FormattableString.Invariant($"{baseTime}.{time.Milliseconds:000}") : baseTime;
    }

    internal static bool TryParse(string text, out double seconds)
    {
        seconds = 0;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3 ||
            !double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var tail) ||
            !double.IsFinite(tail) || (parts.Length > 1 && tail >= 60))
            return false;
        var result = tail;
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var part) ||
                (parts.Length == 3 && i == 1 && part >= 60))
                return false;
            result += part * (i == parts.Length - 2 ? 60d : 3600d);
        }
        seconds = result;
        return true;
    }
}
