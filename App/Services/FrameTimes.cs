namespace VideoNR.Services;

// Source presentation timestamps in Media Foundation's 100 ns time base.
internal sealed class FrameTimes(IEnumerable<long> samples)
{
    private readonly long[] ticks = samples.Distinct().Order().ToArray();

    internal double AtOrBefore(double seconds)
    {
        if (ticks.Length == 0) throw new InvalidOperationException("Nenhum quadro de vídeo encontrado.");
        var i = Array.BinarySearch(ticks, ToTicks(seconds));
        return Seconds(ticks[i >= 0 ? i : Math.Max(0, ~i - 1)]);
    }

    internal double? Adjacent(double seconds, int direction)
    {
        var i = Array.BinarySearch(ticks, ToTicks(seconds));
        i = i >= 0 ? i + direction : direction > 0 ? ~i : ~i - 1;
        return i >= 0 && i < ticks.Length ? Seconds(ticks[i]) : null;
    }

    private static long ToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
    private static double Seconds(long ticks) => (double)ticks / TimeSpan.TicksPerSecond;
}
