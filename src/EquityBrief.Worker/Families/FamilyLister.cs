using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

public sealed record FamilyListOutcome(
    DateOnly? Night,
    int Listed,
    int OpenTrade,
    int UnderAnother,
    int PastFive,
    IReadOnlyList<(string Family, int Qualified, int Listed)> Families);

// The family lister. Draws the page's list for the night from what each setup family passed, and stores it.
//
// It runs in the swing filter's step, after the filter has stored its rows, since the pullback family's
// names are the ones the filter passed. It reads each family's passing names in that family's own order
// and every trade a list made on an earlier night with what became of it, hands both to the one rule that
// draws the list, and writes the answer for the night's session, replacing its own rows where the night
// is run again. It evaluates no gate, makes no request and calls no model.
// see: The nightly run is arithmetic only
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
// see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
public sealed class FamilyLister : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.ListRule, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.FamilyNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.FamilyPick, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "families";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // Whether the swing filter stored its results for the night, which is what the pullback family is read
    // from: a night it stored none for has no list for the families to draw.
    const string FilterRan = "SELECT COUNT(*) FROM gate_result WHERE session_date = $night AND version <> '" + ReplayedResults.Version + "';";

    // The names the swing filter passed on the night, in the order tonight's list has always drawn them:
    // improving businesses first where the night stored its readings, then the filter's own order. A
    // replayed result is none of the night's.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    static readonly string PullbacksOn = @"
        SELECT g.ticker FROM gate_result g
        LEFT JOIN fundamental_reading f ON f.ticker = g.ticker AND f.session_date = g.session_date
        WHERE g.session_date = $night AND g.passed = 1 AND g.version <> '" + ReplayedResults.Version + @"'
        ORDER BY " + FundamentalState.PlaceIn("f.state") + @", g.rank, g.ticker;
    ";

    // Every trade a list made before the night with what became of it on its own horizon: each listed
    // row of a night the families drew, and each name the swing filter passed on a night it listed
    // before them, which is a pullback's trade.
    static readonly string TradesBefore = @"
        SELECT p.ticker, p.session_date, p.family, f.horizon IS NOT NULL, f.outcome, f.resolved_on, " + SetupFamilies.CapIn("p.family") + @"
        FROM family_pick p
        LEFT JOIN gate_result g ON g.ticker = p.ticker AND g.session_date = p.session_date
        LEFT JOIN filter_version v ON v.version = g.version
        LEFT JOIN forward_return f
            ON f.ticker = p.ticker AND f.session_date = p.session_date
            AND f.horizon = " + SetupFamilies.HorizonIn("p.family", "v.settings") + @"
        WHERE p.state = '" + FamilyList.Listed + @"' AND p.session_date < $night
        UNION ALL
        SELECT g.ticker, g.session_date, '" + SetupFamilies.Pullback + @"', f.horizon IS NOT NULL, f.outcome, f.resolved_on, " + SetupFamilies.Pullbacks.CapSessions.ToString(CultureInfo.InvariantCulture) + @"
        FROM gate_result g
        JOIN list_rule r ON r.session_date = g.session_date AND r.rule = '" + ListRules.Filter + @"'
        LEFT JOIN filter_version v ON v.version = g.version
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date
            AND f.horizon = " + SetupFamilies.PullbackHorizonIn("v.settings") + @"
        WHERE g.passed = 1 AND g.session_date < $night
          AND NOT " + FamilyList.HasPicks("g.session_date") + @";
    ";

    // A night run again replaces its own list whole, and no other night's rows are touched.
    const string ClearTheNight = "DELETE FROM family_pick WHERE session_date = $night; DELETE FROM family_night WHERE session_date = $night;";

    // The session the families drew, with the families on the page that night in its order.
    const string RecordTheNight = "INSERT INTO family_night (session_date, families) VALUES ($night, $families);";

    const string Insert = @"
        INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night)
        VALUES ($night, $ticker, $family, $state, $place, $also, $held_family, $held_night);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public FamilyLister(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<FamilyListOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, as the swing filter reads it.
        var night = await NewestAsync(connection, cancellation);

        if (night is not { } session)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, "no session is stored, so no list was drawn", cancellation);

            return new FamilyListOutcome(null, 0, 0, 0, 0, []);
        }

        await using (var ran = connection.CreateCommand())
        {
            ran.CommandText = FilterRan;
            ran.Parameters.AddWithValue("$night", Stamp(session));

            if (Convert.ToInt32(await ran.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) == 0)
            {
                await AppendAsync(connection, null, runId, startedAt, 0, FormattableString.Invariant($"the swing filter stored no result for {session:yyyy-MM-dd}, so no list was drawn"), cancellation);

                return new FamilyListOutcome(null, 0, 0, 0, 0, []);
            }
        }

        var qualifying = new List<FamilyQualifiers>();

        foreach (var family in SetupFamilies.InPageOrder)
        {
            qualifying.Add(new FamilyQualifiers(family.Name, await QualifiersAsync(connection, family, session, cancellation)));
        }

        var open = await OpenTradesAsync(connection, session, [.. qualifying.SelectMany(family => family.Tickers).Distinct(StringComparer.Ordinal)], cancellation);
        var picks = FamilyList.Draw(qualifying, open);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$night", Stamp(session));

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        await using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = RecordTheNight;
            record.Parameters.AddWithValue("$night", Stamp(session));
            record.Parameters.AddWithValue("$families", JsonSerializer.Serialize(SetupFamilies.InPageOrder.Select(family => family.Name)));

            await record.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var pick in picks)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = Insert;
            insert.Parameters.AddWithValue("$night", Stamp(session));
            insert.Parameters.AddWithValue("$ticker", pick.Ticker);
            insert.Parameters.AddWithValue("$family", pick.Family);
            insert.Parameters.AddWithValue("$state", pick.State);
            insert.Parameters.AddWithValue("$place", pick.Place is { } place ? place : DBNull.Value);
            insert.Parameters.AddWithValue("$also", JsonSerializer.Serialize(pick.Also));
            insert.Parameters.AddWithValue("$held_family", pick.HeldBy is { } held ? held.Family : DBNull.Value);
            insert.Parameters.AddWithValue("$held_night", pick.HeldBy is { } holding ? Stamp(holding.Listed) : DBNull.Value);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var families = qualifying
            .Select(family => (family.Family, family.Tickers.Count, picks.Count(pick => pick.Family == family.Family && pick.State == FamilyList.Listed)))
            .ToArray();
        var outcome = new FamilyListOutcome(
            session,
            picks.Count(pick => pick.State == FamilyList.Listed),
            picks.Count(pick => pick.State == FamilyList.OpenTrade),
            picks.Count(pick => pick.State == FamilyList.UnderAnother),
            picks.Count(pick => pick.State == FamilyList.PastFive),
            families);

        await AppendAsync(connection, transaction, runId, startedAt, picks.Count, Detail(outcome), cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // What the stage's row says: the list's size, each family's share of it, and what was held back.
    public static string Detail(FamilyListOutcome outcome) =>
        FormattableString.Invariant($"{outcome.Listed} listed for {outcome.Night:yyyy-MM-dd}: ")
        + string.Join(", ", outcome.Families.Select(family => FormattableString.Invariant($"{family.Listed} of the {family.Qualified} the {family.Family} family passed")))
        + FormattableString.Invariant($"; {outcome.OpenTrade} held back by a trade still open, {outcome.UnderAnother} listed under another family, {outcome.PastFive} past a family's {SetupFamilies.ListedANight}");

    // The names a family passed on the night, in its own order.
    static async Task<IReadOnlyList<string>> QualifiersAsync(SqliteConnection connection, SetupFamily family, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = family.Name == SetupFamilies.Pullback
            ? PullbacksOn
            : throw new InvalidOperationException($"The {family.Name} family is on the page and the lister reads no store for it.");
        command.Parameters.AddWithValue("$night", Stamp(night));

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    // For each of the names, the trade it still holds on the night, read by the open trade rule over every
    // trade a list made before it.
    // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
    static async Task<IReadOnlyDictionary<string, HeldTrade>> OpenTradesAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        var wanted = tickers.ToHashSet(StringComparer.Ordinal);
        var trades = new Dictionary<string, List<(OpenTradeListing Listing, string Family)>>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TradesBefore;
            command.Parameters.AddWithValue("$night", Stamp(night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var ticker = reader.GetString(0);

                if (!wanted.Contains(ticker))
                {
                    continue;
                }

                if (!trades.TryGetValue(ticker, out var held))
                {
                    trades[ticker] = held = [];
                }

                held.Add((
                    new OpenTradeListing(
                        ticker,
                        Date(reader.GetString(1)),
                        reader.GetInt64(3) == 1,
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : Date(reader.GetString(5)),
                        reader.GetInt32(6)),
                    reader.GetString(2)));
            }
        }

        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);

        foreach (var (ticker, held) in trades)
        {
            if (OpenTrades.OpenOn(ticker, night, held.Select(trade => trade.Listing)) is { } kept)
            {
                open[ticker] = new HeldTrade(held.First(trade => trade.Listing.Night == kept.Night).Family, kept.Night);
            }
        }

        return open;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
