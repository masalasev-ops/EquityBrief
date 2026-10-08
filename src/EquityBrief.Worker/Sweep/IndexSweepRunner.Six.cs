using System.Collections.Concurrent;
using System.Net;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

public sealed partial class IndexSweepRunner
{
    // The sector heavyweights' six settings a design, pre-registered before their run: design (a) and design (b).
    public const string HeavyweightsSix = "heavyweights-six";

    public const string FollowersSix = "heavyweights-b-six";

    // The heavyweights' six settings of one design on the index alone, read as the ruling registered them and only
    // those: design (a) over the members as the index's own book reads them, a leader's beta against the index's fund;
    // design (b) over the S&P 500's industries as they stood, their leads against SPY; each holding's result in per cent
    // after its round trip, its benchmark paying none, with each year's edge, the floors and what luck passes of six.
    // see: The S&P 400's and 600's sector heavyweights are searched over six settings a design registered before the run
    // see: A trade's cost comes off the trade and not its benchmark
    async Task<int> HeavyweightsSixAsync(string indexCode, string named, string words, bool designA, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, CancellationToken cancellation)
    {
        var calendar = inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var fund = designA ? (await history.SeriesOfAsync([IndexFunds[indexCode]], through, cancellation)).FirstOrDefault() : null;
        var (tape, sessions) = HeavyweightSweep.Lay(inputs, companies, fund, months.ToHashSet(), first);
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);
        var every = new ConcurrentDictionary<(int From, int To), double?>();

        string? SectorOf(int name) => companies.Companies.GetValueOrDefault(series[name].Name.Ticker).Sector;

        bool ClearsOn(int name, int session, IndexQuality quality) =>
            Array.BinarySearch(series[name].SessionAt, session) is var bar && bar >= 0
            && Clears(indexCode, series[name], bar, income.GetValueOrDefault(series[name].Name.Ticker) ?? [], quality, 1m, SectorOf(name));

        double? EveryOnce(int from, int to) => every.GetOrAdd((from, to), span => HeavyweightSweep.EveryMember(tape, span.From, span.To));

        Func<int, int, IReadOnlyList<string>> leading = (_, _) => [];
        Func<int, string?> industryOf = _ => null;
        var industriesFiled = 0;

        if (!designA)
        {
            var industries = await history.IndustriesAsync(cancellation);
            var spy = (await history.SeriesOfAsync(["SPY"], through, cancellation)).FirstOrDefault();

            output.WriteLine("reading the S&P 500 as it stood for its industries' leads");

            var large = await history.ReadAsync(through, null, cancellation);
            var industryReturns = new SweepIndustries(large, companies, industries);
            var spyCloses = (spy?.Closes ?? []).ToDictionary(pair => pair.Session, pair => pair.Close);
            var leads = new ConcurrentDictionary<(int From, int To), IReadOnlyList<string>>();

            // The industries whose S&P 500 members' value-weighted return over the sessions leads SPY's, the strongest
            // first, none where SPY holds no close at either end.
            leading = (from, to) => leads.GetOrAdd((from, to), span =>
            {
                var (start, day) = (calendar[span.From], calendar[span.To]);

                if (!spyCloses.TryGetValue(day, out var now) || !spyCloses.TryGetValue(start, out var then) || then <= 0)
                {
                    return [];
                }

                var market = (now / then) - 1.0;

                return [.. industryReturns.Between(start, day).Select(pair => (Industry: pair.Key, Lead: pair.Value - market)).Where(pair => pair.Lead > 0).OrderByDescending(pair => pair.Lead).ThenBy(pair => pair.Industry, StringComparer.Ordinal).Select(pair => pair.Industry)];
            });
            industryOf = name => industries.GetValueOrDefault(series[name].Name.Ticker);
            industriesFiled = industries.Count;
        }

        var six = designA ? HeavyweightSix.DesignA : HeavyweightSix.DesignB;
        var figures = new (HeavyweightFigures Before, HeavyweightFigures After, HeavyweightFigures Doubled)[six.Count];

        output.WriteLine(FormattableString.Invariant($"walking the {six.Count} pre-registered setting(s) of design ({(designA ? DesignA : DesignB)}) over {months.Count} monthly rebalances, {HeavyweightSix.Quarters(calendar, months).Count} of them a quarter's first"));

        Parallel.For(0, six.Count, at =>
        {
            var setting = six[at];
            var rebalances = HeavyweightSix.RebalancesOf(setting, calendar, months).ToDictionary(
                session => session,
                session => designA
                    ? HeavyweightSix.ReadA(tape, sessions[session], setting, names, ClearsOn)
                    : HeavyweightSix.ReadB(tape, session, setting, leading, industryOf, SectorOf, ClearsOn));
            var trades = HeavyweightSweep.Walk(tape, rebalances, designA ? setting.Leaders!.Exit : HeavyweightExit.Drop, EveryOnce);

            figures[at] = (
                HeavyweightSweep.Figures(setting.Key, trades),
                HeavyweightSweep.Figures(setting.Key, [.. trades.Select(trade => Costed(series, companies, trade, 1))]),
                HeavyweightSweep.Figures(setting.Key, [.. trades.Select(trade => Costed(series, companies, trade, TradeCost.Doubled))]));
        });

        var passing = figures.Count(one => one.After.MeetsFloors);
        var design = designA ? DesignA : DesignB;
        var note = FormattableString.Invariant($"{Membership(named, inputs)} ") + (designA
            ? FormattableString.Invariant($"Each sector's return is its members' mean and a leader's beta is read against {fund?.Series ?? "no fund"}. Edges are in points of the buy, after each holding's round trip at the published table, its size cut paying none.")
            : FormattableString.Invariant($"An industry's lead is its S&P 500 members' return over the sessions, each weighted by its value at their start, the S&P 500 read as it stood, less SPY's; {industriesFiled:N0} companies carry a GICS industry. Edges are in points of the buy against the equal-weighted members of the holding's sector in the index, after each holding's round trip at the published table, the benchmark paying none."));

        static string Points(double? value) => value is { } one ? FormattableString.Invariant($"{one * 100:+0.00;-0.00}") : "none";

        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " heavyweights design (" + design + "), six settings</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)} design ({design}), its six pre-registered settings</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"registered\">Read as the ruling of 2026-10-08 registered them before the run, and only these: each of the second to the fifth moves one thing from the first, and the sixth moves the second's, the third's and the fourth's together.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"luck\" data-meeting=\"{passing}\" data-luck=\"{HeavyweightSix.Luck:0.00}\">{passing} of {six.Count} settings meet the floors, at least {FamilySweep.TradeFloor} holdings and an edge after costs above nothing in at least {FamilySweep.YearsBeating} of the 8 years, where luck alone passes about {HeavyweightSix.Luck:0.00} of six with no effect at all.</p>"));
        page.Append("<div class=\"table\"><table class=\"six\"><thead><tr><th>Setting</th><th>Holdings</th><th>Edge before costs, points</th><th>After</th><th>At double</th><th>Years above nothing after</th><th>Without the five largest</th><th>Error</th><th>Held, median sessions</th><th>Meets the floors</th></tr></thead><tbody>");

        foreach (var (before, after, doubled) in figures)
        {
            page.Append(FormattableString.Invariant($"<tr data-key=\"{WebUtility.HtmlEncode(after.Key)}\" data-holdings=\"{after.Trades}\" data-edge=\"{Points(after.Edge)}\"><td>{WebUtility.HtmlEncode(after.Key)}</td><td class=\"num\">{after.Trades:N0}</td><td class=\"num\">{Points(before.Edge)}</td><td class=\"num\">{Points(after.Edge)}</td><td class=\"num\">{Points(doubled.Edge)}</td><td class=\"num\">{after.YearsBeating} of 8</td><td class=\"num\">{Points(after.EdgeWithoutLargest)}</td><td class=\"num\">{Points(after.StandardError)}</td><td class=\"num\">{(after.HeldMedian is { } held ? held.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "none")}</td><td>{(after.MeetsFloors ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div><h3>Each year after costs</h3><p>The edge in points over the holdings ended in each year, their count beside it.</p><div class=\"table\"><table class=\"years\"><thead><tr><th>Setting</th>");

        for (var year = 0; year < SweepFigures.Years; year++)
        {
            page.Append(FormattableString.Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        page.Append("</tr></thead><tbody>");

        foreach (var (_, after, _) in figures)
        {
            page.Append($"<tr><td>{WebUtility.HtmlEncode(after.Key)}</td>");

            for (var year = 0; year < SweepFigures.Years; year++)
            {
                page.Append(FormattableString.Invariant($"<td class=\"num\">{Points(after.YearEdge[year])} ({after.YearTrades[year]:N0})</td>"));
            }

            page.Append("</tr>");
        }

        page.Append("</tbody></table></div><h3>Against the first</h3><p>The six are a list registered in advance and not a grid, so no setting has a depth along a dial; each move is read against the first, its edge after costs and its years above nothing.</p><ul class=\"against\">");

        for (var at = 1; at < figures.Length; at++)
        {
            var change = figures[at].After.Edge is { } mine && figures[0].After.Edge is { } base0 ? Points(mine - base0) : "none";

            page.Append(FormattableString.Invariant($"<li>{WebUtility.HtmlEncode(figures[at].After.Key)}: {change} points against the first, {figures[at].After.YearsBeating} of 8 years against {figures[0].After.YearsBeating}.</li>"));
        }

        page.Append("</ul>");

        if (passing == 0)
        {
            var five = SweepNonePassed.StrongestOf(figures.Select(one => new Strongest(one.After.Key, one.After.Trades, one.After.YearsBeating, one.After.Edge)));
            var next = new List<string>(SweepNonePassed.FloorsMissed(five))
            {
                "Any further setting is a new trial: registered in an entry before its run, it adds to the tries the luck figure counts.",
            };

            page.Append(SweepNonePassed.Section(five, next, string.Empty, Points));
        }

        page.Append("</main></body></html>");

        var report = WriteRun(
            folder,
            page.ToString(),
            new
            {
                index = indexCode,
                family = designA ? HeavyweightsSix : FollowersSix,
                design,
                note,
                luck = HeavyweightSix.Luck,
                passing,
                settings = figures.Select(one => new { one.After.Key, one.Before, one.After, one.Doubled }),
            },
            new SweepAnswer(indexCode, HeavyweightRule.Name, design, passing > 0));

        foreach (var (_, after, _) in figures)
        {
            output.WriteLine(FormattableString.Invariant($"{after.Key}: {after.Trades} holdings, edge after costs {Points(after.Edge)} points, {after.YearsBeating} of 8 years{(after.MeetsFloors ? ", meets the floors" : string.Empty)}"));
        }

        output.WriteLine(FormattableString.Invariant($"{passing} of {six.Count} meet the floors, luck alone about {HeavyweightSix.Luck:0.00}"));
        output.WriteLine("report " + report);

        return 0;
    }

    // A holding's result less its round trip at a multiple of the table, its company valued on its buy and its prices
    // read as they traded.
    static HeavyweightTrade Costed(SweepSeries[] series, HeavyweightHistory companies, HeavyweightTrade trade, int multiple)
    {
        if (trade.Result is not { } result || trade.End is not { } end
            || Array.BinarySearch(series[trade.Name].SessionAt, trade.Entry) is var buy && buy < 0
            || Array.BinarySearch(series[trade.Name].SessionAt, end) is var sale && sale < 0)
        {
            return trade;
        }

        var bars = series[trade.Name].Bars;
        var ticker = series[trade.Name].Name.Ticker;
        var bought = bars[buy];
        var sold = bars[sale];
        var entry = bought.RawClose > 0m ? bought.RawClose : bought.Close;
        var exit = sold.RawClose > 0m ? sold.RawClose : sold.Close;
        var value = CompanyValue.On(new SessionClose(bought.Session, bought.Close, entry), companies.Counts.GetValueOrDefault(ticker) ?? [], companies.Splits.GetValueOrDefault(ticker) ?? []);

        return trade with { Result = result - (TradeCost.InPercent(value, entry, exit, multiple) / 100.0) };
    }
}
