using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Reading;

// read-surface, 16.3: what could hit a pick's trade and the operator's own record of its family, read back off the card
// on Tonight against figures worked by hand.
// see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
// see: The operator's own record states its average result once twenty of its trades in a family and index have ended
public partial class ReadSurface
{
    [Fact]
    public async Task APicksCardDrawsWhatCouldHitItAndTheOperatorsRecordOfItsFamily()
    {
        using var store = TakenStore();

        // M1's three stored reactions, +3%, -6% and +4%, on its close of 50.00 with a typical move of 2.00: 0.75, 1.5 and
        // 1.0 typical moves, median 1.0; against the plan's 2.70 of risk, 0.56, 1.11 and 0.74 risks, median 0.74, one past
        // the stop's distance. Its ex-dividend date of 2026-10-14 declared. And the operator's three ended trades of the
        // S&P 400's breakout and one open, taken on two nights on which the rule listed six picks, four of them ended.
        store.Execute(
            "INSERT INTO earnings_reaction (ticker, report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct) VALUES " +
            "('M1', '2026-01-28', 'after', '2026-01-29', NULL, NULL, NULL, 3.0), ('M1', '2026-04-29', 'after', '2026-04-30', NULL, NULL, NULL, -6.0), " +
            "('M1', '2026-07-29', 'after', '2026-07-30', NULL, NULL, NULL, 4.0);");
        store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('M1', '{IndexNight}', '{EquityBrief.Core.Indicators.IndicatorSeries.Atr14}', 2.0, 250);");
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('M1', '2026-10-14', 'ex-dividend', 'unstated', '{}', '2026-10-02T23:40:00Z');");
        store.Execute(
            "INSERT INTO taken_record (index_code, family, unit, won, lost, ended, open_trades, average, same_nights, rule_listed, rule_won, rule_lost, rule_ended, rule_average, night) VALUES " +
            $"('MID', 'breakout', 'risks', 0, 0, 3, 1, NULL, 2, 6, 0, 0, 4, NULL, '{IndexNight}');");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = await CardRowOf(client, "MID", "breakout", "M1");

        // The breakout's cap of 63 sessions after Friday 2026-10-02 ends on Monday 2027-01-04: inside it the CPI releases
        // of 10-14, 11-10 and 12-10 and the FOMC decisions of 10-28 and 12-09, and the CPI table ends before it.
        Assert.Contains("<section class=\"card-hits\" data-through=\"2027-01-04\"><h5>What could hit it before 2027-01-04</h5>", card, StringComparison.Ordinal);
        Assert.Contains("<li data-hit=\"reactions\">Its 3 stored earnings reactions moved a median 1.0 typical moves, 0.74 of this plan's risk; 1 moved further than the stop's distance.</li>", card, StringComparison.Ordinal);
        Assert.Contains("<li data-hit=\"dividend\" data-declared=\"yes\">Ex-dividend 2026-10-14, declared.</li>", card, StringComparison.Ordinal);
        Assert.Equal(
            ["CPI release on 2026-10-14.", "FOMC decision on 2026-10-28.", "CPI release on 2026-11-10.", "FOMC decision on 2026-12-09.", "CPI release on 2026-12-10."],
            System.Text.RegularExpressions.Regex.Matches(card, "<li data-hit=\"event\">([^<]*)</li>").Select(match => match.Groups[1].Value));
        Assert.Contains("<li data-hit=\"past\">The hold runs past the last CPI release the table holds, so one after it is not ruled out.</li>", card, StringComparison.Ordinal);

        // The operator's record under the rule's heading, a trailing rule's trades ended rather than won or lost, and the
        // outline until twenty have ended, with the rule's own picks on the same two nights counted the same way beneath
        // it; the stock's page for an earlier night draws none of it.
        Assert.Contains(
            "<section class=\"card-mine outline\"><h5>Your trades of the breakout on the S&P 400</h5><p>3 ended and 1 open.</p><p class=\"degraded\" data-average=\"none\">3 of 20 ended: the average is drawn once 20 have.</p>" +
            "<p class=\"card-same-nights\" data-nights=\"2\" data-listed=\"6\" data-average=\"none\">The rule's own 6 picks on the same 2 nights, each bought at the plan's buy: 4 ended and 2 open. 4 of 20 ended: the average is drawn once 20 have.</p></section>",
            card,
            StringComparison.Ordinal);
        Assert.DoesNotContain("card-mine", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M1/{IndexNight}")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APicksCardSaysALaterExDividendDateIsNotRuledOutWhereNoDividendOfTheCompanysIsStored()
    {
        // M1's breakout holds 63 sessions, past the 21 the calendar is asked for, the calendar declares it no date, and the
        // store keeps no dividend of the company's to estimate one from: the card says a later one is not ruled out.
        using (var store = TakenStore())
        {
            await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            Assert.Contains(
                "<li data-hit=\"dividend\" data-declared=\"unread\">No ex-dividend date is declared in the sessions the calendar is asked for, and no dividend of the company's is stored yet to estimate a later one from, so one later in the hold is not ruled out.</li>",
                await CardRowOf(client, "MID", "breakout", "M1"),
                StringComparison.Ordinal);
        }

        // With a dividend kept that pays none, nothing is left to estimate, and the card says no date falls in the hold.
        using (var store = TakenStore())
        {
            store.Execute("INSERT INTO dividend_reading (ticker, fetched_at, forward_rate, last_ex_date, by_year) VALUES ('M1', '2026-10-01T23:00:00Z', '0', NULL, '[]');");

            await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            Assert.Contains("<li data-hit=\"dividend\">No ex-dividend date inside the hold, declared or estimated.</li>", await CardRowOf(client, "MID", "breakout", "M1"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PastPicksDrawsTheOperatorsTradesOnTheChosenIndexOpenOnesFirst()
    {
        using var store = TakenStore();

        // On the S&P 400: M1 ended by its stop on 10-06, M3 open, and M2 exited by the operator at 52.00 with no night yet
        // following it; on the S&P 500, AAA open.
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through, ended_on, end_price, end_reason, result) VALUES " +
            "('M1', '2026-10-02T01:00:00Z', 'MID', 'breakout', '2026-10-01', 'Energy', '50', '2026-10-02', 0, 1, '47.30', NULL, '2.70', 63, NULL, NULL, '2026-10-06', '2026-10-06', '47.10', 'stop', -1.0741), " +
            "('M3', '2026-10-03T01:00:00Z', 'MID', 'pullback', '2026-10-02', 'Energy', '30', '2026-10-05', 1, 0, '28', '34', NULL, 20, NULL, NULL, NULL, NULL, NULL, NULL, NULL), " +
            "('M2', '2026-10-01T01:00:00Z', 'MID', 'heavyweight', '2026-09-30', 'Energy', '48', '2026-10-01', 0, 1, NULL, NULL, NULL, NULL, '52', '2026-10-05', NULL, NULL, NULL, NULL, NULL), " +
            "('AAA', '2026-10-03T02:00:00Z', 'GSPC', 'pullback', '2026-10-02', 'Energy', '50', '2026-10-05', 0, 1, '47', '56', NULL, 63, NULL, NULL, NULL, NULL, NULL, NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));
        var rows = System.Text.RegularExpressions.Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-open=\"(yes|no)\">").Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}").ToArray();

        // The open trade first, then the ended newest first: M1 on 10-06, then M2 exited on 10-05; AAA is the S&P 500's.
        Assert.Equal(["M3 yes", "M1 no", "M2 no"], rows);
        Assert.Contains("<h2>Your trades on the S&P 400</h2>", page, StringComparison.Ordinal);
        Assert.Contains($"action=\"{EquityBrief.Web.App.SinglePageApp.ExitPostRoute}M3/2026-10-03T01:00:00Z\"", page, StringComparison.Ordinal);
        Assert.Contains("ended by its stop on 2026-10-06 at 47.10</td><td>-1.07 risks</td>", page, StringComparison.Ordinal);
        Assert.Contains("ended by its exit on 2026-10-05 at 52.00</td><td>not read</td>", page, StringComparison.Ordinal);
        Assert.Contains("30.00 for 2026-10-05, provisional", page, StringComparison.Ordinal);

        // An index the operator took nothing from says so.
        Assert.Contains("You have taken no trade from a card on the S&P 600.", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=600")), StringComparison.Ordinal);
    }
}
