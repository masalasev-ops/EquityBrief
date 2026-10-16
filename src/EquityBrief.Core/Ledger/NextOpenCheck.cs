namespace EquityBrief.Core.Ledger;

// Where the session after a setup's listing opened against its plan. A family's plan carries no entry zone of its own,
// so its zone is its whole range, the stop to the target, and an open at or under the stop, or at or over the target, is
// past it; a plan whose stop trails has no target to open past. From the ruling of 2026-10-15 a setup whose open is
// past its range is still bought there and walked under the same exits, and the gap is counted.
// see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
public enum NextOpenGap
{
    Inside,
    UnderStop,
    OverTarget,
    NoOpen,
}

// One settled setup priced both ways: at the close of the night that listed it and at the next session's open, each with
// its result and, where its benchmark is settled, its edge.
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

// The read-only check of what the ledger's results come to bought at the next session's open rather than at the close of
// the night that listed it, each priced both ways through the core's one pricing function. Nothing here writes anything.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public static class NextOpenCheck
{
    public static NextOpenGap Gap(SetupAnchor anchor, double? open) =>
        open is not { } price || !(price > 0) ? NextOpenGap.NoOpen
        : TradeFills.PastOf(price, anchor.Stop, anchor.Trails ? null : anchor.Target) switch
        {
            FillPast.Stop => NextOpenGap.UnderStop,
            FillPast.Target => NextOpenGap.OverTarget,
            _ => NextOpenGap.Inside,
        };

    // The setup's path bought at the open: the close's path, its result moved by the gap over the risk the close set,
    // none where the open was not held. A gap past the stop or the target is bought and ends where the plan ends.
    public static SetupOutcome? Replay(ReadOnlySpan<double> closes, int from, SetupAnchor anchor, double? open)
    {
        if (open is not { } price || !(price > 0))
        {
            return null;
        }

        var outcome = SetupReplay.Replay(closes, from, anchor);

        return outcome with { Result = TradeFills.AtOpen(outcome.Result, anchor.Entry, anchor.Risk, price) };
    }

    // The same plan entered on every member of the session and bought at its own next open, the members the close's
    // benchmark entered, each moved by its own gap over its own risk.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    public static BenchmarkReading Benchmark(IReadOnlyList<(double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)> members, DateOnly session, SetupPlan plan)
    {
        var (reading, listed) = SetupBenchmark.WithMembers(members, session, plan);

        return reading with { Mean = TradeFills.Benchmark(reading.Mean, listed).Mean };
    }

    // A heavyweights' setup bought at the next open and sold at the same close its path ended on, a fraction of the sum
    // the listing's close put to it, which the position is sized from the night before.
    public static double? HeavyweightResult(ReadOnlySpan<double> closes, int from, int sessions, double? open) =>
        open is { } price && price > 0 && sessions > 0 && from + sessions < closes.Length && closes[from] > 0
            ? TradeFills.AtOpen((closes[from + sessions] / closes[from]) - 1, closes[from], closes[from], price)
            : null;

    // The size cut's return over the same sessions, each member bought at its own next open.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    public static BenchmarkReading HeavyweightCut(IReadOnlyList<(double[] Closes, double[] Opens, int From, bool Stopped)> members, int sessions)
    {
        var reading = HeavyweightPaths.Cut([.. members.Select(member => (member.Closes, member.From, member.Stopped))], sessions);
        var listed = HeavyweightPaths.CutMembers([.. members.Select(member => (member.Closes, member.Opens, member.From))], sessions);

        return reading with { Mean = TradeFills.Benchmark(reading.Mean, listed).Mean };
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
