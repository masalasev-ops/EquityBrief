using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// A setup family's sweep on the S&P 400 or the S&P 600 alone, by hand: the history read once from the live store,
// read-only, over the index's members today, each read as a member on every session, so every figure holds survivors
// only; the index's own strength, market check and benchmark; a listing kept where its close clears $5, its mean dollar
// volume over 50 sessions clears the index's floor and its four newest quarters filed before the session sum above
// nothing; and every trade's result read after its cost at the published table's value, the edge before costs and at
// double the cost beside it. The proposal reads the edge after costs. It writes nothing to the store.
// see: Each index runs every family as rules of its own, ranked and benchmarked on that index's members alone
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public sealed class IndexSweepRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-index";

    public const string FiguresFile = "figures.json";

    // The indices a sweep of their own is read for, and the name each is called by.
    public static IReadOnlyDictionary<string, string> Indices { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MID"] = "S&P 400",
        ["SML"] = "S&P 600",
    };

    // The families a sweep of an index is built for so far, and the words its report names each by.
    public static IReadOnlyDictionary<string, string> Families { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BreakoutRule.Name] = "breakouts'",
        [DriftRule.Name] = "earnings drift's",
    };

    public async Task<int> RunAsync(string indexCode, string family, CancellationToken cancellation = default)
    {
        if (!Indices.TryGetValue(indexCode, out var named))
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", Indices.Keys)}");

            return 2;
        }

        if (!Families.TryGetValue(family, out var words))
        {
            output.WriteLine($"{Verb}: name a family with '--family', one of {string.Join(", ", Families.Keys)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, FamilySweepRunner.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: indexCode);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {named}; pull them first with 'history-pull --members --index {indexCode}', then their history");

            return 2;
        }

        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = await history.IncomeAsync(through, cancellation);
        var folder = Path.Combine(SweepFolder.Resolve(configuredFolder, dataRoot), SweepFolder.RunName(started));

        Directory.CreateDirectory(folder);
        output.WriteLine("run " + Path.GetFileName(folder));

        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var tickers = inputs.Names.Select(name => name.Ticker).ToArray();
        var open = Enumerable.Range(firstScored, nights).Count(session => sessions[session].Breadth >= FamilySweep.MarketFloor);
        var withIncome = tickers.Count(ticker => income.ContainsKey(ticker));

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        var adapter = FamilySweepRunner.For(family, series, sessions, members, firstScored, calendar);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();
        var before = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);
        var doubled = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);
        var (listed, kept) = (0L, 0L);

        FamilyTrade After(FamilyTrade trade, int multiple) =>
            trade.Result is { } result ? trade with { Result = result - CostInRisk(series[trade.Listing.Name], trade.Listing, result, companies, multiple) } : trade;

        foreach (var setting in adapter.Grid.Settings)
        {
            var listings = adapter.Listings(setting).ToArray();
            var clearing = listings.Where(listing => Clears(indexCode, series[listing.Name], listing.Bar, income.GetValueOrDefault(tickers[listing.Name]) ?? [])).ToArray();
            var trades = FamilySweep.Walk(clearing, tickers, YearOf, adapter.Exit, adapter.Benchmark);
            var key = adapter.Grid.Key(setting);

            listed += listings.Length;
            kept += clearing.Length;
            before[key] = FamilySweep.Figures(key, trades, nights);
            read.Add((setting, FamilySweep.Figures(key, [.. trades.Select(trade => After(trade, 1))], nights)));
            doubled[key] = FamilySweep.Figures(key, [.. trades.Select(trade => After(trade, TradeCost.Doubled))], nights);
        }

        var proposal = FamilySweep.Propose(adapter.Grid, read);
        var note = FormattableString.Invariant(
            $"Survivors only: the {named}'s {inputs.Names.Count:N0} members today, each read as a member on every session, which flatters the index, its strength, market check and benchmark read among them alone. A listing is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing; {kept:N0} of the {listed:N0} listings every setting made together cleared them, and {withIncome:N0} of the {inputs.Names.Count:N0} members hold quarters of income. Every edge on this page is after each trade's cost at the published table's value, and the table below sets the edge before costs and at double the cost beside it.");
        var run = new FamilySweepRun(family, $"{named} {words}", calendar[firstScored], through, inputs.Names.Count, nights, open, adapter.Readings, started, clock.UtcNow, note);
        var provisional = adapter.Grid.Key([.. adapter.Grid.Provisional]);
        var shown = new List<string> { provisional };

        if (proposal.Proposed is { } proposed)
        {
            shown.Add(proposed.Key);
        }
        else
        {
            shown.AddRange(SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge))).Select(one => one.Key));
        }

        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, FamilySweepReport.Build(run, adapter.Grid, read, proposal) + Costs([.. shown.Distinct(StringComparer.Ordinal)], before, read.ToDictionary(one => one.Figures.Key, one => one.Figures, StringComparer.Ordinal), doubled));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                index = indexCode,
                run,
                listed,
                kept,
                proposal = proposal.Proposed?.Key,
                afterCosts = read.Select(one => one.Figures),
                beforeCosts = before.Values,
                atDoubleCost = doubled.Values,
            },
            SweepRunner.Json));

        output.WriteLine(proposal.Proposed is { } shownProposal
            ? FormattableString.Invariant($"proposed {shownProposal.Key}, edge after costs {FamilySweepReport.Number(shownProposal.Edge)} over {shownProposal.Trades} trades, before costs {FamilySweepReport.Number(before[shownProposal.Key].Edge)}, at double {FamilySweepReport.Number(doubled[shownProposal.Key].Edge)}")
            : "none passed: no setting meets the floors after costs, and the report states the strongest settings and what could be tried next");
        output.WriteLine("report " + report);

        return 0;
    }

    // Whether a listing clears the index's floors and the profit gate on its session: the close as it traded, the mean
    // dollar volume over the 50 bars to it on the adjusted close and the provider's split-adjusted volume, and the quarters
    // filed before it.
    public static bool Clears(string indexCode, SweepSeries series, int bar, IReadOnlyList<FiledIncome> income)
    {
        var bars = series.Bars;
        var held = bars[bar];
        var traded = held.RawClose > 0m ? held.RawClose : held.Close;
        var window = bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(one => (one.Close, one.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window))
            && MemberReadings.Profit(income, held.Session);
    }

    // A kept trade's round trip in multiples of its risk at a multiple of the table, its company valued on the listing's
    // session and its prices read as they traded for their bands, the sale at the price its result puts it.
    public static double CostInRisk(SweepSeries series, FamilyListing listing, double result, HeavyweightHistory companies, int multiple)
    {
        var held = series.Bars[listing.Bar];
        var ticker = series.Name.Ticker;
        var traded = held.RawClose > 0m ? held.RawClose : held.Close;
        var value = CompanyValue.On(new SessionClose(held.Session, held.Close, traded), companies.Counts.GetValueOrDefault(ticker) ?? [], companies.Splits.GetValueOrDefault(ticker) ?? []);
        var factor = held.Close > 0m ? Statistic.FromRatio(traded / held.Close) : 1.0;
        var sale = listing.Entry + (result * (listing.Entry - listing.Stop));

        return TradeCost.InRisk(
            value,
            Statistic.ToPrice(listing.Entry * factor),
            Statistic.ToPrice(listing.Stop * factor),
            Statistic.ToPrice(Math.Max(sale * factor, 0.01)),
            multiple);
    }

    // The provisional setting's and the proposal's or the strongest settings' edges before costs, at the table's cost and
    // at double.
    static string Costs(IReadOnlyList<string> keys, IReadOnlyDictionary<string, FamilyFigures> before, IReadOnlyDictionary<string, FamilyFigures> after, IReadOnlyDictionary<string, FamilyFigures> doubled)
    {
        var html = new StringBuilder("<h3>The cost of a trade</h3><p>Each setting's edge before costs, after each trade's cost at the published table's value, and at double the cost, the first row the provisional setting.</p><div class=\"table\"><table class=\"costs\"><thead><tr><th>Setting</th><th>Trades</th><th>Edge before costs</th><th>After costs</th><th>At double the cost</th><th>Years above nothing after costs</th></tr></thead><tbody>");

        foreach (var key in keys)
        {
            html.Append(FormattableString.Invariant($"<tr data-key=\"{WebUtility.HtmlEncode(key)}\"><td>{WebUtility.HtmlEncode(key)}</td><td class=\"num\">{after[key].Trades:N0}</td><td class=\"num\">{FamilySweepReport.Number(before[key].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(after[key].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(doubled[key].Edge)}</td><td class=\"num\">{after[key].YearsBeating} of 8</td></tr>"));
        }

        return html.Append("</tbody></table></div>").ToString();
    }
}
