using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 15.5: a family frozen on the S&P 400 draws its card by its live rule from the night of its freeze on, live
// since the day it was registered with its variants beside it and its rule in the words its name carries, its sweep's line
// gone from that night and not before; and the index's Run page draws each registered rule's record over its own trades,
// each trade's result read after its own round trip, with the family's setup row live beside it.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public partial class ReadSurface
{
    // The S&P 400 breakout frozen at its provisional settings with the variants given, written as the command writes them,
    // its live rule's name returned.
    static string FreezeTheBreakout(TemporaryStore store, string at, params IReadOnlyDictionary<string, double>[] variants)
    {
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var (registrations, refusal) = IndexRules.Freeze("breakout", "MID", IndexRules.Provisional(breakout), variants, [], new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);

        foreach (var (registration, place) in registrations!.Select((one, place) => (one, place)))
        {
            RegisteredRule(store, 200 + place, registration, at);
        }

        return registrations![0].Candidate;
    }

    // The S&P 400's night naming the breakout's live rule as the rule its list was read by, as the night writes it once a
    // freeze stands and its evaluator is read.
    static void ReadByTheLiveBreakout(TemporaryStore store, string live) =>
        store.Execute($"UPDATE index_family_night SET settings = json_set(settings, '$.live', json_array(json_object('family', 'breakout', 'candidate', '{Doubled(live)}'))) WHERE index_code = 'MID';");

    static readonly IReadOnlyDictionary<string, double> TheCover = new Dictionary<string, double> { [IndexRuleCandidate.QualityParameter] = 2 };

    static readonly IReadOnlyDictionary<string, double> FourTimesTheVolume = new Dictionary<string, double> { [IndexBreakoutCandidate.HighVolumeParameter] = 3 };

    // One trade an S&P 400 breakout rule kept, ended or not, with its result, its round trip and its benchmark where written.
    static void KeptOnTheIndex(TemporaryStore store, string candidate, string ticker, string session, string? endedOn, double? result, double? cost, double? benchmark) =>
        store.Execute(
            "INSERT INTO index_rule_trade (candidate, index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, risk_moves, reward_to_risk, ended_on, result, cost, benchmark, members) VALUES " +
            $"('{Doubled(candidate)}', 'MID', 'breakout', '{ticker}', '{session}', 1, '100', '97', NULL, '3', 63, 1.5, NULL, " +
            $"{(endedOn is null ? "NULL" : $"'{endedOn}'")}, {Number(result)}, {Number(cost)}, {Number(benchmark)}, {(benchmark is null ? "NULL" : "400")});");

    [Fact]
    public async Task AnSAndP400FamilyFrozenIsDrawnByItsLiveRuleFromTheNightOfItsFreezeAndItsSweepLineGoesThenAndNotBefore()
    {
        // The S&P 400 breakout's sweep found none, recorded the day before the night, and the family frozen at the instant
        // given with two variants, the cover and four times the volume, the night reading its list by the live rule where
        // it did; an answer recorded later where given. The page, and the breakout's card on it.
        async Task<(string Page, string Card)> CardFrozenAt(string at, bool readByIt, string? answeredAgainAt = null)
        {
            using var store = UniversesStore();

            RecordAnswer(store, "20261001T120000Z", "MID", "breakout", null, "none passed", "2026-10-01T12:00:00Z");

            var live = FreezeTheBreakout(store, at, TheCover, FourTimesTheVolume);

            if (readByIt)
            {
                ReadByTheLiveBreakout(store, live);
            }

            if (answeredAgainAt is not null)
            {
                RecordAnswer(store, "20261002T130000Z", "MID", "breakout", null, "none passed", answeredAgainAt);
            }

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            return (page, FamilyCardOf(page, BreakoutRule.Name));
        }

        const string FrozenRule = "The S&P 400 breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath, the profit gate, each trade paying the published spread, half at each end.";

        // Frozen the day after the night: the night's card is the provisional rule's still, its sweep's line drawn.
        var before = await CardFrozenAt("2026-10-03T12:00:00Z", readByIt: false);

        Assert.Contains("<b class=\"provisional\">Provisional: not yet frozen</b> · 1 pick tonight · 0 variants scoring in the background</p>" + SweepLineDrawn, before.Card, StringComparison.Ordinal);
        Assert.DoesNotContain(FrozenRule, before.Page, StringComparison.Ordinal);

        // Frozen before the night, but the night read its list by the provisional rule, as it does where the live rule's
        // evaluator moved: the card is the provisional rule's the list was drawn by, its line drawn.
        var unread = await CardFrozenAt("2026-10-02T12:00:00Z", readByIt: false);

        Assert.Contains("<b class=\"provisional\">Provisional: not yet frozen</b> · 1 pick tonight · 0 variants scoring in the background</p>" + SweepLineDrawn, unread.Card, StringComparison.Ordinal);
        Assert.DoesNotContain(FrozenRule, unread.Page, StringComparison.Ordinal);

        // Frozen on the night and read by it: live since that day with its two variants, its rule in the words its name
        // carries above the card, the line gone, and still listing M1 as the night stored it.
        var frozen = await CardFrozenAt("2026-10-02T12:00:00Z", readByIt: true);

        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-10-02</b> · 1 pick tonight · 2 variants scoring in the background</p>", frozen.Card, StringComparison.Ordinal);
        Assert.DoesNotContain(SweepLineDrawn, frozen.Card, StringComparison.Ordinal);
        Assert.DoesNotContain(SetupFamilies.ProvisionalStatus, frozen.Card, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(frozen.Page, Regex.Escape(FrozenRule)));
        Assert.Contains("<tr data-ticker=\"M1\" data-family=\"breakout\"", frozen.Card, StringComparison.Ordinal);

        // A sweep recorded after the freeze that found none draws the line again beneath the live rule's standing.
        var answeredAfter = await CardFrozenAt("2026-10-02T12:00:00Z", readByIt: true, "2026-10-02T13:00:00Z");

        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-10-02</b> · 1 pick tonight · 2 variants scoring in the background</p>" + SweepLineDrawn, answeredAfter.Card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnSAndP400RunPageDrawsEachFrozenRuleOverItsOwnTradesEachAfterItsOwnRoundTrip()
    {
        // The S&P 400 breakout frozen nine months before the night with one variant, the cover, the night reading its list
        // by the live rule.
        using var store = UniversesStore();
        var live = FreezeTheBreakout(store, "2026-01-02T12:00:00Z", TheCover);
        var variant = Strings(store, "SELECT candidate FROM candidate_register WHERE id = 201;").Single();

        ReadByTheLiveBreakout(store, live);

        // The live rule's three trades. T1 made 1.5 risks and paid 0.1 for its round trip against a benchmark of 0.4, an
        // edge of 1.0; T2 lost 1.0 and paid 0.2 against -0.4, an edge of -0.8; T3, kept two sessions before the night, is
        // open. Decided: two in the first block of 63 sessions, whole by the night, an edge of 0.2 / 2 = 0.1, where the same
        // trades before their round trips would read 0.25. A trade of the variant that ended after the night is read open.
        KeptOnTheIndex(store, live, "T1", "2026-01-05", "2026-01-20", 1.5, 0.1, 0.4);
        KeptOnTheIndex(store, live, "T2", "2026-01-06", "2026-02-10", -1.0, 0.2, -0.4);
        KeptOnTheIndex(store, live, "T3", "2026-09-30", null, null, null, null);
        KeptOnTheIndex(store, variant, "T1", "2026-09-29", "2026-10-05", 1.0, 0.1, 0.2);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var rows = Regex.Matches(records, "<tr data-family=\"[^\"]+\" data-rule=.*?</tr>", RegexOptions.Singleline).Select(match => match.Value).ToArray();

        // One row a standing rule, the live rule first, each family's level 0.05 over its own two trials.
        Assert.Equal(
            [
                $"<tr data-family=\"breakout\" data-rule=\"{live}\" data-live=\"true\" data-trades=\"3\" data-decided=\"2\" data-edge=\"0.1\" data-blocks=\"1\" data-look=\"8\" data-level=\"0.025\" data-from=\"2026-01-02\" data-restarted=\"false\">",
                $"<tr data-family=\"breakout\" data-rule=\"{variant}\" data-live=\"false\" data-trades=\"1\" data-decided=\"0\" data-edge=\"none\" data-blocks=\"0\" data-look=\"8\" data-level=\"0.025\" data-from=\"2026-01-02\" data-restarted=\"false\">",
            ],
            rows.Select(row => row[..(row.IndexOf('>') + 1)]));
        Assert.Contains("<td class=\"r num\">3</td><td class=\"r num\">2</td><td class=\"r num\">0.100</td><td class=\"r num\">1 of 8</td><td class=\"r num\">0.025</td>", rows[0], StringComparison.Ordinal);

        // The card names its index, and the setups' table draws the breakout live since its freeze with its variant and its
        // live rule's record in words, the other setups provisional still.
        Assert.Contains("The S&P 400's registered rules", run, StringComparison.Ordinal);

        var setups = Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>"));

        Assert.Contains("<tr data-family=\"breakout\" data-state=\"live\" data-live-since=\"2026-01-02\" data-variants=\"1\"", setups, StringComparison.Ordinal);
        Assert.Contains("<td class=\"family-record\">2 of 3 trades decided, an edge after each trade's own round trip of 0.100; 1 whole blocks of the 8 its first look reads</td>", setups, StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"drift\" data-state=\"provisional\"", setups, StringComparison.Ordinal);

        // A page with no freeze standing draws no records card.
        using var unfrozen = UniversesStore();
        using var plain = new Host(unfrozen.Root);

        Assert.DoesNotContain("family-records", await plain.CreateClient().GetStringAsync($"/screens/run/{IndexNight}?universe=400"), StringComparison.Ordinal);
    }

    // The S&P 400's heavyweights frozen at the index's own book's settings with one variant, one leader a sector, and the
    // live rule's own book: its rebalance on the night bought M1 and sold M3, bought a month before, at no longer leading,
    // M3 making 5 per cent less a round trip of 0.2 against its size cut's 1, an edge of 3.8 points; the night reading the
    // family by its live rule where it did. The live rule's name returned.
    static string FreezeTheHeavyweights(TemporaryStore store, bool readByIt, string at = "2026-10-02T12:00:00Z")
    {
        var heavyweights = (IndexRuleCandidate)CandidateEvaluators.Find("heavyweight-400")!;
        var (registrations, refusal) = IndexRules.Freeze("heavyweight", "MID", IndexRules.Provisional(heavyweights), [new Dictionary<string, double> { [IndexHeavyweightCandidate.LeadersParameter] = 1 }], [], new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);

        foreach (var (registration, place) in registrations!.Select((one, place) => (one, place)))
        {
            RegisteredRule(store, 300 + place, registration, at);
        }

        var live = registrations![0].Candidate;

        store.Execute(
            "INSERT INTO index_heavyweight_rule_night (candidate, index_code, session_date, bought, sold) VALUES " +
            $"('{Doubled(live)}', 'MID', '{IndexNight}', 1, 1);");
        store.Execute(
            "INSERT INTO index_heavyweight_rule_holding (candidate, index_code, ticker, entered_on, sector, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return, cost) VALUES " +
            $"('{Doubled(live)}', 'MID', 'M1', '{IndexNight}', 'Energy', '50', 1.0, '[]', '{IndexNight}', NULL, NULL, NULL, NULL, NULL, NULL), " +
            $"('{Doubled(live)}', 'MID', 'M3', '2026-09-01', 'Energy', '40', 1.05, '[]', '{IndexNight}', '{IndexNight}', '42', '{IndexHeavyweights.NoLongerTheLeader}', 0.05, 0.01, 0.002);");

        if (readByIt)
        {
            store.Execute($"UPDATE index_family_night SET settings = json_set(settings, '$.live', json_array(json_object('family', 'heavyweight', 'candidate', '{Doubled(live)}'))) WHERE index_code = 'MID';");
        }

        return live;
    }

    [Fact]
    public async Task AnSAndP400HeavyweightsFreezeDrawsItsCardFromItsLiveRulesOwnBookAndItsRecordInPoints()
    {
        // Both heavyweights designs' sweeps on the S&P 400 found none, recorded the day before the night, and the family
        // frozen at the instant given.
        async Task<(string Page, string Card, string Run)> Pages(bool readByIt, string at = "2026-10-02T12:00:00Z")
        {
            using var store = UniversesStore();

            RecordAnswer(store, "20261001T120000Z", "MID", "heavyweight", "a", "none passed", "2026-10-01T12:00:00Z");
            RecordAnswer(store, "20261001T120100Z", "MID", "heavyweight", "b", "none passed", "2026-10-01T12:01:00Z");
            FreezeTheHeavyweights(store, readByIt, at);

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            return (page, HeavyweightCardOf(page), WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400")));
        }

        // Read by the night: the card is the live rule's book, live since the day of the freeze with its variant, holding
        // M1 and not the index's own book's M2, its rebalance buying M1 and selling M3, under its rule's words.
        var (page, card, run) = await Pages(readByIt: true);

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-02\" data-variants=\"1\" data-last-rebalance=\"2026-10-02\" data-next-rebalance=\"2026-11-03\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain(SweepLineDrawn, card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"family-state\">Live rule since <b>2026-10-02</b> · 1 holding tonight · held while leading · 1 variant kept in books of their own · last rebalance 2026-10-02", card, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"M1\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"M2\"", card, StringComparison.Ordinal);
        Assert.Contains("The rebalance of 2026-10-02 bought M1.", card, StringComparison.Ordinal);
        Assert.Contains($"M3 was sold at the close of 2026-10-02: {IndexHeavyweights.NoLongerTheLeader}.", card, StringComparison.Ordinal);
        Assert.Contains(
            "The S&P 400 heavyweights rule of each sector's 10 largest members leading it over 251 sessions, 2 leader(s) a sector with a beta of at least 1, sold on no longer leading, the profit gate, each holding paying the published spread, half at each end.",
            page,
            StringComparison.Ordinal);

        // The Run page: the heavyweights' live rule's record over its own book, M3 decided at 0.05 less 0.002 less 0.01,
        // drawn in points, and the variant's over none; the setup row live beside it.
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var rows = Regex.Matches(records, "<tr data-family=\"heavyweight\" data-rule=.*?</tr>", RegexOptions.Singleline).Select(match => match.Value).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Contains("data-live=\"true\" data-trades=\"2\" data-decided=\"1\" data-edge=\"0.038\"", rows[0], StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">+3.8 points</td>", rows[0], StringComparison.Ordinal);
        Assert.Contains("data-live=\"false\" data-trades=\"0\" data-decided=\"0\" data-edge=\"none\"", rows[1], StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"heavyweight\" data-state=\"live\" data-live-since=\"2026-10-02\" data-variants=\"1\"", Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), StringComparison.Ordinal);

        // A freeze the night did not read: the card is the index's own book, provisional, holding M2, its sweeps' line
        // drawn.
        var (_, unread, _) = await Pages(readByIt: false);

        Assert.Contains("data-state=\"provisional\"", unread, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"M2\"", unread, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"M1\"", unread, StringComparison.Ordinal);
        Assert.Contains(SweepLineDrawn, unread, StringComparison.Ordinal);

        // Frozen the day after the night: the night's card is the index's own book with its sweeps' line, and not before.
        var (_, later, _) = await Pages(readByIt: false, at: "2026-10-03T12:00:00Z");

        Assert.Contains("data-state=\"provisional\"", later, StringComparison.Ordinal);
        Assert.Contains(SweepLineDrawn, later, StringComparison.Ordinal);
    }
}
