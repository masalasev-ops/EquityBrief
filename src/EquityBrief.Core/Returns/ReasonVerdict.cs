namespace EquityBrief.Core.Returns;

// One reason's verdict, or the reason there is none.
//
// `Cleared` is null where nothing is tested, which is not the same as false: a
// reason below its minimum has no verdict rather than a failing one, and a page
// drawing false for both would say a reason had been tested and found wanting
// when it has not been tested at all.
public sealed record Verdict(
    bool? Cleared,
    double? PValue,
    double Threshold,
    int Divisor,
    int Resolved,
    int Sessions,
    string Withheld);

// Whether a reason's wins beat the bar its own setups demanded.
//
// The test is one-sided at 0.05 divided by the family the reason belongs to.
// One-sided because the question is whether a reason beat its own bar and not
// whether it differed from it; divided because running several conditions and
// keeping whichever looks best is the standard way a self-improving system makes
// itself worse while appearing to learn.
// see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
// see: The significance threshold is divided by the family size, and the divisor is shown
public static class ReasonVerdict
{
    // The uncorrected level, decided rather than measured: section 13's own
    // arithmetic is what sets it, where five worthless candidates clear an
    // ordinary test about 23 per cent of the time and twenty clear it about 64.
    // see: The candidate family is at most eight and the threshold is divided by it
    public const double Significance = 0.05;

    // The six live reasons are one family, registered by section 11 before the
    // first listing night. A candidate promoted to live joins them, which
    // restarts the window they are measured over.
    // see: Adding a candidate later restarts the clock
    public const int LiveFamily = 6;

    // Section 17's minimum, and the night floor beside it.
    //
    // Two minimums and not one. A count of rows alone can be filled by a handful
    // of nights of one market move, and the test assumes the setups are
    // independent, which they are not: listings cluster by sector and by date. So
    // the rows have to arrive across at least this many distinct listing
    // sessions, each contributing at least one. Sixty is the quarter of trading
    // the bands look back over and the threshold calibration already uses.
    // see: An unresolved setup is never a win
    public const int MinimumResolved = 250;

    public const int MinimumSessions = 60;

    // The higher floor a live reason's record has to reach before a change may
    // retire it. Nothing at runtime retires a live reason: they are section 11's
    // six and the code's, changed together by a person, and the register refuses
    // a live reason's name in both directions with this floor in its refusal. It
    // is a constant rather than prose so that refusal and section 17's row are
    // held to one figure.
    public const int MinimumBeforeALiveReasonIsRetired = 400;

    public const string BelowTheResolvedMinimum = "resolved";

    public const string BelowTheSessionMinimum = "sessions";

    public const string Shown = "none";

    // One resolved setup, as the verdict reads it: whether it won, the bar its
    // own plan demanded as a percentage, and the session it was listed on.
    public readonly record struct ScoredSetup(bool Won, double? BreakEven, DateOnly Session);

    public static Verdict For(IReadOnlyList<ScoredSetup> setups, int divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(divisor),
                divisor,
                "A family of none divides the threshold by nothing. A verdict with no divisor is a " +
                "verdict at an uncorrected threshold, which is the correction not being applied.");
        }

        var threshold = Significance / divisor;

        // Only the setups that set a bar. A share tested against a bar has to be
        // the share of the rows that bar was averaged over, and a setup that
        // entered and stopped on one session set none.
        // see: A condition is judged against the break-even its own plan demands
        var scored = setups.Where(setup => setup.BreakEven is not null).ToArray();
        var sessions = scored.Select(setup => setup.Session).Distinct().Count();

        // Both minimums, and the one that is short is named rather than the
        // verdict simply being absent: a reader who cannot tell which floor is
        // not met cannot tell how long they are waiting for.
        if (scored.Length < MinimumResolved)
        {
            return new Verdict(null, null, threshold, divisor, scored.Length, sessions, BelowTheResolvedMinimum);
        }

        if (sessions < MinimumSessions)
        {
            return new Verdict(null, null, threshold, divisor, scored.Length, sessions, BelowTheSessionMinimum);
        }

        var wins = scored.Count(setup => setup.Won);
        var probabilities = scored.Select(setup => setup.BreakEven!.Value / 100).ToArray();
        var p = PoissonBinomial.UpperTail(probabilities, wins);

        return new Verdict(p < threshold, p, threshold, divisor, scored.Length, sessions, Shown);
    }
}
