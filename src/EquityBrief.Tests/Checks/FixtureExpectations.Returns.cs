using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the forward return filler over constructed stores. The
// committed fixture has no session after its listings, so the cases the
// expectation works by hand are stored as the night stores them and filled by
// the shipped filler rather than handed to the series.
public partial class FixtureExpectations
{
    static readonly DateOnly ListedOn = new(2026, 1, 1);

    static readonly DateTimeOffset FilledAt = new(2026, 6, 1, 23, 30, 0, TimeSpan.Zero);

    static string Day(int sessionsAfter) =>
        ListedOn.AddDays(sessionsAfter).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Plan(JsonElement plan) => JsonSerializer.Serialize(new
    {
        entryLow = plan.GetProperty("entryLow").GetString(),
        entryHigh = plan.GetProperty("entryHigh").GetString(),
        stop = plan.GetProperty("stop").GetString(),
        firstTradedTarget = plan.GetProperty("firstTradedTarget").GetString(),
    });

    // One listing on the listing night with its bar and the sessions after it, as
    // the shortlist builder and the bar writers store them. A null close leaves
    // the listing session's bar out.
    static void Listed(
        TemporaryStore store,
        string ticker,
        string plan,
        string? listedAt,
        string? rawListedAt,
        IReadOnlyList<string> closes,
        IReadOnlyList<string>? rawCloses = null)
    {
        Insert(
            store,
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"VALUES ('{ticker}', '{Day(0)}', '[]', 0, '{plan}', '{{}}');");

        void Bar(int sessionsAfter, string close, string? raw) => Insert(
            store,
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('{ticker}', '{Day(sessionsAfter)}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', " +
            $"'{Day(sessionsAfter)}T21:10:00Z', {(raw is null ? "NULL" : $"'{raw}'")});");

        if (listedAt is not null)
        {
            Bar(0, listedAt, rawListedAt);
        }

        for (var at = 0; at < closes.Count; at++)
        {
            Bar(at + 1, closes[at], rawCloses?[at] ?? closes[at]);
        }
    }

    static IReadOnlyList<string> Row(TemporaryStore store, string ticker, string horizon) =>
        Query(store, "SELECT outcome, resolved_on, CAST(round(return_pct, 6) AS TEXT), CAST(round(break_even, 6) AS TEXT) FROM forward_return " +
            $"WHERE ticker = '{ticker}' AND horizon = '{horizon}';");

    static string Shape(string? outcome, string? resolvedOn, double? returnPct, double? breakEven) =>
        string.Join("|",
            outcome ?? "null",
            resolvedOn ?? "null",
            returnPct is { } move ? Math.Round(move, 6).ToString(CultureInfo.InvariantCulture) : "null",
            breakEven is { } bar ? Math.Round(bar, 6).ToString(CultureInfo.InvariantCulture) : "null");

    static string Stored(TemporaryStore store, string ticker, string horizon)
    {
        var cells = Row(store, ticker, horizon).Single().Split('|');

        double? Figure(string cell) => cell == "null" ? null : double.Parse(cell, CultureInfo.InvariantCulture);

        return Shape(
            cells[0] == "null" ? null : cells[0],
            cells[1] == "null" ? null : cells[1],
            Figure(cells[2]),
            Figure(cells[3]));
    }

    static string? Text(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetString();

    static double? Number(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetDouble();

    [Fact]
    public async Task ASetupStillInPlayIsScoredWithItsPlanScaledByTheListingSessionsAdjustment()
    {
        // see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
        var constructed = Expected("forward-returns").GetProperty("constructed");
        var plan = constructed.GetProperty("plan");
        var cases = Expected("forward-returns").GetProperty("restated").GetProperty("cases").EnumerateArray().ToArray();

        decimal Price(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

        Assert.Equal(3, cases.Length);

        using var store = new TemporaryStore().Migrated();

        foreach (var (one, at) in cases.Select((one, at) => (one, at)))
        {
            var what = one.GetProperty("case").GetString();
            var closes = one.GetProperty("closes").EnumerateArray().Select(close => close.GetString()!).ToArray();
            var raw = one.GetProperty("rawCloses").EnumerateArray().Select(close => close.GetString()!).ToArray();
            var expected = Shape(Text(one, "outcome"), Text(one, "resolvedOn"), Number(one, "returnPct"), Number(one, "breakEven"));

            ForwardReturn Score(decimal? rawListedAt) => ForwardReturnSeries.OverSetup(
                Closes([.. closes.Select(Price)]),
                Price(plan.GetProperty("stop").GetString()!),
                Price(plan.GetProperty("firstTradedTarget").GetString()!),
                Price(plan.GetProperty("entryHigh").GetString()!),
                Price(one.GetProperty("listedAt").GetString()!),
                rawListedAt);

            var scaled = Score(Price(one.GetProperty("rawListedAt").GetString()!));
            var unscaled = Score(null);

            Assert.Equal(
                (what, expected),
                (what, Shape(scaled.Outcome, scaled.ResolvedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), scaled.ReturnPct, scaled.BreakEven)));

            // Each case separates the two readings, so a filler that scored the
            // plan at its stored prices could not pass it.
            var without = one.GetProperty("withoutTheRestatement");

            Assert.Equal((what, Text(without, "outcome")), (what, unscaled.Outcome));
            Assert.Equal(
                (what, Text(without, "resolvedOn")),
                (what, unscaled.ResolvedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            Assert.NotEqual(Text(one, "outcome") + Text(one, "resolvedOn"), Text(without, "outcome") + Text(without, "resolvedOn"));

            Listed(store, $"RESTATED{at}", Plan(plan), one.GetProperty("listedAt").GetString(), one.GetProperty("rawListedAt").GetString(), closes, raw);
        }

        await new ForwardReturnFiller(FixedClock.At(FilledAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("restated");

        foreach (var (one, at) in cases.Select((one, at) => (one, at)))
        {
            Assert.Equal(
                (one.GetProperty("case").GetString(), Shape(Text(one, "outcome"), Text(one, "resolvedOn"), Number(one, "returnPct"), Number(one, "breakEven"))),
                (one.GetProperty("case").GetString(), Stored(store, $"RESTATED{at}", ForwardReturnSeries.Setup)));
        }

        // Every writer stores a raw close, so a listing bar without one refuses
        // rather than being scored at the plan's stored prices.
        Listed(store, "NORAW", Plan(plan), "100", null, ["105"]);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ForwardReturnFiller(FixedClock.At(FilledAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("no-raw"));

        Assert.Contains("NORAW", refused.Message, StringComparison.Ordinal);

        // The factor is the restatement since the listing only because a listing's
        // own session carries none on its night, which the captured payloads show.
        using var replayed = await WithListings();

        var listings = Query(replayed, "SELECT COUNT(*) FROM listing l JOIN bar b ON b.ticker = l.ticker AND b.session_date = l.session_date;").Single();

        Assert.True(int.Parse(listings, CultureInfo.InvariantCulture) >= 4, $"Read {listings} listing sessions, expected at least 4.");
        Assert.Equal(
            ["0"],
            Query(replayed, "SELECT COUNT(*) FROM listing l JOIN bar b ON b.ticker = l.ticker AND b.session_date = l.session_date WHERE b.close <> b.raw_close;"));
    }

    [Fact]
    public async Task AnOutcomeOnceDecidedIsNeverRewrittenWhenTheBarsItWasScoredOnLeaveTheStore()
    {
        // see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
        var plan = Plan(Expected("forward-returns").GetProperty("constructed").GetProperty("plan"));
        var clock = FixedClock.At(FilledAt, SessionZones.UnitedStates);

        using var store = new TemporaryStore().Migrated();

        // Rising a point a session from a listing close of 100: five sessions is
        // 105, twenty-one is 121, and the target of 110 is reached on the tenth.
        Listed(store, "KEEP", plan, "100", "100", [.. Enumerable.Range(101, 21).Select(close => close.ToString(CultureInfo.InvariantCulture))]);

        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("decided");

        var decided = ForwardReturnSeries.Horizons.ToDictionary(horizon => horizon, horizon => Stored(store, "KEEP", horizon));

        Assert.Equal(Shape("win", Day(5), 5.0, null), decided[ForwardReturnSeries.FiveSessions]);
        Assert.Equal(Shape("win", Day(21), 21.0, null), decided[ForwardReturnSeries.TwentyOneSessions]);
        Assert.Equal(Shape("win", Day(10), 10.0, 50.0), decided[ForwardReturnSeries.Setup]);

        // The listing session and the two after it leave the store, as the year's
        // retention takes them, and a second listing falls a point a session.
        Insert(store, $"DELETE FROM bar WHERE ticker = 'KEEP' AND session_date < '{Day(3)}';");
        Listed(store, "FALL", plan, "100", "100", [.. Enumerable.Range(0, 21).Select(fall => (99 - fall).ToString(CultureInfo.InvariantCulture))]);

        var outcome = await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("kept");

        foreach (var horizon in ForwardReturnSeries.Horizons)
        {
            Assert.Equal((horizon, decided[horizon]), (horizon, Stored(store, "KEEP", horizon)));
        }

        Assert.Equal(Shape("loss", Day(5), -5.0, null), Stored(store, "FALL", ForwardReturnSeries.FiveSessions));
        Assert.Equal(Shape("loss", Day(21), -21.0, null), Stored(store, "FALL", ForwardReturnSeries.TwentyOneSessions));
        Assert.Equal(Shape("loss", Day(11), -11.0, 50.0), Stored(store, "FALL", ForwardReturnSeries.Setup));

        // The base rate is over every name-night, the kept row among them.
        foreach (var horizon in new[] { ForwardReturnSeries.FiveSessions, ForwardReturnSeries.TwentyOneSessions })
        {
            Assert.Equal(["50", "50"], Query(store, $"SELECT base_rate FROM forward_return WHERE horizon = '{horizon}' ORDER BY ticker;"));
        }

        Assert.Equal(new ForwardReturnOutcome(2, 3, 3, 0, 3), outcome);
        Assert.Equal(
            ["3|2 listing(s), 3 row(s) written, 3 kept as decided, 3 newly matured, 0 not yet matured"],
            Query(store, "SELECT rows_written, detail FROM run_log WHERE run_id = 'kept';"));
    }

    [Fact]
    public async Task AnOpenHorizonWhoseListingCloseHasLeftTheStoreStaysOpen()
    {
        var plan = Plan(Expected("forward-returns").GetProperty("constructed").GetProperty("plan"));

        using var store = new TemporaryStore().Migrated();

        // Above the zone for the whole cap. Scored from no listing close, the setup
        // would enter nowhere and read as never entered at the cap.
        Listed(store, "GONE", plan, null, null, [.. Enumerable.Repeat("105", ForwardReturnSeries.SetupSessionCap)]);

        await new ForwardReturnFiller(FixedClock.At(FilledAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("gone");

        foreach (var horizon in ForwardReturnSeries.Horizons)
        {
            Assert.Equal((horizon, Shape(null, null, null, null)), (horizon, Stored(store, "GONE", horizon)));
        }
    }

    [Fact]
    public async Task AListingWithNothingLeftToScoreReadsNoBarAndAnOpenOneReadsNoSessionPastTheCap()
    {
        // A stored close no reader could parse, placed where a read that should
        // not happen would reach it.
        const string Unreadable = "not a price";

        var constructed = Expected("forward-returns").GetProperty("constructed");
        var plan = Plan(constructed.GetProperty("plan"));

        using var store = new TemporaryStore().Migrated();

        Listed(store, "DONE", plan, "100", "100", ["105", Unreadable]);
        Insert(
            store,
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) VALUES " +
            $"('DONE', '{Day(0)}', '5', 'win', '{Day(5)}', 5.0, NULL, NULL), " +
            $"('DONE', '{Day(0)}', '21', 'win', '{Day(21)}', 21.0, NULL, NULL), " +
            $"('DONE', '{Day(0)}', 'setup', 'win', '{Day(10)}', 10.0, NULL, 50.0);");

        // A setup with no plan never resolves, and its two session horizons are decided.
        Listed(store, "NOPLAN", "{}", "100", "100", ["105", Unreadable]);
        Insert(
            store,
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) VALUES " +
            $"('NOPLAN', '{Day(0)}', '5', 'win', '{Day(5)}', 5.0, NULL, NULL), " +
            $"('NOPLAN', '{Day(0)}', '21', 'win', '{Day(21)}', 21.0, NULL, NULL), " +
            $"('NOPLAN', '{Day(0)}', 'setup', NULL, NULL, NULL, NULL, NULL);");

        // The expectation's case past the cap, with a session no reader could parse
        // after the target.
        var past = constructed.GetProperty("pastTheCap");
        var runs = past.GetProperty("runs").EnumerateArray()
            .SelectMany(run => Enumerable.Repeat(run.GetProperty("close").GetString()!, run.GetProperty("sessions").GetInt32()))
            .Append(Unreadable)
            .ToArray();

        Listed(store, "OPEN", plan, past.GetProperty("listedAt").GetString(), past.GetProperty("listedAt").GetString(), runs);

        await new ForwardReturnFiller(FixedClock.At(FilledAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("reads");

        Assert.Equal(Shape("win", Day(10), 10.0, 50.0), Stored(store, "DONE", ForwardReturnSeries.Setup));
        Assert.Equal(Shape(null, null, null, null), Stored(store, "NOPLAN", ForwardReturnSeries.Setup));
        Assert.Equal(
            Shape(Text(past, "outcome"), Text(past, "resolvedOn"), Number(past, "returnPct"), Number(past, "breakEven")),
            Stored(store, "OPEN", ForwardReturnSeries.Setup));
    }

    [Fact]
    public async Task EveryCaseTheEntryRuleIsWorkedOverIsWhatTheFillerStoresFromThePlanTheListingCarries()
    {
        // The filler reads the zone's top edge off the stored plan, which carries its
        // bottom edge beside it, and the listing night's own close off the bar store.
        // see: A setup is scored from its entry, and a target reached before the entry is never a win
        var constructed = Expected("forward-returns").GetProperty("constructed");
        var plan = constructed.GetProperty("plan");
        var cases = constructed.GetProperty("cases").EnumerateArray().ToArray();

        Assert.Equal(4, cases.Length);
        Assert.NotEqual(plan.GetProperty("entryLow").GetString(), plan.GetProperty("entryHigh").GetString());

        using var store = new TemporaryStore().Migrated();

        foreach (var (one, at) in cases.Select((one, at) => (one, at)))
        {
            var closes = one.GetProperty("closes").EnumerateArray().Select(close => close.GetString()!).ToArray();

            Listed(store, $"ENTRY{at}", Plan(plan), one.GetProperty("listedAt").GetString(), one.GetProperty("listedAt").GetString(), closes);
        }

        await new ForwardReturnFiller(FixedClock.At(FilledAt, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("entry");

        foreach (var (one, at) in cases.Select((one, at) => (one, at)))
        {
            Assert.Equal(
                (one.GetProperty("case").GetString(), Shape(Text(one, "outcome"), Text(one, "resolvedOn"), Number(one, "returnPct"), Number(one, "breakEven"))),
                (one.GetProperty("case").GetString(), Stored(store, $"ENTRY{at}", ForwardReturnSeries.Setup)));
        }
    }
}
