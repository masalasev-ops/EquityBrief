using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Candidates;

// A change in code that changed what a rule's trades do, which starts a revision of the rule as a change in its settings
// does: the evaluator its registrations run on, the first session it ran, what it changed in a sentence, and the exit the
// code placed from then as its fingerprint. An entry with no first session starts no revision: it records the exit the
// rule's registrations stand on. The newest entry of an evaluator holds the exit the code places now, and a check fails
// where the code's exit differs from it, so a change to an exit lands with the revision it starts.
// see: Any change that changes what a rule's trades do starts a new revision, in its settings or in its code
public sealed record CodeRevision(string Evaluator, DateOnly? From, string Changed, string Exit);

public static class CodeRevisions
{
    public const string BooksOwnExit = "Gains a stop and a take-profit of its own, the exit its long test chose, beside how it sold before";

    static readonly DateOnly SecondRestart = new(2026, 11, 16);

    // The plan every price family's registration states at the second restart, walked by the code as it stands since: the
    // exit those registrations stand on, which starts no revision of its own.
    const string PlanAtTheRestart = "The plan its registration states, walked as the second restart walks it";

    const string PlanExit = "1db1e261446c14fa";

    // Every code revision and every exit the registrations stand on, by evaluator.
    public static IReadOnlyList<CodeRevision> All { get; } =
    [
        new("swing-filter", null, PlanAtTheRestart, PlanExit),
        new("breakout", null, PlanAtTheRestart, PlanExit),
        new("drift", null, PlanAtTheRestart, PlanExit),
        new("dip-500", null, PlanAtTheRestart, PlanExit),
        new("pullback-400", null, PlanAtTheRestart, PlanExit),
        new("breakout-400", null, PlanAtTheRestart, PlanExit),
        new("drift-400", null, PlanAtTheRestart, PlanExit),
        new("dip-400", null, PlanAtTheRestart, PlanExit),
        new("pullback-600", null, PlanAtTheRestart, PlanExit),
        new("breakout-600", null, PlanAtTheRestart, PlanExit),
        new("drift-600", null, PlanAtTheRestart, PlanExit),
        new("dip-600", null, PlanAtTheRestart, PlanExit),
        new("heavyweight", SecondRestart, BooksOwnExit, "5da804c4d7b41916"),
        new("heavyweight-400", SecondRestart, BooksOwnExit, "50fcc6ea367d4950"),
        new("heavyweight-600", SecondRestart, BooksOwnExit, "97bd1af9becef122"),
        new("quality-500", SecondRestart, BooksOwnExit, "72b7abf00b6bc66d"),
        new("quality-400", SecondRestart, BooksOwnExit, "08449f867777f5d9"),
        new("quality-600", SecondRestart, BooksOwnExit, "86aefd95b831891b"),
        new("improving-500", SecondRestart, BooksOwnExit, "7593ab119b1760d8"),
        new("improving-400", SecondRestart, BooksOwnExit, "50fcc6ea367d4950"),
        new("improving-600", SecondRestart, BooksOwnExit, "7593ab119b1760d8"),
    ];

    // The code revisions of an evaluator that start a revision, in the order they ran.
    public static IReadOnlyList<CodeRevision> Of(string evaluator) =>
        [.. All.Where(entry => entry.Evaluator == evaluator && entry.From is not null).OrderBy(entry => entry.From)];

    // The book an evaluator's rule keeps, by family and index, none for a price family's.
    public static (string Family, string Index)? BookOf(string evaluator) => evaluator switch
    {
        "heavyweight" => (Families.HeavyweightRule.Name, "GSPC"),
        "heavyweight-400" => (Families.HeavyweightRule.Name, "MID"),
        "heavyweight-600" => (Families.HeavyweightRule.Name, "SML"),
        "quality-500" => (Families.QualityRule.Name, "GSPC"),
        "quality-400" => (Families.QualityRule.Name, "MID"),
        "quality-600" => (Families.QualityRule.Name, "SML"),
        "improving-500" => (Families.ImprovingRule.Name, "GSPC"),
        "improving-400" => (Families.ImprovingRule.Name, "MID"),
        "improving-600" => (Families.ImprovingRule.Name, "SML"),
        _ => null,
    };

    // The exit the code places now for an evaluator's rule, as its fingerprint: a book's exit and the stop and take-profit it
    // places on fixed prices, or the plan's take-profit and walk on fixed prices every price family trades by.
    public static string ExitNow(string evaluator) => Fingerprint(BookOf(evaluator) is { } book ? BookWords(book.Family, book.Index) : PlanWords());

    static readonly IReadOnlyList<TurnedDown> Turned = [new(103, new DateOnly(2026, 1, 2)), new(112, new DateOnly(2026, 1, 5)), new(125, new DateOnly(2026, 1, 6))];

    static string BookWords(string family, string index)
    {
        var exit = BookExits.Of(family, index);
        var placed = TakeProfit.ForBook(100, 2, Turned, exit);

        return Invariant($"{exit.Moves}|{exit.Multiple}|{exit.Placing}|{exit.Half}|{placed?.Stop}|{placed?.TakeProfit?.Price}|{placed?.TakeProfit?.Source}");
    }

    static string PlanWords()
    {
        var words = new StringBuilder();

        foreach (var placing in new[] { TakeProfitPlacing.Turned, TakeProfitPlacing.Risk, TakeProfitPlacing.Beyond })
        {
            foreach (var half in new[] { false, true })
            {
                foreach (var sale in new[] { new PlanSale(null, null, []), new PlanSale(108, null, []), new PlanSale(null, null, [104, 109]) })
                {
                    var shaped = TakeProfit.Shaped(sale, 100, 96, Turned, 2, placing, half);

                    words.Append(Invariant($"{placing}|{half}|{shaped.Target}|{shaped.HalfAt}|{string.Join(",", shaped.Staged)};"));
                }
            }
        }

        double[] opens = [100, 99.5, 98.2, 97.9, 99, 101, 103, 105, 107, 104, 102, 109, 111, 108, 106, 104];
        double[] lows = [99, 98.5, 97.5, 97.0, 98.4, 100, 102, 104, 106, 103, 101, 108, 110, 107, 105, 103];
        double[] closes = [100, 98.8, 98.0, 98.6, 100.5, 102.5, 104.5, 106.5, 105, 103, 108, 110.5, 109, 107, 105, 104];

        foreach (var (placing, half) in new[] { (TakeProfitPlacing.Risk, false), (TakeProfitPlacing.Beyond, false), (TakeProfitPlacing.Turned, true) })
        {
            var order = new PlanOrder("fingerprint", PlanPoints.DistanceEntry(1), 98, 5, PlanPoints.TypicalStop(4), 95, null, 1, null, [], null, null, 10)
            {
                TakeProfit = 2,
                TurnedDown = Turned,
                Placing = placing,
                Half = half,
            };
            var walked = PlanHook.Walk(order, opens, lows, closes, 0);

            words.Append(Invariant($"{placing}|{half}|{walked.Waiting}|{walked.FillBar}|{walked.Fill}|{walked.Stop}|{walked.Outcome};"));
        }

        return words.ToString();
    }

    static string Fingerprint(string words) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(words)))[..16].ToLowerInvariant();

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
