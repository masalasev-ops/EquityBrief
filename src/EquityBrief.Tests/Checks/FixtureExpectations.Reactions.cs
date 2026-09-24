using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 11.7: each print's earnings reaction, worked by hand over constructed bars
// for every timing and edge, and over the fixture's captured calendar and bars.
public partial class FixtureExpectations
{
    [Fact]
    public void EachPrintsReactionIsWorkedByHandForEveryTimingAndAPrintTheBarsDoNotReachIsLeftOutAndCounted()
    {
        // Ten sessions, Monday 2026-03-02 to Friday 2026-03-13 less the weekend, each closing at the
        // price beside it.
        var monday = new DateOnly(2026, 3, 2);
        decimal[] closes = [100m, 110m, 99m, 99m, 120m, 90m, 90.9m, 91.8m, 94.2m, 95.4m];
        LadderBar[] bars =
        [
            .. closes.Select((close, day) => new LadderBar(Session(monday, day), close + 1, close - 1, close)),
        ];

        ReactionPrint[] prints =
        [
            // Before the open on Tuesday: Tuesday's own move, 110 on 100, +10.
            new(new DateOnly(2026, 3, 3), EventTiming.Before, "1.00", "1.10", "10"),
            // After the close on Tuesday: Wednesday's, 99 on 110, -10.
            new(new DateOnly(2026, 3, 3), EventTiming.After, "1.00", "0.90", "-10"),
            // Unstated on Friday: read on its own day, as the earnings rule reads it, 120 on 99.
            new(new DateOnly(2026, 3, 6), EventTiming.Unstated, "2.00", "2.50", "25"),
            // No estimate filed on the second Monday: its actual stands and no surprise does,
            // whatever difference the provider sent beside it.
            new(new DateOnly(2026, 3, 9), EventTiming.Before, null, "0.75", "0"),
            // After the close on the last stored session: the session it moved is not stored.
            new(new DateOnly(2026, 3, 13), EventTiming.After, "1.00", "1.00", "0"),
            // Before the first stored session: no close before it is stored, which is how the
            // retention drops a print with its bars.
            new(new DateOnly(2026, 2, 27), EventTiming.After, "1.00", "1.00", "0"),
        ];

        // The first two share a date, so each is worked on its own, the way the annotator calls the
        // rule one print at a time.
        var before = EarningsReactions.Of([prints[0]], bars).Reactions.Single();
        var after = EarningsReactions.Of([prints[1]], bars).Reactions.Single();

        Assert.Equal((new DateOnly(2026, 3, 3), 10.0), (before.Session, Math.Round(before.MovePct, 9)));
        Assert.Equal((new DateOnly(2026, 3, 4), -10.0), (after.Session, Math.Round(after.MovePct, 9)));
        Assert.Equal(10.0, before.SurprisePct);

        var record = EarningsReactions.Of(prints[2..], bars);

        Assert.Equal(2, record.Reactions.Count);
        Assert.Equal(2, record.Unreached);

        var unstated = record.Reactions[0];

        Assert.Equal((new DateOnly(2026, 3, 6), EventTiming.Unstated), (unstated.Session, unstated.Timing));
        Assert.Equal(21.212121212, Math.Round(unstated.MovePct, 9));

        var noEstimate = record.Reactions[1];

        Assert.Equal((new DateOnly(2026, 3, 9), "0.75"), (noEstimate.Session, noEstimate.Actual));
        Assert.Null(noEstimate.Estimate);
        Assert.Null(noEstimate.SurprisePct);
        Assert.Equal(-25.0, Math.Round(noEstimate.MovePct, 9));

        static DateOnly Session(DateOnly monday, int day) => monday.AddDays(day < 5 ? day : day + 2);
    }

    [Fact]
    public async Task TheFixturesReactionsAreTheOnesWorkedByHandFromTheCapturedCalendarAndBars()
    {
        var expected = Expected("reactions");

        using var store = await FixtureReplay.ReplayedAsync();

        var read = 0;

        foreach (var name in expected.GetProperty("reactions").EnumerateObject())
        {
            var stored = Query(
                store,
                $"SELECT report_date || '|' || timing || '|' || reaction_session || '|' || IFNULL(estimate, '') || '|' || IFNULL(actual, '') || '|' || IFNULL(surprise_pct, '') || '|' || move_pct FROM earnings_reaction WHERE ticker = '{name.Name}' ORDER BY report_date;");

            var want = name.Value.EnumerateArray().ToArray();

            Assert.Equal(want.Length, stored.Count);

            foreach (var (print, row) in want.Zip(stored.Select(line => line.Split('|'))))
            {
                Assert.Equal(print.GetProperty("reportDate").GetString(), row[0]);
                Assert.Equal(print.GetProperty("timing").GetString(), row[1]);
                Assert.Equal(print.GetProperty("session").GetString(), row[2]);
                Assert.Equal(print.GetProperty("estimate").GetString() ?? string.Empty, row[3]);
                Assert.Equal(print.GetProperty("actual").GetString() ?? string.Empty, row[4]);

                if (print.GetProperty("surprise").ValueKind == JsonValueKind.Null)
                {
                    Assert.Equal(string.Empty, row[5]);
                }
                else
                {
                    Assert.InRange(
                        Math.Abs(double.Parse(row[5], CultureInfo.InvariantCulture) - double.Parse(print.GetProperty("surprise").GetString()!, CultureInfo.InvariantCulture)),
                        0,
                        1e-9);
                }

                Assert.InRange(Math.Abs(double.Parse(row[6], CultureInfo.InvariantCulture) - print.GetProperty("movePct").GetDouble()), 0, 0.0001);

                read++;
            }
        }

        // Every print the calendar holds for a name whose bars reach it, and none the bars do not.
        Assert.Equal(16, read);
        Assert.Equal(0, expected.GetProperty("unreached").GetInt32());
        Assert.Equal(read, int.Parse(Query(store, "SELECT COUNT(*) FROM earnings_reaction;")[0], CultureInfo.InvariantCulture));
        Assert.Contains($", {read} earnings reaction(s), 0 print(s) the bars do not reach", Query(store, "SELECT detail FROM run_log WHERE stage = 'moves';")[0], StringComparison.Ordinal);
    }
}
