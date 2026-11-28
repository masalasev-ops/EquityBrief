using System.Net;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The AI stock picker's cards from the night of 2026-11-24, read back off the page over a constructed store: a pick waiting
// for its open reads its range in the operator's words, a pick that opened above its range and did not come back reads not
// bought with its side and what the open would have made, the line under the count names the range, and no card says it buys
// at whatever price it opens.
// see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
public partial class ReadSurface
{
    // The night of 24 Nov: AAA, BBB and CCC picked, each a close of 100 and a typical move of 2, so a range of 96 to 101.
    // AAA opens on the 25th at 103 and trades no lower than 102, then at 104 no lower than 103 on the 27th: not bought, above.
    // BBB opens at 100.5 inside it. CCC holds no session after the night, so its open is still to come.
    static TemporaryStore PickerRangeStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code) VALUES "
            + "('judge-pick-24', '2026-11-24', 'v', 1, 20261124, 'picked', '2026-11-25T01:00:00Z', '2026-11-25T01:10:00Z', 3, 3, 2, 0, '', 'GSPC');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance) VALUES "
            + $"('2026-11-24', 1, 'judge-pick-24', 'GSPC', 'AAA', '1 week', '{JudgeMenu.Whole}', 'A first reason.', '100', 2.0, '98', '104', NULL), "
            + $"('2026-11-24', 2, 'judge-pick-24', 'GSPC', 'BBB', '1 week', '{JudgeMenu.Whole}', 'A second reason.', '100', 2.0, '98', '104', NULL), "
            + $"('2026-11-24', 3, 'judge-pick-24', 'GSPC', 'CCC', '1 week', '{JudgeMenu.Whole}', 'A third reason.', '100', 2.0, '98', '104', NULL);");
        store.Execute(
            "INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
            + "('GSPC', '2026-11-24', 'AAA', 'v', '{}', 'r', 's', '2026-11-25T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-24', 'BBB', 'v', '{}', 'r', 's', '2026-11-25T01:00:00Z', 'f'), "
            + "('GSPC', '2026-11-24', 'CCC', 'v', '{}', 'r', 's', '2026-11-25T01:00:00Z', 'f');");

        static string Bar(string ticker, string day, string open, string low, string close) =>
            $"('{ticker}', '{day}', '{open}', '{open}', '{low}', '{close}', 1000, 'eod', '{day}T22:00:00Z')";

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES "
            + string.Join(", ", Bar("AAA", "2026-11-24", "100", "100", "100"), Bar("AAA", "2026-11-25", "103", "102", "103"), Bar("AAA", "2026-11-27", "104", "103", "104"),
                Bar("BBB", "2026-11-24", "100", "100", "100"), Bar("BBB", "2026-11-25", "100.5", "100", "101"), Bar("BBB", "2026-11-27", "101", "100.5", "101"),
                Bar("CCC", "2026-11-24", "100", "100", "100"))
            + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('AAA', '2026-11-24', 'atr14', 2.0, 14), ('BBB', '2026-11-24', 'atr14', 2.0, 14), ('CCC', '2026-11-24', 'atr14', 2.0, 14);");

        return store;
    }

    [Fact]
    public async Task APickerCardFromTheRangesFirstNightReadsItsRangeAndAPickOutsideItReadsNotBoughtWithItsSide()
    {
        using var store = PickerRangeStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge"));

        // The range in the operator's words, worked from the last close, on the night's three cards. Its variant "a fixed 5" took
        // the same three, so from the operator's order of 2026-11-24, item 11, it draws them as chips under one line and no
        // card, and item 13's tally reads what its account did, worked by hand: BBB bought inside its range at its open of
        // 100.5, CCC still to open within its order, AAA above its range and not bought.
        // From the operator's order of 2026-11-28, item 6, the three cases in plain words with the last close on the row's first
        // line, the floor of $96.00 under the stop of $98.00 so an open below it is skipped as too far below the buy.
        Assert.Equal(3, CountOf(page, "<span>Buys between $96.00 and $101.00</span><b class=\"judge-close\">Last close $100.00</b><span class=\"sale-note judge-buy\"><span class=\"judge-case\">Opens in that range: bought at the open.</span><span class=\"judge-case\">Opens higher than $101.00: waits up to 2 days for the price to dip back to $101.00 and buys there; otherwise skipped.</span><span class=\"judge-case\">Opens lower than $96.00: skipped, since it opened too far below the buy.</span></span>"));
        Assert.Equal(3, CountOf(page, "<p class=\"facts-h\">The plan</p>"));
        Assert.DoesNotContain("The plan code set", page, StringComparison.Ordinal);

        // A floor standing at the stop says the idea has failed; no floor says nothing of an open below.
        Assert.Equal(
            "Opens lower than $98.00: skipped. That's already below its stop, so the idea has failed.",
            SinglePageApp.BuyCases(new Core.Loop.BuyRange(101, 98), 98m)[2]);
        Assert.Equal(2, SinglePageApp.BuyCases(new Core.Loop.BuyRange(101, null), 98m).Count);

        var five = System.Text.RegularExpressions.Regex.Match(page, "<section class=\"card\" data-card=\"judge-fixed-five\">.*?</section>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        Assert.Contains("Tonight it took the same 3 picks as the version above.", five, StringComparison.Ordinal);
        Assert.Equal(["AAA", "BBB", "CCC"], System.Text.RegularExpressions.Regex.Matches(five, "<span class=\"judge-chip\" data-ticker=\"([A-Z]+)\">").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("<article", five, StringComparison.Ordinal);
        Assert.Contains("Each buys within its range.", five, StringComparison.Ordinal);

        // From the operator's correction of 2026-11-24, item 7: the tally once, under the chips, and the variant named once, in
        // its heading, the sentence repeating both gone.
        var chipsAt = five.IndexOf("class=\"judge-chips\"", StringComparison.Ordinal);
        var tallyAt = five.IndexOf("Bought so far: 1 of 3. Waiting within the 2-day order: 1. Not bought: 1 (1 opened above its range).", StringComparison.Ordinal);

        Assert.True(chipsAt >= 0 && tallyAt > chipsAt, "The tally is not under the chips.");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(five, "Bought so far: "));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(five, "A fixed 5, version \\d+'s variant"));
        Assert.DoesNotContain("in this version's own account and walked to their exits", five, StringComparison.Ordinal);

        // Its account's figure: BBB's small move rounds to nothing, read as 0.0% and never as -0.0%.
        Assert.Contains("Its own account: 0.0% since Wed 25 Nov, 1 open, 0 closed.", five, StringComparison.Ordinal);

        // From item 12, a variant's card waiting to buy says how it buys in the approved words: within its range from the range's
        // first night, at the open before it.
        var version = new JudgeVersionView(1, new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24), [], new JudgeScoreboard(0, 0, null, null, 300), []);
        var waiting = new JudgeCardView(3, 3, "CCC", null, null, null, "A third reason.", 100m, 98m, 104m, null, "1 week", JudgeMenu.Whole) { Range = JudgeAccounts.RangeOf(new DateOnly(2026, 11, 24), 100m, 2.0) };

        Assert.Equal("Buys between $96.00 and $101.00 Opens above $101.00: waits up to 2 days for the price to come back to $101.00, then not bought. Opens below $96.00: not bought. In version 1's own account.", SinglePageApp.HeldWords(waiting, version));
        Assert.Equal("Picked before its range began, so it buys at the open. In version 1's own account.", SinglePageApp.HeldWords(waiting with { Range = null }, version));

        // A pick bought at the open that no night has read yet is counted as not yet read, never as waiting for an open that
        // may already have passed.
        Assert.Equal("Bought so far: 0 of 2. Waiting within the 2-day order: 1. Its open not yet read by a night: 1. Not bought: 0.", SinglePageApp.BoughtSoFarWords([waiting, waiting with { Range = null }]));

        // AAA opened above and did not come back: not bought, its side named, and the open's walk not yet ended.
        Assert.Contains($"<p class=\"judge-since\" data-range-skip>Not bought: {JudgeAccounts.OpenedAbove}. What buying at the open would have made is recorded once its walk there ends.</p>", page, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(page, "data-range-skip"));
        Assert.Contains("AAA<span>1, not bought: outside its range</span>", page, StringComparison.Ordinal);

        // The line under the count names the range, and no line says it buys at whatever price it opens.
        Assert.Contains(SinglePageApp.TonightNote, page, StringComparison.Ordinal);
        Assert.Contains("Each buys only within its range: from 2 typical daily moves under the last close to 0.5 of one above it.", SinglePageApp.TonightNote, StringComparison.Ordinal);
        Assert.DoesNotContain("whatever price it opens", page, StringComparison.Ordinal);
        Assert.DoesNotContain(SinglePageApp.BeforeRangeLine, page, StringComparison.Ordinal);
    }

    static int CountOf(string text, string part)
    {
        var count = 0;

        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
