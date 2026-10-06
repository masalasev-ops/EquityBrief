using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Cards;

// One index's cards on a night: how many were written, and why none were where its part failed.
public sealed record IndexCards(string Index, int Cards, string? Fault = null);

// What the decision cards wrote on a night: the night read, none where the store holds no bar, and each index's cards.
public sealed record DecisionCardsOutcome(DateOnly? Session, IReadOnlyList<IndexCards> Indices)
{
    public int Cards => Indices.Sum(index => index.Cards);
}

// The decision cards. After every index's families and books are read, one card for each stock a family listed on the
// night on each index and for each stock a sector heavyweights' book bought on it: the checklist's first five lines, each
// a tick, a note or a warning with its reason in words, the plan's prices, the rule the card names and the rule's record
// as the card read it, with the card's values it was read with. Read from stored rows alone, with no request and no
// model; a night run again replaces its own cards. A failure in one index's cards undoes them, is named on the stage's
// row and the next index is read, so the night goes on whatever became of its cards.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: The nightly run is arithmetic only
// see: Every computed table's writer is its own deleter
public sealed class DecisionCards : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.RuleRecord, Touch.Read),
            new StoreTouch(Store.DecisionCard, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "decision-cards";

    // The word a card of the sector heavyweights is stored under, the family's own.
    public static string Heavyweights => HeavyweightRule.Name;

    // The indices read, in the order the page offers them.
    public static IReadOnlyList<string> Indices { get; } = [IndexFamilies.LargeIndex, .. IndexFamilies.Indices];

    // The stage row's outcome where every index's cards were written, and where one or more were not.
    public const string Ok = "ok";

    public const string NotComputed = "not computed";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string ClearTheNight = "DELETE FROM decision_card WHERE session_date = $night;";

    const string LargePicks = @"
        SELECT ticker, family, place FROM family_pick
        WHERE session_date = $night AND place IS NOT NULL
        ORDER BY place, ticker;
    ";

    const string IndexPicks = @"
        SELECT ticker, family, place FROM index_family_pick
        WHERE index_code = $index AND session_date = $night AND place IS NOT NULL
        ORDER BY place, ticker;
    ";

    const string LargeHeavyweights = @"
        SELECT ticker, sector, entry_close FROM heavyweight_holding
        WHERE entered_on = $night
        ORDER BY sector, ticker;
    ";

    const string IndexHeavyweights = @"
        SELECT ticker, sector, entry_close FROM index_heavyweight_holding
        WHERE index_code = $index AND entered_on = $night
        ORDER BY sector, ticker;
    ";

    // The swing filter's row for a member, which holds the pullback's plans and every member's market gate.
    const string GateRow = @"
        SELECT swing_entry, swing_stop, swing_target, clear_stop, clear_target, gates FROM gate_result
        WHERE session_date = $night AND ticker = $ticker;
    ";

    const string FamilyRow = @"
        SELECT entry, stop, target, gates FROM family_result
        WHERE session_date = $night AND ticker = $ticker AND family = $family;
    ";

    const string IndexFamilyRow = @"
        SELECT entry, stop, target, trail, cap FROM index_family_result
        WHERE index_code = $index AND session_date = $night AND ticker = $ticker AND family = $family;
    ";

    const string IndexNightRow = @"
        SELECT breadth, settings FROM index_family_night
        WHERE index_code = $index AND session_date = $night;
    ";

    const string ReadingRow = @"
        SELECT state, dollar_volume, company_value FROM member_reading
        WHERE index_code = $index AND session_date = $night AND ticker = $ticker;
    ";

    // A member's quarters and its company as the newest fetch before the night stored them, as the member reader reads them.
    const string QuartersBefore = @"
        SELECT period_end, filing_date, net_income, operating_income, interest_expense FROM reported_quarter r
        WHERE ticker = $ticker AND fetched_at = (
            SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = $ticker AND session_date < $night);
    ";

    const string SectorsBefore = @"
        SELECT c.ticker, c.sector FROM company c
        WHERE c.sector IS NOT NULL AND c.fetched_at = (
            SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    const string MembersOf = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session);
    ";

    // The session the sector window starts on: the one the window's sessions before the night lie back from it.
    const string WindowStart = @"
        SELECT session_date FROM (SELECT DISTINCT session_date FROM bar WHERE session_date <= $night)
        ORDER BY session_date DESC LIMIT 1 OFFSET $back;
    ";

    const string ClosesOn = "SELECT ticker, session_date, close FROM bar WHERE session_date = $from OR session_date = $night;";

    const string ReportsAfter = @"
        SELECT event_date, timing FROM calendar
        WHERE ticker = $ticker AND kind = 'earnings' AND event_date >= $night
        ORDER BY event_date;
    ";

    const string RecordOf = @"
        SELECT rule, trades, won, average, unit, median_sessions, ended_by, worst_close, first_session, last_session, membership
        FROM rule_record WHERE index_code = $index AND family = $family;
    ";

    const string InsertCard = @"
        INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record)
        VALUES ($index, $night, $family, $ticker, $place, $entry, $stop, $target, $rule, $settings, $lines, $record);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;
    readonly CardSettings settings;

    public DecisionCards(IClock clock, string databaseFile, CardSettings? settings = null)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.settings = settings ?? CardSettings.Defaults;
    }

    // The words an index is named by on a card.
    public static string NameOf(string index) => index == IndexFamilies.LargeIndex ? "S&P 500" : IndexRuleCandidate.NameOf(index);

    public async Task<DecisionCardsOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var written = new List<IndexCards>();

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new DecisionCardsOutcome(null, []);
            }

            var night = Date(newest);

            await ExecuteAsync(connection, null, ClearTheNight, [("$night", Stamp(night))], cancellation);

            foreach (var index in Indices)
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

                try
                {
                    var cards = await IndexAsync(connection, transaction, index, night, cancellation);

                    await transaction.CommitAsync(cancellation);
                    written.Add(new IndexCards(index, cards));
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    written.Add(new IndexCards(index, 0, IndexFamilies.Cause(failure)));
                }
            }

            await AppendAsync(connection, runId, startedAt, written.Sum(index => index.Cards), written.Any(index => index.Fault is not null) ? NotComputed : Ok, Detail(written), cancellation);

            return new DecisionCardsOutcome(night, written);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            IndexCards[] all =
            [
                .. written,
                .. Indices.Where(index => written.All(one => one.Index != index)).Select(index => new IndexCards(index, 0, IndexFamilies.Cause(failure))),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, all.Sum(index => index.Cards), Detail(all) + "; the step stopped on " + IndexFamilies.Cause(failure), cancellation);

            return new DecisionCardsOutcome(null, all);
        }
    }

    static string Detail(IReadOnlyList<IndexCards> indices) =>
        string.Join("; ", indices.Select(index => index.Fault is null
            ? FormattableString.Invariant($"{index.Cards} card(s) on the {NameOf(index.Index)}")
            : $"the {NameOf(index.Index)}'s cards not computed tonight: {index.Fault}"));

    // One index's cards: each stock a family listed and each stock its book bought, in the page's order.
    async Task<int> IndexAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        var large = index == IndexFamilies.LargeIndex;
        var picks = await PicksAsync(connection, transaction, large ? LargePicks : IndexPicks, index, night, cancellation);
        var bought = await BoughtAsync(connection, transaction, large ? LargeHeavyweights : IndexHeavyweights, index, night, cancellation);

        if (picks.Count == 0 && bought.Count == 0)
        {
            return 0;
        }

        var sectors = await SectorsAsync(connection, transaction, cancellation);
        var ranks = await RanksAsync(connection, transaction, index, night, sectors, cancellation);
        var (breadth, floor, indexSettings) = large ? (null, 0d, null) : await IndexNightAsync(connection, transaction, index, night, cancellation);
        var written = 0;

        foreach (var pick in picks)
        {
            var plan = large
                ? await LargePlanAsync(connection, transaction, pick, night, cancellation)
                : await IndexPlanAsync(connection, transaction, index, pick, night, indexSettings, cancellation);
            var market = large
                ? await LargeMarketAsync(connection, transaction, pick.Ticker, night, true, cancellation)
                : new MarketReading(breadth, floor, true);

            written += await WriteAsync(connection, transaction, index, night, pick.Family, pick.Ticker, pick.Place, plan, market, sectors, ranks, cancellation);
        }

        var place = 0;

        foreach (var buy in bought)
        {
            var plan = new Plan(buy.Entry, null, null, null, [FormattableString.Invariant($"it led {buy.Sector} at the rebalance of {night:yyyy-MM-dd}, its trend and its beta passing")]);
            var market = large
                ? await LargeMarketAsync(connection, transaction, buy.Ticker, night, false, cancellation)
                : new MarketReading(breadth, floor, false);

            written += await WriteAsync(connection, transaction, index, night, Heavyweights, buy.Ticker, ++place, plan, market, sectors, ranks, cancellation);
        }

        return written;
    }

    // A card's plan: its buy, its stop and target where the rule sets them, the sessions it is held for at most where it
    // caps them, and the gates its first line names.
    sealed record Plan(decimal? Entry, decimal? Stop, decimal? Target, int? Cap, IReadOnlyList<string> Gates);

    sealed record Pick(string Ticker, string Family, int Place);

    sealed record Bought(string Ticker, string Sector, decimal? Entry);

    async Task<int> WriteAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, string family, string ticker, int place, Plan plan, MarketReading market, IReadOnlyDictionary<string, string> sectors, IReadOnlyList<SectorPlace> ranks, CancellationToken cancellation)
    {
        var named = NameOf(index);
        var words = SetupFamilies.Named(family)?.Label.ToLowerInvariant() ?? SetupFamilies.SectorHeavyweights.Heading.ToLowerInvariant();
        var (state, dollarVolume, value) = await ReadingAsync(connection, transaction, index, ticker, night, cancellation);
        var quarters = await QuartersAsync(connection, transaction, ticker, night, cancellation);
        var sector = sectors.GetValueOrDefault(ticker);
        var record = await RecordAsync(connection, transaction, index, family, cancellation);
        var held = record is null ? null : RuleRecordFigures.HeldBy(record.EndedBy, settings.HeldShare);
        var report = await ReportAsync(connection, transaction, ticker, night, cancellation);
        var noStop = plan.Stop is null;
        double? trip = plan.Entry is not { } buy
            ? null
            : noStop
                ? TradeCost.InPercent(value, buy, buy)
                : plan.Stop is { } stop && buy > stop ? TradeCost.InRisk(value, buy, stop, buy) : null;

        CardLine[] lines =
        [
            CardLines.Trend(words, named, plan.Gates),
            CardLines.Business(CardLines.Business(quarters, night, sector), state, settings),
            CardLines.Market(market, sector is null ? null : ranks.FirstOrDefault(rank => rank.Sector == sector), named, settings),
            CardLines.Earnings(report, held, plan.Cap, settings),
            CardLines.Cost(dollarVolume, trip, noStop, index == IndexFamilies.LargeIndex, settings),
        ];

        await ExecuteAsync(connection, transaction, InsertCard,
        [
            ("$index", index),
            ("$night", Stamp(night)),
            ("$family", family),
            ("$ticker", ticker),
            ("$place", place),
            ("$entry", Price(plan.Entry)),
            ("$stop", Price(plan.Stop)),
            ("$target", Price(plan.Target)),
            ("$rule", FormattableString.Invariant($"The {words} on the {named}")),
            ("$settings", settings.Json()),
            ("$lines", LinesJson(lines)),
            ("$record", record is null ? DBNull.Value : RecordJson(record, held)),
        ], cancellation);

        return 1;
    }

    // The lines as a card stores them.
    public static string LinesJson(IReadOnlyList<CardLine> lines) =>
        JsonSerializer.Serialize(lines.Select(line => new { line = line.Number, name = line.Name, verdict = line.Mark, words = line.Words }));

    // A rule's record as a card read it.
    sealed record Record(string Rule, int Trades, double? Won, double? Average, string Unit, int? MedianSessions, IReadOnlyList<int> EndedBy, double? WorstClose, string From, string Through, string Membership);

    string RecordJson(Record record, int? held) => JsonSerializer.Serialize(new
    {
        rule = record.Rule,
        trades = record.Trades,
        won = record.Won,
        average = record.Average,
        unit = record.Unit,
        medianSessions = record.MedianSessions,
        heldSessions = held,
        heldShare = settings.HeldShare,
        worstClose = record.WorstClose,
        from = record.From,
        through = record.Through,
        membership = record.Membership,
    });

    static async Task<IReadOnlyList<Pick>> PicksAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string index, DateOnly night, CancellationToken cancellation)
    {
        var picks = new List<Pick>();

        await using var command = Command(connection, transaction, sql, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            picks.Add(new Pick(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return picks;
    }

    static async Task<IReadOnlyList<Bought>> BoughtAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string index, DateOnly night, CancellationToken cancellation)
    {
        var bought = new List<Bought>();

        await using var command = Command(connection, transaction, sql, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bought.Add(new Bought(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : Money(reader.GetString(2))));
        }

        return bought;
    }

    // An S&P 500 pick's plan: the pullback's off the swing filter's row, on the plan its trade gate read, and every other
    // family's off its own row, a trailing family's with no target; its gates named by their stored reasons.
    async Task<Plan> LargePlanAsync(SqliteConnection connection, SqliteTransaction transaction, Pick pick, DateOnly night, CancellationToken cancellation)
    {
        var cap = SetupFamilies.Named(pick.Family)?.CapSessions;

        if (pick.Family == SetupFamilies.Pullback)
        {
            await using var command = Command(connection, transaction, GateRow, [("$night", Stamp(night)), ("$ticker", pick.Ticker)]);
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (!await reader.ReadAsync(cancellation))
            {
                return new Plan(null, null, null, cap, []);
            }

            var gates = FamilyRule.GatesOf(reader.GetString(5));
            var clear = gates.FirstOrDefault(gate => gate.Name == "trade")?.Values.GetValueOrDefault("input") == "clear";

            return new Plan(
                MoneyOrNone(reader, 0),
                MoneyOrNone(reader, clear ? 3 : 1),
                MoneyOrNone(reader, clear ? 4 : 2),
                cap,
                [.. gates.Select(gate => $"{gate.Name}, {gate.Reason}")]);
        }

        await using (var command = Command(connection, transaction, FamilyRow, [("$night", Stamp(night)), ("$ticker", pick.Ticker), ("$family", pick.Family)]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                return new Plan(
                    MoneyOrNone(reader, 0),
                    MoneyOrNone(reader, 1),
                    MoneyOrNone(reader, 2),
                    cap,
                    [.. FamilyRule.GatesOf(reader.GetString(3)).Select(gate => $"{gate.Name}, {gate.Reason}")]);
            }
        }

        return new Plan(null, null, null, cap, []);
    }

    // An S&P 400 or 600 pick's plan off its index's row, and its gates as the night's settings state its rule: the floors,
    // the profit gate, the market check and the family's own setting, since those rows store the rule and not its values.
    static async Task<Plan> IndexPlanAsync(SqliteConnection connection, SqliteTransaction transaction, string index, Pick pick, DateOnly night, JsonElement? settings, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, IndexFamilyRow, [("$index", index), ("$night", Stamp(night)), ("$ticker", pick.Ticker), ("$family", pick.Family)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var gates = IndexGates(settings, pick.Family);

        if (!await reader.ReadAsync(cancellation))
        {
            return new Plan(null, null, null, null, gates);
        }

        return new Plan(
            MoneyOrNone(reader, 0),
            MoneyOrNone(reader, 1),
            MoneyOrNone(reader, 2),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            gates);
    }

    // The gates an S&P 400 or 600 rule reads, in the words its night's settings state them.
    public static IReadOnlyList<string> IndexGates(JsonElement? settings, string family)
    {
        if (settings is not { } read)
        {
            return [];
        }

        var gates = new List<string>();

        if (read.TryGetProperty("floors", out var floors))
        {
            gates.Add(FormattableString.Invariant($"floors, a close of at least ${floors.GetProperty("price").GetDecimal():0.##} and {floors.GetProperty("dollarVolume").GetDecimal() / 1_000_000m:0.##} million dollars a session over {floors.GetProperty("sessions").GetInt32()} sessions"));
        }

        if (read.TryGetProperty("profitGate", out var profit))
        {
            gates.Add(FormattableString.Invariant($"profit, the {profit.GetProperty("quarters").GetInt32()} newest quarters' net income above nothing"));
        }

        if (read.TryGetProperty("marketFloor", out var market) && family != Heavyweights)
        {
            gates.Add(FormattableString.Invariant($"market, the index's breadth at or above {market.GetDouble():0%}"));
        }

        if (read.TryGetProperty(family, out var rule))
        {
            var words = rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("setting", out var setting) ? setting.GetString() : rule.GetString();

            if (!string.IsNullOrWhiteSpace(words))
            {
                gates.Add("its setting, " + words.Replace("|", ", ", StringComparison.Ordinal).Replace("=", " ", StringComparison.Ordinal));
            }
        }

        return gates;
    }

    static async Task<(double? Breadth, double Floor, JsonElement? Settings)> IndexNightAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, IndexNightRow, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation) || reader.IsDBNull(1))
        {
            return (null, 0, null);
        }

        using var document = JsonDocument.Parse(reader.GetString(1));
        var settings = document.RootElement.Clone();

        return (reader.IsDBNull(0) ? null : reader.GetDouble(0), settings.TryGetProperty("marketFloor", out var floor) ? floor.GetDouble() : 0, settings);
    }

    // An S&P 500 member's market gate as the swing filter stored it, its breadth and the floor it was read against.
    static async Task<MarketReading> LargeMarketAsync(SqliteConnection connection, SqliteTransaction transaction, string ticker, DateOnly night, bool readByTheFamily, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, GateRow, [("$night", Stamp(night)), ("$ticker", ticker)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation))
        {
            return new MarketReading(null, 0, readByTheFamily);
        }

        var market = FamilyRule.GatesOf(reader.GetString(5)).FirstOrDefault(gate => gate.Name == "market");

        return new MarketReading(
            market is not null && double.TryParse(market.Values.GetValueOrDefault("breadth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var breadth) ? breadth : null,
            market is not null && double.TryParse(market.Values.GetValueOrDefault("floor"), NumberStyles.Float, CultureInfo.InvariantCulture, out var floor) ? floor : 0,
            readByTheFamily);
    }

    static async Task<(string? State, decimal? DollarVolume, decimal? Value)> ReadingAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string ticker, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, ReadingRow, [("$index", index), ("$night", Stamp(night)), ("$ticker", ticker)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (reader.IsDBNull(0) ? null : reader.GetString(0), MoneyOrNone(reader, 1), MoneyOrNone(reader, 2))
            : (null, null, null);
    }

    static async Task<IReadOnlyList<FiledIncome>> QuartersAsync(SqliteConnection connection, SqliteTransaction transaction, string ticker, DateOnly night, CancellationToken cancellation)
    {
        var quarters = new List<FiledIncome>();

        await using var command = Command(connection, transaction, QuartersBefore, [("$ticker", ticker), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            quarters.Add(new FiledIncome(Date(reader.GetString(0)), Date(reader.GetString(1)), MoneyOrNone(reader, 2), MoneyOrNone(reader, 3), MoneyOrNone(reader, 4)));
        }

        return quarters;
    }

    static async Task<IReadOnlyDictionary<string, string>> SectorsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        var sectors = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var command = Command(connection, transaction, SectorsBefore, []);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sectors[reader.GetString(0)] = reader.GetString(1);
        }

        return sectors;
    }

    // The index's sectors ranked by their members' mean return over the window to the night, each member's from its close
    // on the session the window starts on to its close on the night, a member missing either read in no sector.
    static async Task<IReadOnlyList<SectorPlace>> RanksAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyDictionary<string, string> sectors, CancellationToken cancellation)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = Command(connection, transaction, MembersOf, [("$index", index), ("$session", Stamp(night))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add(reader.GetString(0));
            }
        }

        if (await ScalarAsync(connection, WindowStart, [("$night", Stamp(night)), ("$back", CardLines.SectorSessions)], cancellation, transaction) is not string start)
        {
            return [];
        }

        var closes = new Dictionary<string, (decimal? From, decimal? Night)>(StringComparer.Ordinal);

        await using (var command = Command(connection, transaction, ClosesOn, [("$from", start), ("$night", Stamp(night))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var ticker = reader.GetString(0);

                if (!members.Contains(ticker))
                {
                    continue;
                }

                var close = Money(reader.GetString(2));
                var held = closes.GetValueOrDefault(ticker);

                closes[ticker] = reader.GetString(1) == start ? held with { From = close } : held with { Night = close };
            }
        }

        return CardLines.Rank(closes
            .Where(member => member.Value is { From: > 0m, Night: not null } && sectors.ContainsKey(member.Key))
            .Select(member => (sectors[member.Key], Statistic.FromRatio(member.Value.Night!.Value / member.Value.From!.Value) - 1)));
    }

    // The next report on file whose session comes after the night, the session it moves counted on the exchange's calendar.
    static async Task<ReportAhead?> ReportAsync(SqliteConnection connection, SqliteTransaction transaction, string ticker, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, ReportsAfter, [("$ticker", ticker), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var date = Date(reader.GetString(0));
            var timing = (reader.IsDBNull(1) ? null : reader.GetString(1)) switch
            {
                "before" => EventTiming.Before,
                "after" => EventTiming.After,
                _ => EventTiming.Unstated,
            };
            var moves = timing == EventTiming.After ? date.AddDays(1) : date;

            // A report that moved the night or an earlier session is behind the trade, and the next one is read.
            if (moves <= night)
            {
                continue;
            }

            return new ReportAhead(date, timing, CardLines.SessionsTo(night, date, timing));
        }

        return null;
    }

    static async Task<Record?> RecordAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string family, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, RecordOf, [("$index", index), ("$family", family)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation))
        {
            return null;
        }

        return new Record(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            RuleRecordFigures.EndedByFrom(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10));
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation) =>
        await ExecuteAsync(connection, null, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Stage),
            ("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$outcome", outcome),
            ("$rows_written", rows),
            ("$detail", detail),
        ], cancellation);

    // The stage's row after a failure outside any index's cards, on a connection of its own, and none where the store will
    // not take it: recording the failure is never what stops the night.
    async Task AppendAfterAFailureAsync(string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await AppendAsync(connection, runId, startedAt, rows, NotComputed, detail, cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
        }
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, sql, parameters);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = Command(connection, transaction, sql, parameters);

        return await command.ExecuteScalarAsync(cancellation);
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static decimal Money(string stored) => decimal.Parse(stored, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

    static decimal? MoneyOrNone(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : Money(reader.GetString(column));

    static object Price(decimal? price) => price is { } stated ? stated.ToString(CultureInfo.InvariantCulture) : DBNull.Value;
}
