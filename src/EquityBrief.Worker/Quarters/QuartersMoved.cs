using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Readings;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Quarters;

// What one index's lists the change moves on one night: the picks the list drawn under the parts as read again adds and
// removes against the list drawn under the parts the night stored, and the members whose parts changed.
public sealed record QuartersMovedNight(DateOnly Night, string Index, IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int PartsChanged, bool MatchesTheNight);

// What reading each quarter from its filing and then its results release moves over the nights from a day, read against
// what each night stored and writing nothing. On the S&P 400 and 600 each member's profit and cover parts are read again,
// the profit over the quarters the night's fetch stored and the cover over those the fetches before it stored, as the
// member readings read it before the night's fetch; and each index's list is drawn again five a family in the night's own
// order with the trades the night held, a member the change passes drawn after the night's own passers, since the night
// stored no place for a member failing a part, and treated as holding no open trade. On the S&P 500 each member's state is
// read again over the quarters the fetches before the night stored, and the list is drawn again with the swing filter's
// passes ordered by the state, improving first, and the filter's own rank within a state, every other family's passes as
// stored. Each list drawn under the parts as read again is set against the same list drawn under the parts as stored, and
// whether that one is the night's own list is said beside it. It reads the store read-only and asks nothing of anyone.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
public static class QuartersMoved
{
    public const string Verb = "quarters-moved";

    // The picks of the night the operator asked about, each named part by part.
    public static DateOnly NamedNight { get; } = new(2026, 11, 5);

    public static IReadOnlyList<string> Named { get; } = ["EW", "OHI", "SBRA", "LTC"];

    const string LargeIndex = "GSPC";

    const string IndexNights = "SELECT DISTINCT session_date FROM index_family_night WHERE index_code = $index AND session_date >= $from AND fault IS NULL ORDER BY session_date;";

    const string LargeNights = "SELECT session_date, families FROM family_night WHERE session_date >= $from ORDER BY session_date;";

    const string Answers = "SELECT ticker, family, passed, place, gates FROM index_family_result WHERE index_code = $index AND session_date = $night;";

    const string IndexPicks = "SELECT ticker, family, state, held_family, held_night FROM index_family_pick WHERE index_code = $index AND session_date = $night;";

    const string LargePicks = "SELECT ticker, family, state, held_family, held_night FROM family_pick WHERE session_date = $night;";

    const string FilterPasses = "SELECT ticker, rank FROM gate_result WHERE session_date = $night AND passed = 1 AND version <> $replayed;";

    const string FamilyPasses = "SELECT ticker FROM family_result WHERE session_date = $night AND family = $family AND passed = 1 ORDER BY place, ticker;";

    const string StatesOn = "SELECT ticker, state FROM fundamental_reading WHERE session_date = $night;";

    const string SectorOf = "SELECT sector FROM company WHERE ticker = $ticker AND substr(fetched_at, 1, 10) <= $night ORDER BY fetched_at DESC LIMIT 1;";

    const string StoredReading = "SELECT index_code, profit, coverage FROM member_reading WHERE ticker = $ticker AND session_date = $night ORDER BY index_code LIMIT 1;";

    public static async Task<IReadOnlyList<QuartersMovedNight>> RunAsync(string databaseFile, DateOnly from, TextWriter output, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var moved = new List<QuartersMovedNight>();

        foreach (var index in IndexFamilies.Indices)
        {
            foreach (var night in await NightsAsync(connection, IndexNights, from, index, cancellation))
            {
                moved.Add(await IndexNightAsync(connection, index, night, cancellation));
            }
        }

        foreach (var (night, families) in await LargeNightsAsync(connection, from, cancellation))
        {
            moved.Add(await LargeNightAsync(connection, night, families, cancellation));
        }

        foreach (var one in moved.Where(one => one.Added.Count > 0 || one.Removed.Count > 0 || !one.MatchesTheNight))
        {
            output.WriteLine(FormattableString.Invariant(
                $"{Verb}: {Stamp(one.Night)} {one.Index}: {one.PartsChanged} member(s) whose parts changed, {one.Added.Count + one.Removed.Count} pick(s) moved") +
                (one.Removed.Count > 0 ? ", removed " + string.Join(", ", one.Removed) : string.Empty) +
                (one.Added.Count > 0 ? ", added " + string.Join(", ", one.Added) : string.Empty) +
                (one.MatchesTheNight ? string.Empty : "; drawn again under its stored parts the list is not the night's own, so its moves are read against the list drawn again"));
        }

        foreach (var index in moved.Select(one => one.Index).Distinct(StringComparer.Ordinal))
        {
            var nights = moved.Where(one => one.Index == index).ToArray();

            output.WriteLine(FormattableString.Invariant(
                $"{Verb}: {index}: {nights.Sum(one => one.Added.Count + one.Removed.Count)} pick(s) moved over {nights.Length} night(s) from {Stamp(from)}, {nights.Sum(one => one.Removed.Count)} removed and {nights.Sum(one => one.Added.Count)} added; {nights.Sum(one => one.PartsChanged)} member-night(s) whose parts changed"));
        }

        if (NamedNight >= from)
        {
            foreach (var ticker in Named)
            {
                output.WriteLine($"{Verb}: " + await NamedAsync(connection, ticker, NamedNight, cancellation));
            }
        }

        return moved;
    }

    // One S&P 400 or 600 night: each member's profit and cover parts read again, and the list drawn again.
    static async Task<QuartersMovedNight> IndexNightAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var answers = new List<(string Ticker, string Family, bool Passed, int? Place, IReadOnlyList<EquityBrief.Core.Filter.Gate>? Gates)>();

        await foreach (var row in RowsAsync(connection, Answers, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            answers.Add((
                row.GetString(0),
                row.GetString(1),
                row.GetInt64(2) == 1,
                row.IsDBNull(3) ? null : row.GetInt32(3),
                row.IsDBNull(4) ? null : FamilyRule.GatesOf(row.GetString(4))));
        }

        var tickers = answers.Select(answer => answer.Ticker).Distinct(StringComparer.Ordinal).ToArray();
        var profitRead = await IndexFamilies.QuartersReadAsync(connection, tickers, night, cancellation);
        var coverRead = await IndexFamilies.QuartersReadAsync(connection, tickers, night, cancellation, night.AddDays(-1));
        var parts = new Dictionary<string, (bool Profit, bool Cover)>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            var sector = await ScalarAsync(connection, SectorOf, [("$ticker", ticker), ("$night", Stamp(night))], cancellation) as string;

            parts[ticker] = (
                MemberReadings.Profit(IncomeOf(profitRead, ticker), night),
                MemberReadings.Coverage(IncomeOf(coverRead, ticker), night, sector));
        }

        bool PassesNow((string Ticker, string Family, bool Passed, int? Place, IReadOnlyList<EquityBrief.Core.Filter.Gate>? Gates) answer) =>
            answer.Gates is { } gates
                ? gates.All(gate => gate.Name == RuleRows.Profit ? parts[answer.Ticker].Profit : gate.Name == RuleRows.Cover ? parts[answer.Ticker].Cover : gate.Passed)
                : answer.Passed;

        var changed = answers.Where(answer => PassesNow(answer) != answer.Passed).Select(answer => answer.Ticker).Distinct(StringComparer.Ordinal).Count();

        IReadOnlyList<FamilyQualifiers> Qualifying(bool again) =>
        [
            .. IndexNightRead.Families.Select(family => new FamilyQualifiers(
                family,
                [
                    .. answers.Where(answer => answer.Family == family && answer.Passed && (!again || PassesNow(answer))).OrderBy(answer => answer.Place).ThenBy(answer => answer.Ticker, StringComparer.Ordinal).Select(answer => answer.Ticker),
                    .. again
                        ? answers.Where(answer => answer.Family == family && !answer.Passed && PassesNow(answer)).OrderBy(answer => answer.Ticker, StringComparer.Ordinal).Select(answer => answer.Ticker)
                        : [],
                ])),
        ];

        var (open, stored) = await PicksAsync(connection, IndexPicks, [("$index", index), ("$night", Stamp(night))], cancellation);

        return Compared(night, index, FamilyList.Draw(Qualifying(false), open), FamilyList.Draw(Qualifying(true), open), stored, changed);
    }

    // One S&P 500 night: each member's state read again, and the list drawn again with the swing filter's passes in the
    // order the states give them.
    static async Task<QuartersMovedNight> LargeNightAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> families, CancellationToken cancellation)
    {
        var passes = new List<(string Ticker, double Rank)>();

        await foreach (var row in RowsAsync(connection, FilterPasses, [("$night", Stamp(night)), ("$replayed", EquityBrief.Core.Filter.ReplayedResults.Version)], cancellation))
        {
            passes.Add((row.GetString(0), row.IsDBNull(1) ? double.MaxValue : row.GetDouble(1)));
        }

        var stored = new Dictionary<string, string?>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, StatesOn, [("$night", Stamp(night))], cancellation))
        {
            stored[row.GetString(0)] = row.GetString(1);
        }

        var read = await IndexFamilies.QuartersReadAsync(connection, passes.Select(pass => pass.Ticker), night, cancellation, night.AddDays(-1));
        var states = passes.ToDictionary(pass => pass.Ticker, pass => (string?)StateOf(read.GetValueOrDefault(pass.Ticker) ?? []), StringComparer.Ordinal);
        var changed = passes.Count(pass => !string.Equals(stored.GetValueOrDefault(pass.Ticker), states[pass.Ticker], StringComparison.Ordinal));
        var others = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var family in families.Where(family => family != SetupFamilies.Pullback))
        {
            var tickers = new List<string>();

            await foreach (var row in RowsAsync(connection, FamilyPasses, [("$night", Stamp(night)), ("$family", family)], cancellation))
            {
                tickers.Add(row.GetString(0));
            }

            others[family] = tickers;
        }

        IReadOnlyList<FamilyQualifiers> Qualifying(Func<string, string?> stateOf) =>
        [
            .. families.Select(family => new FamilyQualifiers(
                family,
                family == SetupFamilies.Pullback
                    ? [.. passes.OrderBy(pass => FundamentalState.Place(stateOf(pass.Ticker))).ThenBy(pass => pass.Rank).ThenBy(pass => pass.Ticker, StringComparer.Ordinal).Select(pass => pass.Ticker)]
                    : others.GetValueOrDefault(family) ?? [])),
        ];

        var (open, picked) = await PicksAsync(connection, LargePicks, [("$night", Stamp(night))], cancellation);

        return Compared(night, LargeIndex, FamilyList.Draw(Qualifying(ticker => stored.GetValueOrDefault(ticker)), open), FamilyList.Draw(Qualifying(ticker => states[ticker]), open), picked, changed);
    }

    // The two lists set against each other, each pick named with its family, and whether the one under the stored parts is
    // the night's own.
    static QuartersMovedNight Compared(DateOnly night, string index, IReadOnlyList<FamilyPick> before, IReadOnlyList<FamilyPick> after, IReadOnlySet<string> stored, int changed)
    {
        static HashSet<string> Listed(IReadOnlyList<FamilyPick> picks) =>
            [.. picks.Where(pick => pick.State == FamilyList.Listed).Select(pick => $"{pick.Ticker} ({pick.Family})")];

        var (was, now) = (Listed(before), Listed(after));

        return new QuartersMovedNight(
            night,
            index,
            [.. now.Except(was).Order(StringComparer.Ordinal)],
            [.. was.Except(now).Order(StringComparer.Ordinal)],
            changed,
            was.SetEquals(stored));
    }

    // The trades a night's list held back, by the family and night that listed each, and the picks it listed.
    static async Task<(IReadOnlyDictionary<string, HeldTrade> Open, IReadOnlySet<string> Listed)> PicksAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, sql, parameters, cancellation))
        {
            var state = row.GetString(2);

            if (state == FamilyList.OpenTrade && !row.IsDBNull(3) && !row.IsDBNull(4))
            {
                open.TryAdd(row.GetString(0), new HeldTrade(row.GetString(3), Date(row.GetString(4))));
            }
            else if (state == FamilyList.Listed)
            {
                listed.Add($"{row.GetString(0)} ({row.GetString(1)})");
            }
        }

        return (open, listed);
    }

    // One named pick's parts on a night, as the night stored them and as read again, with the quarters each reads.
    static async Task<string> NamedAsync(SqliteConnection connection, string ticker, DateOnly night, CancellationToken cancellation)
    {
        string? index = null;
        bool? profit = null;
        bool? coverage = null;

        await foreach (var row in RowsAsync(connection, StoredReading, [("$ticker", ticker), ("$night", Stamp(night))], cancellation))
        {
            index = row.GetString(0);
            profit = !row.IsDBNull(1) && row.GetInt64(1) == 1;
            coverage = row.IsDBNull(2) ? null : row.GetInt64(2) == 1;
        }

        string? state = null;

        await foreach (var row in RowsAsync(connection, "SELECT state FROM fundamental_reading WHERE ticker = $ticker AND session_date = $night;", [("$ticker", ticker), ("$night", Stamp(night))], cancellation))
        {
            state = row.GetString(0);
        }

        var profitRead = await IndexFamilies.QuartersReadAsync(connection, ticker, night, cancellation);
        var coverRead = await IndexFamilies.QuartersReadAsync(connection, ticker, night, cancellation, night.AddDays(-1));
        var sector = await ScalarAsync(connection, SectorOf, [("$ticker", ticker), ("$night", Stamp(night))], cancellation) as string;
        var income = profitRead.Select(quarter => quarter.Income).ToArray();
        var covered = coverRead.Select(quarter => quarter.Income).ToArray();

        static string Said(bool? held) => held is { } read ? (read ? "passes" : "fails") : "not read";

        static string Quarters(IEnumerable<ReadQuarter> quarters) =>
            string.Join(", ", quarters.Select(quarter => FormattableString.Invariant($"{quarter.PeriodEnd:yyyy-MM-dd} from its {(quarter.From == QuarterFrom.Filing ? "filing" : "release")}")));

        var four = profitRead.Take(MemberReadings.Quarters);
        var cover = MemberReadings.CoverQuarters(covered, night).Select(one => one.PeriodEnd).ToHashSet();

        return FormattableString.Invariant($"{ticker} on {night:yyyy-MM-dd} under {index ?? "no index the member readings hold"}: ")
            + $"the profit check {Said(profit)} as stored and {Said(MemberReadings.Profit(income, night))} as read again, over {Quarters(four)}; "
            + $"the interest cover {Said(coverage)} as stored and {Said(MemberReadings.Coverage(covered, night, sector))} as read again, over {Quarters(coverRead.Where(quarter => cover.Contains(quarter.PeriodEnd)))}; "
            + $"the state {state ?? "none"} as stored and {StateOf(coverRead)} as read again";
    }

    // A member's state over its quarters as the rules read them, the trajectory over the two newest.
    static string StateOf(IReadOnlyList<ReadQuarter> read) =>
        QuarterReadings.StateOf(QuarterReadings.Trajectory([.. read.Select(quarter => FundamentalReader.BusinessOf(quarter, null))]));

    static FiledIncome[] IncomeOf(IReadOnlyDictionary<string, IReadOnlyList<ReadQuarter>> read, string ticker) =>
        [.. (read.GetValueOrDefault(ticker) ?? []).Select(quarter => quarter.Income)];

    static async Task<IReadOnlyList<DateOnly>> NightsAsync(SqliteConnection connection, string sql, DateOnly from, string index, CancellationToken cancellation)
    {
        var nights = new List<DateOnly>();

        await foreach (var row in RowsAsync(connection, sql, [("$index", index), ("$from", Stamp(from))], cancellation))
        {
            nights.Add(Date(row.GetString(0)));
        }

        return nights;
    }

    static async Task<IReadOnlyList<(DateOnly Night, IReadOnlyList<string> Families)>> LargeNightsAsync(SqliteConnection connection, DateOnly from, CancellationToken cancellation)
    {
        var nights = new List<(DateOnly, IReadOnlyList<string>)>();

        await foreach (var row in RowsAsync(connection, LargeNights, [("$from", Stamp(from))], cancellation))
        {
            nights.Add((Date(row.GetString(0)), JsonSerializer.Deserialize<string[]>(row.GetString(1)) ?? []));
        }

        return nights;
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
