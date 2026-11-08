using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// Each family's starting settings searched again over the long history, by hand and on one index at a time: the breakout's
// and the drift's grids walked over the long reader's history once a setting, each test year from the family's own first
// choosing on what ended before it began with the floors in proportion to its learning years, searched at the first test
// year and every third after with the setting carried between, and judged against the rule's provisional setting as the
// tester judges a proposal. Each family's result is written as it finishes, so a run stopped before the night's window goes
// on from the next family when run again. It registers nothing and retires nothing.
// see: A search over the long history re-runs every third test year with its setting carried between
// see: History reaches a list only through a test on years a proposal never saw and the operator's approval
public sealed class LongSearch(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSearch, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-search";

    // The families whose search walks over the long reader's history: the grids the tester's procedures walk. Every other
    // family's search reads the standing history alone and is named with that reason.
    public static IReadOnlyList<string> Searched { get; } = [BreakoutRule.Name, DriftRule.Name];

    // From the 23.8 correction the dip searched over the long history too, its own sweep's grid at its first stage.
    // see: A search over the long history re-runs every third test year with its setting carried between
    public static IReadOnlyList<string> SearchedOwn { get; } = [DipRule.Name];

    public static IReadOnlyList<string> StandingAlone { get; } = [SetupFamilies.Pullback, HeavyweightRule.Name, QualityRule.Name, ImprovingRule.Name, FundamentalsRule.Name];

    public const string StandingAloneWhy = "its search reads the standing history alone, so it is searched there by the monthly run";

    // What blocks the pullback's search over the long history on an index, and what would unblock it.
    public static string PullbackBlocked(string index) => index == IndexFamilies.LargeIndex
        ? "the S&P 500's pullback is the swing filter, changed through the shape moves proposed for its open version and walked over no grid a search chooses from, so no setting of it can be searched; nothing 23.10 brings unblocks it, which wants the filter's own settings laid out as a grid and searched as a procedure, a checkpoint of its own"
        : "its search, the pullback's two-stage design search, keeps the sweeps' eight scored years a stock's year each, so a fold learning on more than eight would fold its earlier years into its last; nothing 23.10 brings unblocks it, which wants the design search's tally laid out over the long history's years, a checkpoint of its own";

    const string Written = "SELECT family FROM long_search WHERE index_code = $index AND through = $through;";

    const string InsertSearch = @"
        INSERT INTO long_search (run_id, index_code, family, through, first_test_year, words, current_words, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, stable_folds, searches, passed, years, fund_units, fund_edge, current_fund_edge)
        VALUES ($run_id, $index_code, $family, $through, $first_test_year, $words, $current_words, $units, $blocks, $adjusted, $gate, $stable, $counted, $better, $trimmed, $counts, $stable_folds, $searches, $passed, $years, $fund_units, $fund_edge, $current_fund_edge);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', 1, 0, 0, '0', $detail);";

    // One family's search on an index: its folds, the proposal run on all finished data and its verdict.
    // From the 23.8 correction beside it the second line: each test year's chosen setting's trades and the rule today's
    // against the index's own fund held over each trade's sessions, a trade on average.
    public sealed record SearchRead(string Family, int FirstTestYear, IReadOnlyList<LongFold<int[]>> Folds, int[]? Proposal, string? Words, string Current, LoopEvidence Evidence, LoopVerdict Verdict, int StableFolds, int FundUnits = 0, double? FundEdge = null, double? CurrentFundEdge = null);

    // From the 23.8 correction `again` searches every family afresh where the store holds its search through the same
    // session, a correction's remedy writing a newer row the Loop page reads beside the one it replaces.
    public async Task<int> RunAsync(string index, bool print, CancellationToken cancellation = default, bool again = false)
    {
        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, WalkForwardTester.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var written = print || again ? [] : await WrittenAsync(index, through, cancellation);

        foreach (var family in StandingAlone)
        {
            output.WriteLine($"{Verb}: the {family} on the {DecisionCards.NameOf(index)} is not searched over the long history: {(family == SetupFamilies.Pullback ? PullbackBlocked(index) : StandingAloneWhy)}");
        }

        if (Searched.Concat(SearchedOwn).All(written.Contains))
        {
            output.WriteLine($"{Verb}: every family's search on the {DecisionCards.NameOf(index)} through {Text(through)} is written; nothing to do");

            return 0;
        }

        var longHistory = new LongHistoryReader(databaseFile);
        var inputs = await longHistory.ReadAsync(index, through, output.WriteLine, cancellation);
        var (read, income) = await WalkForwardTester.WithIncomeAsync(
            await WalkForwardTester.WithFundAsync(await WalkForwardTester.ReadLongAsync(index, history, through, inputs, cancellation), longHistory, through, cancellation),
            longHistory,
            cancellation);

        output.WriteLine($"{Verb}: {WalkForwardTester.ProfitWords(index, income)}");

        foreach (var family in Searched.Where(family => !written.Contains(family)))
        {
            if (SweepRunner.InTheNightsWindow(clock.UtcNow, WalkForwardTester.Expected))
            {
                output.WriteLine($"{Verb}: stopped before the night's window with the {family} not yet searched; run it again after the night and it goes on from there");

                return 0;
            }

            var started = clock.UtcNow;
            var searched = Search(read, family, LongTest.FirstTestYear(family, index), output.WriteLine);

            output.WriteLine(Line(index, searched));

            if (!print)
            {
                await WriteAsync(index, through, started, searched, cancellation);
            }
        }

        // From the 23.8 correction the dip, over its own sweep's reading of the long history, against its live rule.
        if (!written.Contains(DipRule.Name))
        {
            if (SweepRunner.InTheNightsWindow(clock.UtcNow, WalkForwardTester.Expected))
            {
                output.WriteLine($"{Verb}: stopped before the night's window with the {DipRule.Name} not yet searched; run it again after the night and it goes on from there");

                return 0;
            }

            var started = clock.UtcNow;
            var (dip, why) = await WalkForwardTester.LongDipAsync(databaseFile, read, longHistory, history, through, started, output.WriteLine, cancellation);

            if (dip is null)
            {
                output.WriteLine($"{Verb}: the {DipRule.Name} on the {DecisionCards.NameOf(index)} is not searched over the long history: {why}");
            }
            else
            {
                var searched = SearchOwn(read, dip.Read, dip.Setting, dip.Levels, dip.Words, LongTest.FirstTestYear(DipRule.Name, index), output.WriteLine);

                output.WriteLine(Line(index, searched));

                if (!print)
                {
                    await WriteAsync(index, through, started, searched, cancellation);
                }
            }
        }

        if (print)
        {
            output.WriteLine($"{Verb}: printed alone, nothing written");
        }

        return 0;
    }

    // The search as a procedure over a read: each setting walked once over the whole history, each fold reading only what
    // ended before its year through its view, searched or carried as the schedule says, the rule's provisional setting walked
    // beside it, and the two judged on the folds' years as the tester judges a proposal.
    public static SearchRead Search(LoopRead read, string family, int firstTestYear, Action<string> progress)
    {
        var drift = family == DriftRule.Name;
        var adapter = FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar, drift ? LoopProcedures.DriftStopFloor : 0);
        var currentAdapter = drift ? FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar) : adapter;
        int[] provisional = family == BreakoutRule.Name ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen];
        var settings = adapter.Grid.Settings;
        var walked = new IReadOnlyList<LoopProcedures.Walked>[settings.Count];

        progress(FormattableString.Invariant($"{Verb}: walking the {family}'s {settings.Count} setting(s) on the {DecisionCards.NameOf(read.Index)} over the long history"));

        for (var at = 0; at < settings.Count; at++)
        {
            walked[at] = LoopProcedures.Walk(read, adapter, settings[at], ClearOf(read));

            if ((at + 1) % 25 == 0)
            {
                progress(FormattableString.Invariant($"{Verb}: {at + 1} of {settings.Count} setting(s) walked"));
            }
        }

        var current = LoopProcedures.Walk(read, currentAdapter, provisional, ClearOf(read));
        var floorWords = drift ? FormattableString.Invariant($", its stop held at least {LoopProcedures.DriftStopFloor:0.##} typical move under the buy") : string.Empty;

        return SearchOver(read, family, firstTestYear, adapter.Grid, settings, walked, current, setting => RuleReplay.SwingWords(family, setting) + floorWords, RuleReplay.SwingWords(family, provisional));
    }

    // From the 23.8 correction the dip searched over the long history the same way: its own sweep's grid walked once a
    // setting over the long reader's history, its second stage not read, against its live rule's setting and levels.
    // see: A search over the long history re-runs every third test year with its setting carried between
    public static SearchRead SearchOwn(LoopRead read, OwnProcedures.IOwnSwing own, int[] liveSetting, IReadOnlyList<DialLevel<FamilyListing>> liveLevels, string liveWords, int firstTestYear, Action<string> progress)
    {
        var settings = own.Grid.Settings;
        var walked = new IReadOnlyList<LoopProcedures.Walked>[settings.Count];

        static IReadOnlyList<LoopProcedures.Walked> Kept(IReadOnlyList<FamilyTrade> trades, System.Collections.Concurrent.ConcurrentDictionary<(int Name, int Session), int> held) =>
        [
            .. trades.Select(trade => new LoopProcedures.Walked(
                trade.Listing.Session,
                trade.Result is not null && held.TryGetValue((trade.Listing.Name, trade.Listing.Session), out var sessions) ? trade.Listing.Session + sessions : null,
                trade)),
        ];

        progress(FormattableString.Invariant($"{Verb}: walking the {own.Family}'s {settings.Count} setting(s) on the {DecisionCards.NameOf(read.Index)} over the long history"));

        for (var at = 0; at < settings.Count; at++)
        {
            var held = new System.Collections.Concurrent.ConcurrentDictionary<(int Name, int Session), int>();

            walked[at] = Kept(own.TradesUnder(settings[at], [], held), held);

            if ((at + 1) % 25 == 0)
            {
                progress(FormattableString.Invariant($"{Verb}: {at + 1} of {settings.Count} setting(s) walked"));
            }
        }

        var heldNow = new System.Collections.Concurrent.ConcurrentDictionary<(int Name, int Session), int>();
        var current = Kept(own.TradesUnder(liveSetting, liveLevels, heldNow), heldNow);

        return SearchOver(read, own.Family, firstTestYear, own.Grid, settings, walked, current, setting => "the " + own.Family + " rule at " + own.Grid.Key(setting), liveWords);
    }

    // The search's folds and verdict over walks already made: each fold reading only what ended before its year through its
    // view, searched or carried as the schedule says, and the setting walked beside it judged on the folds' years as the
    // tester judges a proposal, with the fund line beside it.
    static SearchRead SearchOver(LoopRead read, string family, int firstTestYear, FamilyGrid grid, IReadOnlyList<int[]> settings, IReadOnlyList<LoopProcedures.Walked>[] walked, IReadOnlyList<LoopProcedures.Walked> current, Func<int[], string> words, string currentWords)
    {
        var folds = LoopFolds.Of(read.Calendar, read.Calendar[^1], firstTestYear);
        var historyYears = read.Calendar[^1].Year - read.Calendar[read.FirstScored].Year + 1;

        int[]? ChooseIn(LoopFold fold)
        {
            var floors = LongSearchRules.Floors(FamilySweep.TradeFloor, FamilySweep.YearsBeating, read.LearningYears(fold), historyYears);

            return LongSearchRules.Choose(
                [
                    .. settings.Select((setting, at) =>
                    {
                        var learning = new FoldView<LoopProcedures.Walked>(fold, walked[at], one => read.Calendar[one.Entry], one => one.Exit is { } exit ? read.Calendar[exit] : null).Learning;
                        var trades = learning.Where(one => one.Edge is not null).Select(one => (read.Calendar[one.Entry].Year, one.Edge!.Value)).ToArray();

                        return (setting, LongSearchRules.FiguresOf(grid.Key(setting), trades), grid.Changes(setting));
                    }),
                ],
                floors);
        }

        var longFolds = LongSearchRules.Folds([.. folds.Select(fold => fold.Year)], firstTestYear, year => ChooseIn(folds.First(fold => fold.Year == year)));
        var proposal = ChooseIn(new LoopFold(read.Calendar[^1].Year + 1, read.Calendar[^1].AddDays(1), read.Calendar[^1].AddDays(1), false));
        var placeOf = settings.Select((setting, place) => (setting, place)).ToDictionary(pair => grid.Key(pair.setting), pair => pair.place, StringComparer.Ordinal);

        static IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<LoopProcedures.Walked> trades) => [.. trades.Select(one => (one.Entry, one.Edge))];

        var evidence = LoopProcedures.Evidence(folds, read.Calendar, [.. longFolds.Select(one => one.Chosen is null ? null : Units(walked[placeOf[grid.Key(one.Chosen)]]))], Units(current));
        var cap = walked.SelectMany(one => one).Concat(current).Select(one => one.Trade.Listing.Cap).DefaultIfEmpty(0).Max();
        var verdict = Verdict(evidence, read.Calendar, folds, cap);

        // The second line: the trades each test year's chosen setting and the rule today entered in that year, against the
        // index's own fund held over each trade's sessions.
        // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
        IEnumerable<double?> FundIn(IReadOnlyList<LoopProcedures.Walked> trades, int year) =>
            trades.Where(one => read.Calendar[one.Entry].Year == year).Select(one => read.FundEdge(one.Entry, one.Exit, one.Trade));

        var (fundUnits, fundEdge) = FundLine.Mean(longFolds.Where(one => one.Chosen is not null).SelectMany(one => FundIn(walked[placeOf[grid.Key(one.Chosen!)]], one.Year)));
        var (_, currentFund) = FundLine.Mean(longFolds.SelectMany(one => FundIn(current, one.Year)));

        return new SearchRead(
            family,
            firstTestYear,
            longFolds,
            proposal,
            proposal is null ? null : words(proposal),
            currentWords,
            evidence,
            verdict,
            proposal is null ? 0 : longFolds.Count(one => one.Chosen is { } setting && setting.Zip(proposal).All(pair => Math.Abs(pair.First - pair.Second) <= 1)),
            fundUnits,
            fundEdge,
            currentFund);
    }

    // A search's verdict, judged as the tester judges a proposal: its blocks from its first fold's first session to the
    // newest, at the cap a block of its trades waits.
    public static LoopVerdict Verdict(LoopEvidence evidence, IReadOnlyList<DateOnly> calendar, IReadOnlyList<LoopFold> folds, int cap) =>
        LoopJudge.Judge([evidence], folds.Count > 0 ? calendar.ToList().IndexOf(folds[0].TestFrom) : calendar.Count - 1, calendar.Count - 1, cap)[0];

    // From the 23.8 correction the clear a listing reads over the long history: on the S&P 500, whose rules read no profit
    // gate, the index's floors alone; on the S&P 400 and 600 the walk's own clear, the floors and the profit gate over the
    // pulled quarters and the SEC's facts from the first year they cover 90 in every 100 members, the floors alone before it.
    // see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
    public static Func<SweepSeries, int, bool>? ClearOf(LoopRead read) => read.Index == IndexFamilies.LargeIndex ? Floors(read.Index) : null;

    // The clear a listing reads over the long years: the index's floors on its session, its close as traded and its dollar
    // volume over the 50 sessions to it, as the long ledger reads them, the profit gate's quarters not being held there.
    public static Func<SweepSeries, int, bool> Floors(string index) => (one, bar) =>
    {
        var day = one.Bars[bar];
        var traded = day.RawClose > 0m ? day.RawClose : day.Close;
        var window = one.Bars[Math.Max(0, bar - Core.Readings.MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(each => (each.Close, each.Volume)).ToArray();

        return Core.Ledger.SetupGates.ClearsTheFloors(index, traded, Core.Readings.MemberReadings.DollarVolume(window));
    };

    static string Line(string index, SearchRead searched) =>
        FormattableString.Invariant(
            $"{Verb}: the {searched.Family} on the {DecisionCards.NameOf(index)} from {searched.FirstTestYear}, {searched.Folds.Count(one => one.Searched)} search(es) over {searched.Folds.Count} test year(s): {(searched.Words is null ? "no setting meets the floors on all finished data" : "would run " + searched.Words)} against {searched.Current}; {searched.Verdict.Units} trade(s), adjusted p {(searched.Verdict.Adjusted is { } p ? p.ToString("0.0000", CultureInfo.InvariantCulture) : "none")}, better in {searched.Verdict.Better} of {searched.Verdict.Counted} counted year(s), {(searched.Verdict.Passes && searched.Words is not null ? "passed, and goes to the operator as a proposal" : "not passed")}; against the index's own fund held over the same sessions, {Shown(searched.FundEdge)} a trade over {searched.FundUnits} against the rule today's {Shown(searched.CurrentFundEdge)}");

    static string Shown(double? figure) => figure is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";

    async Task<HashSet<string>> WrittenAsync(string index, DateOnly through, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = Written;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$through", Text(through));

        var families = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            families.Add(reader.GetString(0));
        }

        return families;
    }

    async Task WriteAsync(string index, DateOnly through, DateTimeOffset started, SearchRead searched, CancellationToken cancellation)
    {
        var runId = FormattableString.Invariant($"{Verb}-{index}-{searched.Family}-{started:yyyyMMddTHHmmssZ}");
        var years = searched.Folds.Zip(searched.Evidence.Years, (fold, year) => new
        {
            year = fold.Year,
            searched = fold.Searched,
            chosen = fold.Chosen is null ? null : RuleReplay.SwingWords(searched.Family, fold.Chosen),
            complete = year.Complete,
            current_units = year.CurrentUnits,
            proposed_units = year.ProposedUnits,
            current_total = year.CurrentTotal,
            proposed_total = year.ProposedTotal,
        });

        using var held = await DrainLock.AcquireAsync(dataRoot, () => Task.Delay(SetupLedger.Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var transaction = connection.BeginTransaction();

        static object Or(object? value) => value ?? DBNull.Value;

        async Task ExecuteAsync(string sql, IReadOnlyList<(string Name, object Value)> parameters)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync(cancellation);
        }

        var verdict = searched.Verdict;

        await ExecuteAsync(InsertSearch,
        [
            ("$run_id", runId),
            ("$index_code", index),
            ("$family", searched.Family),
            ("$through", Text(through)),
            ("$first_test_year", searched.FirstTestYear),
            ("$words", Or(searched.Words)),
            ("$current_words", searched.Current),
            ("$units", verdict.Units),
            ("$blocks", verdict.Blocks),
            ("$adjusted", Or(verdict.Adjusted)),
            ("$gate", verdict.Gate ? 1 : 0),
            ("$stable", verdict.Stable ? 1 : 0),
            ("$counted", verdict.Counted),
            ("$better", verdict.Better),
            ("$trimmed", Or(verdict.Trimmed)),
            ("$counts", verdict.Counts ? 1 : 0),
            ("$stable_folds", searched.StableFolds),
            ("$searches", searched.Folds.Count(one => one.Searched)),
            ("$passed", verdict.Passes && searched.Words is not null ? 1 : 0),
            ("$years", JsonSerializer.Serialize(years)),
            ("$fund_units", searched.FundUnits),
            ("$fund_edge", Or(searched.FundEdge)),
            ("$current_fund_edge", Or(searched.CurrentFundEdge)),
        ]);

        await ExecuteAsync(AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Verb),
            ("$started_at", Instant(started)),
            ("$ended_at", Instant(clock.UtcNow)),
            ("$detail", Line(index, searched)),
        ]);

        await transaction.CommitAsync(cancellation);
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
