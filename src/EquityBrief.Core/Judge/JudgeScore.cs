namespace EquityBrief.Core.Judge;

// One group of where the AI judge looked: its name, the picks in it, those closed and what they made per $1 risked.
public sealed record JudgeLooked(string Group, int Picks, int Closed, double? PerDollar);

// The scoreboard of the AI judge's live record: its trades closed, those that made money, what they made per $1 risked,
// how much more than the same plans bought on every stock those nights, and how many closed trades a firm verdict waits for.
public sealed record JudgeScoreboard(int Closed, int Winners, double? PerDollar, double? OverSamePlan, int Needed);

// The AI judge's page figures, worked by code from its walked trades: the scoreboard, whether its account beats random
// picks so far against their band, and where it looked against the rules' picks of the same nights. A replayed night's
// trades are read apart and never counted in the live record.
// see: The AI judge's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the judge waits for that run
// owes: The AI judge's live record read at 300 closed picks
public static class JudgeScore
{
    public const int Firm = 300;

    public const string Better = "Better";

    public const string Worse = "Worse";

    public const string NotToldApart = "Not told apart yet";

    public const string NoSetupFound = "Picks no setup found";

    public const string Shared = "Picks it shared with the rules";

    public const string PassedOver = "Rule picks it passed over";

    // The scoreboard over a record's results in its risk, each closed one counted and an open one left out, with each closed
    // pick's result over the same plan on every stock where read.
    public static JudgeScoreboard Of(IReadOnlyList<(double? Result, double? OverSamePlan)> record)
    {
        var closed = record.Where(one => one.Result is not null).ToArray();
        var over = closed.Select(one => one.OverSamePlan).OfType<double>().ToArray();

        return new JudgeScoreboard(
            closed.Length,
            closed.Count(one => one.Result > 0),
            closed.Length == 0 ? null : closed.Average(one => one.Result!.Value),
            over.Length == 0 ? null : over.Average(),
            Firm);
    }

    // Whether the judge's account beats random picks so far: better above the band's high, worse under its low, and not
    // told apart inside it or where either is not read.
    public static string AgainstRandom(decimal? judge, JudgeBand? band) =>
        judge is not { } value || band is null ? NotToldApart
        : value > band.High ? Better
        : value < band.Low ? Worse
        : NotToldApart;

    // Where the judge looked, against the rules' picks of the same nights, a pick matched by its stock and the session it
    // was bought on: its picks no rule found, those it shared with a rule, and the rules' picks it passed over, each with
    // the picks in it, those closed and what they made per $1 risked.
    public static IReadOnlyList<JudgeLooked> Looked(
        IReadOnlyList<(string Ticker, DateOnly Bought, double? Result)> judge,
        IReadOnlyList<(string Ticker, DateOnly Bought, double? Result)> rules)
    {
        var ruled = rules.Select(one => (one.Ticker, one.Bought)).ToHashSet();
        var judged = judge.Select(one => (one.Ticker, one.Bought)).ToHashSet();

        static JudgeLooked Group(string name, IReadOnlyList<double?> results)
        {
            var closed = results.OfType<double>().ToArray();

            return new JudgeLooked(name, results.Count, closed.Length, closed.Length == 0 ? null : closed.Average());
        }

        return
        [
            Group(NoSetupFound, [.. judge.Where(one => !ruled.Contains((one.Ticker, one.Bought))).Select(one => one.Result)]),
            Group(Shared, [.. judge.Where(one => ruled.Contains((one.Ticker, one.Bought))).Select(one => one.Result)]),
            Group(PassedOver, [.. rules.Where(one => !judged.Contains((one.Ticker, one.Bought))).Select(one => one.Result)]),
        ];
    }
}
