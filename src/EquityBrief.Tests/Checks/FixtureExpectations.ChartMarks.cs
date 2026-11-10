using EquityBrief.Core.Bars;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Charts;
using EquityBrief.Worker.Indicators;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 22.38 and 22.40 and the refetch guard, the operator's second answers of 2026-11-04 on the chart audit, each worked by hand over
// constructed closes: an answer holding another stock's prices, the operator's word on a pulled session, and a dividend an answer
// does not carry.
// see: A refetch answer whose day's move the stored closes do not show is refused, and the stock keeps its year and is asked again
// see: From phase 23's restart the sweep's history and every rule's read of the pull read the marks, each stock from its start and past each bad print
// see: A dividend the provider's answer does not yet carry is asked for again on the five nights after and weekly, and the stock's pages and the run page say so while it waits
public partial class FixtureExpectations
{
    static readonly DateOnly Day1 = new(2026, 9, 28);
    static readonly DateOnly Day2 = new(2026, 9, 29);

    static IReadOnlyDictionary<DateOnly, (decimal Close, long Volume)> Held(decimal first, decimal second, long volume = 1000) =>
        new Dictionary<DateOnly, (decimal Close, long Volume)> { [Day1] = (first, 1000), [Day2] = (second, volume) };

    [Theory]
    // Another stock's prices, the store flat: 19.50 to 361 is refused.
    [InlineData(19.50, 19.43, 19.50, 361, true)]
    // Worked by hand at the limit: 100 to 125 is 25%, not over it, and 100 to 126 is over it with the store flat.
    [InlineData(100, 100, 100, 125, false)]
    [InlineData(100, 100, 100, 126, true)]
    // A real move the store shows too stands, 40% in both.
    [InlineData(100, 140, 100, 140, false)]
    // Apart by 25 points stands and apart by more does not: a fall of 30% against one of 5.1% is 24.9 points, against 4.9% is 25.1.
    [InlineData(100, 94.9, 100, 70, false)]
    [InlineData(100, 95.1, 100, 70, true)]
    // A split the store has not applied: the store falls by half and the answer is the smooth side.
    [InlineData(100, 50, 50, 50, false)]
    public void ARefetchAnswerIsHeldToTheStoredClosesOverTheSameTwoSessions(double storedFirst, double storedSecond, double answerFirst, double answerSecond, bool refused)
    {
        var answer = RefetchAnswer.Refused(Held((decimal)storedFirst, (decimal)storedSecond), [(Day1, (decimal)answerFirst), (Day2, (decimal)answerSecond)]);

        Assert.Equal(refused, answer is not null);
    }

    [Fact]
    public void AStoredSessionWithNoVolumeIsNoWitnessAndASessionTheStoreDoesNotHoldIsNotRead()
    {
        Assert.Null(RefetchAnswer.Refused(Held(100m, 100m, volume: 0), [(Day1, 100m), (Day2, 140m)]));
        Assert.Null(RefetchAnswer.Refused(Held(100m, 100m), [(Day1.AddDays(-1), 10m), (Day1, 100m)]));
    }

    [Fact]
    public async Task ARefetchServingAnotherStocksPricesLeavesTheYearAndMarksTheStockAndAMoveTheStoreHoldsReplaces()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 4);

        foreach (var name in new[] { "FALSE", "REAL" })
        {
            Member(store, name);
        }

        Stored(store, "FALSE", sessions.Select(day => Priced(day, 19.5m)));
        Stored(store, "REAL", sessions.Select((day, at) => Priced(day, at == sessions.Count - 1 ? 30m : 20m)));

        var history = new SeriesByTicker(new()
        {
            ["FALSE"] = [.. sessions.Select((day, at) => Priced(day, at == sessions.Count - 1 ? 361m : 19.5m))],
            ["REAL"] = [.. sessions.Select((day, at) => Priced(day, at == sessions.Count - 1 ? 30.5m : 20.5m))],
        });
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RefetchNamesAsync(["FALSE", "REAL"], "refetch-1");

        Assert.Equal(19.5m, CloseOn(store, "FALSE", sessions[^1]));
        Assert.Equal("suspect", StateOfName(store, "FALSE").State);
        Assert.Contains("another stock's prices", StateOfName(store, "FALSE").Reason, StringComparison.Ordinal);
        Assert.Equal(30.5m, CloseOn(store, "REAL", sessions[^1]));
        Assert.Equal(["FALSE"], outcome.Suspect);
    }

    static CheckedSession Marked(int day, decimal close) =>
        new(new DateOnly(2021, 4, day), close, close, close, close, close, 1000, true);

    static SessionMark Mark(int day, string kind) => new(new DateOnly(2021, 4, day), kind, "words");

    [Fact]
    public void ABadPrintIsReadPastAndAMoveCheckedAndKeptIsNoMoveAndAnyOtherStands()
    {
        CheckedSession[] sessions = [Marked(5, 40m), Marked(6, 24m), Marked(7, 41m), Marked(8, 41m), Marked(9, 60m)];

        Assert.Equal(3, ChartCheck.MovesOf(sessions).Count);

        // Worked by hand: past 6 April, 40 to 41 moves 2.5%, and 41 to 60 stands.
        Assert.Equal(new DateOnly(2021, 4, 9), Assert.Single(ChartCheck.MovesOf(sessions, [Mark(6, SessionMarking.BadPrint)])).Session);
        Assert.Equal(2, ChartCheck.MovesOf(sessions, [Mark(9, SessionMarking.CheckedAndKept)]).Count);
        Assert.Empty(ChartCheck.MovesOf(sessions, [Mark(6, SessionMarking.BadPrint), Mark(9, SessionMarking.CheckedAndKept)]));

        // A start says nothing of the moves after it.
        Assert.Equal(3, ChartCheck.MovesOf(sessions, [Mark(5, SessionMarking.StartsHere)]).Count);
    }

    static void PulledRows(TemporaryStore store, string ticker, string pull, params (string Session, decimal Close)[] rows)
    {
        using var connection = store.Open();

        foreach (var (session, close) in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
                VALUES ($ticker, $session, $close, $close, $close, $close, $close, 1000, $pull);";
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session);
            command.Parameters.AddWithValue("$close", close.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$pull", pull);
            command.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task AStartLeavesTheSessionsBeforeItOutOfThePullsReadAndNoPulledRowMoves()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates);

        PulledRows(store, "CUT", "pull-1", ("2020-11-18", 900m), ("2020-11-19", 1m), ("2020-11-20", 30m), ("2020-11-23", 31m));
        PulledRows(store, "OTHER", "pull-1", ("2020-11-18", 900m), ("2020-11-19", 1m));

        var marks = new SessionMarks(clock, store.DatabaseFile);

        Assert.Equal(1, await marks.MarkAsync("CUT", new DateOnly(2020, 11, 20), SessionMarking.StartsHere, "Its history starts here.", "mark-1"));

        // A second word on the same session changes nothing, the first standing.
        Assert.Equal(0, await marks.MarkAsync("CUT", new DateOnly(2020, 11, 20), SessionMarking.BadPrint, "Another word.", "mark-2"));
        await Assert.ThrowsAsync<ArgumentException>(() => marks.MarkAsync("CUT", new DateOnly(2020, 11, 23), "a word no one ruled", "Another word.", "mark-3"));

        using var connection = store.Open();

        IReadOnlyList<string> Read(string ticker)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_date FROM pulled_bar WHERE ticker = $ticker AND " + SessionMarking.FromItsStart + " ORDER BY session_date;";
            command.Parameters.AddWithValue("$ticker", ticker);

            var rows = new List<string>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }

        Assert.Equal(["2020-11-20", "2020-11-23"], Read("CUT"));
        Assert.Equal(["2020-11-18", "2020-11-19"], Read("OTHER"));

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM pulled_bar WHERE ticker = 'CUT';";
        Assert.Equal(4L, (long)count.ExecuteScalar()!);

        using var kept = connection.CreateCommand();
        kept.CommandText = "SELECT kind, says FROM session_mark WHERE ticker = 'CUT';";
        using var row = kept.ExecuteReader();
        Assert.True(row.Read());
        Assert.Equal((SessionMarking.StartsHere, "Its history starts here."), (row.GetString(0), row.GetString(1)));
    }

    [Fact]
    public async Task TheCheckReadsTheOperatorsMarksOverTheStore()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 4);
        var pulled = TradedTo(sessions[0], 6).SkipLast(1).ToArray();
        var clock = FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates);

        void Stock(string ticker, params decimal[] closes)
        {
            Member(store, ticker);
            Stored(store, ticker, sessions.Select(day => Priced(day, 100m)));
            PulledRows(store, ticker, "pull-1", [.. pulled.Select((day, at) => (FormattableString.Invariant($"{day:yyyy-MM-dd}"), closes[at])), (FormattableString.Invariant($"{sessions[0]:yyyy-MM-dd}"), 100m)]);
        }

        // Each falls by over a quarter and comes back on the middle session, 100 to 60 to 100, or starts past a fall.
        Stock("NONE", 100m, 100m, 60m, 100m, 100m);
        Stock("BADP", 100m, 100m, 60m, 100m, 100m);
        Stock("KEPT", 100m, 100m, 100m, 100m, 60m);
        Stock("CUTS", 900m, 900m, 100m, 100m, 100m);

        var marks = new SessionMarks(clock, store.DatabaseFile);

        await marks.MarkAsync("BADP", pulled[2], SessionMarking.BadPrint, "a bad print", "mark-1");
        await marks.MarkAsync("KEPT", pulled[4], SessionMarking.CheckedAndKept, "checked and kept", "mark-2");
        await marks.MarkAsync("CUTS", pulled[2], SessionMarking.StartsHere, "it starts here", "mark-3");

        await new ChartChecker(clock, store.DatabaseFile).RunAsync("GSPC", "night-1");

        IReadOnlyList<string> Moves(string ticker) => Query(store, $"SELECT session_date FROM chart_check WHERE ticker = '{ticker}' AND flag = 'b' ORDER BY session_date;");

        // The fall and the return unmarked, and on each marked stock neither.
        Assert.Equal(2, Moves("NONE").Count);
        Assert.Empty(Moves("BADP"));

        // KEPT's return to 100 on the stored year's first session stands, its fall on the marked session gone.
        Assert.Equal([FormattableString.Invariant($"{sessions[0]:yyyy-MM-dd}")], Moves("KEPT"));
        Assert.Empty(Moves("CUTS"));

        // A stock a start names says so in its mark's words and no line says it first traded there.
        Assert.Empty(Query(store, "SELECT ticker FROM chart_check WHERE ticker = 'CUTS' AND kind = 'first session';"));
        Assert.Single(Query(store, "SELECT ticker FROM chart_check WHERE ticker = 'NONE' AND kind = 'first session';"));
    }

    [Fact]
    public async Task AStartEndsALongViewAndAnAverageWarmUpReadsNoSessionBeforeIt()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates);
        var sessions = TradedTo(FaultNight, 4);
        var before = TradedTo(sessions[0], 90).SkipLast(1).ToArray();
        var start = before[^25];

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            Member(store, ticker);
            Stored(store, ticker, sessions.Select(day => Priced(day, 100m)));
            PulledRows(store, ticker, "pull-1", [.. before.Select(day => (FormattableString.Invariant($"{day:yyyy-MM-dd}"), 100m)), (FormattableString.Invariant($"{sessions[0]:yyyy-MM-dd}"), 100m)]);
        }

        await new SessionMarks(clock, store.DatabaseFile).MarkAsync("BBB", start, SessionMarking.StartsHere, "it starts here", "mark-1");
        await new ChartAverager(clock, store.DatabaseFile).RunAsync("averages-1");
        await new EquityBrief.Worker.Moves.PageHistoryReader(clock, store.DatabaseFile).RunAsync("history-1");

        IReadOnlyList<string> Filled(string ticker, string name) => Query(store, $"SELECT sessions FROM chart_average WHERE ticker = '{ticker}' AND name = '{name}';");

        // Twenty-four sessions before the year's first and that session give a 20-day average on all four sessions either way, and a
        // 50-day average on none of the marked stock's, where the unmarked one reads 89.
        Assert.Contains(FormattableString.Invariant($"{sessions[0]:yyyy-MM-dd}"), Filled("AAA", "sma50")[0], StringComparison.Ordinal);
        Assert.Equal("[]", Filled("BBB", "sma50")[0]);

        var month = FormattableString.Invariant($"{before[0]:yyyy-MM}");
        var views = PageRows(store, "SELECT ticker, closes FROM long_view ORDER BY ticker;").ToDictionary(row => row[0], row => row[1]);

        Assert.Contains(month, views["AAA"], StringComparison.Ordinal);
        Assert.DoesNotContain(month, views["BBB"], StringComparison.Ordinal);
    }

    [Fact]
    public void ThePullsReadersReadFromAStocksStart()
    {
        foreach (var source in new[]
        {
            "src/EquityBrief.Api/Reading/ReadApi.Chart.cs",
            "src/EquityBrief.Worker/Charts/ChartChecker.cs",
            "src/EquityBrief.Worker/Indicators/ChartAverager.cs",
            "src/EquityBrief.Worker/Moves/PageHistoryReader.cs",
        })
        {
            Assert.Contains("SessionMarking.FromItsStart", File.ReadAllText(Path.Combine(Repository.Root, source)), StringComparison.Ordinal);
        }
    }

    [Theory]
    // Worked by hand. A dividend of 1 on a raw close of 100 should leave a step of 1%. Adjusted 99 to 100 against raw 100 to 100 shows
    // a step of 1%, no gap; adjusted equal to raw shows none, a gap of 1%.
    [InlineData(1, 99, 100, 100, 100, false)]
    [InlineData(1, 100, 100, 100, 100, true)]
    // At the limit: 0.5 on 100 with no step is a gap of 0.5%, which is not carried, and 0.49 is too small to read.
    [InlineData(0.5, 100, 100, 100, 100, true)]
    [InlineData(0.49, 100, 100, 100, 100, false)]
    // Half carried: 2 on 100 with a step of 1.75% is a gap of 0.25%, carried, and with a step of 1% a gap of 1%, not.
    [InlineData(2, 98.28, 100, 100, 100, false)]
    [InlineData(2, 99.01, 100, 100, 100, true)]
    public void ADividendIsNotCarriedAtAGapOfHalfOfOnePercent(double amount, double closeBefore, double rawBefore, double closeAfter, double rawAfter, bool notCarried)
    {
        Assert.Equal(notCarried, DividendCarry.NotCarried(DividendCarry.Gap((decimal)amount, (decimal)closeBefore, (decimal)rawBefore, (decimal)closeAfter, (decimal)rawAfter)));
    }

    [Fact]
    public void AGapOfNothingToReadIsNoGapAndSessionsNotEitherSideOfTheExDateAreNotRead()
    {
        Assert.False(DividendCarry.NotCarried(DividendCarry.Gap(0m, 100m, 100m, 100m, 100m)));
        Assert.Null(DividendCarry.GapAcross([(Day2, 100m, 100m)], Day2, 1m));
        Assert.Null(DividendCarry.GapAcross([(Day2, 100m, 100m), (Day2.AddDays(1), 100m, 100m)], Day2, 1m));
        Assert.Null(DividendCarry.GapAcross([(Day1, 100m, 100m)], Day2, 1m));
        Assert.Equal(0.01m, DividendCarry.GapAcross([(Day1, 100m, 100m), (Day2, 100m, 100m)], Day2, 1m));
    }

    sealed class DividendActions(string ticker, DateOnly exDate, decimal amount) : ICorporateActionFeed
    {
        public Task<IReadOnlyList<CorporateAction>> ActionsAsync(string exchange, DateOnly session, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CorporateAction>>(session != exDate
                ? []
                : [new CorporateAction(ticker, exDate, ActionKind.Dividend, amount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    Paid = new DividendPaid(ticker, exDate, amount, amount, null, null, null, null, null),
                }]);

        public int Requests => 0;
    }

    static (string ExDate, int Retries, string AskedOn)? WaitOf(TemporaryStore store, string ticker)
    {
        using var connection = store.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ex_date, retries, asked_on FROM dividend_wait WHERE ticker = $ticker;";
        command.Parameters.AddWithValue("$ticker", ticker);

        using var reader = command.ExecuteReader();

        return reader.Read() ? (reader.GetString(0), reader.GetInt32(1), reader.GetString(2)) : null;
    }

    [Fact]
    public async Task ADividendTheAnswerDoesNotCarryWaitsIsAskedForFiveNightsAndWeeklyAndAnAnswerCarryingItEndsTheWait()
    {
        using var store = new TemporaryStore().Migrated();
        var nights = TradedTo(new DateOnly(2026, 10, 30), 30);
        var exDate = nights[1];

        Member(store, "PAYS");
        Stored(store, "PAYS", nights.Take(2).Select(day => Priced(day, 100m)));

        // The answer's adjusted closes equal its raw ones, so the dividend's step is not in them.
        var history = new SeriesByTicker(new() { ["PAYS"] = [.. nights.Select(day => Priced(day, 100m))] });

        CorporateActionChecker Checker(DateOnly night, IHistoricalBarFeed feed) =>
            new(new DividendActions("PAYS", exDate, 2m), feed, FixedClock.At(NightAt(night), SessionZones.UnitedStates), store.DatabaseFile);

        var first = await Checker(exDate, history).RunAsync("GSPC", "night-1");

        Assert.Equal(1, history.Requests);
        Assert.Equal("ok", StateOfName(store, "PAYS").State);
        Assert.Equal(exDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), WaitOf(store, "PAYS")!.Value.ExDate);
        Assert.Equal(0, WaitOf(store, "PAYS")!.Value.Retries);
        Assert.Contains("PAYS", Assert.Single(first.Waiting!), StringComparison.Ordinal);
        ADividendsRowIsPartial(store, "night-1");

        // Asked on each of the next five nights, each counted once.
        for (var night = 1; night <= CorporateActionChecker.RetryNights; night++)
        {
            await Checker(nights[1 + night], history).RunAsync("GSPC", $"night-{night + 1}");
            Assert.Equal(night + 1, history.Requests);
            Assert.Equal(night, WaitOf(store, "PAYS")!.Value.Retries);
        }

        // Then not on the nights inside the week.
        var asked = nights[1 + CorporateActionChecker.RetryNights];
        var inside = nights.First(day => day > asked && day < asked.AddDays(CorporateActionChecker.WeeklyRetryDays));
        var quiet = await Checker(inside, history).RunAsync("GSPC", "night-inside");

        Assert.Equal(CorporateActionChecker.RetryNights + 1, history.Requests);
        Assert.Contains("PAYS", Assert.Single(quiet.Waiting!), StringComparison.Ordinal);

        // And on the night its week comes round, where an answer carrying it ends the wait.
        var round = nights.First(day => day >= asked.AddDays(CorporateActionChecker.WeeklyRetryDays));
        var carried = new SeriesByTicker(new()
        {
            ["PAYS"] = [.. nights.Select(day => day < exDate
                ? new ProviderBar(day, 98m, 98m, 98m, 98m, 100m, 1000)
                : Priced(day, 100m))],
        });
        var over = await Checker(round, carried).RunAsync("GSPC", "night-round");

        Assert.Equal(1, carried.Requests);
        Assert.Null(WaitOf(store, "PAYS"));
        Assert.Empty(over.Waiting!);
    }

    // What the Run page's failed region reads, partial and naming the stock.
    static void ADividendsRowIsPartial(TemporaryStore store, string runId)
    {
        var row = Assert.Single(PageRows(store, $"SELECT outcome, detail FROM run_log WHERE run_id = '{runId}' AND stage = 'actions';"));

        Assert.Equal("partial", row[0]);
        Assert.Contains("dividend(s) the provider's answer does not yet carry", row[1], StringComparison.Ordinal);
        Assert.Contains("PAYS", row[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStockThatLeavesTheIndexTakesItsWaitWithItAndIsNotAsked()
    {
        using var store = new TemporaryStore().Migrated();
        var nights = TradedTo(new DateOnly(2026, 10, 30), 4);

        Member(store, "PAYS");
        Stored(store, "PAYS", nights.Select(day => Priced(day, 100m)));

        var history = new SeriesByTicker(new() { ["PAYS"] = [.. nights.Select(day => Priced(day, 100m))] });

        await new CorporateActionChecker(new DividendActions("PAYS", nights[^2], 2m), history, FixedClock.At(NightAt(nights[^2]), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        Assert.NotNull(WaitOf(store, "PAYS"));

        using (var connection = store.Open())
        {

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE membership SET \"left\" = '2026-10-30' WHERE ticker = 'PAYS';";
            command.ExecuteNonQuery();
        }

        var requests = history.Requests;
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(nights[^1]), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-2");

        Assert.Equal(requests, history.Requests);
        Assert.Null(WaitOf(store, "PAYS"));
        Assert.Empty(outcome.Waiting!);
    }

    [Fact]
    public void TheCheckFlagsADividendAtTheSameGapAndReadsARestatedAmountAtWhatItPaid()
    {
        CheckedSession Session(int day, decimal close, decimal raw) =>
            new(new DateOnly(2025, 11, day), close, close, close, close, raw, 1000, false);

        // AMCR's 2025-11-28 worked by hand: 0.65 restated after 1 for 5 is 0.13 paid on 8.52, 1.5%; a step of 1.5% carries it, and
        // none leaves a gap of 1.5%.
        StockToCheck Stock(decimal before) => new("AMCR", [Session(26, before, 8.52m), Session(28, 8.52m, 8.52m)], new Dictionary<string, IReadOnlyDictionary<DateOnly, double>>(), new Dictionary<DateOnly, (decimal, decimal?)>(), null, null, "ok", false, null);
        StockEvents Events = new(new HashSet<DateOnly>(), new HashSet<DateOnly>(), [new CheckedDividend(new DateOnly(2025, 11, 28), 0.65m)], [new CheckedSplit(new DateOnly(2026, 1, 15), 1m, 5m)]);
        var night = new DateOnly(2025, 11, 28);

        Assert.DoesNotContain(ChartCheck.Read(Stock(8.39m), Events, night, []), finding => finding.Kind == "action not carried");
        Assert.Contains(ChartCheck.Read(Stock(8.52m), Events, night, []), finding => finding.Kind == "action not carried");
    }
}
