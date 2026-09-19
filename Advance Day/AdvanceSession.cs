using System;

// Read-only clock observer. No game state or day counters are written here.
internal enum AdvanceResult { Running, Complete, Stalled, ClockChanged }
internal sealed class AdvanceSession
{
    internal const float Morning = 0.15f;
    internal int TargetDay { get; private set; }
    private readonly double start;
    private readonly double target;
    private double last;
    private float lastProgressAt;
    internal int ProgressPercent { get { return (int)Math.Max(0, Math.Min(100, 100 * (last - start) / (target - start))); } }

    internal AdvanceSession(int day, float timeK, float now)
    {
        TargetDay = checked(day + 1);
        start = last = day + (double)timeK;
        target = TargetDay + (double)Morning;
        lastProgressAt = now;
    }

    internal AdvanceResult Observe(int day, float timeK, float now)
    {
        double current = day + (double)timeK;
        // The capped game Update advances <=0.1 simulated second. A large jump
        // indicates a competing clock-changing mod or a scripted time change.
        if (double.IsNaN(current) || double.IsInfinity(current) || timeK < 0f || timeK > 1f ||
            current < last - 0.00001 || current - last > 0.01)
            return AdvanceResult.ClockChanged;
        if (current > last + 0.0000001)
        {
            lastProgressAt = now;
            last = current;
        }
        if (current >= target) return AdvanceResult.Complete;
        if (now - lastProgressAt >= 15f) return AdvanceResult.Stalled;
        return AdvanceResult.Running;
    }
}
