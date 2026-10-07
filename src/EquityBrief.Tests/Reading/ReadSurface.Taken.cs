using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EquityBrief.Tests.Reading;

// read-surface, 16.2: a pick's plan in the operator's money, worked by hand from an account and read back off the card
// on Tonight; each family's management in its rule's own terms on every index; the concentration line on both sides of
// its value with an open trade on another index; the card's presses, each refused without the page's own header and
// each refusal writing nothing; and the account's settings found in no byte of the store, the run log, the captured log
// lines or an exported report.
// see: A pick's card sizes its plan from the operator's own settings, and the report's plan still sizes none
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
// see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
public partial class ReadSurface
{
    // Section 17's two values and section 18's two rows the card's plan in money and its presses draw.
    internal static readonly string[] TakenClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Card position cap"),
        CheckReach.Key(Scope.LimitsTable, "Card sector open trades"),
        CheckReach.Key(Scope.FailureTable, "No account kept for a pick's card, or one that cannot be read"),
        CheckReach.Key(Scope.FailureTable, "A press on a pick's card the card refuses"),
    ];

    [Fact]
    public void APicksPlanIsSizedFromTheAccountRoundedDownAndNoLargerThanTheCap()
    {
        var account = new AccountSettings(10_000m, 1m, AccountSettings.ProposedCap);

        // 100.00 at risk over a stop 2.70 under a buy of 50.00 buys 37.04 shares, 37 rounded down, under the 40 a fifth
        // of the account buys: 99.90 at risk, a position of 1,850.00, 18.5% of the account, and a round trip of 0.05 a
        // share 1.85.
        var risked = PositionSize.Of(account, 50.00m, 47.30m, null, 0.05m)!;

        Assert.Equal((37L, 99.90m, 1850.00m, 0.185m, false, false), (risked.Shares, risked.AtRisk, risked.Value, risked.ShareOfAccount, risked.Capped, risked.WholeAtRisk));
        Assert.Equal(1.85m, risked.RoundTrip);

        // A stop 0.50 under it: the risk buys 200, and the cap, 2,000.00 over 50.00, 40.
        var capped = PositionSize.Of(account, 50.00m, 49.50m, null, null)!;

        Assert.Equal((40L, 20.00m, 2000.00m, 0.2m, true, false), (capped.Shares, capped.AtRisk, capped.Value, capped.ShareOfAccount, capped.Capped, capped.WholeAtRisk));
        Assert.Null(capped.RoundTrip);

        // A holding with no stop: an equal share of the account across a book of 22 holdings, 454.55 over 50.00, 9
        // shares, the whole position at risk; and under a cap of 0.02 of the account, 200.00 over 50.00, 4.
        var held = PositionSize.Of(account, 50.00m, null, 22, null)!;

        Assert.Equal((9L, 450.00m, 450.00m, false, true), (held.Shares, held.AtRisk, held.Value, held.Capped, held.WholeAtRisk));

        var heldUnderCap = PositionSize.Of(account with { PositionCap = 0.02m }, 50.00m, null, 22, null)!;

        Assert.Equal((4L, 200.00m, true, true), (heldUnderCap.Shares, heldUnderCap.AtRisk, heldUnderCap.Capped, heldUnderCap.WholeAtRisk));

        // A stop at the buy, no buy, and a holding with no stop and no book size nothing.
        Assert.Null(PositionSize.Of(account, 50.00m, 50.00m, null, null));
        Assert.Null(PositionSize.Of(account, null, 47.30m, null, null));
        Assert.Null(PositionSize.Of(account, 50.00m, null, null, null));

        // The page refuses settings it cannot keep and proposes a fifth.
        Assert.Equal(0.2m, AccountSettings.ProposedCap);
        Assert.NotNull(AccountSettings.Refusal(0m, 1m, 0.2m));
        Assert.NotNull(AccountSettings.Refusal(10_000m, 0m, 0.2m));
        Assert.NotNull(AccountSettings.Refusal(10_000m, 1m, 1.01m));
        Assert.Null(AccountSettings.Refusal(10_000m, 1m, 1m));
    }

    [Fact]
    public async Task APicksCardDrawsItsPlanInTheOperatorsMoneyOnTonightAndInPricesWhereNoAccountIsKept()
    {
        using var store = TakenStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // No account kept: the plan in prices and risks with the line linking the account page, and no shares.
        var unset = await CardRowOf(client, "MID", "breakout", "M1");

        Assert.Contains($"<p class=\"card-money degraded\" data-account=\"unset\">Your account is not set, so the plan is drawn in prices and risks: <a href=\"{EquityBrief.Web.Marks.MarkRenderer.AccountRoute}\">", unset, StringComparison.Ordinal);
        Assert.DoesNotContain("data-shares=", unset, StringComparison.Ordinal);

        // 10,000 at a risk of 1% under the cap the page proposes: 37 shares at a stop 2.70 under a buy of 50.00.
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1m, AccountSettings.ProposedCap));

        var sized = await CardRowOf(client, "MID", "breakout", "M1");

        Assert.Contains("<p class=\"card-money\" data-shares=\"37\" data-at-risk=\"99.90\">Buy 37 shares at 50.00, a position of 1,850.00, 18.5% of the account, with 99.90 at risk to the stop at 47.30, the 1% a trade your settings give.", sized, StringComparison.Ordinal);
        Assert.DoesNotContain("the position cap limits it", sized, StringComparison.Ordinal);

        // The sector heavyweights' buy, which sets no stop: an equal share across the 22 holdings its book can hold, 9.
        var bought = await CardRowOf(client, "MID", HeavyweightRule.Name, "M2");

        Assert.Equal(22, GicsSectors.Eleven.Count * IndexHeavyweights.Provisional.Leaders);
        Assert.Contains("<p class=\"card-money\" data-shares=\"9\" data-at-risk=\"450.00\">Buy 9 shares at 50.00, a position of 450.00, 4.5% of the account. The whole position is at risk", bought, StringComparison.Ordinal);

        // A stop 0.50 under the buy: the risk buys 200 and the cap 40, and the card says the cap cut it.
        store.Execute("UPDATE decision_card SET stop = '49.50' WHERE ticker = 'M1';");

        var cut = await CardRowOf(client, "MID", "breakout", "M1");

        Assert.Contains("<p class=\"card-money\" data-shares=\"40\" data-at-risk=\"20.00\">", cut, StringComparison.Ordinal);
        Assert.Contains("Fewer shares than the risk allows: the position cap limits it.", cut, StringComparison.Ordinal);

        // A file that cannot be read is read as none kept, and the account page draws it empty with the cap it proposes.
        File.WriteAllText(AccountFile.PathIn(store.Root), "{\"size\": \"ten thousand\"");

        Assert.Contains("data-account=\"unset\"", await CardRowOf(client, "MID", "breakout", "M1"), StringComparison.Ordinal);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/account"));

        Assert.Contains("<section class=\"account\" data-set=\"false\">", page, StringComparison.Ordinal);
        Assert.Contains("<input name=\"size\" inputmode=\"decimal\" required value=\"\">", page, StringComparison.Ordinal);
        Assert.Contains("<input name=\"cap\" inputmode=\"decimal\" required value=\"0.2\">", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachPicksManagementIsItsRulesOwnOnEveryIndexAndNamesNoStepTheRuleLacks()
    {
        // The S&P 400's breakout and heavyweights and the S&P 600's breakout and drift, each worked by hand from the
        // plan its night's row states.
        using var store = TakenStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Equal(
            [
                "Sell at the close of a session that closes below the stop, 47.30 at the buy, raised after each close to that close less 2.70 and never lowered.",
                "No target: the trade is ended by its stop or its cap.",
                "Otherwise sell at the close 63 sessions after the buy.",
            ],
            ManagementOf(await CardRowOf(client, "MID", "breakout", "M1")));
        Assert.Equal(
            [
                "Held while it leads its sector: sell at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the rule reads, where the rule would no longer buy it.",
                "Sell at its last close as a member if it leaves the index.",
                "No stop and no target: nothing sells it at a price set in advance, so the whole position is at risk.",
            ],
            ManagementOf(await CardRowOf(client, "MID", HeavyweightRule.Name, "M2")));
        Assert.Equal(
            [
                "Sell at the close of a session that closes below the stop, 47.00 at the buy, raised after each close to that close less 3.00 and never lowered.",
                "No target: the trade is ended by its stop or its cap.",
                "Otherwise sell at the close 63 sessions after the buy.",
            ],
            ManagementOf(await CardRowOf(client, "SML", "breakout", "S1")));
        Assert.Equal(
            [
                "Sell at the close of a session that closes below 37.00.",
                "Sell at the close of a session that closes at or above 46.00.",
                "Otherwise sell at the close 60 sessions after the buy.",
            ],
            ManagementOf(await CardRowOf(client, "SML", "drift", "S2")));

        // The S&P 500's pullback and breakout over the night the family pages draw, with a drift pick and a heavyweights'
        // buy added: each stop a close below the stop the card states, a fixed rule's target and no trail, a trailing
        // rule's trail the plan's own distance and no target, and each cap its family's.
        using var large = await FamilyPagesStore();

        large.Execute($"UPDATE family_night SET families = '[\"pullback\",\"breakout\",\"drift\"]' WHERE session_date = '{TheSwitch}';");
        Evening(large, TheSwitch, filter: false, [new Member("D1", Trend: false, Setup: false)]);
        large.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', 'D1', 'drift', 1, 0, 1, '40', '37', '46', 1.0, '[]', '{{\"gates\":[]}}');");
        large.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) " +
            $"VALUES ('{TheSwitch}', 'D1', 'drift', 'listed', 1, '[]', NULL, NULL);");
        large.Execute(
            "INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) " +
            $"VALUES ('{TheSwitch}', 'Energy', 1, 'HW', 'Heavy Co', '5000', 0.25, 0.12, 0.13, 1, 1);");
        large.Execute(
            "INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through) " +
            $"VALUES ('HW', '{TheSwitch}', 'Energy', 'Heavy Co', '60', 1.0, '[]', '{TheSwitch}');");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), large.DatabaseFile).RunAsync("cards");

        using var largeHost = new Host(large.Root);
        using var largeClient = largeHost.CreateClient();

        Assert.Equal(
            [
                "Held while it leads its sector: sell at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the rule reads, where the rule would no longer buy it.",
                "Sell at its last close as a member if it leaves the index.",
                "No stop and no target: nothing sells it at a price set in advance, so the whole position is at risk.",
            ],
            ManagementOf(await CardRowOf(largeClient, "GSPC", HeavyweightRule.Name, "HW", TheSwitch)));

        var plans = WatchRows(large, $"SELECT family || '|' || ticker || '|' || entry || '|' || stop || '|' || IFNULL(target, '') FROM decision_card WHERE index_code = 'GSPC' AND family <> '{HeavyweightRule.Name}' ORDER BY family, ticker;");

        Assert.Equal(["breakout", "drift", "pullback"], plans.Select(plan => plan.Split('|')[0]).Distinct());
        Assert.Equal(8, plans.Count);

        foreach (var plan in plans)
        {
            var (family, ticker) = (plan.Split('|')[0], plan.Split('|')[1]);
            var (entry, stop) = (decimal.Parse(plan.Split('|')[2], CultureInfo.InvariantCulture), decimal.Parse(plan.Split('|')[3], CultureInfo.InvariantCulture));
            var target = plan.Split('|')[4];
            var rule = Assert.IsType<SetupFamily>(SetupFamilies.Named(family));
            var cap = rule.CapSessions;

            string[] expected = !rule.Trails
                ? [
                    FormattableString.Invariant($"Sell at the close of a session that closes below {stop:0.00}."),
                    FormattableString.Invariant($"Sell at the close of a session that closes at or above {decimal.Parse(target, CultureInfo.InvariantCulture):0.00}."),
                    FormattableString.Invariant($"Otherwise sell at the close {cap} sessions after the buy."),
                ]
                : [
                    FormattableString.Invariant($"Sell at the close of a session that closes below the stop, {stop:0.00} at the buy, raised after each close to that close less {entry - stop:0.00} and never lowered."),
                    "No target: the trade is ended by its stop or its cap.",
                    FormattableString.Invariant($"Otherwise sell at the close {cap} sessions after the buy."),
                ];

            var drawn = ManagementOf(await CardRowOf(largeClient, "GSPC", family, ticker, TheSwitch));

            Assert.Equal(expected, drawn);
            Assert.All(drawn, step => Assert.DoesNotContain("even", step.Replace("never", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheConcentrationLineCountsTheOperatorsOpenTradesInTheSectorAcrossTheThreeIndices()
    {
        // Worked by hand: the stock's own open trade and an ended one are not counted, nor one in another sector.
        Assert.Equal(
            ("tick", "1 of your open trades is in Energy across the S&P 500, 400 and 600."),
            Words(CardLines.Concentration("M1", "Energy", [("AAA", "Energy"), ("M1", "Energy"), ("CCC", "Utilities")])));
        Assert.Equal(
            ("warning", "2 of your open trades are in Energy across the S&P 500, 400 and 600, at or over 2: another adds to one sector's risk."),
            Words(CardLines.Concentration("M1", "Energy", [("AAA", "Energy"), ("BBB", "Energy"), ("M1", "Energy")])));
        Assert.Equal(CardVerdict.Warn, CardLines.Concentration("M1", null, []).Verdict);
        Assert.Equal(2, CardLines.SectorOpenWarnAt);

        // Read back off the S&P 400's Tonight: one open trade in Energy on the S&P 500 ticks, a second warns, and M1's own
        // open trade, an ended trade in Energy and an open trade in another sector move neither.
        using var store = TakenStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Equal("Energy", WatchRows(store, "SELECT sector FROM decision_card WHERE ticker = 'M1';").Single());

        TakenRow(store, "AAA", "GSPC", "Energy", exited: false);
        TakenRow(store, "M1", "MID", "Energy", exited: false);
        TakenRow(store, "CCC", "SML", "Energy", exited: true);
        TakenRow(store, "UUU", "SML", "Utilities", exited: false);

        Assert.Equal(
            "6|tick|Concentration|1 of your open trades is in Energy across the S&P 500, 400 and 600.",
            LineSix(await CardRowOf(client, "MID", "breakout", "M1")));

        TakenRow(store, "BBB", "GSPC", "Energy", exited: false);

        Assert.Equal(
            "6|warning|Concentration|2 of your open trades are in Energy across the S&P 500, 400 and 600, at or over 2: another adds to one sector's risk.",
            LineSix(await CardRowOf(client, "MID", "breakout", "M1")));

        // The stock's page for an earlier night offers no control and draws no sixth line.
        var named = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M1/{IndexNight}"));

        Assert.DoesNotContain("data-line=\"6\"", named, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"card-press\"", named, StringComparison.Ordinal);
        Assert.DoesNotContain("card-taken-says", named, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachPressOnAPicksCardNeedsThePagesOwnHeaderAndWritesNothingTheCardRefuses()
    {
        using var store = TakenStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var take = $"{SinglePageApp.TakenPostRoute}MID/{IndexNight}/breakout/M1";

        // Above its boxes the control says what it records and what each empty box means, so a fill box and a date box
        // are not left to say it alone.
        var drawn = await CardRowOf(client, "MID", "breakout", "M1");
        var says = drawn.IndexOf($"<p class=\"card-taken-says\">{MarkRenderer.TakenSays}</p>", StringComparison.Ordinal);

        Assert.True(says >= 0 && says < drawn.IndexOf($"action=\"{take}\"", StringComparison.Ordinal), "The Taken control's boxes are drawn with nothing above them saying what they record.");
        Assert.Contains("Leave Fill empty to use the next session's opening price once it is stored", MarkRenderer.TakenSays, StringComparison.Ordinal);
        Assert.Contains("the date empty for the session after this pick's night", MarkRenderer.TakenSays, StringComparison.Ordinal);

        string Rows() => string.Join(";", WatchRows(store, "SELECT ticker || ' ' || fill || ' ' || fill_date || ' ' || provisional || ' ' || entered || ' ' || stop || ' ' || IFNULL(trail, '-') || ' ' || IFNULL(cap, '-') || ' ' || IFNULL(sector, '-') || ' ' || IFNULL(exit_price, '-') FROM taken_trade ORDER BY taken_at;"));

        // Without the page's own header nothing is taken.
        var (status, _) = await CardPress(client, take, [], fromThePage: false);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(string.Empty, Rows());

        // A fill at the stop and one under it are refused; a card the store does not hold is refused.
        foreach (var under in new[] { "47.30", "46" })
        {
            var refused = await CardPress(client, take, [("price", under)]);

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Contains("is at or under the stop of 47.30", refused.Said, StringComparison.Ordinal);
        }

        var unheld = await CardPress(client, $"{SinglePageApp.TakenPostRoute}MID/{IndexNight}/drift/M1", []);

        Assert.Equal((HttpStatusCode.Conflict, true), (unheld.Status, unheld.Said.Contains("no card of the drift on MID for 2026-10-02 is stored", StringComparison.Ordinal)));
        Assert.Equal(string.Empty, Rows());

        // With no price: the plan's buy, provisional, for the session after Friday's night, Monday 2026-10-05, managed by
        // the card's stop, trail and cap.
        var taken = await CardPress(client, take, []);

        Assert.Equal(HttpStatusCode.OK, taken.Status);
        Assert.Equal("M1 50 2026-10-05 1 0 47.30 2.70 63 Energy -", Rows());

        // A second open trade in the stock is refused.
        var twice = await CardPress(client, take, [("price", "51")]);

        Assert.Equal((HttpStatusCode.Conflict, true), (twice.Status, twice.Said.Contains("you already hold an open trade in it", StringComparison.Ordinal)));
        Assert.Equal("M1 50 2026-10-05 1 0 47.30 2.70 63 Energy -", Rows());

        // The card draws the trade as provisional, with its Not taken press.
        var card = await CardRowOf(client, "MID", "breakout", "M1");
        var takenAt = Regex.Match(card, "data-taken-at=\"([^\"]+)\" data-provisional=\"yes\">Taken at 50.00 for 2026-10-05, provisional: the plan's buy until the next session's open is stored.").Groups[1].Value;

        Assert.NotEqual(string.Empty, takenAt);
        Assert.Contains($"action=\"{SinglePageApp.NotTakenPostRoute}M1/{takenAt}\"", card, StringComparison.Ordinal);

        // Not taken: refused without the header, and removes the row with it.
        Assert.Equal(HttpStatusCode.Forbidden, (await CardPress(client, $"{SinglePageApp.NotTakenPostRoute}M1/{takenAt}", [], fromThePage: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await CardPress(client, $"{SinglePageApp.NotTakenPostRoute}M1/{takenAt}", [])).Status);
        Assert.Equal(string.Empty, Rows());

        // An entered price and date are kept as entered, never provisional.
        Assert.Equal(HttpStatusCode.OK, (await CardPress(client, take, [("price", "51.25"), ("date", "2026-10-06")])).Status);
        Assert.Equal("M1 51.25 2026-10-06 0 1 47.30 2.70 63 Energy -", Rows());

        var entered = WatchRows(store, "SELECT taken_at FROM taken_trade;").Single();

        // Once a night has followed it, Not taken is refused and the row stands; an exit is refused without the header and
        // recorded with it, and recorded once.
        store.Execute("UPDATE taken_trade SET followed_through = '2026-10-06';");

        Assert.Equal(HttpStatusCode.Conflict, (await CardPress(client, $"{SinglePageApp.NotTakenPostRoute}M1/{entered}", [])).Status);
        Assert.Equal("M1 51.25 2026-10-06 0 1 47.30 2.70 63 Energy -", Rows());
        Assert.Equal(HttpStatusCode.Forbidden, (await CardPress(client, $"{SinglePageApp.ExitPostRoute}M1/{entered}", [("price", "55"), ("date", "2026-10-09")], fromThePage: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await CardPress(client, $"{SinglePageApp.ExitPostRoute}M1/{entered}", [("price", "55"), ("date", "2026-10-09")])).Status);
        Assert.Equal("M1 51.25 2026-10-06 0 1 47.30 2.70 63 Energy 55", Rows());
        Assert.Equal(HttpStatusCode.Conflict, (await CardPress(client, $"{SinglePageApp.ExitPostRoute}M1/{entered}", [("price", "56"), ("date", "2026-10-12")])).Status);
        Assert.Equal("M1 51.25 2026-10-06 0 1 47.30 2.70 63 Energy 55", Rows());

        // The card draws the ended trade and its Taken press again.
        var ended = await CardRowOf(client, "MID", "breakout", "M1");

        Assert.Contains("Taken at 51.25 for 2026-10-06; exited at 55.00 on 2026-10-09.", ended, StringComparison.Ordinal);
        Assert.Contains($"action=\"{take}\"", ended, StringComparison.Ordinal);

        // The account page's press: refused without the header and with settings the page cannot keep, each writing nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await CardPress(client, SinglePageApp.AccountPostRoute, [("size", "10000"), ("risk", "1"), ("cap", "0.2")], fromThePage: false)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await CardPress(client, SinglePageApp.AccountPostRoute, [("size", "10000"), ("risk", "1"), ("cap", "1.5")])).Status);
        Assert.False(File.Exists(AccountFile.PathIn(store.Root)));
    }

    [Fact]
    public async Task TheAccountsSettingsAreFoundInNoByteOfTheStoreTheRunLogTheLogLinesOrAnExport()
    {
        using var store = TakenStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        var log = new CapturedLog();
        string[] distinctive = ["7654321", "0.4321", "0.3579"];
        string export;

        using (var host = new LoggedHost(store.Root, log))
        {
            using var client = host.CreateClient();

            var saved = await CardPress(client, SinglePageApp.AccountPostRoute, [("size", "7654321.97"), ("risk", "0.4321"), ("cap", "0.3579")]);

            Assert.Equal(HttpStatusCode.OK, saved.Status);
            Assert.Equal(new AccountSettings(7654321.97m, 0.4321m, 0.3579m), AccountFile.Read(store.Root));

            // The card sized from them, a trade taken from it, the stock's page and its export.
            Assert.Contains("data-shares=\"", await CardRowOf(client, "MID", "breakout", "M1"), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await CardPress(client, $"{SinglePageApp.TakenPostRoute}MID/{IndexNight}/breakout/M1", [])).Status);

            await client.GetStringAsync("/screens/name/M1");
            export = await client.GetStringAsync(ReportExporter.Route + "M1");
        }

        // The file sits under the data root, which the shipped settings name as the folder the ignore list covers.
        Assert.Equal(store.Root, Path.GetDirectoryName(AccountFile.PathIn(store.Root)));

        using (var shipped = JsonDocument.Parse(Corpus.Read("src/EquityBrief.Api/appsettings.json")))
        {
            var root = shipped.RootElement.GetProperty("EquityBrief").GetProperty("DataRoot").GetString();

            Assert.Contains($"/{root}/", Corpus.Read(".gitignore").Split('\n').Select(line => line.Trim()));
        }

        // No byte of the store, its journal included, no run log row, no log line and no export carries them.
        var bytes = string.Concat(
            Directory.GetFiles(store.Root, StoreLocation.DatabaseFileName + "*")
                .Select(file =>
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.Latin1);

                    return reader.ReadToEnd();
                }));
        var runLog = string.Join("\n", WatchRows(store, "SELECT run_id || ' ' || stage || ' ' || IFNULL(outcome, '') || ' ' || IFNULL(spend, '') || ' ' || IFNULL(detail, '') FROM run_log;"));
        var lines = log.Lines.ToArray();

        Assert.True(lines.Length >= 3, $"The host logged {lines.Length} lines, so the capture is not reading its log.");
        Assert.Equal("1", WatchRows(store, "SELECT COUNT(*) FROM taken_trade;").Single());
        Assert.DoesNotContain("data-shares=", export, StringComparison.Ordinal);

        foreach (var value in distinctive)
        {
            Assert.DoesNotContain(value, bytes, StringComparison.Ordinal);
            Assert.DoesNotContain(value, runLog, StringComparison.Ordinal);
            Assert.DoesNotContain(lines, line => line.Contains(value, StringComparison.Ordinal));
            Assert.DoesNotContain(value, export, StringComparison.Ordinal);
        }
    }

    // The universes' store with what the card's plan and presses read: M1's breakout plan at a stop 2.70 under its buy,
    // the S&P 600's night listing S1 on its breakout and S2 on its drift, and every member's company filed under Energy.
    static TemporaryStore TakenStore()
    {
        var store = UniversesStore();

        store.Execute("UPDATE index_family_result SET stop = '47.30', trail = '2.70' WHERE index_code = 'MID' AND ticker = 'M1' AND family = 'breakout';");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('SML', '{IndexNight}', 2, 0.5, 1, '{IndexFamilies.Settings("SML")}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('SML', '{IndexNight}', 'S1', 'breakout', 1, 1, '50', '47', NULL, '3', 63, 2.4, NULL), " +
            $"('SML', '{IndexNight}', 'S2', 'drift', 1, 1, '40', '37', '46', NULL, 60, 1.2, NULL);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('SML', '{IndexNight}', 'S1', 'breakout', 'listed', 1, '[]', NULL, NULL, NULL), " +
            $"('SML', '{IndexNight}', 'S2', 'drift', 'listed', 1, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) " +
            "SELECT DISTINCT ticker, '2026-10-01T23:50:00Z', NULL, 'Energy', NULL, NULL, NULL FROM membership;");

        return store;
    }

    // A taken trade written as the card's press writes one, for the sixth line to count.
    static void TakenRow(TemporaryStore store, string ticker, string index, string sector, bool exited) =>
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through) VALUES " +
            $"('{ticker}', '2026-10-0{(exited ? 1 : 2)}T14:00:00Z', '{index}', 'breakout', '2026-09-30', '{sector}', '50', '2026-10-01', 0, 1, '47', NULL, '3', 63, " +
            $"{(exited ? "'52', '2026-10-02'" : "NULL, NULL")}, NULL);");

    // A card's row off its index's Tonight, decoded.
    static async Task<string> CardRowOf(HttpClient client, string index, string family, string ticker, string night = IndexNight)
    {
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(TonightOf(index, night)));
        var id = $"card-{index}-{family}-{ticker}".ToLowerInvariant();
        var row = Regex.Match(page, $"<tr class=\"card-row\" id=\"{id}\" hidden><td colspan=\"\\d+\">(.*?)</td></tr>", RegexOptions.Singleline);

        Assert.True(row.Success, $"The {index} page draws no card row for {ticker}'s {family}, drawing {string.Join(", ", Regex.Matches(page, "<tr class=\"card-row\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value))}.");

        return row.Groups[1].Value;
    }

    // The management a card states, step by step.
    static string[] ManagementOf(string card) =>
    [
        .. Regex.Matches(Regex.Match(card, "<ul class=\"card-management\">(.*?)</ul>", RegexOptions.Singleline).Groups[1].Value, "<li>([^<]*)</li>")
            .Select(match => match.Groups[1].Value),
    ];

    static string LineSix(string card)
    {
        var line = Regex.Match(card, "<li class=\"card-line\" data-line=\"6\" data-verdict=\"([a-z]+)\"><span class=\"card-mark\" data-verdict=\"\\1\">\\1</span> <b class=\"card-name\">([^<]*)</b> <span class=\"card-words\">([^<]*)</span></li>");

        Assert.True(line.Success, "The card draws no sixth line.");

        return $"6|{line.Groups[1].Value}|{line.Groups[2].Value}|{line.Groups[3].Value}";
    }

    static (string Mark, string Words) Words(CardLine line) => (line.Mark, line.Words);

    static async Task<(HttpStatusCode Status, string Said)> CardPress(HttpClient client, string route, IReadOnlyList<(string Name, string Value)> form, bool fromThePage = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new FormUrlEncodedContent(form.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))),
        };

        if (fromThePage)
        {
            request.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
        }

        using var response = await client.SendAsync(request);

        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    // Every line the host logs, each with its arguments, at every level.
    sealed class CapturedLog : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Writer(categoryName, Lines);

        public void Dispose()
        {
        }

        sealed class Writer(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var arguments = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(" ", pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;

                lines.Enqueue($"{category} {formatter(state, exception)} {arguments} {exception}");
            }
        }
    }

    sealed class LoggedHost(string root, CapturedLog log) : WebApplicationFactory<ReadApi>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(StoreLocation.DataRootKey, root);
            Providers.ResearchModelFeedTests.PinModels(builder);
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(log));
        }
    }
}
