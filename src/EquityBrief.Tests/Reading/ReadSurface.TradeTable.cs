using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 12.2 correction on the operator's ruling of 2026-09-27: a name's trade table draws the
// ladder's first tranche and the one swing plan the night's live rule read, and no plan of a candidate that was
// not live on the night, whichever filter version the night ran under. Read off the rendered name page for one
// night of each version over a constructed store, each plan at prices no other plan shares.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
public partial class ReadSurface
{
    const string TradeTableTicker = "TT";

    // The four nights: no version open, the trade gate reading the ladder at section 17's proposed values;
    // version 2, reading the nearest bands, its row storing no input, as a row written before the input was
    // stored; and versions 3 and 4, each reading section 10's plan.
    const string LadderNight = "2026-09-21";
    const string NearestBandsNight = "2026-09-22";
    const string SectionTenNight = "2026-09-23";
    const string VersionFourNight = "2026-09-24";

    // The nearest bands' stop and target, and section 10's, each a price no other figure on the page carries.
    const string NearestStop = "96.11";
    const string NearestTarget = "110.22";
    const string ClearStop = "98.33";
    const string ClearTarget = "108.44";

    // One night's row for the name: every gate passed, both swing plans entered at 100, and the trade gate's
    // values carrying the input named or none.
    static void TradeGate(TemporaryStore store, string session, string version, string? input)
    {
        var values = input is null ? "{\"reward to risk\":\"2.0\"}" : $"{{\"input\":\"{input}\",\"reward to risk\":\"2.0\"}}";
        var gates = $"{{\"gates\":[{{\"gate\":\"trade\",\"passed\":true,\"reason\":\"reward to risk 2.0 at or above 1.5\",\"values\":{values}}}],\"notes\":[]}}";

        store.Execute(
            "INSERT OR IGNORE INTO membership (index_code, ticker, joined, \"left\", observed_at, name) " +
            $"VALUES ('GSPC', '{TradeTableTicker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z', 'Trade Table Company');");
        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
            "ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk, swing_stop_moves, " +
            "clear_stop, clear_target, clear_reward_to_risk, clear_stop_moves, exclusions, passed, rank, gates) " +
            $"VALUES ('{TradeTableTicker}', '{session}', '{version}', 'code', 1, 1, 1, 'pullback', 1, 1, 1, 2.5, 1.1, '100', '{NearestStop}', '{NearestTarget}', 2.6, 1.2, " +
            $"'{ClearStop}', '{ClearTarget}', 2.7, 1.3, '[]', 1, 1, '{gates}');");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{session}', 'filter');");
        PickListing(store, TradeTableTicker, session);
        PickBar(store, TradeTableTicker, session, "100");
    }

    static TemporaryStore TradeTableStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES " +
            "('2', '{\"trade\":\"swing\"}', '2026-09-22T00:00:00Z', '2026-09-23T00:00:00Z', 'test'), " +
            "('3', '{\"trade\":\"clear\"}', '2026-09-23T00:00:00Z', '2026-09-24T00:00:00Z', 'test'), " +
            "('4', '{\"trade\":\"clear\"}', '2026-09-24T00:00:00Z', NULL, 'test');");

        TradeGate(store, LadderNight, "none", "ladder");
        TradeGate(store, NearestBandsNight, "2", null);
        TradeGate(store, SectionTenNight, "3", "clear");
        TradeGate(store, VersionFourNight, "4", "clear");

        return store;
    }

    [Fact]
    public async Task ANamesTradeTableDrawsTheLadderAndThePlanItsNightsLiveRuleReadAndNoCandidatesPlanOnAnyVersion()
    {
        using var store = TradeTableStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (night, version, live, drawn, absent) in new[]
        {
            // The ladder read: the ladder row alone, marked, and neither swing plan's prices anywhere on the page.
            (LadderNight, "none", "ladder", (string?)null, new[] { NearestStop, NearestTarget, ClearStop, ClearTarget }),
            // No input stored under a version reading the nearest bands: that plan, as Past picks trades it, and
            // never section 10's.
            (NearestBandsNight, "2", "swing", "data-stop=\"" + NearestStop + "\" data-target=\"" + NearestTarget + "\"", new[] { ClearStop, ClearTarget }),
            // Section 10's plan read under versions 3 and 4, and never the nearest bands', now a candidate's.
            (SectionTenNight, "3", "clear", "data-stop=\"" + ClearStop + "\" data-target=\"" + ClearTarget + "\"", new[] { NearestStop, NearestTarget }),
            (VersionFourNight, "4", "clear", "data-stop=\"" + ClearStop + "\" data-target=\"" + ClearTarget + "\"", new[] { NearestStop, NearestTarget }),
        })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{TradeTableTicker}/{night}"));
            var card = Assert.Single(Blocks(page, "<section class=\"card\"[^>]* data-card=\"gates\">.*?</section>"));

            Assert.Contains($"data-session=\"{night}\" data-version=\"{version}\"", card, StringComparison.Ordinal);

            // The ladder's first tranche on every night, marked on the night the trade gate read it.
            Assert.Contains($"<tr data-plan=\"ladder\" data-read=\"{(live == "ladder" ? "yes" : "no")}\"", card, StringComparison.Ordinal);

            var rows = Regex.Matches(card, "<tr data-plan=\"([a-z]+)\" data-read=\"(yes|no)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)).ToArray();

            Assert.Equal(live == "ladder" ? [("ladder", "yes")] : [("ladder", "no"), (live, "yes")], rows);
            Assert.Single(Regex.Matches(card, "the plan the trade gate read"));

            if (drawn is { } prices)
            {
                Assert.Contains($"<tr data-plan=\"{live}\" data-read=\"yes\" data-entry=\"100\" {prices}", card, StringComparison.Ordinal);
            }

            // The other plan's stop and target appear nowhere on the page, the code-written summary included.
            // "On the list before" is set aside: it draws an earlier night's trade on that night's own live
            // plan, which on the nights before section 10's plan was live is the nearest bands'.
            var rest = Regex.Replace(page, "<section class=\"card\" id=\"on-the-list-before\".*?</section>", string.Empty, RegexOptions.Singleline);

            foreach (var price in absent)
            {
                Assert.DoesNotContain(price, rest, StringComparison.Ordinal);
            }

            // The key names the two plans drawn and says why the alternative is not.
            Assert.Contains("The trade is read from the ladder's first tranche and from the swing plan that night's live rule used", card, StringComparison.Ordinal);
        }
    }
}
