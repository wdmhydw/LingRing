namespace GongSiOrganizer.RadialPrototype.Presentation;

internal enum MoveFeedbackOutcome { Success, Partial, Failure }

internal readonly record struct MoveFeedbackFrame(
    double Collapse, double Badge, double Opacity, double SpinnerAngle, bool Pending, bool Finished);

// Wall-clock state: fast moves skip the waiting indicator, and only a real result produces a badge.
internal sealed class MoveFeedbackTimeline
{
    internal const double CollapseMilliseconds = 180;
    internal const double PendingDelayMilliseconds = 280;
    private double? _completedAt;
    public MoveFeedbackOutcome? Outcome { get; private set; }
    public int Moved { get; private set; }
    public int Total { get; private set; }

    public void Complete(MoveFeedbackOutcome outcome, int moved, int total, double elapsed)
    {
        if (Outcome is not null) return;
        Outcome = outcome;
        Total = Math.Max(1, total);
        Moved = Math.Clamp(moved, 0, Total);
        _completedAt = elapsed;
    }

    public MoveFeedbackFrame At(double elapsed, bool animate)
    {
        var collapse = animate ? Math.Clamp(elapsed / CollapseMilliseconds, 0, 1) : 1;
        var pending = Outcome is null && elapsed >= PendingDelayMilliseconds;
        var startsAt = _completedAt is { } completed
            ? Math.Max(completed, animate ? CollapseMilliseconds : 0)
            : PendingDelayMilliseconds;
        var badge = elapsed < startsAt ? 0 : animate ? Math.Clamp((elapsed - startsAt) / 150, 0, 1) : 1;
        var hold = Outcome == MoveFeedbackOutcome.Success ? 700 : 1200;
        var fade = _completedAt is null ? 0 : Math.Clamp((elapsed - startsAt - hold) / (animate ? 130 : 1), 0, 1);
        return new(collapse, badge, 1 - fade, animate && pending ? elapsed * .3 % 360 : -90, pending, fade >= 1);
    }
}
