using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The Run page's background variants waiting for a slot, read back over a constructed store: each with the session its live
// rule's orders are walked after, the orders that ended, those its floor kept out of a fill and its result over those it bought
// beside the live rule's over the same orders, a registered one saying so, and none drawn where none was entered.
// see: A background variant a full family has no room for is walked each night from its live rule's own orders and registers in the first slot that frees
public partial class ReadSurface
{
    internal static string[] WaitingVariantRunRows => [CheckReach.Key("15.10 Run", "Background variants waiting for a slot")];

    [Fact]
    public async Task TheRunPageDrawsEachWaitingVariantWithItsRecordBesideItsLiveRules()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Equal(string.Empty, MarkRenderer.WaitingVariants(await ApiOf(store).WaitingVariantsAsync()));

        store.Execute(
            "INSERT INTO waiting_variant (candidate, live, evaluator, moves, walked_from, entered_at, evidence, run_id) VALUES "
            + "('the drift rule, not bought on an open more than 2 typical days under its price', 'the live drift rule', 'drift', '{\"plan_range_floor\":2}', '2026-11-25', '2026-11-27T20:00:00Z', 'e', 'r'), "
            + "('the breakout rule, not bought on an open more than 2 typical days under its price', 'the live breakout rule', 'breakout', '{\"plan_range_floor\":2}', '2026-11-25', '2026-11-27T20:00:00Z', 'e', 'r');");

        // The drift's three orders by hand: AAA kept out by the floor, the live rule making +1.0 on it; BBB bought by both at
        // +2.0; CCC bought by both at -1.0. The variant: (2.0 - 1.0) / 2 = 0.50 over its 2; the live rule (1.0 + 2.0 - 1.0) / 3
        // = 0.67 over its 3.
        store.Execute(
            "INSERT INTO waiting_variant_trade (waiting_id, session_date, ticker, ended_on, filled_on, fill, result, live_result, under_floor, run_id) VALUES "
            + "(1, '2026-11-26', 'AAA', '2026-11-27', NULL, NULL, NULL, 1.0, 1, 'r'), "
            + "(1, '2026-11-26', 'BBB', '2026-12-01', '2026-11-27', '50', 2.0, 2.0, 0, 'r'), "
            + "(1, '2026-11-27', 'CCC', '2026-12-02', '2026-11-30', '30', -1.0, -1.0, 0, 'r');");

        // The breakout's variant has registered since.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES "
            + "(1, 'the breakout rule, not bought on an open more than 2 typical days under its price', 'r', 't', 'breakout', '{}', 'v', 'registered', NULL, '2026-12-03T23:00:00Z', 'a slot freed');");

        var rows = await ApiOf(store).WaitingVariantsAsync();
        var drawn = MarkRenderer.WaitingVariants(rows);

        Assert.Equal(2, rows.Count);
        Assert.Equal((3, 1, 2, 0.5, 3), (rows[0].Ended, rows[0].UnderFloor, rows[0].Filled, rows[0].Result!.Value, rows[0].LiveFilled));
        Assert.Equal(0.67, Math.Round(rows[0].LiveResult!.Value, 2));
        Assert.Contains("orders ended, 1 not bought on an open under its floor; 0.50 risks a trade over the 2 it bought, the live rule 0.67 over the 3 it bought of the same orders", drawn, StringComparison.Ordinal);
        Assert.Contains(">3 of its live rule", drawn, StringComparison.Ordinal);
        Assert.Contains("walked from the orders after 25 Nov 2026", drawn, StringComparison.Ordinal);
        Assert.Contains("data-waiting=\"1\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-registered=\"1\"", drawn, StringComparison.Ordinal);
        Assert.Contains("registered in a slot that freed", drawn, StringComparison.Ordinal);
        Assert.Contains("none it bought has ended, the live rule none ended of the same orders", drawn, StringComparison.Ordinal);
    }
}
