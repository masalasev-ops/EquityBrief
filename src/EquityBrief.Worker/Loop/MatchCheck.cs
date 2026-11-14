using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// One live rule's picks on a night as the match check read them: the rule, the picks it kept, those whose order the registered
// plan placed again from the stored bars the same, and each that differed in words.
public sealed record MatchedRule(string Rule, int Picks, int Matched, IReadOnlyList<string> Mismatches);

// From the operator's order of 2026-11-14, item 7: on each of the first five sessions after a live rule's registration, each
// pick it kept is placed again under the plan its registration states, from the stock's stored bars through the night, and its
// buy, its stop and its take-profit held to the order the night stored. A mismatch means the live rule is not the rule that was
// tested; it is named on the step's row and the Run page, and nothing is changed. It reads the store alone, writes its row on
// the run log, makes no request and calls no model.
// see: The first five sessions after a restart hold each live rule's picks to its registered plan placed again, and change nothing
public sealed class MatchCheck : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.RulePick, Touch.Read),
            new StoreTouch(Store.DipTrade, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "match-check";

    // The sessions after a registration the check reads, on the operator's order.
    public const int Sessions = 5;

    // A price stored and placed again is the same where the two agree to a thousandth of a cent.
    const double Within = 0.00001;

    const string SessionsAfter = "SELECT DISTINCT session_date FROM bar WHERE session_date > $after ORDER BY session_date LIMIT $count;";

    // Each table a live rule keeps its picks in: the rule's name, the stock, the night's close and stop, its target and the
    // distance a trailing stop keeps, the sessions it is given, its family and the order the night stored.
    const string FamilyPicks = "SELECT ticker, entry, stop, target, NULL, cap, family, plan FROM family_trade WHERE candidate = $rule AND session_date = $night AND plan IS NOT NULL ORDER BY place;";

    const string IndexPicks = "SELECT ticker, entry, stop, target, trail, cap, family, plan FROM index_rule_trade WHERE candidate = $rule AND session_date = $night AND plan IS NOT NULL ORDER BY place;";

    const string SwingPicks = "SELECT ticker, entry, stop, target, NULL, cap, family, plan FROM rule_pick WHERE rule = $rule AND session_date = $night AND plan IS NOT NULL ORDER BY place;";

    const string DipPicks = "SELECT ticker, entry, stop, NULL, NULL, time_limit, 'dip', plan FROM dip_trade WHERE rule = $rule AND session_date = $night AND plan IS NOT NULL ORDER BY place;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public MatchCheck(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // Every live rule in its first five sessions on the night, each pick placed again and held to its stored order; the step's
    // row says how many matched and names every mismatch, and is partial where one did not match.
    public async Task<IReadOnlyList<MatchedRule>> RunAsync(string runId, DateOnly night, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var register = await PlanPointTest.RegisterRowsAsync(connection, null, cancellation);
        var checkedRules = new List<MatchedRule>();

        foreach (var row in CandidateFamily.Standing(register, started).Where(row => FamilyRecords.IsLive(row.Candidate)))
        {
            if (PlanHook.Of(CandidateEvaluator.Read(row.Parameters)) is not { } plan
                || !(await FirstSessionsAsync(connection, DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime), cancellation)).Contains(night))
            {
                continue;
            }

            var picks = 0;
            var matched = 0;
            var mismatches = new List<string>();

            foreach (var query in new[] { FamilyPicks, IndexPicks, SwingPicks, DipPicks })
            {
                foreach (var pick in await PicksAsync(connection, query, row.Candidate, night, cancellation))
                {
                    picks++;

                    var placed = await PlanTrades.OrderAsync(connection, null, plan, pick.Family, pick.Ticker, night, pick.Stop, pick.Target, pick.Trail, pick.Cap, cancellation);

                    if (Differs(pick.Ticker, pick.Entry, pick.Stored, placed) is { } said)
                    {
                        mismatches.Add(said);
                    }
                    else
                    {
                        matched++;
                    }
                }
            }

            checkedRules.Add(new MatchedRule(row.Candidate, picks, matched, mismatches));
        }

        var detail = Said(checkedRules);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", checkedRules.Any(rule => rule.Mismatches.Count > 0) ? "partial" : "ok");
            command.Parameters.AddWithValue("$detail", detail);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return checkedRules;
    }

    // The step's row in plain words: how many picks each live rule kept and how many matched, every mismatch named, or that no
    // live rule is in its first five sessions.
    public static string Said(IReadOnlyList<MatchedRule> rules) =>
        rules.Count == 0
            ? FormattableString.Invariant($"no live rule is in its first {Sessions} sessions after its registration")
            : string.Join("; ", rules.Select(rule => rule.Mismatches.Count == 0
                ? FormattableString.Invariant($"{rule.Rule}: {rule.Matched} of {rule.Picks} pick(s) matched the plan it was registered with")
                : FormattableString.Invariant($"{rule.Rule}: {rule.Matched} of {rule.Picks} pick(s) matched, and these did not, so the live rule is not the rule that was tested: {string.Join(", ", rule.Mismatches)}")));

    // Where a stored order and the one placed again differ, in words: its buy, its stop or its take-profit; none where they agree.
    public static string? Differs(string ticker, decimal entry, PlanOrder? stored, PlanOrder placed)
    {
        if (stored is null)
        {
            return $"{ticker} stored no order";
        }

        var buy = stored.Level ?? Statistic.FromPrice(entry);
        var againBuy = placed.Level ?? Statistic.FromPrice(entry);

        if (stored.Words != placed.Words)
        {
            return $"{ticker} was placed under \"{stored.Words}\" and its registration states \"{placed.Words}\"";
        }

        if (Math.Abs(buy - againBuy) > Within)
        {
            return FormattableString.Invariant($"{ticker}'s buy stored {buy:0.00} and placed again {againBuy:0.00}");
        }

        var stop = PlanPoints.StopOf(stored.Stop, buy, stored.RuleStop, stored.ZoneStop, stored.TypicalDay);
        var againStop = PlanPoints.StopOf(placed.Stop, againBuy, placed.RuleStop, placed.ZoneStop, placed.TypicalDay);

        if (stop is not { } held || againStop is not { } again || Math.Abs(held - again) > Within)
        {
            return FormattableString.Invariant($"{ticker}'s stop stored {(stop is { } a ? a.ToString("0.00", CultureInfo.InvariantCulture) : "none")} and placed again {(againStop is { } b ? b.ToString("0.00", CultureInfo.InvariantCulture) : "none")}");
        }

        var taking = stored.TakeProfit is { } multiple ? TakeProfit.Placed(stored.Sale, buy, held, stored.TurnedDown, multiple)?.Price : null;
        var takingAgain = placed.TakeProfit is { } again2 ? TakeProfit.Placed(placed.Sale, againBuy, again, placed.TurnedDown, again2)?.Price : null;

        return taking is null && takingAgain is null || taking is { } x && takingAgain is { } y && Math.Abs(x - y) <= Within
            ? null
            : FormattableString.Invariant($"{ticker}'s take-profit stored {(taking is { } c ? c.ToString("0.00", CultureInfo.InvariantCulture) : "none")} and placed again {(takingAgain is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "none")}");
    }

    sealed record StoredPick(string Ticker, decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, string Family, PlanOrder? Stored);

    static async Task<IReadOnlyList<StoredPick>> PicksAsync(SqliteConnection connection, string query, string rule, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = query;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$night", Stamp(night));

        var picks = new List<StoredPick>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var entry = Money.FromStorage(reader.GetString(1));
            var stop = Money.FromStorage(reader.GetString(2));
            decimal? target = reader.IsDBNull(3) ? null : Money.FromStorage(reader.GetString(3));
            decimal? trail = reader.IsDBNull(4) ? (query == FamilyPicks && target is null ? entry - stop : null) : Money.FromStorage(reader.GetString(4));

            picks.Add(new StoredPick(reader.GetString(0), entry, stop, target, trail, reader.GetInt32(5), reader.GetString(6), PlanOrder.FromJson(reader.GetString(7))));
        }

        return picks;
    }

    static async Task<IReadOnlyList<DateOnly>> FirstSessionsAsync(SqliteConnection connection, DateOnly registered, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsAfter;
        command.Parameters.AddWithValue("$after", Stamp(registered.AddDays(-1)));
        command.Parameters.AddWithValue("$count", Sessions);

        var sessions = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return sessions;
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
