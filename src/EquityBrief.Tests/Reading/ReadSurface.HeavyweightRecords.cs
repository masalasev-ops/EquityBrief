using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 14.6: each registered sector heavyweights rule's record read back off the run page over constructed
// holdings in a book of its own, its edge in points and its whole blocks by the session each holding ended on, and
// the card on tonight's page reading the day its live rule registered and the variants beside it off the register.
// see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
// see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
public partial class ReadSurface
{
    // The part of the run page's setups row the freeze adds.
    internal static readonly string[] HeavyweightRecordPageClaims =
    [
        CheckReach.Key("15.10 Run", "The setup families, the sector heavyweights' registered rules among them, each holding's edge its percent return less its size cut's and counted in the block of the session it ended on"),
    ];

    // One holding a rule's book kept, ended or not, with its result and its size cut's return where it ended.
    static void HeldByRule(TemporaryStore store, string candidate, string ticker, string entered, string? ended, double? result, double? cut) =>
        store.Execute(
            "INSERT INTO heavyweight_rule_holding (candidate, ticker, entered_on, sector, company, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return) VALUES " +
            $"('{Doubled(candidate)}', '{ticker}', '{entered}', 'Energy', 'CIK 0000000101', '100', 1.0, '[]', '{ended ?? entered}', " +
            $"{(ended is null ? "NULL" : $"'{ended}'")}, {(ended is null ? "NULL" : "'101'")}, {(ended is null ? "NULL" : "'no longer the leader'")}, " +
            $"{(result is { } made ? made.ToString(CultureInfo.InvariantCulture) : "NULL")}, {(cut is { } its ? its.ToString(CultureInfo.InvariantCulture) : "NULL")});");

    [Fact]
    public async Task EachRegisteredHeavyweightsRuleIsReadOnTheRunPageOverItsOwnBookByTheSessionEachHoldingEnded()
    {
        // The four the freeze writes, registered on Friday 2026-01-02, a session the exchange traded.
        using var store = await FamilyNightStore();

        var live = TheSetupFamilies.Heavyweights[0].Candidate;
        var bothExits = TheSetupFamilies.Heavyweights[1].Candidate;
        var members = TheSetupFamilies.Heavyweights[2].Candidate;
        var every = TheSetupFamilies.Heavyweights[3].Candidate;

        for (var at = 0; at < TheSetupFamilies.Heavyweights.Count; at++)
        {
            RegisteredRule(store, 201 + at, TheSetupFamilies.Heavyweights[at], "2026-01-02T12:00:00Z");
        }

        // The live rule's book. H1 sold on 2026-02-02 having made 5% against its size cut's 2%, an edge of 3 points;
        // H2 sold on 2026-03-02 having lost 4% against 1%, an edge of -5 points; both end inside the first block of
        // 63 sessions from 2026-01-02, which ends on 2026-04-02 and is whole by the night. H3 is held; H4 was sold
        // after the night and is read as held on it. Decided: H1 and H2, an edge of (3 - 5) / 2 = -1 point, one
        // whole block.
        HeldByRule(store, live, "H1", "2026-01-02", "2026-02-02", 0.05, 0.02);
        HeldByRule(store, live, "H2", "2026-02-02", "2026-03-02", -0.04, 0.01);
        HeldByRule(store, live, "H3", "2026-09-01", null, null, null);
        HeldByRule(store, live, "H4", "2026-08-03", "2026-10-05", 0.1, 0.0);

        // The variant selling on either exit kept two holdings of its own: H1 bought beside the live rule's and sold
        // on the same session, an edge of 3 points in the first block; and H5 bought on 2026-03-02, in the first
        // block, and sold on 2026-05-01, in the second, from 2026-04-06 to 2026-07-06 and whole by the night, an edge
        // of 1 point counted in the block it ended in. Decided: an edge of (3 + 1) / 2 = 2 points over two whole
        // blocks, where counting H5 at its buy would read one. The live rule's holdings are none of its.
        HeldByRule(store, bothExits, "H1", "2026-01-02", "2026-02-02", 0.05, 0.02);
        HeldByRule(store, bothExits, "H5", "2026-03-02", "2026-05-01", 0.02, 0.01);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var rows = Regex.Matches(records, "<tr data-family=\"heavyweight\" data-rule=.*?</tr>", RegexOptions.Singleline).Select(match => match.Value).ToArray();

        // One row a rule, the live rule first and the variants in their names' order, each read over its own book at
        // the family's own level, 0.05 over its four distinct trials.
        Assert.Equal(
            [
                $"<tr data-family=\"heavyweight\" data-rule=\"{live}\" data-live=\"true\" data-trades=\"4\" data-decided=\"2\" data-edge=\"-0.01\" data-blocks=\"1\" data-look=\"8\" data-level=\"0.0125\" data-from=\"2026-01-02\" data-restarted=\"false\">",
                $"<tr data-family=\"heavyweight\" data-rule=\"{every}\" data-live=\"false\" data-trades=\"0\" data-decided=\"0\" data-edge=\"none\" data-blocks=\"0\" data-look=\"8\" data-level=\"0.0125\" data-from=\"2026-01-02\" data-restarted=\"false\">",
                $"<tr data-family=\"heavyweight\" data-rule=\"{bothExits}\" data-live=\"false\" data-trades=\"2\" data-decided=\"2\" data-edge=\"0.02\" data-blocks=\"2\" data-look=\"8\" data-level=\"0.0125\" data-from=\"2026-01-02\" data-restarted=\"false\">",
                $"<tr data-family=\"heavyweight\" data-rule=\"{members}\" data-live=\"false\" data-trades=\"0\" data-decided=\"0\" data-edge=\"none\" data-blocks=\"0\" data-look=\"8\" data-level=\"0.0125\" data-from=\"2026-01-02\" data-restarted=\"false\">",
            ],
            rows.Select(row => row[..(row.IndexOf('>') + 1)]));

        // The cells a reader reads: the family's heading, the rule marked live, and the edge in points of the buy.
        Assert.Contains(
            $"<td class=\"setup\">{SetupFamilies.SectorHeavyweights.Heading}</td><td>{live} <b>live</b><span class=\"record-from\">its record counts from 2026-01-02</span></td><td class=\"r num\">4</td><td class=\"r num\">2</td><td class=\"r num\">-1.0 points</td><td class=\"r num\">1 of 8</td><td class=\"r num\">0.0125</td>",
            rows[0],
            StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">+2.0 points</td><td class=\"r num\">2 of 8</td>", rows[2], StringComparison.Ordinal);

        // The same counts the store gives, by each rule's own rows alone.
        Assert.Equal(["4", "2"], [.. Strings(store, $"SELECT COUNT(*) FROM heavyweight_rule_holding WHERE entered_on <= '{TheSwitch}' GROUP BY candidate ORDER BY COUNT(*) DESC;")]);

        // Tonight's card reads the day its live rule registered and the three variants beside it off the register.
        var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")));

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-01-02\" data-variants=\"3\"", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-01-02</b> · 0 holdings tonight · held while leading · 3 variants kept in books of their own", card, StringComparison.Ordinal);
    }
}
