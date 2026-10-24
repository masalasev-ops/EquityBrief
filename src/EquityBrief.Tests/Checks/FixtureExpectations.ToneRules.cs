using System.Globalization;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from the 20.8 correction of 2026-10-24: the per-rule verb over a constructed history hands the
// walk the release readings of every reading version whose history read is complete, so the drift's tone variant at a
// complete version is walked, where handed none it is kept not testable for the reading it lacks.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
// see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
public partial class FixtureExpectations
{
    // Three members of the S&P 400 over the weekdays from 2021-01-04 to 2022-02-25, 300 sessions, so 2022 is a test year.
    // Each closes a hundredth higher a session from 100 and trades 70,000 shares a session; each files four quarters
    // earning a dollar before the history opens; all three are today's members, read as survivors since no fund's
    // holdings are stored.
    static TemporaryStore ToneRulesStore()
    {
        var calendar = Weekdays(new DateOnly(2021, 1, 4), 300);
        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $close, $high, $low, $close, 70000, 'constructed', '2026-10-06T00:00:00Z', $close);";

        var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
        var session = insert.Parameters.Add("$day", SqliteType.Text);
        var close = insert.Parameters.Add("$close", SqliteType.Text);
        var high = insert.Parameters.Add("$high", SqliteType.Text);
        var low = insert.Parameters.Add("$low", SqliteType.Text);

        foreach (var name in new[] { "A", "B", "C" })
        {
            for (var day = 0; day < calendar.Length; day++)
            {
                var price = 100m + (0.01m * day);

                ticker.Value = name;
                session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                close.Value = price.ToString(CultureInfo.InvariantCulture);
                high.Value = (price + 1m).ToString(CultureInfo.InvariantCulture);
                low.Value = (price - 1m).ToString(CultureInfo.InvariantCulture);
                insert.ExecuteNonQuery();
            }
        }

        using var rest = connection.CreateCommand();

        rest.Transaction = transaction;
        rest.CommandText = string.Concat(
            from name in new[] { "A", "B", "C" }
            select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('MID', '{name}', 'US', NULL, NULL, NULL, 'constructed');")
            + string.Concat(
                from name in new[] { "A", "B", "C" }
                from quarter in new[] { ("2019-12-31", "2020-02-14"), ("2020-03-31", "2020-05-08"), ("2020-06-30", "2020-08-07"), ("2020-09-30", "2020-11-06") }
                select $"INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES ('{name}', '{quarter.Item1}', '{quarter.Item2}', '1.00', '2.00', NULL, 'constructed');");
        rest.ExecuteNonQuery();
        transaction.Commit();

        return store;
    }

    [Fact]
    public async Task ThePerRuleVerbHandsTheWalkEveryCompleteVersionsReleaseReadingsSoTheToneVariantIsWalked()
    {
        // The S&P 400's history above, reading version 1's history read complete, and the drift's tone variant registered
        // on the S&P 400 at version 1 beside its provisional rule.
        using var store = ToneRulesStore();

        PinVersion(store, complete: true);

        var registered = await RegisterVerbAt(store, new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), RegisterVerb.IndexVariants, "drift", "--index", "MID", "--variants", "readingVersion=1");

        Assert.Equal(0, registered.Code);

        // The per-rule verb, printing and writing nothing: the variant is walked and judged against the provisional rule,
        // its line naming no reading it lacks.
        using var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var code = await new WalkForwardTester(clock, store.DatabaseFile, store.Root, output).RulesAsync("MID", "2026-10", print: true);
        var said = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var tone = Assert.Single(said, line => line.StartsWith("drift, ", StringComparison.Ordinal) && line.Contains("release reading", StringComparison.Ordinal));

        Assert.Equal(0, code);
        Assert.DoesNotContain(RuleTests.ReleaseReading, tone, StringComparison.Ordinal);
        Assert.Contains("against", tone, StringComparison.Ordinal);
    }
}
