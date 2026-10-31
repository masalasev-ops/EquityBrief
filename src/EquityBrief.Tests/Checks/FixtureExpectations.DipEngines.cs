using System.Globalization;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.5: the short-term dip's live rule read for every engine over a constructed history. Its walk is
// the dip's own; under an exit of the menu each listing is walked as the dip walks one, a target its close reaches sold
// at the next open and a time stop at its own close, against the same exit on every member, each worked by hand; the
// autopsy's exits for the dip state no change to put, since its night reads no exit of the menu; and a choice of its grid
// is the change a dip registration states, its places with the drop's volume last, or none where it crossed a level no
// registration states.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
// see: Every change the engines propose is one its family's night reads, so each is put to the operator
public partial class FixtureExpectations
{
    const string DipEngineListing = "2019-01-22";

    // The dip sweep's constructed history with A's path after its listing changed: A closes 1 over its listing close c on
    // the session after and 13 over it on the second, opening 12.5 over c on the third and rising a tenth a session after,
    // B and C as the sweep's.
    static TemporaryStore DipEngineStore()
    {
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 300);
        var listing = Array.IndexOf(calendar, DateOnly.ParseExact(DipEngineListing, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < listing - 1 ? 100m + (0.1m * day)
                : day == listing - 1 ? a[day - 1] - 0.3m
                : day == listing ? a[day - 1] - 3m
                : day == listing + 1 ? a[listing] + 1m
                : day == listing + 2 ? a[listing] + 13m
                : a[day - 1] + 0.1m;
        }

        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $open, $high, $low, $close, 300000, 'constructed', '2026-10-24T00:00:00Z', $close);";

        var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
        var session = insert.Parameters.Add("$day", SqliteType.Text);
        var open = insert.Parameters.Add("$open", SqliteType.Text);
        var close = insert.Parameters.Add("$close", SqliteType.Text);
        var high = insert.Parameters.Add("$high", SqliteType.Text);
        var low = insert.Parameters.Add("$low", SqliteType.Text);

        foreach (var name in new[] { "A", "B", "C" })
        {
            for (var day = 0; day < calendar.Length; day++)
            {
                var (price, opened, spread) = name switch
                {
                    "A" => (a[day], day == listing + 1 ? a[listing] + 0.5m : day == listing + 3 ? a[listing] + 12.5m : a[day], 1m),
                    "B" => (50m + (0.05m * day), 50m + (0.05m * day) - 0.02m, 0.5m),
                    _ => (80m - (0.05m * day), 80m - (0.05m * day), 0.5m),
                };

                ticker.Value = name;
                session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                open.Value = opened.ToString(CultureInfo.InvariantCulture);
                close.Value = price.ToString(CultureInfo.InvariantCulture);
                high.Value = (Math.Max(price, opened) + spread).ToString(CultureInfo.InvariantCulture);
                low.Value = (Math.Min(price, opened) - spread).ToString(CultureInfo.InvariantCulture);
                insert.ExecuteNonQuery();
            }
        }

        using var members = connection.CreateCommand();

        members.Transaction = transaction;
        members.CommandText = string.Concat(
            from name in new[] { "A", "B", "C" }
            select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('MID', '{name}', 'US', NULL, NULL, NULL, 'constructed');");
        members.ExecuteNonQuery();
        transaction.Commit();

        return store;
    }

    [Fact]
    public async Task TheDipsLiveRuleIsWalkedForTheEnginesAndAMenusExitIsSoldAsTheDipSellsWorkedByHand()
    {
        using var store = DipEngineStore();
        var dip = await DipSweepRead(store);
        int[] provisional = [.. dip.Grid.Provisional];
        var parameters = LoopRegistrations.DipParameters("MID", [.. provisional, 0])!;
        var walk = RuleWalk.Dip(dip.Read, dip, parameters, "the dip rule at its provisional setting", LoopChange.NoHooks);
        var names = dip.Tickers.ToList();
        var (a, b, c) = (names.IndexOf("A"), names.IndexOf("B"), names.IndexOf("C"));
        var session = Array.IndexOf(dip.Calendar, DateOnly.ParseExact(DipEngineListing, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        int BarOf(int name) => Array.BinarySearch(dip.Series[name].SessionAt, session);

        double Move(int name) => dip.Series[name].Atr[BarOf(name)];

        // The rule's one listing is A's on the listing's session, and its walk is the dip's own: the same trade the dip's
        // sweep keeps at the setting after its round trip, sold at the third session's open, its edge the trade's.
        var listed = Assert.Single(walk.Listings);
        var tested = Assert.Single(dip.TradesUnder(provisional, []));
        var own = Assert.Single(walk.Own);

        Assert.Equal((a, session), (listed.Name, listed.Session));
        Assert.Equal((tested.Listing, tested.Result, tested.Benchmark), (own.Trade.Listing, own.Trade.Result, own.Trade.Benchmark));
        Assert.Equal((session, session + 3), (own.Entry, own.Exit!.Value));
        Assert.Equal(tested.Result!.Value - tested.Benchmark, listed.Edge!.Value, 12);

        // A target at 1.5 risks: A's close reaches it on the second session, 13 over c against a risk of 4 typical moves,
        // and it is sold at the third session's open, 12.5 over c; bought at the open 0.5 over c, 12 over the risk. B and C,
        // each closing a twentieth a session from its own, reach neither stop nor target and are sold at the tenth session's
        // close, 0.5 over and 0.5 under their closes. Bought at the open each moves by its own gap, as the sweep's does.
        var target = ExitMenu.Swing.First(one => one.Kind == ExitKind.Target && one.K == 1.5);
        var atTarget = Assert.Single(dip.AtOpenUnder(dip.ListingsUnder(provisional, []), provisional, target));
        var gaps = ((0.5 / Move(a)) + (0.03 / Move(b)) - (0.05 / Move(c))) / 3;

        Assert.Equal(12.0 / (4 * Move(a)), atTarget.Result!.Value, 12);
        Assert.Equal((((12.5 / Move(a)) + (0.5 / Move(b)) - (0.5 / Move(c))) / (4 * 3)) - (gaps / 4), atTarget.Benchmark, 12);

        var underTarget = Assert.Single(walk.Under(target));

        Assert.Equal((session, session + 3), (underTarget.Entry, underTarget.Exit!.Value));
        Assert.Equal(Assert.Single(dip.Costed([atTarget], 1)).Result, underTarget.Trade.Result);

        // A time stop at 5 sessions: A is sold at the fifth session's own close, 13.3 over c, 12.8 over the risk bought at
        // the open; B and C at theirs, 0.25 over and 0.25 under.
        var timeStop = ExitMenu.Swing.First(one => one.Kind == ExitKind.TimeStop && one.N == 5);
        var atTime = Assert.Single(dip.AtOpenUnder(dip.ListingsUnder(provisional, []), provisional, timeStop));

        Assert.Equal(12.8 / (4 * Move(a)), atTime.Result!.Value, 12);
        Assert.Equal((((13.3 / Move(a)) + (0.25 / Move(b)) - (0.25 / Move(c))) / (4 * 3)) - (gaps / 4), atTime.Benchmark, 12);
        Assert.Equal(session + 5, Assert.Single(walk.Under(timeStop)).Exit!.Value);

        // A half sale is not walked for the dip and buys nothing.
        Assert.DoesNotContain(walk.Under(ExitMenu.Swing.First(one => one.Kind == ExitKind.HalfTarget)), one => one.Trade.Result is not null);

        // The autopsy's exits for the dip state no change to put.
        var (proposals, _) = ExitProcedures.Autopsy(dip.Read, walk, _ => { });

        Assert.Equal(ExitProcedures.Proposals, proposals.Count);
        Assert.All(proposals, one => Assert.Null(one.Change));

        // A grid's choice is the change a dip registration states: its places with the drop's volume last, a 1 where it
        // crossed the drop's volume and none where it crossed another of the second stage's levels.
        var figures = FamilySweep.Figures("none", [], dip.Nights);
        var volume = dip.Levels.First(level => level.Dial == IndexSweepRunner.DipRead.VolumeDial);
        var other = dip.Levels.First(level => level.Dial != IndexSweepRunner.DipRead.VolumeDial);

        Assert.Equal([.. provisional, 0], WalkForwardTester.DipChange("MID", new OwnProcedures.SwingChoice(provisional, [], figures))!.Places!);
        Assert.Equal([.. provisional, 1], WalkForwardTester.DipChange("MID", new OwnProcedures.SwingChoice(provisional, [volume], figures))!.Places!);
        Assert.Equal(LoopRegistrations.KeyOf("MID", [.. provisional, 1]), WalkForwardTester.DipChange("MID", new OwnProcedures.SwingChoice(provisional, [volume], figures))!.Key);
        Assert.Null(WalkForwardTester.DipChange("MID", new OwnProcedures.SwingChoice(provisional, [other], figures)));
    }
}
