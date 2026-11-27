using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.31a: the trained partner learns an index's own members over 2005 to 2014 and is checked on 2015
// to 2018; which readings mattered most, its check's spread, a reading's fifths and the grid of two are worked by hand over
// a constructed tree and table; its rows read no other index's members and nothing after 2018, and its tables hold no name,
// ticker or date; and no rule's version reads any of it.
// see: The trained partner learns each index's members over 2005 to 2014 and is checked on 2015 to 2018, and its tables name no stock and read nothing from 2019
public partial class FixtureExpectations
{
    internal static string[] PartnerClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The trained partner"),
    ];

    internal static string[] PartnerRows => [.. ComponentAccess.PartnerDeskRows, .. SchemaColumns.PartnerStoreRows, .. PartnerClaims];

    static readonly int PartnerRsi = LedgerReadings.IndexOf("rsi");

    static readonly int PartnerVolume = LedgerReadings.IndexOf("volume_ratio");

    static PartnerRow PartnerAt(DateOnly session, double rsi, double label, int endedDays = 30)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[PartnerRsi] = rsi;
        readings[PartnerVolume] = 1.0;

        return new PartnerRow(session, session.AddDays(endedDays), readings, label);
    }

    // One tree over rsi and the volume ratio, splitting on rsi alone at 50: 0 at or under it, 1 over it.
    static TreeModel PartnerTree() => new(
        [PartnerRsi, PartnerVolume],
        0,
        [new ScoreTree([0], [50f], [~0], [~1], [0.0, 1.0], 1)],
        4,
        -1,
        1,
        "constructed",
        new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal));

    [Fact]
    public void ThePartnersImportancesSpreadFifthsAndGridAreWorkedByHandOverAConstructedTable()
    {
        PartnerRow[] rows =
        [
            // Four rows to learn on, 2005 to 2014, rsi 40, 45, 55 and 60: their median 50.
            PartnerAt(new DateOnly(2006, 3, 1), 40, 0.0),
            PartnerAt(new DateOnly(2008, 3, 3), 45, 0.0),
            PartnerAt(new DateOnly(2011, 3, 1), 55, 0.2),
            PartnerAt(new DateOnly(2014, 3, 3), 60, 0.2),

            // Five to check on, 2015 to 2018.
            PartnerAt(new DateOnly(2015, 3, 2), 60, 0.1),
            PartnerAt(new DateOnly(2016, 3, 1), 60, 0.1),
            PartnerAt(new DateOnly(2016, 6, 1), 40, -0.1),
            PartnerAt(new DateOnly(2017, 3, 1), 40, -0.1),
            PartnerAt(new DateOnly(2018, 3, 1), 40, 0.0),

            // Neither: a month of 2014 ending in 2015, one of 2018 ending in 2019, and a row of 2019 with a figure that would
            // move every mean it reached.
            PartnerAt(new DateOnly(2014, 12, 19), 60, 99),
            PartnerAt(new DateOnly(2018, 12, 14), 60, 99),
            PartnerAt(new DateOnly(2019, 3, 1), 60, 99),
        ];

        var (train, check) = Partner.Split(rows);

        Assert.Equal([0, 1, 2, 3], train);
        Assert.Equal([4, 5, 6, 7, 8], check);

        // Setting rsi to its median of 50 moves each check row's score from 1 to 0 where its rsi is 60, two of five, and
        // nothing where it is 40: 0.4. The volume ratio moves nothing: 0.
        var model = PartnerTree();

        Assert.Equal([(PartnerRsi, 0.4), (PartnerVolume, 0.0)], Partner.Importances(model, rows, train, check));

        // The fifth scored highest, one row, made 0.1; the fifth scored lowest, the first of the three scored 0, made -0.1.
        Assert.Equal(0.2, Partner.Spread(model, rows, check)!.Value, 9);

        // rsi's cut points off the four learning rows, 40, 45, 55 and 60: the first fifth at or under 40 holds 4 cases,
        // 0, -0.1, -0.1 and 0, a mean of -0.05; the second 45 alone, 0; the third 55 alone, 0.2; the fourth the three at 60,
        // 0.2, 0.1 and 0.1, a mean of 0.1333; the fifth none. No row of 2019 or ending in it is counted.
        Assert.Equal([40.0, 45.0, 55.0, 60.0], Partner.CutPoints(rows, train, PartnerRsi));

        var table = Partner.Table(rows, train, check, PartnerRsi);

        Assert.Equal([4, 1, 1, 3, 0], table.Select(cell => cell.Cases));
        Assert.Equal(-0.05, table[0].Mean!.Value, 9);
        Assert.Equal(0.0, table[1].Mean!.Value, 9);
        Assert.Equal(0.2, table[2].Mean!.Value, 9);
        Assert.Equal(0.4 / 3, table[3].Mean!.Value, 9);
        Assert.Null(table[4].Mean);

        // The grid of rsi and the volume ratio: every volume ratio 1, its cut points 1, so all stand in its first fifth, the
        // first column holding rsi's table and every other cell none.
        var grid = Partner.Grid(rows, train, check, PartnerRsi, PartnerVolume);

        Assert.Equal(25, grid.Count);
        Assert.Equal([4, 1, 1, 3, 0], grid.Where(cell => cell.SecondFifth == 1).Select(cell => cell.Cases));
        Assert.All(grid.Where(cell => cell.SecondFifth != 1), cell => Assert.Equal(0, cell.Cases));
        Assert.Equal("rsi", Partner.NameOf(PartnerRsi));
    }

    static void PartnerStore(TemporaryStore store, DateOnly[] calendar, int tickers)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var reading = connection.CreateCommand();
        using var bar = connection.CreateCommand();

        reading.Transaction = transaction;
        reading.CommandText = "INSERT INTO session_reading (index_code, session_date, ticker, readings, pin, source) VALUES ($index, $session, $ticker, $readings, $pin, 'build');";
        bar.Transaction = transaction;
        bar.CommandText = "INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES ($ticker, $session, $close, $close, $close, $close, $close, 1000, 'constructed');";

        foreach (var name in new[] { "$index", "$session", "$ticker", "$readings", "$pin" })
        {
            reading.Parameters.AddWithValue(name, string.Empty);
        }

        foreach (var name in new[] { "$ticker", "$session", "$close" })
        {
            bar.Parameters.AddWithValue(name, string.Empty);
        }

        reading.Parameters["$pin"].Value = LedgerReadings.Version;

        // Each S&P 600 member's month of sessions from each 21st: its rsi set on the month's first session, and its close
        // rising a twentieth over the month where that rsi stands over 50 and falling a twentieth where it does not. The
        // S&P 400's one member reads an rsi of 99, which no S&P 600 row holds.
        foreach (var (index, ticker, at) in Enumerable.Range(0, tickers).Select(at => ("SML", $"S{at}", at)).Append(("MID", "M0", 99)))
        {
            var close = 100m;

            for (var session = 0; session < calendar.Length; session++)
            {
                var month = session / Partner.LabelSessions;
                var rsi = at == 99 ? 99 : 30 + (((month * 7) + (at * 13)) % 41);

                if (session > 0 && session % Partner.LabelSessions == 0)
                {
                    var before = 30 + ((((month - 1) * 7) + (at * 13)) % 41);

                    close *= at == 99 ? 1m : before > 50 ? 1.05m : 0.95m;
                }

                var readings = new double?[LedgerReadings.Count];

                readings[PartnerRsi] = rsi;
                readings[PartnerVolume] = 1 + (session % 3);

                var day = calendar[session].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                reading.Parameters["$index"].Value = index;
                reading.Parameters["$session"].Value = day;
                reading.Parameters["$ticker"].Value = ticker;
                reading.Parameters["$readings"].Value = Worker.Ledger.SetupLedger.ReadingsText(readings);
                reading.ExecuteNonQuery();

                bar.Parameters["$ticker"].Value = ticker;
                bar.Parameters["$session"].Value = day;
                bar.Parameters["$close"].Value = close.ToString(CultureInfo.InvariantCulture);
                bar.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    // Over a constructed store of twelve S&P 600 members and one S&P 400 member from 2005 into 2019: the rows are the S&P 600's
    // alone, a member each 21 sessions by 2018, each month's return worked by hand; without the word nothing is fitted; with it
    // the trees learn on 2005 to 2014 alone and are checked on 2015 to 2018, rsi mattering most and the top fifth's month
    // above the bottom's; the tables name only the catalogue's readings; and the stores refuse an update and a delete.
    [Fact]
    public async Task ThePartnerLearnsAnIndexsOwnMembersOver2005To2014AndIsCheckedOn2015To2018()
    {
        using var store = new TemporaryStore().Migrated();
        var calendar = Weekdays(new DateOnly(2005, 1, 3), new DateOnly(2019, 6, 28));

        PartnerStore(store, calendar, 12);

        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 5, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var output = new StringWriter();
        var desk = new PartnerDesk(clock, store.DatabaseFile, store.Root, output);
        var rows = await desk.RowsAsync("SML");
        var last = Array.FindLastIndex(calendar, day => day.Year <= Partner.CheckThrough);
        var perMember = Enumerable.Range(0, last + 1).Count(at => at % Partner.LabelSessions == 0 && at + Partner.LabelSessions <= last);

        Assert.Equal(12 * perMember, rows.Count);
        Assert.All(rows, row => Assert.True(row.Ended.Year <= Partner.CheckThrough));
        Assert.DoesNotContain(rows, row => row.Readings[PartnerRsi] == 99);

        // Member S0's first month: rsi 30 on 2005-01-03, so its close falls a twentieth by the 21st session after.
        Assert.Equal(-0.05, rows.First(row => row.Session == calendar[0] && row.Readings[PartnerRsi] == 30).Label, 9);

        Assert.Equal(0, await desk.RunAsync("SML", word: false));
        Assert.Contains("nothing fitted", output.ToString(), StringComparison.Ordinal);

        var (train, check) = Partner.Split(rows);

        Assert.Equal(0, await desk.RunAsync("SML", word: true));

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT trained_rows, checked_rows, spread, model FROM partner_run WHERE index_code = 'SML';";

        using (var reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(train.Count, reader.GetInt32(0));
            Assert.Equal(check.Count, reader.GetInt32(1));
            Assert.True(reader.GetDouble(2) > 0, "the top fifth's month stands over the bottom's");
            Assert.Equal(train.Count, TreeModel.Parse(reader.GetString(3)).Rows);
            Assert.False(reader.Read());
        }

        command.CommandText = "SELECT reading FROM partner_cell WHERE kind = 'importance' ORDER BY fifth;";

        Assert.Equal("rsi", (string)command.ExecuteScalar()!);

        command.CommandText = "SELECT reading, COALESCE(second, '') FROM partner_cell;";

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                Assert.Contains(reader.GetString(0), Recipes.Fields);
                Assert.True(reader.GetString(1).Length == 0 || Recipes.Fields.Contains(reader.GetString(1)));
            }
        }

        foreach (var table in new[] { "partner_run", "partner_cell" })
        {
            Assert.Throws<SqliteException>(() => store.Execute($"UPDATE {table} SET index_code = 'x';"));
            Assert.Throws<SqliteException>(() => store.Execute($"DELETE FROM {table};"));
        }

        // No rule's version reads the partner or its tables.
        var pinned = CandidateEvaluators.All.SelectMany(evaluator => evaluator.OwnSources).Concat(CandidateEvaluator.EvaluationSources).Concat(RuleHooks.Sources).ToArray();

        Assert.DoesNotContain("src/EquityBrief.Core/Loop/Partner.cs", pinned);
        Assert.DoesNotContain("src/EquityBrief.Worker/Loop/PartnerDesk.cs", pinned);
    }
}
