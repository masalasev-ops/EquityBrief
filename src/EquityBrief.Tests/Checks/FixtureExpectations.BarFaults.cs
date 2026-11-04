using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Returns;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 22.33: what the chart audit of 2026-11-04 found the store holding, each worked by hand over constructed bars: a split the
// provider posted after its night, a stock behind the night reading ok, copied candles, a ticker the provider keeps under
// two codes, and a split the provider's own adjusted closes still carry.
// see: The corporate action check reads the day files of the five sessions before its own, and refetches a member an action the provider posted late landed on
// see: A member holding a copied candle or behind the night is asked for its year again, and stays suspect until an answer clears it
public partial class FixtureExpectations
{
    internal static readonly string[] TickerJoinRows = [.. ComponentAccess.TickerJoinRows, .. SchemaColumns.TickerJoinStoreRows];

    static readonly DateOnly FaultNight = new(2026, 10, 2);

    static DateTimeOffset NightAt(DateOnly session) => new(session.Year, session.Month, session.Day, 23, 0, 0, TimeSpan.Zero);

    // Actions the provider files on the sessions it is handed, each asked day remembered.
    sealed class ActionsByDay(params (string Ticker, DateOnly On)[] filed) : ICorporateActionFeed
    {
        public List<DateOnly> Asked { get; } = [];

        public HashSet<DateOnly> Refused { get; } = [];

        public int Requests { get; private set; }

        public Task<IReadOnlyList<CorporateAction>> ActionsAsync(string exchange, DateOnly session, CancellationToken cancellation = default)
        {
            Requests += 2;
            Asked.Add(session);

            return Refused.Contains(session)
                ? throw new ProviderRefusal("the file was refused", transient: false)
                : Task.FromResult<IReadOnlyList<CorporateAction>>([.. filed.Where(one => one.On == session).Select(one => new CorporateAction(one.Ticker, one.On, ActionKind.Split, "2/1"))]);
        }
    }

    // A provider's series by stock, one request an ask.
    sealed class SeriesByTicker(Dictionary<string, IReadOnlyList<ProviderBar>> series) : IHistoricalBarFeed
    {
        public List<string> Asked { get; } = [];

        public int Requests => Asked.Count;

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Asked.Add(ticker);

            return series.TryGetValue(ticker, out var bars)
                ? Task.FromResult<IReadOnlyList<ProviderBar>>([.. bars.Where(bar => bar.SessionDate >= from && bar.SessionDate <= to)])
                : throw new ProviderRefusal($"nothing served for {ticker}", transient: false);
        }
    }

    static ProviderBar Priced(DateOnly on, decimal close, long volume = 1000) => new(on, close, close + 1, close - 1, close, close, volume);

    static ProviderBar Copy(DateOnly on, decimal close) => new(on, close, close, close, close, close, 0);

    static IReadOnlyList<DateOnly> TradedTo(DateOnly through, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = through; sessions.Count < count; day = day.AddDays(-1))
        {
            if (ExchangeClosures.IsSession(day) && day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Insert(0, day);
            }
        }

        return sessions;
    }

    static void Member(TemporaryStore store, string ticker) =>
        store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2026-10-02T23:00:00Z');");

    static void Stored(TemporaryStore store, string ticker, IEnumerable<ProviderBar> bars)
    {
        using var connection = store.Open();

        foreach (var bar in bars)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($t, $d, $o, $h, $l, $c, $v, 'historical', '2026-09-01T00:00:00Z', $r);";
            insert.Parameters.AddWithValue("$t", ticker);
            insert.Parameters.AddWithValue("$d", bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Money.Bind(insert, "$o", bar.Open);
            Money.Bind(insert, "$h", bar.High);
            Money.Bind(insert, "$l", bar.Low);
            Money.Bind(insert, "$c", bar.Close);
            Money.Bind(insert, "$r", bar.RawClose);
            insert.Parameters.AddWithValue("$v", bar.Volume);
            insert.ExecuteNonQuery();
        }
    }

    static void AskedOn(TemporaryStore store, string ticker, DateOnly session) =>
        store.Execute(FormattableString.Invariant($"INSERT INTO series_state (ticker, state, reason, checked_at, retries) VALUES ('{ticker}', 'ok', NULL, '{NightAt(session):yyyy-MM-ddTHH:mm:ssZ}', 0);"));

    static (string State, string? Reason) StateOfName(TemporaryStore store, string ticker)
    {
        using var connection = store.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT state, reason FROM series_state WHERE ticker = $t;";
        read.Parameters.AddWithValue("$t", ticker);
        using var reader = read.ExecuteReader();

        return reader.Read() ? (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)) : ("none", null);
    }

    static decimal CloseOn(TemporaryStore store, string ticker, DateOnly on)
    {
        using var connection = store.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT close FROM bar WHERE ticker = $t AND session_date = $d;";
        read.Parameters.AddWithValue("$t", ticker);
        read.Parameters.AddWithValue("$d", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return Money.FromStorage((string)read.ExecuteScalar()!);
    }

    [Fact]
    public async Task ASplitTheProviderPostedAfterItsNightIsReadFromThatDaysFileTheNightsAfterAndRefetched()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 8);
        var spin = sessions[^2];

        // CTVA's shape: filed for the session before tonight, and the night of that session asked nothing about it. BBB's was
        // read on its own night, so it is not asked again.
        foreach (var name in new[] { "LATE", "BBB" })
        {
            Member(store, name);
            Stored(store, name, sessions.Select(day => Priced(day, 77m)));
        }

        AskedOn(store, "LATE", sessions[0]);
        AskedOn(store, "BBB", spin);

        var feed = new ActionsByDay(("LATE", spin), ("BBB", spin));
        var history = new SeriesByTicker(new()
        {
            ["LATE"] = [.. sessions.Select(day => Priced(day, day < spin ? 11.65m : 12.57m))],
        });
        var outcome = await new CorporateActionChecker(feed, history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        // Tonight's file and the five sessions before it, each asked once: twelve requests a night whatever the universe.
        Assert.Equal([FaultNight, .. sessions.SkipLast(1).TakeLast(CorporateActionChecker.LateFileSessions)], feed.Asked);
        Assert.Equal(12, feed.Requests);
        Assert.Equal(["LATE"], outcome.Late);
        Assert.Equal(["LATE"], history.Asked);
        Assert.Equal(11.65m, CloseOn(store, "LATE", sessions[0]));
        Assert.Equal(("ok", (string?)null), StateOfName(store, "LATE"));
        Assert.Equal(77m, CloseOn(store, "BBB", sessions[0]));

        // The night after, the same file asks for nothing: it was asked on the night it was found.
        var again = await new CorporateActionChecker(feed, history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-2");

        Assert.Empty(again.Late ?? []);
        Assert.Equal(["LATE"], history.Asked);
    }

    [Fact]
    public async Task AnEarlierDayFileThatCannotBeReadFailsNothingAndIsCountedOnTheStage()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 8);

        Member(store, "AAA");
        Stored(store, "AAA", sessions.Select(day => Priced(day, 10m)));

        var feed = new ActionsByDay();

        feed.Refused.Add(sessions[^2]);

        var outcome = await new CorporateActionChecker(feed, new SeriesByTicker([]), FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        Assert.Equal(1, outcome.LateFilesUnread);

        using var connection = store.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT outcome, detail FROM run_log WHERE run_id = 'night-1';";
        using var reader = read.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("ok", reader.GetString(0));
        Assert.Contains("1 earlier day file(s) unread", reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMemberBehindTheNightIsAskedAndReadsSuspectUntilAnAnswerReachesTheNight()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 8);
        var heldTo = sessions[^4];

        // HOS's shape: the day file stops carrying it, its state read ok and its newest bar fell seven sessions behind.
        foreach (var name in new[] { "SHOWN", "GONE", "FINE" })
        {
            Member(store, name);
            Stored(store, name, sessions.Where(day => name == "FINE" || day <= heldTo).Select(day => Priced(day, 7m)));
            AskedOn(store, name, sessions[0]);
        }

        var history = new SeriesByTicker(new()
        {
            ["SHOWN"] = [.. sessions.Select(day => Priced(day, 7m))],
            ["GONE"] = [.. sessions.Where(day => day <= heldTo).Select(day => Priced(day, 7m))],
        });
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        Assert.Equal(["GONE", "SHOWN"], outcome.Behind);
        Assert.Equal(["GONE", "SHOWN"], history.Asked);
        Assert.Equal(("ok", (string?)null), StateOfName(store, "SHOWN"));

        var (state, reason) = StateOfName(store, "GONE");

        Assert.Equal("suspect", state);
        Assert.Contains(FormattableString.Invariant($"its newest bar is {heldTo:yyyy-MM-dd}, behind the night"), reason, StringComparison.Ordinal);
        Assert.Equal(["GONE"], outcome.Suspect);
        Assert.DoesNotContain("FINE", history.Asked);
    }

    [Fact]
    public async Task PastTheLimitABehindMemberIsMarkedWithoutARequest()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 4);
        var names = Enumerable.Range(0, CorporateActionChecker.FaultAskLimit + 2).Select(at => FormattableString.Invariant($"N{at:00}")).ToArray();

        foreach (var name in names)
        {
            Member(store, name);
            Stored(store, name, sessions.SkipLast(1).Select(day => Priced(day, 7m)));
        }

        Member(store, "NIGHT");
        Stored(store, "NIGHT", sessions.Select(day => Priced(day, 7m)));

        var history = new SeriesByTicker(names.ToDictionary(name => name, _ => (IReadOnlyList<ProviderBar>)[.. sessions.Select(day => Priced(day, 7m))]));
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        Assert.Equal(CorporateActionChecker.FaultAskLimit, history.Requests);
        Assert.Equal([names[^2], names[^1]], outcome.Suspect);
        Assert.Contains("past the 20 members a night asks for", StateOfName(store, names[^1]).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopiedCandleHasNoVolumeAndEveryPriceTheCloseBefore()
    {
        Assert.True(CopiedCandle.Is(0, 21.25m, 21.25m, 21.25m, 21.25m, 21.25m));
        Assert.False(CopiedCandle.Is(1, 21.25m, 21.25m, 21.25m, 21.25m, 21.25m));
        Assert.False(CopiedCandle.Is(0, 21.25m, 21.25m, 21.25m, 21.26m, 21.25m));
        Assert.False(CopiedCandle.Is(0, 21.26m, 21.25m, 21.25m, 21.25m, 21.25m));
        Assert.False(CopiedCandle.Is(0, 21.25m, 21.26m, 21.25m, 21.25m, 21.25m));
        Assert.False(CopiedCandle.Is(0, 21.25m, 21.25m, 21.24m, 21.25m, 21.25m));
        Assert.False(CopiedCandle.Is(0, 21.25m, 21.25m, 21.25m, 21.25m, 21.3m));
    }

    [Fact]
    public async Task AMemberHoldingACopiedCandleIsAskedAndClearedOnlyByAnAnswerWithoutIt()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 6);
        var copied = sessions[2];

        // MSA's shape: a day the exchange traded stored as a copy of the day before, and a first-day zero that copies nothing.
        foreach (var name in new[] { "CLEARS", "STAYS" })
        {
            Member(store, name);
            Stored(store, name, sessions.Select(day => day == copied ? Copy(day, 173.85m) : Priced(day, day < copied ? 173.85m : 190m)));
        }

        Member(store, "FIRST");
        Stored(store, "FIRST", sessions.Select((day, at) => at == 0 ? new ProviderBar(day, 135m, 135m, 135m, 135m, 135m, 0) : Priced(day, 140m)));

        var history = new SeriesByTicker(new()
        {
            ["CLEARS"] = [.. sessions.Select(day => Priced(day, day <= copied ? 189.73m : 190m))],
            ["STAYS"] = [.. sessions.Select(day => day == copied ? Copy(day, 173.85m) : Priced(day, day < copied ? 173.85m : 190m))],
        });
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "night-1");

        Assert.Equal(["CLEARS", "STAYS"], outcome.Copied);
        Assert.Equal(["CLEARS", "STAYS"], history.Asked);
        Assert.Equal(189.73m, CloseOn(store, "CLEARS", copied));
        Assert.Equal(("ok", (string?)null), StateOfName(store, "CLEARS"));
        Assert.Equal("suspect", StateOfName(store, "STAYS").State);
        Assert.Contains(FormattableString.Invariant($"1 copied candle(s), no volume at the close before, the newest on {copied:yyyy-MM-dd}"), StateOfName(store, "STAYS").Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefetchByHandReplacesEachYearNamedAndAStockItCannotServeKeepsItsYear()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 4);

        foreach (var name in new[] { "GOOD", "BAD" })
        {
            Member(store, name);
            Stored(store, name, sessions.Select(day => Priced(day, 77m)));
        }

        var history = new SeriesByTicker(new() { ["GOOD"] = [.. sessions.Select(day => Priced(day, 11.65m))] });
        var outcome = await new CorporateActionChecker(new ActionsByDay(), history, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .RefetchNamesAsync(["GOOD", "BAD"], "refetch-1");

        Assert.Equal((1, 2, 2), (outcome.Refetched, outcome.Requests, history.Requests));
        Assert.Equal(11.65m, CloseOn(store, "GOOD", sessions[0]));
        Assert.Equal(77m, CloseOn(store, "BAD", sessions[0]));
        Assert.Equal(["BAD"], outcome.Suspect);
        Assert.Equal("ok", StateOfName(store, "GOOD").State);
        Assert.Equal("suspect", StateOfName(store, "BAD").State);
    }

    [Fact]
    public async Task AJoinedTickerReadsTheOldTickersEarlierSessionsAtTheNewScaleAndAnyOtherTickerAsItIs()
    {
        var day = new DateOnly(2026, 4, 28);
        var inner = new SeriesByTicker(new()
        {
            ["NEW"] = [Priced(day, 100m), Priced(day.AddDays(1), 101m)],
            ["OLD"] = [new ProviderBar(day.AddDays(-1), 48m, 50m, 47m, 49m, 49m, 500), Priced(day, 50m), Priced(day.AddDays(1), 50.5m)],
            ["SOLO"] = [Priced(day, 9m)],
        });
        var feed = new JoinedHistoricalBarFeed(inner, () => new Dictionary<string, string> { ["NEW"] = "OLD" });
        var joined = await feed.BarsAsync("NEW", day.AddDays(-10), day.AddDays(5));

        // Worked by hand: the two closes of 28 April, 100 and 50, set a scale of 2; 27 April's prices double, its raw close and
        // volume stand as sent, and the old ticker's sessions from the new one's first on are left out.
        Assert.Equal([day.AddDays(-1), day, day.AddDays(1)], joined.Select(bar => bar.SessionDate));
        Assert.Equal(new ProviderBar(day.AddDays(-1), 96m, 100m, 94m, 98m, 49m, 500), joined[0]);
        Assert.Equal(100m, joined[1].Close);
        Assert.Equal(["NEW", "OLD"], inner.Asked);

        // Asked for no earlier than its own first session, it reads no old session and makes no second request.
        inner.Asked.Clear();
        Assert.Equal(2, (await feed.BarsAsync("NEW", day, day.AddDays(5))).Count);
        Assert.Equal(["NEW"], inner.Asked);

        inner.Asked.Clear();
        Assert.Single(await feed.BarsAsync("SOLO", day.AddDays(-10), day.AddDays(5)));
        Assert.Equal(["SOLO"], inner.Asked);

        // No session in common leaves the scale at 1.
        Assert.Equal(49m, JoinedHistoricalBarFeed.Joined([Priced(day, 100m)], [new ProviderBar(day.AddDays(-1), 48m, 50m, 47m, 49m, 49m, 500)])[0].Close);
    }

    [Fact]
    public async Task ATickerJoinIsWrittenOnceAndTheRowHoldsWhoItWasRuledFor()
    {
        using var store = new TemporaryStore().Migrated();
        var joins = new TickerJoins(FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(1, await joins.JoinAsync("VSXY", "VSCO", "ticker-join-1"));
        Assert.Equal(0, await joins.JoinAsync("VSXY", "OTHER", "ticker-join-2"));
        Assert.Equal("VSCO", TickerJoins.Read(store.DatabaseFile)["VSXY"]);
    }

    static PulledSession Pulled(DateOnly on, decimal close) => new(on, close, close, close, close, close, 100);

    [Fact]
    public void ASplitTheAdjustedClosesStillCarryIsAppliedToTheSessionsBeforeItAndOneTheyCarryIsLeft()
    {
        var day = new DateOnly(2021, 7, 6);

        // PECO's shape: one for three, 11 jumps to 33. Worked by hand: the sessions before it times 3 read 33, and nothing moves.
        PulledSession[] jump = [Pulled(day.AddDays(-2), 10m), Pulled(day.AddDays(-1), 11m), Pulled(day, 33m), Pulled(day.AddDays(1), 34m)];
        var reverse = new SplitAnswer(day, 1m, 3m);

        Assert.Equal([reverse], SplitCarry.NotCarried(jump, [reverse]));

        var carried = SplitCarry.Carried(jump, [reverse]);

        Assert.Equal([30m, 33m, 33m, 34m], carried.Select(one => one.Close));
        Assert.Equal([10m, 11m, 33m, 34m], carried.Select(one => one.RawClose));
        Assert.Equal([100L, 100L, 100L, 100L], carried.Select(one => one.Volume));

        // WOR's shape: 1622 for 1000, 100 falls to 63.5: the fall is within 15% of what the ratio gives, and earlier prices fall by it.
        var spin = new SplitAnswer(day, 1622m, 1000m);

        Assert.Equal(61.6523m, SplitCarry.Carried([Pulled(day.AddDays(-1), 100m), Pulled(day, 63.5m)], SplitCarry.NotCarried([Pulled(day.AddDays(-1), 100m), Pulled(day, 63.5m)], [spin]))[0].Close);

        // The closes already carry it, the ratio does not fit the move, or the ratio is too near one to tell from a day's move.
        Assert.Empty(SplitCarry.NotCarried([Pulled(day.AddDays(-1), 33m), Pulled(day, 33.5m)], [reverse]));
        Assert.Empty(SplitCarry.NotCarried([Pulled(day.AddDays(-1), 10m), Pulled(day, 15.5m)], [new SplitAnswer(day, 1m, 2m)]));
        Assert.Empty(SplitCarry.NotCarried([Pulled(day.AddDays(-1), 10m), Pulled(day, 9.5m)], [new SplitAnswer(day, 105m, 100m)]));

        // No session before the split: nothing to read it from.
        Assert.Empty(SplitCarry.NotCarried([Pulled(day, 33m)], [reverse]));
    }

    sealed class SplitsOf(params SplitAnswer[] splits) : ISplitHistoryFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<SplitAnswer>> SplitsAsync(string ticker, DateOnly from, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<SplitAnswer>>(ticker == "JUMP" ? splits : []);
        }
    }

    [Fact]
    public async Task ACarriedSplitIsWrittenAsANewPullBesideTheOldAndAStockWithNoneWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();
        var day = new DateOnly(2021, 7, 6);

        foreach (var (ticker, first, second) in new[] { ("JUMP", 11m, 33m), ("QUIET", 33m, 33m) })
        {
            store.Execute(FormattableString.Invariant($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '2021-07-02', '{first}', '{first}', '{first}', '{first}', '{first}', 100, 'history-pull-1'), ('{ticker}', '2021-07-06', '{second}', '{second}', '{second}', '{second}', '{second}', 100, 'history-pull-1');"));
        }

        var feed = new SplitsOf(new SplitAnswer(day, 1m, 3m));
        var outcome = await HistoryPull.CarrySplitsAsync(feed, FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile, ["JUMP", "QUIET"], "history-pull-2");

        Assert.Equal((2, 2), (outcome.Rows, outcome.Requests));

        using var connection = store.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT ticker, session_date, close, raw_close, pull FROM pulled_bar WHERE pull = 'history-pull-2' ORDER BY 2;";
        using var reader = read.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(string.Join('|', reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        Assert.Equal(["JUMP|2021-07-02|33|11", "JUMP|2021-07-06|33|33"], rows);

        using var old = connection.CreateCommand();
        old.CommandText = "SELECT COUNT(*) FROM pulled_bar WHERE pull = 'history-pull-1';";
        Assert.Equal(4L, old.ExecuteScalar());
    }

    [Fact]
    public async Task ClearingOutcomesRemovesOnlyTheNamedStocksFromTheDayOn()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct) VALUES
            ('CTVA', '2026-09-14', 'setup', 'loss', '2026-10-01', -84.39),
            ('CTVA', '2026-08-03', 'setup', 'win', '2026-08-20', 4.0),
            ('CTVA', '2026-09-30', '5', NULL, NULL, NULL),
            ('OTHER', '2026-09-14', 'setup', 'loss', '2026-10-01', -3.0);");

        var removed = await new ForwardReturnFiller(FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile)
            .ClearDecidedAsync(["CTVA"], new DateOnly(2026, 10, 1));

        Assert.Equal(1, removed);

        using var connection = store.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT ticker || '|' || session_date FROM forward_return ORDER BY 1;";
        using var reader = read.ExecuteReader();
        var left = new List<string>();

        while (reader.Read())
        {
            left.Add(reader.GetString(0));
        }

        Assert.Equal(["CTVA|2026-08-03", "CTVA|2026-09-30", "OTHER|2026-09-14"], left);
    }
}
