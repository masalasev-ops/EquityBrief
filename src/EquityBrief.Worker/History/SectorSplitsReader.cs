using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// The sector splits over 2005 onward, read-only and writing no store row: first the map's agreement with today's GICS on
// the names both read and the headings it names nothing for; then on each index each family's settled setups, the long
// ledger's as ruled before 2019 and the standing ledger's from it, split by its sector fund's 5-day change, the fund against
// its 20-day average and its sector's third among the sectors by its members' 63-day mean return, each on its sector as it
// stood, by era, and the three indices together; and the pullback with its sector below its 20-day average, said to go to
// the operator as a variant only where it holds in every era over the three together.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public sealed class SectorSplitsReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.LongHolding, Touch.Read),
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.LongMarketBar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
        ],
        Feeds: []);

    static readonly string[] Indices = ["GSPC", "MID", "SML"];

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Shown(double? figure) => figure is { } held ? held.ToString("0.000", CultureInfo.InvariantCulture) : "none";

    static string Said(SplitSide side) =>
        FormattableString.Invariant($"{side.Trades} trades, {(side.Trades > 0 ? (100.0 * side.Targets / side.Trades).ToString("0", CultureInfo.InvariantCulture) + "%" : "none")} at target, edge after costs {Shown(side.Edge)}");

    public static async Task<IReadOnlyList<string>> MeasureAsync(string databaseFile, Action<string>? progress = null, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        async Task EachAsync(string sql, IReadOnlyList<(string Name, string Value)> parameters, Action<SqliteDataReader> row)
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
                row(reader);
            }
        }

        var lines = new List<string>();
        var calendar = new List<DateOnly>();

        await EachAsync("SELECT session_date FROM long_market_bar WHERE series = 'GSPC' ORDER BY session_date;", [], row => calendar.Add(Day(row.GetString(0))));

        var placeOf = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);

        double?[] Closes(IEnumerable<(DateOnly Day, double Close)> read)
        {
            var closes = new double?[calendar.Count];

            foreach (var (day, close) in read)
            {
                if (placeOf.TryGetValue(day, out var at))
                {
                    closes[at] = close;
                }
            }

            return closes;
        }

        var funds = new Dictionary<string, double?[]>(StringComparer.Ordinal);

        foreach (var fund in GicsSectors.Funds.Values)
        {
            var read = new List<(DateOnly, double)>();

            await EachAsync("SELECT session_date, close FROM long_market_bar WHERE series = $series;", [("$series", fund)], row =>
                read.Add((Day(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1))))));
            funds[fund] = Closes(read);
        }

        // Each code's filed sector today, the companies pull's and then a fetch's, the newest winning.
        var filed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var query in new[] { "SELECT ticker, sector FROM pulled_company WHERE sector IS NOT NULL ORDER BY pull;", "SELECT ticker, sector FROM company WHERE sector IS NOT NULL ORDER BY fetched_at;" })
        {
            await EachAsync(query, [], row => filed[row.GetString(0)] = row.GetString(1));
        }

        // The map against today's GICS: each code's newest heading on each index, the names both read, and the headings it
        // names nothing for.
        var newest = new Dictionary<(string, string), (string Period, string Heading)>();
        var unmapped = new SortedSet<string>(StringComparer.Ordinal);

        await EachAsync("SELECT index_code, period, ticker, industry FROM long_holding WHERE ticker IS NOT NULL AND industry IS NOT NULL;", [], row =>
        {
            var key = (row.GetString(0), row.GetString(2));

            if (!newest.TryGetValue(key, out var held) || string.CompareOrdinal(row.GetString(1), held.Period) > 0)
            {
                newest[key] = (row.GetString(1), row.GetString(3));
            }

            if (SectorMap.Of(row.GetString(3)) is null)
            {
                unmapped.Add(row.GetString(3));
            }
        });

        var both = newest.Where(pair => filed.ContainsKey(pair.Key.Item2) && SectorMap.Of(pair.Value.Heading) is not null).ToArray();
        var agreed = both.Count(pair => SectorMap.Of(pair.Value.Heading)!.Value.Sector == filed[pair.Key.Item2]);

        lines.Add(FormattableString.Invariant($"the map against today's GICS: {agreed} of {both.Length} names both read agree, {(both.Length > 0 ? (100.0 * agreed / both.Length).ToString("0.0", CultureInfo.InvariantCulture) : "none")}%; {unmapped.Count} heading(s) it names nothing for{(unmapped.Count > 0 ? ": " + string.Join("; ", unmapped) : string.Empty)}"));

        var all = new List<SplitSetup>();

        foreach (var index in Indices)
        {
            progress?.Invoke($"sector splits: reading the {index}");

            var headings = new Dictionary<string, List<(DateOnly Period, string Heading)>>(StringComparer.Ordinal);

            await EachAsync("SELECT period, ticker, industry FROM long_holding WHERE index_code = $index AND ticker IS NOT NULL AND industry IS NOT NULL ORDER BY period;", [("$index", index)], row =>
            {
                if (!headings.TryGetValue(row.GetString(1), out var held))
                {
                    headings[row.GetString(1)] = held = [];
                }

                held.Add((Day(row.GetString(0)), row.GetString(2)));
            });

            var spans = new Dictionary<string, List<(DateOnly Joined, DateOnly? Left)>>(StringComparer.Ordinal);

            await EachAsync("SELECT ticker, joined, \"left\" FROM long_span WHERE index_code = $index;", [("$index", index)], row =>
            {
                if (!spans.TryGetValue(row.GetString(0), out var held))
                {
                    spans[row.GetString(0)] = held = [];
                }

                held.Add((Day(row.GetString(1)), row.IsDBNull(2) ? null : Day(row.GetString(2))));
            });

            // A code's sector on a session as it stood: its heading at the newest quarter end on or before the session, the
            // first where the session is before every one, through the schedules' end, and its filed GICS sector after.
            string? SectorOn(string ticker, DateOnly day)
            {
                if (day > SectorMap.SchedulesThrough)
                {
                    return GicsSectors.On(ticker, filed.GetValueOrDefault(ticker), day);
                }

                if (!headings.TryGetValue(ticker, out var held) || held.Count == 0)
                {
                    return null;
                }

                var at = held.FindLastIndex(one => one.Period <= day);

                return SectorMap.AsItStood(held[at < 0 ? 0 : at].Heading, day);
            }

            var closes = new Dictionary<string, double?[]>(StringComparer.Ordinal);

            foreach (var ticker in spans.Keys)
            {
                var read = new List<(DateOnly, double)>();

                await EachAsync("SELECT session_date, close FROM long_bar WHERE ticker = $ticker;", [("$ticker", ticker)], row =>
                    read.Add((Day(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1))))));
                closes[ticker] = Closes(read);
            }

            // Each session's sectors ranked by their members' mean 63-day return as they stood.
            var thirds = new Dictionary<string, string>[calendar.Count];

            for (var at = 0; at < calendar.Count; at++)
            {
                var day = calendar[at];
                var means = spans
                    .Where(pair => LongLedger.MemberOn(pair.Value, day))
                    .Select(pair => (Sector: SectorOn(pair.Key, day), Return: SectorSplits.Return(closes[pair.Key], at)))
                    .Where(one => one.Sector is not null && one.Return is not null)
                    .GroupBy(one => one.Sector!, StringComparer.Ordinal)
                    .Select(group => (Sector: group.Key, Mean: group.Average(one => one.Return!.Value)))
                    .OrderByDescending(one => one.Mean)
                    .ToArray();

                thirds[at] = means.Select((one, place) => (one.Sector, Third: SectorSplits.Third(place, means.Length))).ToDictionary(one => one.Sector, one => one.Third, StringComparer.Ordinal);
            }

            var setups = new List<SplitSetup>();

            void Add(SqliteDataReader row)
            {
                var family = row.GetString(0);
                var ticker = row.GetString(1);
                var day = Day(row.GetString(2));

                if (!placeOf.TryGetValue(day, out var at))
                {
                    return;
                }

                var entry = Statistic.FromPrice(Money.FromStorage(row.GetString(3)));
                var stop = Statistic.FromPrice(Money.FromStorage(row.GetString(4)));
                double? value = row.IsDBNull(5) ? null : row.GetDouble(5);
                double? result = row.IsDBNull(6) ? null : row.GetDouble(6);
                double? edge = row.IsDBNull(7) ? null : row.GetDouble(7);
                var book = LikeSetups.IsBook(family);
                var after = LikeSetups.AfterCost(book ? Core.Loop.ChanceForm.Book : Core.Loop.ChanceForm.Trailing, result, null, entry, stop, value);
                double? cost = result is { } made && after is { } net ? (book ? ((made * 100) - net) / 100 : made - net) : null;
                var sector = SectorOn(ticker, day);
                var fund = sector is not null && funds.TryGetValue(SectorMap.FundOf(sector), out var series) ? series : null;

                setups.Add(new SplitSetup(
                    index,
                    family,
                    LongLedger.EraOf(day.Year),
                    row.GetInt64(9) == 1,
                    row.GetString(8) == SetupEnds.Target,
                    edge is { } held && cost is { } paid ? held - paid : null,
                    fund is null ? null : SectorSplits.Change(fund, at),
                    fund is null ? null : SectorSplits.Average(fund, at),
                    sector is not null ? thirds[at].GetValueOrDefault(sector) : null));
            }

            await EachAsync("SELECT family, ticker, session_date, entry, stop, company_value, result, edge, end, settled FROM long_setup WHERE index_code = $index;", [("$index", index)], Add);
            await EachAsync("SELECT family, ticker, session_date, entry, stop, company_value, result, edge, end, settled FROM setup WHERE index_code = $index AND session_date >= $from;", [("$index", index), ("$from", LongLedger.StandingFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))], Add);

            lines.AddRange(Lines(index, setups));
            all.AddRange(setups);
        }

        lines.AddRange(Lines("the three together", all));

        // The variant: the pullback with its sector below its 20-day average against above it, in every era over the three
        // together, each index beside.
        var pullbacks = all.Where(setup => setup.Family == SetupFamilies.Pullback).ToArray();
        var eras = LongLedger.Eras
            .Select(era => (era.Era, SectorSplits.Side(pullbacks.Where(setup => setup.Era == era.Era && setup.Average == SectorSplits.Below)), SectorSplits.Side(pullbacks.Where(setup => setup.Era == era.Era && setup.Average == SectorSplits.Above))))
            .ToArray();
        var holds = SectorSplits.VariantHolds(eras);

        lines.Add((holds
            ? "the pullback with its sector below its 20-day average holds in every era over the three together, at least 50 trades a side, and goes to the operator as a variant; nothing is registered"
            : "the pullback with its sector below its 20-day average does not hold in every era over the three together, at least 50 trades a side, and goes to no one")
            + ": " + string.Join("; ", eras.Select(era => $"{era.Era} below {Said(era.Item2)} against above {Said(era.Item3)}")));

        foreach (var index in Indices)
        {
            var own = pullbacks.Where(setup => setup.Index == index).ToArray();

            lines.Add($"beside it, the {index}: " + string.Join("; ", LongLedger.Eras.Select(era => $"{era.Era} below {Said(SectorSplits.Side(own.Where(setup => setup.Era == era.Era && setup.Average == SectorSplits.Below)))} against above {Said(SectorSplits.Side(own.Where(setup => setup.Era == era.Era && setup.Average == SectorSplits.Above)))}")));
        }

        return lines;
    }

    // Each family's three splits on an index, or the three together, side by side by era.
    static IEnumerable<string> Lines(string over, IReadOnlyList<SplitSetup> setups)
    {
        (string Name, Func<SplitSetup, string?> Read, string[] Sides)[] splits =
        [
            ("its fund's 5-day change", setup => setup.Change, [SectorSplits.Up, SectorSplits.Down]),
            ("its fund against its 20-day average", setup => setup.Average, [SectorSplits.Above, SectorSplits.Below]),
            ("its sector's rank by 63-day return", setup => setup.Rank, [SectorSplits.Top, SectorSplits.Middle, SectorSplits.Bottom]),
        ];

        foreach (var family in setups.GroupBy(setup => setup.Family, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (var (name, read, sides) in splits)
            {
                foreach (var era in LongLedger.Eras)
                {
                    var inEra = family.Where(setup => setup.Era == era.Era).ToArray();

                    if (inEra.Length == 0)
                    {
                        continue;
                    }

                    yield return $"{over} {family.Key} by {name}, {era.Era}: " + string.Join("; ", sides.Select(side => $"{side} {Said(SectorSplits.Side(inEra.Where(setup => read(setup) == side)))}"))
                        + $"; none read {inEra.Count(setup => read(setup) is null)}";
                }
            }
        }
    }
}
