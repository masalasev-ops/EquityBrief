using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

public sealed record FetchOutcome(
    int RowsWritten,
    int Requests,
    int MembersStored,
    int RowsDropped,
    DateOnly Session,
    DateOnly Oldest,
    // Current members the file carried nothing for. Two of 503 on an ordinary
    // night, and carried out of the fetch rather than swallowed: the count no
    // longer refuses the file, so it has to be somewhere a person reads it. A
    // rise from two to two hundred is a fact about the provider that nothing
    // else would show.
    IReadOnlyList<string> Unaccounted,
    // The sessions the store was missing since the last night that ran, each
    // fetched in bulk and stored before tonight's. None on an ordinary night.
    IReadOnlyList<DateOnly>? CaughtUp = null,
    // The members each caught-up session's file carried nothing for, which
    // tonight's file reports as `Unaccounted`. Discarded until the phase 5
    // sign-off, so a caught-up file short of members was stored with nothing
    // anywhere saying which names it left without that session.
    IReadOnlyDictionary<DateOnly, IReadOnlyList<string>>? CaughtUpShort = null,
    // Rows the files this fetch read listed as not traded, and rows the reader
    // passed over as unreadable, over every file the fetch read. Both went to
    // the night's stdout alone until the phase 5 sign-off, which a scheduled
    // task discards.
    int NotTraded = 0,
    int Unreadable = 0);

// The nightly bar fetch. One bulk request, stored for current members only,
// then the sessions that have fallen out of the retention window are dropped.
//
// One request for the night, whatever the universe is. That is the rule the
// nightly path is shaped around, and it is why the fetcher takes a bulk file
// and filters it rather than asking the provider per name.
// see: The nightly run is arithmetic only
//
// The filter reads membership rather than the file, and that distinction is
// load bearing. The provider returns every name the exchange traded, which
// includes names that have left the index and names that were never in it: the
// captured fixture holds two of the first and two of the second, so a fetcher
// that stored what it was sent would store four names the index does not have.
public sealed class BarFetcher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.BulkPrice]);

    public const string Stage = "fetch";
    public const string Source = "bulk";
    public const string Exchange = "US";

    // One year kept, which is the limits table's figure and the window the
    // level builder reads. Expressed as a year rather than as a session count
    // because the boundary is a date and a session count would drift with
    // holidays.
    public const int RetentionYears = 1;

    readonly IBulkPriceFeed feed;
    readonly IClock clock;
    readonly string databaseFile;

    public BarFetcher(IBulkPriceFeed feed, IClock clock, string databaseFile)
    {
        this.feed = feed;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The names a night stores bars for: every name that has not left by
    // tonight's session. A name that has left keeps its rows and its history and
    // is simply not added to.
    // see: Your own listing history is kept forever
    //
    // Not left by tonight, rather than never left, and not joined by tonight
    // either. Until the phase 5 sign-off this was `left IS NULL`, which read an
    // announced change as effective the night it was announced: the rebalance
    // effective 2026-09-21 was on the membership feed by 2026-09-10, so three
    // names still in the index until then were stored nothing for and four names
    // not yet in it were. A name announced to join is stored from the
    // announcement, so it joins with its year whole rather than with a hole
    // between the backfill and its first night as a member; it is listed only
    // from its effective date, which is the other predicate and lives with the
    // stages that list.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker FROM membership
        WHERE index_code = $index AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string StoreBar = @"
        INSERT INTO bar (
            ticker, session_date, open, high, low, close,
            volume, source, observed_at, raw_close)
        VALUES (
            $ticker, $session_date, $open, $high, $low, $close,
            $volume, $source, $observed_at, $raw_close)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    // The retention drop, and one of the two removals SCHEMA sanctions. It
    // takes every session below a date boundary, for every name at once, so
    // what is left is still a contiguous series ending tonight. Nothing here
    // can remove a bar from inside a series it leaves standing, which is the
    // thing the append-only rule is about.
    const string DropOlderThan = @"
        DELETE FROM bar WHERE session_date < $oldest;
    ";

    const string BarCount = "SELECT COUNT(*) FROM bar;";

    // How many missing names a refusal names before it stops listing them.
    // Enough to recognise the shape of the absence, short of printing five
    // hundred tickers into a run log row.
    const int FirstNamed = 5;

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<FetchOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync();

        // The session this night is for, decided here and asked for by name.
        //
        // The feed is told which session rather than asked for the last day,
        // because a request with no date has nothing to compare its answer
        // against. A night is scheduled after the close, so the session is the
        // date the run's own instant falls on in the exchange's zone
        // (see: The night runs at a fixed UTC instant, moved only when a night finds the day's file not yet posted).
        var session = clock.SessionDateAt(started);

        var members = await MembersAsync(connection, indexCode, session);
        var before = await CountAsync(connection);

        // Any session the store is missing since the last night that ran,
        // fetched in bulk before tonight's.
        //
        // A night that did not run leaves every name short the same session, and
        // nothing downstream can see it: the observed calendar is the union of
        // the dates the names hold, so a session none of them holds is one it
        // says the exchange never traded, and every indicator, move and forward
        // return would count across the hole as though it were one session. The
        // closure table says which weekdays should be there, and each one missing
        // costs one more bulk request, which grows with the nights missed and
        // never with the names. The provider serves an older session's file as
        // prices, which is what makes this possible.
        // see: A session the night finds missing is fetched in bulk before tonight's
        // see: Bars are never interpolated
        var oldest = session.AddYears(-RetentionYears);
        var last = await LastStoredAsync(connection, session);

        var missed = last is { } since
            ? ExchangeClosures.SessionsBetween(since, session).Where(day => day >= oldest).ToArray()
            : [];

        var caughtUp = new List<BulkBar[]>();
        var caughtUpShort = new SortedDictionary<DateOnly, IReadOnlyList<string>>();
        var notTradedFrom = feed.NotSessions.Count;
        var unreadableFrom = feed.Unreadable.Count;

        foreach (var day in missed)
        {
            var (dayWanted, dayMissing) = await AcceptedAsync(day, members, missedSession: true, cancellationToken);

            caughtUp.Add(dayWanted);

            if (dayMissing.Length > 0)
            {
                caughtUpShort[day] = dayMissing;
            }
        }

        // Tonight's, through the same checks. One request, counted by the feed
        // rather than asserted by the caller, because a caller that looped would
        // still report one.
        var (wanted, missing) = await AcceptedAsync(session, members, missedSession: false, cancellationToken);

        // Every session in one transaction, the missed ones first, so a night
        // that stops partway stores none of them rather than a series with the
        // hole moved rather than filled.
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var row in caughtUp.SelectMany(day => day).Concat(wanted))
        {
            await StoreAsync(connection, row, started);
        }

        var stored = await CountAsync(connection);

        // The retention boundary, taken from the session the night asked for.
        // Before 2.3 it was read back out of the rows, which made the boundary a
        // property of what the provider happened to send: a payload for an older
        // session would have moved it backwards and kept sessions the night
        // should have dropped.
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = DropOlderThan;
            drop.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await drop.ExecuteNonQueryAsync();
        }

        var after = await CountAsync(connection);

        await transaction.CommitAsync();

        // Both figures measured from the store rather than self-reported, which
        // is what SCHEMA requires of rows_written and what makes the halt
        // condition read a fact rather than a stage's opinion.
        var outcome = new FetchOutcome(
            stored - before,
            feed.Requests,
            wanted.Length,
            stored - after,
            session,
            oldest,
            missing,
            missed,
            caughtUpShort,
            feed.NotSessions.Count - notTradedFrom,
            feed.Unreadable.Count - unreadableFrom);

        await AppendAsync(connection, runId, started, outcome);

        return outcome;
    }

    // One session's file, read and checked before anything is stored.
    //
    // The feed's two accumulating lists are read from where they stood before
    // this call, so a night that fetches several sessions judges each against
    // its own rows rather than against every file it has read.
    async Task<(BulkBar[] Wanted, string[] Missing)> AcceptedAsync(
        DateOnly day,
        HashSet<string> members,
        bool missedSession,
        CancellationToken cancellationToken)
    {
        var unreadableFrom = feed.Unreadable.Count;
        var notSessionsFrom = feed.NotSessions.Count;

        IReadOnlyList<BulkBar> rows;

        try
        {
            rows = await feed.RowsAsync(Exchange, day, cancellationToken);
        }
        catch (Exception failure) when (missedSession && failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                Missed(day) + " and its file could not be fetched: " + failure.Message +
                " A gap stops computation rather than being counted across, so the night stops.",
                failure);
        }

        var named = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var wanted = rows
            .Where(row => members.Contains(row.Ticker))
            .OrderBy(row => row.Ticker, StringComparer.Ordinal)
            .ToArray();

        // A current member whose row the reader refused, named before anything
        // is stored.
        //
        // The reader passes over a row it cannot read rather than refusing the
        // file, because the file is the whole exchange and a fund's fractional
        // volume is not this index's business: two of the first four days
        // fetched at index size carried six such rows, and each refused the
        // night for every name with a message that named nothing. What a row
        // passed over must never do is leave a member quietly without tonight's
        // bar, which is what this refuses, with the ticker and the reason.
        var refusedMembers = feed.Unreadable
            .Skip(unreadableFrom)
            .Where(row => members.Contains(row.Ticker))
            .OrderBy(row => row.Ticker, StringComparer.Ordinal)
            .ToArray();

        if (refusedMembers.Length > 0)
        {
            throw new InvalidOperationException(
                "The bulk file for " + named +
                $" carries {refusedMembers.Length} current member row(s) the reader refused: " +
                string.Join("; ", refusedMembers.Take(FirstNamed).Select(row => row.Reason)) +
                ". A member's bar that cannot be read is refused rather than skipped, because a " +
                "member with no bar tonight would read as a shorter history.");
        }

        // Every current member accounted for, before anything is stored.
        //
        // The bulk file is the whole exchange and every member of a US index is
        // listed on it, so a member that appears neither as a bar nor as a
        // symbol that did not trade is a member the file does not carry. A file
        // short of names stored as a whole one leaves most of the index
        // silently stale with no gap to find, which is the failure that looks
        // current and is false.
        //
        // Only the fetcher can ask this. The feed does not know how many names
        // the index holds, which is why this is a different row from the wrong
        // session and is refused in a different place.
        // see: A feed is unavailable when it does not answer, and wrong when it answers with something else
        var accounted = wanted
            .Select(row => row.Ticker)
            .Concat(feed.NotSessions.Skip(notSessionsFrom))
            .ToHashSet(StringComparer.Ordinal);

        var missing = members.Where(member => !accounted.Contains(member))
            .OrderBy(member => member, StringComparer.Ordinal)
            .ToArray();

        // Every one, and not merely some. Corrected at 2.4 by the first live
        // night over the whole index: two of 503 current members, EQR and PSTG,
        // are absent from an ordinary day's file, so refusing on any absence
        // refuses every night. A name the file carries nothing for has no bar
        // tonight, which is that name's own shorter history and is what the gap
        // machinery already reads.
        //
        // What is unambiguous is a file carrying nothing for any of them. That
        // is the wrong file or a session the exchange has not traded, and it is
        // not the same as a file with no rows at all: the payload can be full of
        // symbols this index does not hold. A night run before the close
        // produced exactly that, which is how this rule was measured rather than
        // chosen. For a missed session it can also be a closure the table does
        // not hold, which the message says.
        if (missing.Length == members.Count && members.Count > 0)
        {
            throw new InvalidOperationException(
                (missedSession ? Missed(day) + ", and its file " : "The bulk file for " + named + " ") +
                $"carries nothing for any of the {members.Count} current member(s), the first being " +
                $"{string.Join(", ", missing.Take(FirstNamed))}. A payload holding none of the " +
                "index is the wrong file or a session the exchange has not traded, and storing " +
                "nothing from it would read as a night that ran." +
                (missedSession ? " If the exchange was closed that day, the closure table is missing it." : string.Empty));
        }

        return (wanted, missing);
    }

    static string Quoted(DateOnly day) =>
        "\"" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"";

    // A count and the first few names, as JSON. Tickers are letters, digits, a
    // dot and a hyphen, so none of them needs escaping inside the quotes.
    static string Named(IReadOnlyList<string> names) =>
        $"{{\"count\":{names.Count},\"first\":[" +
        string.Join(",", names.Take(FirstNamed).Select(name => "\"" + name + "\"")) +
        "]}";

    static string Missed(DateOnly day) =>
        "The store is missing session " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        ", which the exchange traded and no night stored";

    // The newest session a night's bulk file stored before tonight's, which is
    // where the last night that ran left it.
    //
    // Read off the bulk rows rather than off every bar, because a backfill
    // writes a joining name's year through tonight: read over every bar, one
    // joiner backfilled on the night after a night that did not run made the
    // missed session look stored, and the index was left short it with nothing
    // saying so. A refetch is the same shape for one name. Only a store no bulk
    // file has reached yet falls back to every bar, which is a first run whose
    // backfill is the whole index and is where the last night left it.
    const string LastStoredBefore = @"
        SELECT COALESCE(
            (SELECT MAX(session_date) FROM bar WHERE source = $bulk AND session_date < $session),
            (SELECT MAX(session_date) FROM bar WHERE session_date < $session));
    ";

    static async Task<DateOnly?> LastStoredAsync(SqliteConnection connection, DateOnly session)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LastStoredBefore;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bulk", Source);

        return await command.ExecuteScalarAsync() is string text
            ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<HashSet<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> CountAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BarCount;

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    static async Task StoreAsync(SqliteConnection connection, BulkBar row, DateTimeOffset observed)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StoreBar;
        command.Parameters.AddWithValue("$ticker", row.Ticker);
        command.Parameters.AddWithValue("$session_date", row.Bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$open", Money.ToStorage(row.Bar.Open));
        command.Parameters.AddWithValue("$high", Money.ToStorage(row.Bar.High));
        command.Parameters.AddWithValue("$low", Money.ToStorage(row.Bar.Low));
        command.Parameters.AddWithValue("$close", Money.ToStorage(row.Bar.Close));
        command.Parameters.AddWithValue("$volume", row.Bar.Volume);
        command.Parameters.AddWithValue("$source", Source);
        command.Parameters.AddWithValue("$observed_at", observed.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$raw_close", Money.ToStorage(row.Bar.RawClose));

        await command.ExecuteNonQueryAsync();
    }

    async Task AppendAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset started,
        FetchOutcome outcome)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        // Everything the night's fetch line says, on the row a person reads the
        // next morning. The line goes to stdout, which a scheduled task
        // discards, and until the phase 5 sign-off the counts of rows passed
        // over and of members a file carried nothing for were on the line and
        // not here. Names are the first few, beside the whole count.
        command.Parameters.AddWithValue(
            "$detail",
            $"{{\"members\":{outcome.MembersStored},\"dropped\":{outcome.RowsDropped}," +
            FormattableString.Invariant($"\"session\":\"{outcome.Session:yyyy-MM-dd}\",\"oldest\":\"{outcome.Oldest:yyyy-MM-dd}\",") +
            "\"caughtUp\":[" +
            string.Join(",", (outcome.CaughtUp ?? []).Select(day => Quoted(day))) +
            "]," +
            $"\"notTraded\":{outcome.NotTraded},\"unreadable\":{outcome.Unreadable}," +
            $"\"unaccounted\":{Named(outcome.Unaccounted)}," +
            "\"caughtUpShort\":{" +
            string.Join(",", (outcome.CaughtUpShort ?? new Dictionary<DateOnly, IReadOnlyList<string>>())
                .Select(entry => Quoted(entry.Key) + ":" + Named(entry.Value))) +
            "}}");

        await command.ExecuteNonQueryAsync();
    }
}
