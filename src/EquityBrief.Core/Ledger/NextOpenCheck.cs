namespace EquityBrief.Core.Ledger;

// Where the session after a setup's listing opened against its plan. A family's plan carries no entry zone of its own,
// so its zone is its whole range, the stop to the target, and an open at or under the stop, or at or over the target, is
// past it and places no trade there; a plan whose stop trails has no target to open past.
// see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
public enum NextOpenGap
{
    Inside,
    UnderStop,
    OverTarget,
    NoOpen,
}

// One settled setup priced both ways: at the close of the night that listed it, as the ledger prices it, and at the
// next session's open, each with its result and, where its benchmark is settled, its edge.
public sealed record NextOpenPricing(string Family, int Year, NextOpenGap Gap, double? ResultAtClose, double? EdgeAtClose, double? ResultAtOpen, double? EdgeAtOpen);

// One family's settled setups on an index over a year, or over every year where the year is none: the results and the
// edges both ways, each mean over the setups priced both ways so the difference is paired, and how often the next open
// fell past the plan's range on either side or was not held.
public sealed record NextOpenGroup(
    string Family,
    int? Year,
    int Settled,
    int Priced,
    double? ResultAtClose,
    double? ResultAtOpen,
    int EdgesPriced,
    double? EdgeAtClose,
    double? EdgeAtOpen,
    int UnderStop,
    int OverTarget,
    int NoOpen)
{
    public double? ResultDifference => ResultAtOpen - ResultAtClose;

    public double? EdgeDifference => EdgeAtOpen - EdgeAtClose;
}

// The read-only check of what the ledger's results come to when each setup is bought at the next session's open rather
// than at the close of the night that listed it, which the night computes after that close. The stop, the target, the
// trail and the cap stay where the plan put them in prices, and the path is read over the same closes from the session
// after the listing onward, so the cap's close is the same session either way and only the buy moves. Nothing here
// writes anything.
public static class NextOpenCheck
{
    public static NextOpenGap Gap(SetupAnchor anchor, double? open)
    {
        if (open is not { } price || !(price > 0))
        {
            return NextOpenGap.NoOpen;
        }

        if (price <= anchor.Stop)
        {
            return NextOpenGap.UnderStop;
        }

        return !anchor.Trails && price >= anchor.Target ? NextOpenGap.OverTarget : NextOpenGap.Inside;
    }

    public static SetupAnchor AtOpen(SetupAnchor anchor, double open) => anchor with { Entry = open };

    // The setup's path bought at the open, none where the open falls outside the plan's range or was not held. Its result
    // is stated in the risk the plan put up at the listing's close, since the position is sized the night before from
    // that close and its stop: the exit is the same close either way, so a buy dearer than the close lowers the result by
    // the difference over that risk, and an open a cent over the stop does not swell it as a risk of a cent would.
    public static SetupOutcome? Replay(ReadOnlySpan<double> closes, int from, SetupAnchor anchor, double? open)
    {
        if (Gap(anchor, open) != NextOpenGap.Inside)
        {
            return null;
        }

        var outcome = SetupReplay.Replay(closes, from, AtOpen(anchor, open!.Value));

        return outcome.Result is { } result ? outcome with { Result = result * (open.Value - anchor.Stop) / anchor.Risk } : outcome;
    }

    // The same plan entered on every member of the session at its own next open, as the benchmark at the close enters it
    // at each member's close: a member whose next open falls outside its range is not entered, one with no next session
    // held is entered and left out where its prices stopped, and open otherwise.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    public static BenchmarkReading Benchmark(IReadOnlyList<(double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)> members, DateOnly session, SetupPlan plan)
    {
        var sum = 0.0;
        var ended = 0;
        var entered = 0;
        var stopped = 0;

        foreach (var (closes, opens, bar, move, pricesStopped) in members)
        {
            if (bar < 0 || bar >= closes.Length || plan.On(session, closes[bar], move) is not { } anchor)
            {
                continue;
            }

            if (bar + 1 >= closes.Length)
            {
                entered++;
                stopped += pricesStopped ? 1 : 0;

                continue;
            }

            double? open = bar + 1 < opens.Length ? opens[bar + 1] : null;

            if (Replay(closes, bar, anchor, open) is not { } outcome)
            {
                continue;
            }

            entered++;

            if (outcome.Result is { } result)
            {
                sum += result;
                ended++;
            }
            else if (pricesStopped)
            {
                stopped++;
            }
        }

        return new BenchmarkReading(ended > 0 ? sum / ended : null, ended, entered, stopped);
    }

    // A heavyweights' setup bought at the next open and sold at the same close its path ended on, a fraction of the sum
    // the listing's close put to it, which the position is sized from the night before.
    public static double? HeavyweightResult(ReadOnlySpan<double> closes, int from, int sessions, double? open) =>
        open is { } price && price > 0 && sessions > 0 && from + sessions < closes.Length && closes[from] > 0 ? (closes[from + sessions] - price) / closes[from] : null;

    // The size cut's return over the same sessions, each member bought at its own next open.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    public static BenchmarkReading HeavyweightCut(IReadOnlyList<(double[] Closes, double[] Opens, int From, bool Stopped)> members, int sessions)
    {
        var sum = 0.0;
        var ended = 0;
        var entered = 0;
        var left = 0;

        foreach (var (closes, opens, from, stopped) in members)
        {
            if (from < 0 || from >= closes.Length || !(closes[from] > 0))
            {
                continue;
            }

            if (from + sessions >= closes.Length)
            {
                entered++;
                left += stopped ? 1 : 0;

                continue;
            }

            if (HeavyweightResult(closes, from, sessions, from + 1 < opens.Length ? opens[from + 1] : null) is not { } result)
            {
                continue;
            }

            entered++;
            sum += result;
            ended++;
        }

        return new BenchmarkReading(ended > 0 ? sum / ended : null, ended, entered, left);
    }

    // Each family's figures over every year and then year by year, the families in the order given and any other after
    // them in the order first met.
    public static IReadOnlyList<NextOpenGroup> Groups(IEnumerable<NextOpenPricing> setups, IReadOnlyList<string>? order = null)
    {
        var held = setups.ToArray();
        var families = (order ?? []).Where(family => held.Any(setup => setup.Family == family))
            .Concat(held.Select(setup => setup.Family))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var groups = new List<NextOpenGroup>();

        foreach (var family in families)
        {
            var rows = held.Where(setup => setup.Family == family).ToArray();

            groups.Add(Group(family, null, rows));
            groups.AddRange(rows.GroupBy(setup => setup.Year).OrderBy(year => year.Key).Select(year => Group(family, year.Key, [.. year])));
        }

        return groups;
    }

    static NextOpenGroup Group(string family, int? year, IReadOnlyList<NextOpenPricing> rows)
    {
        var priced = rows.Where(row => row.ResultAtClose is not null && row.ResultAtOpen is not null).ToArray();
        var edged = rows.Where(row => row.EdgeAtClose is not null && row.EdgeAtOpen is not null).ToArray();

        return new NextOpenGroup(
            family,
            year,
            rows.Count,
            priced.Length,
            priced.Length > 0 ? priced.Average(row => row.ResultAtClose!.Value) : null,
            priced.Length > 0 ? priced.Average(row => row.ResultAtOpen!.Value) : null,
            edged.Length,
            edged.Length > 0 ? edged.Average(row => row.EdgeAtClose!.Value) : null,
            edged.Length > 0 ? edged.Average(row => row.EdgeAtOpen!.Value) : null,
            rows.Count(row => row.Gap == NextOpenGap.UnderStop),
            rows.Count(row => row.Gap == NextOpenGap.OverTarget),
            rows.Count(row => row.Gap == NextOpenGap.NoOpen));
    }
}
