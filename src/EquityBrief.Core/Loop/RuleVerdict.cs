namespace EquityBrief.Core.Loop;

// What a rule's newest monthly per-rule test reads as beside its paper account and its trade log: passed, within luck
// where the gate did not clear the bar, the first screen it failed in the tester's own order, not testable with the
// reading it lacks, or not tested yet where no test holds it. Its record since registration is labelled apart, too few
// until the count screen's own minimum is met, and the two are never merged into one figure.
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public static class RuleVerdict
{
    public const string Passed = "passed the test";

    public const string WithinLuck = "within luck";

    public const string NotTested = "not tested yet";

    // Beside the live or provisional rule's verdict: its setting was chosen on the years the test reads.
    public const string Chosen = "chosen on these years, so it reads high against its variants";

    // In place of a verdict on the operator's own account.
    public const string Yours = "your own trades, which the test does not read";

    public const string BlockFloor = "block floor";

    public const string StabilityScreen = "stability screen";

    public const string TrimmedScreen = "trimmed screen";

    public const string Count = "count";

    public static string Failed(string screen) => "failed the test's " + screen;

    public static string NotTestable(string why) => "not testable: " + why;

    // The verdict a stored test row reads as: a rule with a reason is not testable; below the block floor no p-value
    // was read; then the gate, the stability screen, the trimmed screen and the count, in the tester's own order.
    public static string Of(bool passed, string? why, double? adjusted, bool? gate, bool? stable, double? trimmed, bool? counts) =>
        why is { } lacking ? NotTestable(lacking)
        : passed ? Passed
        : adjusted is null ? Failed(BlockFloor)
        : gate != true ? WithinLuck
        : stable != true ? Failed(StabilityScreen)
        : !(trimmed > 0) ? Failed(TrimmedScreen)
        : counts != true ? Failed(Count)
        : Passed;

    // Where a verdict stands in the paper accounts' table: passed first, within luck next, a failed screen, not
    // testable, not tested yet, and the operator's own account last.
    public static int Rank(string verdict) =>
        verdict == Passed ? 0
        : verdict == WithinLuck ? 1
        : verdict.StartsWith(Failed(string.Empty), StringComparison.Ordinal) ? 2
        : verdict.StartsWith(NotTestable(string.Empty), StringComparison.Ordinal) ? 3
        : verdict == NotTested ? 4
        : 5;

    // A record since registration's label until the count screen's own minimum is met, trades ended for a swing rule and
    // months for a book; none once it is met.
    public static string? TooFew(bool book, int trades, int months) =>
        book
            ? months < LoopScreens.BookMonths ? FormattableString.Invariant($"too few months ({months} of {LoopScreens.BookMonths})") : null
            : trades < LoopScreens.SwingTrades ? FormattableString.Invariant($"too few trades ({trades} of {LoopScreens.SwingTrades})") : null;

    // The record since registration's count in one line: a swing rule's trades and those decided against the test's count
    // of trades, a book's holdings and its months against the test's count of months, and your own trades and those
    // decided alone.
    public static string CountLine(bool book, int trades, int decided, int months, bool yours) =>
        yours
            ? FormattableString.Invariant($"{trades} {(trades == 1 ? "trade" : "trades")} · {decided} decided")
            : book
                ? FormattableString.Invariant($"{trades} {(trades == 1 ? "holding" : "holdings")} · {months} of {LoopScreens.BookMonths} months")
                : FormattableString.Invariant($"{trades} {(trades == 1 ? "trade" : "trades")} · {decided} decided of {LoopScreens.SwingTrades}");

    // The whole calendar months from a record's first session to the newest, which a book's label counts.
    public static int MonthsBetween(DateOnly from, DateOnly to) =>
        Math.Max(0, ((to.Year * 12) + to.Month) - ((from.Year * 12) + from.Month) - (to.Day < from.Day ? 1 : 0));
}
