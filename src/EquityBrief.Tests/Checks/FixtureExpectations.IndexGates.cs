using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11: the S&P 400's and 600's nights store every part's answer for every member, the market
// check its own, worked by hand over a constructed night with the check closed: each member's parts under the breakout
// in the rule's order, a part it failed saying what it had, its row's reason still the first part it failed; the
// pullback's own setups passing their setup read with the check open on a night it closed; and the card of the stocks one
// gate short read off the stored parts drawing the four the check alone keeps off, counted as the market line counts them.
// see: The S&P 400's and 600's nights store every part's answer for every member, the market check its own
public partial class FixtureExpectations
{
    // A member's parts as one line, each part's name with a plus where it passed and a minus where it failed.
    static string PartsLine(IReadOnlyList<EquityBrief.Core.Filter.Gate> gates) =>
        string.Join(" ", gates.Select(gate => gate.Name + (gate.Passed ? "+" : "-")));

    [Fact]
    public async Task AClosedNightStoresEveryPartOfEveryMembersRuleAndItsCardDrawsTheStocksTheMarketCheckAloneKeepsOff()
    {
        using var store = ClosedIndexStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-gates");

        IReadOnlyDictionary<string, (string Reason, IReadOnlyList<EquityBrief.Core.Filter.Gate> Gates)> Stored(string family) =>
            FamilyRows(store, $"SELECT ticker, reason, gates FROM index_family_result WHERE index_code = 'MID' AND family = '{family}' ORDER BY ticker;")
                .Select(row => row.Split('|', 3))
                .ToDictionary(part => part[0], part => (part[1], FamilyRule.GatesOf(part[2])), StringComparer.Ordinal);

        // Seven of the seventeen members close above their 200-day average, a breadth of 7/17, 41.2%, under the floor of
        // 45%: every member's first part is the check, failing with its figures, and every row's reason still names it.
        var breakout = Stored(BreakoutRule.Name);
        var market = "the index's own breadth 41.2% below its floor of 45%";

        Assert.Equal(17, breakout.Count);
        Assert.All(breakout.Values, row => Assert.Equal(IndexNightRead.MarketClosed, row.Reason));
        Assert.All(breakout.Values, row => Assert.Equal(
            ("market", false, market, (7.0 / 17.0).ToString("R", CultureInfo.InvariantCulture), "0.45"),
            (row.Gates[0].Name, row.Gates[0].Passed, row.Gates[0].Reason, row.Gates[0].Values["breadth"], row.Gates[0].Values["floor"])));

        // Under the breakout read with the check open, IA, IB, IC and ID break out on 2.0, 1.6, 1.8 and 1.7 times their
        // volume and pass every other part; FL breaks out under the floor, its fifty sessions to the night 9 at 99 and 40 at
        // 100 on 50,000 shares and the night at 102 on 100,000, a mean of 254,750,000 / 50 = 5,095,000 a session; PR
        // breaks out on quarters of 10, -30, 5 and 5, a loss of 10; NH closes under its high, and the ten at 90 under
        // theirs.
        Assert.Equal(
            [
                .. new[] { "F1", "F10", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9" }.Select(ticker => ticker + " market- setup- floors+ profit+"),
                "FL market- setup+ floors- profit+",
                "IA market- setup+ floors+ profit+",
                "IB market- setup+ floors+ profit+",
                "IC market- setup+ floors+ profit+",
                "ID market- setup+ floors+ profit+",
                "NH market- setup- floors+ profit+",
                "PR market- setup+ floors+ profit-",
            ],
            breakout.Select(row => row.Key + " " + PartsLine(row.Value.Gates)));
        Assert.Equal(IndexNightRead.NoSetup, breakout["NH"].Gates[1].Reason);
        Assert.Equal("closed at 102.00 with 5,095,000 a session over the 50 sessions to tonight, against the least close of 5.00 and its floor of 10,000,000", breakout["FL"].Gates[2].Reason);
        Assert.Equal("net income of -10 over its 4 newest quarters as filed, not above nothing", breakout["PR"].Gates[3].Reason);
        Assert.All(breakout.Values.SelectMany(row => row.Gates).Where(gate => gate.Passed), gate => Assert.Equal(string.Empty, gate.Reason));

        // No member's pullback, drift or fundamentals-first setup lists with the check open: the breakouts close above every
        // high before, NH 0.3 under its twenty sessions' high on a typical move near 1, the ten more than five typical moves
        // under theirs, and none reports. The fundamentals-first family reads no readings tonight, so its five parts reading
        // them fail too.
        foreach (var family in new[] { SetupFamilies.Pullback, DriftRule.Name, FundamentalsRule.Name })
        {
            Assert.All(Stored(family).Values, row => Assert.Equal(("market", false, "setup", false), (row.Gates[0].Name, row.Gates[0].Passed, row.Gates[1].Name, row.Gates[1].Passed)));
        }

        Assert.Equal("market- setup- floors+ readings- profit+ revenue- margin- cash- trend-", PartsLine(Stored(FundamentalsRule.Name)["IA"].Gates));

        // The card reads the four the check alone keeps off, in the page's family order and by ticker, each with the check's
        // words, and the market line counts them; it draws no Watch press, the watch list holding the S&P 500's names.
        var read = new ReadApi(store.DatabaseFile, clock);
        var results = await read.IndexResultsAsync("MID", night);
        var close = TonightScreen.IndexCloseAcross(results, await read.IndexMembersAsync("MID", night))!;

        Assert.Equal(["IA|breakout|market", "IB|breakout|market", "IC|breakout|market", "ID|breakout|market"], close.Select(cell => $"{cell.Ticker}|{cell.Family}|{cell.Gate}"));
        Assert.All(close, cell => Assert.Equal(market, cell.Words));

        var line = TonightScreen.IndexLine(Universes.Mid, (await read.IndexNightAsync("MID", night))!, [], results, 0, close);
        var card = new MarkRenderer().CloseToBuy(TonightScreen.IndexCloseToBuy(close, new Dictionary<string, DateOnly>(StringComparer.Ordinal), night), false, false, Universes.Mid.Word);

        Assert.Equal((true, 4, false), (line.CloseRead, line.Close, line.Open));
        Assert.Contains("data-count=\"4\"", card, StringComparison.Ordinal);
        Assert.Equal(4, card.Split("data-gate=\"market\"").Length - 1);
        Assert.DoesNotContain("<button", card, StringComparison.Ordinal);

        // A night stored before the parts were is read as reading none, and its card says so.
        Assert.Null(TonightScreen.IndexCloseAcross([.. results.Select(row => row with { Gates = null })], []));
    }

    [Fact]
    public void ThePullbacksOwnSetupsPassTheirSetupReadWithTheCheckOpenOnANightTheCheckClosed()
    {
        // The business-first history's night with its breadth read at 0.40, under the floor of 0.45: the pullback's base
        // reads its own market dial at 45%, so it lists none on the night as read, while AAA, BBB and CCC, which dip to a
        // support band and turn up there, are the setups it lists with the check open. Each passes the floors, which the S&P
        // 500 holds none of past the $5 close, and the profit check on four quarters of 10, so the check is the one part it
        // fails; the four weaker members list no setup and file no quarters.
        var inputs = BusinessFirstHistory.Inputs(BusinessFirstHistory.Night + 1);
        var prepared = IndexNightRead.Prepare(IndexFamilies.LargeIndex, inputs.Sessions[^1], inputs.Names, BusinessFirstHistory.Income())!;
        var closed = prepared with { Sessions = [.. prepared.Sessions.Select((one, at) => at == prepared.At ? one with { Breadth = 0.40 } : one)] };
        var read = IndexNightRead.Provisional(closed, SetupFamilies.Pullback);
        var answers = IndexNightRead.Answers(closed, SetupFamilies.Pullback, read).ToDictionary(answer => answer.Ticker, StringComparer.Ordinal);

        Assert.True(prepared.Open);
        Assert.False(closed.Open);
        Assert.Empty(read.Listed);
        Assert.All(answers.Values, answer => Assert.Equal((false, IndexNightRead.MarketClosed), (answer.Passed, answer.Reason)));
        Assert.Equal(
            [
                "AAA market- setup+ floors+ profit+",
                "BBB market- setup+ floors+ profit+",
                "CCC market- setup+ floors+ profit+",
                "Z0 market- setup- floors+ profit-",
                "Z1 market- setup- floors+ profit-",
                "Z2 market- setup- floors+ profit-",
                "Z3 market- setup- floors+ profit-",
            ],
            answers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + " " + PartsLine(pair.Value.Gates!)));
        Assert.Equal("the index's own breadth 40.0% below its floor of 45%", answers["AAA"].Gates![0].Reason);
        Assert.Equal("0 of the 4 quarters the profit check sums filed with their net income before tonight", answers["Z0"].Gates![3].Reason);
    }
}
