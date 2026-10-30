using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 21.7 what the tester matches the setups like a rule's listing among: every settled setup of a swing family the
// ledger holds on an index whose live rule passed it, each with the day its path ended, its readings, its result after
// costs and whether it won, and the newest tester run's ridge score fitted in each fold, a year each. A listing in a
// year is matched on the readings weighing most in that year's fold score, among the setups whose paths ended before the
// year began, so nothing that ended inside the fold's year reaches it; a listing in a year no fold score stands for, or
// holding none of the readings matched on, reads no expected value.
// see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
public sealed class LikePool(IReadOnlyList<LikePool.Settled> setups, IReadOnlyDictionary<(string Family, int Year), RidgeModel> folds)
{
    public sealed record Settled(string Family, string Ticker, DateOnly Session, DateOnly Ended, double?[] Readings, double? Result, bool? Won);

    const string Runs = "SELECT run_id FROM loop_run WHERE index_code = $index ORDER BY started_at DESC LIMIT 1;";

    const string Folds = "SELECT family, year, parameters FROM loop_model WHERE index_code = $index AND run_id = $run AND kind = $kind;";

    static readonly string SettledSwings =
        "SELECT family, ticker, session_date, ended_on, result, cost, \"end\", target, entry, stop, edge, company_value, "
        + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND settled = 1 AND live_pass = 1 AND ended_on IS NOT NULL AND family IN ('pullback', 'breakout', 'drift') ORDER BY session_date, family, ticker;";

    const int ReadingsFrom = 12;

    readonly Dictionary<(string Family, int Year), (SimilarSetups.Prepared Pool, IReadOnlyList<int> Readings, string[] Columns)?> prepared = [];

    public static LikePool None { get; } = new([], new Dictionary<(string Family, int Year), RidgeModel>());

    public int Count => setups.Count;

    public static async Task<LikePool> ReadAsync(SqliteConnection connection, string index, CancellationToken cancellation)
    {
        var folds = new Dictionary<(string Family, int Year), RidgeModel>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Runs;
            command.Parameters.AddWithValue("$index", index);

            if (await command.ExecuteScalarAsync(cancellation) is string run)
            {
                await using var models = connection.CreateCommand();

                models.CommandText = Folds;
                models.Parameters.AddWithValue("$index", index);
                models.Parameters.AddWithValue("$run", run);
                models.Parameters.AddWithValue("$kind", ScoreProcedures.Ridge);

                await using var reader = await models.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    folds[(reader.GetString(0), reader.GetInt32(1))] = RidgeModel.Parse(reader.GetString(2));
                }
            }
        }

        var setups = new List<Settled>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SettledSwings;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var family = reader.GetString(0);
                double? result = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                double? edge = reader.IsDBNull(10) ? null : reader.GetDouble(10);
                var form = SetupChance.FormOf(LikeSetups.IsBook(family), !reader.IsDBNull(7));
                var readings = new double?[LedgerReadings.Count];

                for (var at = 0; at < readings.Length; at++)
                {
                    readings[at] = reader.IsDBNull(ReadingsFrom + at) ? null : reader.GetDouble(ReadingsFrom + at);
                }

                setups.Add(new Settled(
                    family,
                    reader.GetString(1),
                    Day(reader.GetString(2)),
                    Day(reader.GetString(3)),
                    readings,
                    LikeSetups.AfterCost(
                        form,
                        result,
                        reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        Statistic.FromPrice(Money.FromStorage(reader.GetString(8))),
                        Statistic.FromPrice(Money.FromStorage(reader.GetString(9))),
                        reader.IsDBNull(11) ? null : reader.GetDouble(11)),
                    SetupChance.WonOf(form, reader.IsDBNull(6) ? null : reader.GetString(6), result, edge)));
            }
        }

        return new LikePool(setups, folds);
    }

    // The expected value of the setups like each listing of a walk, by its place, none where the pool holds no fold score
    // for the family.
    public Func<int, double?>? For(string family, RuleWalk walk, LoopRead read)
    {
        if (!folds.Keys.Any(key => key.Family == family))
        {
            return null;
        }

        var figures = new Dictionary<int, double?>();

        return place =>
        {
            if (figures.TryGetValue(place, out var held))
            {
                return held;
            }

            var listing = walk.Listings[place];
            var figure = walk.ReadingsAt(place) is { } readings
                ? ExpectedAt(family, read.Calendar[listing.Session].Year, read.Series[listing.Name].Name.Ticker, readings)
                : null;

            figures[place] = figure;

            return figure;
        };
    }

    // The expected value of the setups like one listing of a family's in a year: matched on the readings weighing most in
    // the year's fold score among the setups whose paths ended before the year began, the listing's own stock left out;
    // none where no fold score stands for the year, the listing holds none of the readings matched on, or fewer than the
    // fewest matched hold a result.
    public double? ExpectedAt(string family, int year, string ticker, IReadOnlyList<double?> readings) =>
        PoolOf(family, year) is { } pool && pool.Readings.All(reading => readings[reading] is not null)
            ? pool.Pool.Match(ticker, [.. pool.Readings.Select(reading => readings[reading]!.Value)], pool.Columns, SetupChance.FormOf(LikeSetups.IsBook(family), true))?.Chance?.Mean
            : null;

    // A fold's pool: the family's settled setups whose paths ended before the fold's year began and that hold every
    // reading its score weighs most, placed once; none where no fold score stands for the year.
    (SimilarSetups.Prepared Pool, IReadOnlyList<int> Readings, string[] Columns)? PoolOf(string family, int year)
    {
        if (prepared.TryGetValue((family, year), out var held))
        {
            return held;
        }

        (SimilarSetups.Prepared, IReadOnlyList<int>, string[])? pool = null;

        if (folds.TryGetValue((family, year), out var model))
        {
            var readings = SimilarSetups.Readings(model);
            var start = new DateOnly(year, 1, 1);
            var places = setups.Where(setup => setup.Family == family).Select(setup => setup.Session).Distinct().Order()
                .Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
            SimilarSetup[] among =
            [
                .. setups
                    .Where(setup => setup.Family == family && setup.Ended < start && readings.All(reading => setup.Readings[reading] is not null))
                    .Select(setup => new SimilarSetup(setup.Ticker, places[setup.Session], [.. readings.Select(reading => setup.Readings[reading]!.Value)], 0, setup.Result, setup.Won)),
            ];

            pool = (SimilarSetups.Prepare(among, readings.Count), readings, [.. readings.Select(reading => LedgerReadings.All[reading].Column)]);
        }

        prepared[(family, year)] = pool;

        return pool;
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
