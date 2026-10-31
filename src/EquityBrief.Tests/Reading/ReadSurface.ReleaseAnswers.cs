using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.8: the Ledger page's card of what the release readings came to, drawn from the newest run the tester
// stored for the index and the family chosen, each answer after the local model's cutoff and beside it before in the
// family's own unit, the month and the cutoff named, an older run's rows and another index's not drawn and a line where
// no run has worked it; and the Loop page's split beneath a proposal whose change reads a release reading, its total less
// the rule's in the test years before the cutoff's year and from it, worked by hand from its stored test years, a proposal
// reading none drawing no split and none drawn before a reading version's history read is complete.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public partial class ReadSurface
{
    // The rows of sections 15.19 and 15.20 this check reaches from 20.8.
    internal static readonly string[] ReleaseAnswerClaims =
    [
        CheckReach.Key(Scope.LedgerPage, "What the release readings came to"),
        CheckReach.Key(Scope.LoopPage, "A change reading a release reading"),
    ];

    static void AnswerRow(TemporaryStore store, string run, string index, string family, string question, string answer, string side, int setups, int winners, double result, double edge) =>
        store.Execute(
            "INSERT INTO release_answer_edge (run_id, month, index_code, family, question, answer, side, cutoff, setups, winners, mean_result, mean_edge) VALUES "
            + FormattableString.Invariant($"('{run}', '2026-10', '{index}', '{family}', '{question}', '{answer}', '{side}', '2025-01-31', {setups}, {winners}, {result.ToString("R", CultureInfo.InvariantCulture)}, {edge.ToString("R", CultureInfo.InvariantCulture)});"));

    // A reading version whose history read is complete, stating the model's cutoff.
    static void CompletePin(TemporaryStore store, string cutoff) =>
        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
            + $"VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '{cutoff}', '2026-10-18T00:00:00Z', '2026-10-21T00:00:00Z', 'constructed');");

    [Fact]
    public async Task TheLedgerPageDrawsWhatTheReleaseReadingsCameToAsTheNewestRunStoredItAndWorksNoneOfIt()
    {
        using var store = new TemporaryStore().Migrated();

        LedgerYearRow(store, "pullback", 2025, 40, 10, 0, 0, 40, "0.1", "0.05", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
        LedgerYearRow(store, "heavyweight", 2025, 40, 10, 0, 0, 40, "0.01", "0.005", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Ledger(string family) => WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/ledger?universe=500&family={family}"));

        // No run has worked it yet: the card says so and draws no table.
        var none = await Ledger("pullback");

        Assert.Contains("<p class=\"degraded\" data-answers=\"none\">No monthly run has worked what the release readings came to for this setup on this index yet: they join the ledger once their history read is complete.</p>", none, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class=\"ledger-answers\"", none, StringComparison.Ordinal);

        // An older run, then the newest: results better on both sides of the cutoff and worse after it alone, the
        // heavyweights' results better after it, and a newer run on the S&P 400, which the S&P 500's page does not read.
        AnswerRow(store, "loop-links-GSPC-20261001T120000Z", "GSPC", "pullback", "results", "better", "after", 9, 9, 9.0, 9.0);
        AnswerRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "pullback", "results", "better", "before", 30, 18, 0.25, 0.125);
        AnswerRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "pullback", "results", "better", "after", 12, 7, 0.5, 0.375);
        AnswerRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "pullback", "results", "worse", "after", 4, 1, -0.75, -0.5);
        AnswerRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "heavyweight", "results", "better", "after", 6, 4, 0.031, 0.012);
        AnswerRow(store, "loop-links-MID-20261017T120000Z", "MID", "pullback", "results", "better", "after", 8, 8, 8.0, 8.0);

        var page = await Ledger("pullback");
        var table = Regex.Match(page, "<table class=\"ledger-answers\".*?</table>", RegexOptions.Singleline).Value;

        Assert.Contains("data-run=\"loop-links-GSPC-20261016T120000Z\" data-month=\"2026-10\" data-cutoff=\"2025-01-31\">Worked by the monthly run for 2026-10, split at the local model's cutoff of 2025-01-31 by the day the SEC accepted each release.</p>", page, StringComparison.Ordinal);
        Assert.Equal(
            ["results|better", "results|worse"],
            Regex.Matches(table, "<tr data-question=\"([a-z]+)\" data-answer=\"([a-z ]+)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));

        // Results better: after the cutoff 12 setups, 7 winning, a mean result of half a risk and an edge of 0.375, and
        // beside them before it 30, 18, a quarter and an eighth, each in risks; worse after it alone, its before side none.
        Assert.Contains(
            "<tr data-question=\"results\" data-answer=\"better\"><td>results</td><td>better</td>"
            + "<td class=\"r num\">12</td><td class=\"r num\">7</td><td class=\"r num\" data-result-mean=\"0.5\"><span class=\"gain\">+0.50</span></td><td class=\"r num\" data-edge-mean=\"0.375\"><span class=\"gain\">+0.38</span></td>"
            + "<td class=\"r num\">30</td><td class=\"r num\">18</td><td class=\"r num\" data-result-mean=\"0.25\"><span class=\"gain\">+0.25</span></td><td class=\"r num\" data-edge-mean=\"0.125\"><span class=\"gain\">+0.13</span></td></tr>",
            table,
            StringComparison.Ordinal);
        Assert.Contains(
            "<tr data-question=\"results\" data-answer=\"worse\"><td>results</td><td>worse</td>"
            + "<td class=\"r num\">4</td><td class=\"r num\">1</td><td class=\"r num\" data-result-mean=\"-0.75\"><span class=\"loss\">-0.75</span></td><td class=\"r num\" data-edge-mean=\"-0.5\"><span class=\"loss\">-0.50</span></td>"
            + "<td class=\"r num\">none</td><td class=\"r num\">none</td><td class=\"r num\">none</td><td class=\"r num\">none</td></tr>",
            table,
            StringComparison.Ordinal);
        Assert.Contains("Result, risks", table, StringComparison.Ordinal);
        Assert.DoesNotContain("data-result-mean=\"9\"", table, StringComparison.Ordinal);
        Assert.DoesNotContain("data-result-mean=\"8\"", table, StringComparison.Ordinal);

        // The heavyweights' rows in their own unit, per cent of the buy and points.
        var heavyweights = Regex.Match(await Ledger("heavyweight"), "<table class=\"ledger-answers\".*?</table>", RegexOptions.Singleline).Value;

        Assert.Contains("Result, % of the buy", heavyweights, StringComparison.Ordinal);
        Assert.Contains("Edge, points", heavyweights, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-result-mean=\"0.031\"><span class=\"gain\">+3.1</span></td><td class=\"r num\" data-edge-mean=\"0.012\"><span class=\"gain\">+1.2</span></td>", heavyweights, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLoopPageDrawsAChangeReadingAReleaseReadingWithItsTestYearsBeforeTheCutoffAndAfterIt()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{OctoberRun}', '2026-10', 'MID', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

        // A condition on the release's results and one on the RSI, each over five test years.
        LoopProposalRow(store, "drift", "winners against losers, ranked 1", "also requires release_results at or above 1", "the drift rule today", "risks", 56, "0.3", 0, 0, 4, 2, "4.1", 0, "0.09", 3);
        LoopProposalRow(store, "drift", "winners against losers, ranked 2", "also requires rsi at or above 61.5", "the drift rule today", "risks", 56, "0.3", 0, 0, 4, 2, "4.1", 0, "0.09", 3);

        foreach (var proposal in new[] { "winners against losers, ranked 1", "winners against losers, ranked 2" })
        {
            LoopTestRow(store, "drift", proposal, 2022, 1, "a", 10, 10, 1.0, 2.0);
            LoopTestRow(store, "drift", proposal, 2023, 1, "a", 12, 12, 2.0, 1.5);
            LoopTestRow(store, "drift", proposal, 2024, 1, "a", 8, 8, 0.5, 1.5);
            LoopTestRow(store, "drift", proposal, 2025, 1, "a", 20, 20, 1.0, 3.0);
            LoopTestRow(store, "drift", proposal, 2026, 0, "a", 6, 6, 0.25, 0.75);
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Loop() => WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        // No reading version's history read is complete, so no cutoff is read and no split drawn.
        Assert.DoesNotContain("class=\"loop-cutoff\"", await Loop(), StringComparison.Ordinal);

        CompletePin(store, "2025-01-31");

        var page = await Loop();

        // Worked by hand: before the cutoff's year, 2022 to 2024, the proposal's 5.0 less the rule's 3.5 over 30 trades,
        // +1.50; from it, 2025 to 2026, 3.75 less 1.25 over 26, +2.50. The RSI's condition reads no release reading and
        // draws none.
        Assert.Single(Regex.Matches(page, "class=\"loop-cutoff\""));
        Assert.Contains(
            "<p class=\"loop-cutoff\" data-cutoff=\"2025-01-31\" data-before=\"1.5\" data-after=\"2.5\"><b>Before and after the local model's cutoff of 2025-01-31.</b> It reads what the local model read of the results releases. "
            + "The total less the rule's in the test years before the cutoff, which the model may have known the outcome of: 2022 to 2024, <span class=\"gain\">+1.50</span> per $1 risked over 30 trade(s). "
            + "In the years from it, the ones relied on: 2025 to 2026, <span class=\"gain\">+2.50</span> per $1 risked over 26 trade(s).</p>",
            page,
            StringComparison.Ordinal);

        var first = page.IndexOf("data-proposal=\"winners against losers, ranked 1\"", StringComparison.Ordinal);
        var second = page.IndexOf("data-proposal=\"winners against losers, ranked 2\"", StringComparison.Ordinal);
        var split = page.IndexOf("class=\"loop-cutoff\"", StringComparison.Ordinal);

        Assert.True(first < split && split < second, "The split is drawn beneath the proposal reading a release reading and before the next.");
    }

    // A passing proposal of the S&P 400's October run stating a condition, in the words given.
    static void PassingCondition(TemporaryStore store, string family, string proposal, string words, string column, double adjusted) =>
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + FormattableString.Invariant($"VALUES ('{OctoberRun}', 'MID', '{family}', '{proposal}', '{words}', 'the {family} rule today', 'risks', 400, 19, {adjusted}, 1, 1, 4, 3, 1.5, 1, 0.1, 3, 1, NULL, '{Condition(column, 1)}');"));

    [Fact]
    public async Task TheLoopPageAndItsPressesHoldBackAProposalReadingAReleaseReadingWhoseYearsFromTheCutoffStandAtNothingOrUnder()
    {
        using var store = new TemporaryStore().Migrated();

        // From the 20.8 correction of 2026-10-19, on the operator's ruling of that day: the S&P 400's October run, its newest,
        // two drift conditions passing with a change, the stronger on the release's results and the other on the RSI, and
        // the breakout's one condition, on the release's tone.
        Run(store, OctoberRun, "MID", "2026-10");
        PassingCondition(store, "drift", "winners against losers, ranked 1", "also requires release_results at or above 1", "release_results", 0.001);
        PassingCondition(store, "drift", "winners against losers, ranked 2", "also requires rsi at or above 61.5", "rsi", 0.002);
        PassingCondition(store, "breakout", "winners against losers, ranked 1", "also requires release_tone at or above 1", "release_tone", 0.001);

        // Worked by hand: the drift's results condition from the cutoff's year, 2025 and 2026, 1.25 against the rule's 3.75,
        // -2.50, though 5.0 against 3.5 before it; the breakout's from 2025, 3.0 against exactly 3.0, nothing, though +3.0
        // before it. Each is held back; the RSI's reads no release reading.
        foreach (var proposal in new[] { "winners against losers, ranked 1", "winners against losers, ranked 2" })
        {
            LoopTestRow(store, "drift", proposal, 2022, 1, "a", 10, 10, 1.0, 2.0);
            LoopTestRow(store, "drift", proposal, 2023, 1, "a", 12, 12, 2.0, 1.5);
            LoopTestRow(store, "drift", proposal, 2024, 1, "a", 8, 8, 0.5, 1.5);
            LoopTestRow(store, "drift", proposal, 2025, 1, "a", 20, 20, 3.0, 1.0);
            LoopTestRow(store, "drift", proposal, 2026, 0, "a", 6, 6, 0.75, 0.25);
        }

        LoopTestRow(store, "breakout", "winners against losers, ranked 1", 2024, 1, "a", 10, 10, 1.0, 4.0);
        LoopTestRow(store, "breakout", "winners against losers, ranked 1", 2025, 1, "a", 10, 10, 3.0, 3.0);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Loop() => WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));
        async Task<string> Badge() => await client.GetStringAsync(SinglePageApp.LoopPutRoute);
        async Task<(HttpStatusCode Status, string Said)> Press(string family, string proposal, string decision, string? reason = null) =>
            await CardPress(client, Decide("MID", OctoberRun, family), [("proposal", proposal), ("decision", decision), .. reason is null ? Array.Empty<(string, string)>() : [("reason", reason)]], true);

        // No reading version's history read is complete, so nothing is held back: the two stronger are put.
        Assert.DoesNotContain("held-cutoff", await Loop(), StringComparison.Ordinal);
        Assert.Equal("2", await Badge());

        CompletePin(store, "2025-01-31");

        const string Words = "Held back: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, 2025, is not above nothing, so it is not put to you.";
        const string OnPage = "Set aside: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, 2025, is not above nothing, so it is not put to you.";
        var page = await Loop();

        // Both held back with the words, in the reader's words on the page, beneath each one's two sides, which stay drawn;
        // the RSI's condition put to you with its presses, and the breakout waiting with the words.
        Assert.Equal(2, Regex.Count(page, "<p class=\"loop-held-cutoff\" data-held=\"cutoff\">" + Regex.Escape(OnPage) + "</p>"));
        Assert.Equal(2, Regex.Count(page, "<p class=\"degraded\" data-decision=\"held-cutoff\">" + Regex.Escape(OnPage) + "</p>"));
        Assert.Contains("data-after=\"-2.5\"", page, StringComparison.Ordinal);
        Assert.Contains("data-after=\"0\"", page, StringComparison.Ordinal);
        Assert.Contains("Its passing change tried reads a release reading. " + OnPage + " The next monthly run is on 2026-11-07.", page, StringComparison.Ordinal);
        Assert.Equal("1", await Badge());

        // The presses: the held proposals refused with the words, an approval and a decline alike, writing nothing; the
        // RSI's condition approved.
        Assert.Contains("Nothing was decided. " + Words, (await Press("drift", "winners against losers, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Contains("Nothing was decided. " + Words, (await Press("drift", "winners against losers, ranked 1", "decline", "not now")).Said, StringComparison.Ordinal);
        Assert.Contains("Nothing was decided. " + Words, (await Press("breakout", "winners against losers, ranked 1", "approve")).Said, StringComparison.Ordinal);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM loop_decision;"));
        Assert.Equal(HttpStatusCode.OK, (await Press("drift", "winners against losers, ranked 2", "approve")).Status);
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM loop_decision;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM loop_decision WHERE proposal = 'winners against losers, ranked 2' AND decision = 'approved';"));
    }
}
