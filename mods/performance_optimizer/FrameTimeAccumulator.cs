namespace SneakOut.PerformanceOptimizer;

internal sealed class FrameTimeAccumulator
{
    private const int BucketCount = 1001;
    private readonly int[] _histogram = new int[BucketCount];
    private int _frames;
    private int _overBudgetFrames;
    private int _severeStutters;
    private double _sumMilliseconds;
    private double _maxMilliseconds;

    public void Record(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0d)
        {
            return;
        }

        var bucket = Math.Clamp((int)Math.Round(milliseconds), 0, BucketCount - 1);
        _histogram[bucket]++;
        _frames++;
        _sumMilliseconds += milliseconds;
        _maxMilliseconds = Math.Max(_maxMilliseconds, milliseconds);
        if (milliseconds > 33.333d)
        {
            _overBudgetFrames++;
        }

        if (milliseconds > 100d)
        {
            _severeStutters++;
        }
    }

    public FrameTimeSnapshot SnapshotAndReset()
    {
        var snapshot = new FrameTimeSnapshot(
            _frames,
            _frames == 0 ? 0d : 1000d * _frames / _sumMilliseconds,
            Percentile(0.50d),
            Percentile(0.95d),
            Percentile(0.99d),
            _maxMilliseconds,
            _overBudgetFrames,
            _severeStutters);
        Array.Clear(_histogram);
        _frames = 0;
        _overBudgetFrames = 0;
        _severeStutters = 0;
        _sumMilliseconds = 0d;
        _maxMilliseconds = 0d;
        return snapshot;
    }

    private double Percentile(double percentile)
    {
        if (_frames == 0)
        {
            return 0d;
        }

        var target = Math.Max(1, (int)Math.Ceiling(_frames * percentile));
        var seen = 0;
        for (var index = 0; index < _histogram.Length; index++)
        {
            seen += _histogram[index];
            if (seen >= target)
            {
                return index;
            }
        }

        return BucketCount - 1;
    }
}

internal readonly record struct FrameTimeSnapshot(
    int Frames,
    double AverageFps,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaxMilliseconds,
    int OverBudgetFrames,
    int SevereStutters);
