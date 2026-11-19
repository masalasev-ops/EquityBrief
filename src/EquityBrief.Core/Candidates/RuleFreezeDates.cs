namespace EquityBrief.Core.Candidates;

// On the operator's ruling of 2026-11-14 the rules the second restart sets are frozen from 15 Nov 2026 until the monthly run of
// 6 Feb 2027: the page says so beside a live version's first session, and from their rulings of 2026-11-15 `register` refuses to
// restart a live rule's record inside it. One place for both dates, which the read surface and the worker read alike.
public static class RuleFreezeDates
{
    public static DateOnly From { get; } = new(2026, 11, 15);

    public static DateOnly Until { get; } = new(2027, 2, 6);

    // Whether a day falls inside the freeze: from its first day and before the monthly run that ends it.
    public static bool Holds(DateOnly day) => day >= From && day < Until;

    // Why an approval or a restore inside the freeze is refused, in the apply step's answer and on the Monthly check-up.
    // see: An approval inside the rule freeze is refused and replaces no live rule
    public static string ApprovalRefused =>
        "the rules are frozen until the monthly run of " + Until.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
        + ", so an approval changes no rule before it";
}
