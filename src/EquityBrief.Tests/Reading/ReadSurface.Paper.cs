using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.4: paper trading and each account's trade log. An account is walked by hand a session at a time, a
// skip, an uncosted trade and an open position through a split among it; each round trip is read in its order; and the
// page and the trade log are read back off the rendered markup over a constructed store, the test years verdict and the
// record since registration apart, with the account's settings in no byte of the store or the run log.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public partial class ReadSurface
{
    // The rows 19.4 adds that this check reaches: paper trading's seven and the trade log's two.
    internal static readonly string[] PaperRows =
    [
        .. new[] { "Your account", "The chart", "The chart's key", "Every account", "An account's test years verdict", "An account's record since registration", "An account's start and the version it counts", "The lines beneath" }
            .Select(row => CheckReach.Key("15.21 Paper trading", row)),
        CheckReach.Key("15.17 Past picks", "The trade log's account"),
        CheckReach.Key("15.17 Past picks", "An account's trade log"),
    ];

    static readonly DateOnly[] PaperSessions = [new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];

    [Fact]
    public void AnAccountIsWalkedASessionAtATimeWithASkipAnUncostedTradeAndAnOpenPositionThroughASplit()
    {
        // An account of 10,000 risking a tenth a trade with no cap below the whole account.
        var account = new AccountSettings(10_000m, 10m, 1m);
        var (d1, d2, d3, d4, d5) = (PaperSessions[0], PaperSessions[1], PaperSessions[2], PaperSessions[3], PaperSessions[4]);

        // AA bought on d1 at 50 over a stop at 40 paying 0.10 a share round trip, sold on d3 at 60; BB bought the same
        // session second at 20 over a stop at 19; CC bought on d2 at 10 over a stop at 5 with no cost read, still open,
        // its closes stored after a two-for-one split as the store rescales them, 5 on d2.
        PaperTrade[] trades =
        [
            new("AA", d1, 50m, 40m, d3, 60m, 0.10m, 1),
            new("BB", d1, 20m, 19m, null, null, 0.02m, 2),
            new("CC", d2, 10m, 5m, null, null, null, 1),
        ];
        var closes = new Dictionary<(string, DateOnly), decimal>
        {
            [("AA", d1)] = 50m, [("AA", d2)] = 52m, [("AA", d3)] = 55m,
            [("CC", d2)] = 5m, [("CC", d3)] = 5.25m, [("CC", d4)] = 5.5m, [("CC", d5)] = 6m,
        };
        var funds = new Dictionary<DateOnly, decimal> { [d1] = 400m, [d5] = 420m };

        var walk = PaperAccount.Walk(account, trades, PaperSessions, (ticker, session) => closes.TryGetValue((ticker, session), out var close) ? close : null, session => funds.TryGetValue(session, out var close) ? close : null);

        // d1: AA's risk buys a tenth of 10,000 over 10 a share, 100 shares, its cap 200; 5,000 and half its round trip,
        // 5, leave 4,995. BB's risk then buys 999.5 over 1, 999, capped at 9,995 over 20, 499: 9,980 is over the free cash
        // and it is skipped. The value is 4,995 and AA at its close, 5,000: 9,995.
        Assert.Equal([100L, 0L, 199L], walk.Trades.Select(one => one.Shares));
        Assert.Equal([null, PaperAccount.UnderTheCash, null], walk.Trades.Select(one => one.Skipped));
        Assert.Equal(9_995m, walk.Values[0].Value);

        // d2: CC's risk buys 999.5 over 5, 199 shares at 1,990 with no cost, leaving 3,005; AA marked at 52 over 50, 5,200,
        // and CC at its buy, 1,990: 10,195.
        Assert.Equal(10_195m, walk.Values[1].Value);

        // d3: AA sold first at 60 less half its round trip, 5,995, to 9,000; CC marked by its closes' ratio, 5.25 over 5,
        // 2,089.5: 11,089.5. d4 and d5 mark CC at 5.5 and 6 over 5, 2,189 and 2,388, the split moving nothing.
        Assert.Equal([9_995m, 10_195m, 11_089.5m, 11_189m, 11_388m], walk.Values.Select(one => one.Value));

        // Two taken, one skipped, one of the taken with no cost read; a return of 0.1388 on the size, its worst drop the
        // 5 under the start on d1, and the fund's 0.05 over the same sessions.
        Assert.Equal((2, 1, 1), (walk.Taken, walk.Skipped, walk.Uncosted));
        Assert.Equal(0.1388, walk.Return!.Value, 12);
        Assert.Equal(0.0005, walk.WorstDrop!.Value, 12);
        Assert.Equal(0.05, walk.FundReturn!.Value, 12);
        Assert.Equal(0.0888, walk.Difference!.Value, 12);
        Assert.Equal(d1, walk.MarkedFrom);
        Assert.Equal([-0.0005, 0.0195, 0.10895, 0.1189, 0.1388], walk.Returns.Select(one => Math.Round(one.Return, 6)));

        // A fund whose series starts after the first session is named from where it starts and read for no return.
        var later = PaperAccount.Walk(account, trades, PaperSessions, (ticker, session) => closes.TryGetValue((ticker, session), out var close) ? close : null, session => session >= d3 ? 410m : null);

        Assert.Null(later.FundReturn);
        Assert.Equal(d3, later.FundFrom);

        // A position whose close is not yet read stands at its cost, and the walk marks from the first session it reads
        // every one.
        var unread = PaperAccount.Walk(account, trades, PaperSessions, (ticker, session) => ticker == "CC" && session == d3 ? null : closes.TryGetValue((ticker, session), out var close) ? close : null);

        Assert.Equal(d4, unread.MarkedFrom);
    }

    [Fact]
    public void ATradesRoundTripIsReadInItsOrderAndItsSaleFromItsResult()
    {
        // Stored in risks: 0.05 of a 10 risk, 0.5 a share. As a fraction of the buy: 0.002 of 100, 0.2. The member
        // readings' cost: 0.144 per cent of 50, 0.072. Before the readings were kept, the published table at the company's
        // value, $1.5 billion in the $1 to 2 billion band at a price of 50 in the top price band, 0.129 per cent each side:
        // 0.0645. A company valued at none reads the same band. No bar on the buy session: none.
        Assert.Equal(0.5m, PaperAccount.RoundTripOf(50m, 40m, 0.05, null, null, true, null));
        Assert.Equal(0.2m, PaperAccount.RoundTripOf(100m, null, null, 0.002, null, true, null));
        Assert.Equal(0.072m, PaperAccount.RoundTripOf(50m, 40m, null, null, 0.144, true, null));
        Assert.Equal(0.0645m, PaperAccount.RoundTripOf(50m, 40m, null, null, null, true, 1_500_000_000m));
        Assert.Equal(TradeCost.RoundTrip(null, 50m, 50m), PaperAccount.RoundTripOf(50m, 40m, null, null, null, true, null));
        Assert.Null(PaperAccount.RoundTripOf(50m, 40m, null, null, null, false, 1_500_000_000m));

        // A swing trade's sale is its buy and its result's risks over the stop's distance: 2 risks of 10 from 50 is 70; a
        // holding's its buy grown by its result, 0.05 on 100 is 105; an open trade has none.
        Assert.Equal(70m, PaperAccount.SaleOf(50m, 40m, PaperSessions[2], 2, false));
        Assert.Equal(105m, PaperAccount.SaleOf(100m, null, PaperSessions[2], 0.05, true));
        Assert.Null(PaperAccount.SaleOf(50m, 40m, null, null, false));
    }

    [Fact]
    public async Task PaperTradingAndATradeLogDrawEachAccountsVerdictAndItsRecordApartAndTheSettingsInNoStoreByte()
    {
        using var store = await FamilyPagesStore();

        // The breakout's live rule and one variant registered on 2026-01-02; the variant keeps three trades of its own:
        // T7 ended at -0.5 risks and T1 at 1 on 2026-09-29 and 30, and T6 bought on 2026-10-01, open, at 100 over a stop at
        // 97. T6 split two for one since, so the store holds its closes rescaled, 50 on its buy session and 51.5 on the
        // night: three per cent on three at risk, a risk so far, where its buy against tonight's close would read -16.
        RegisteredRule(store, 201, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 202, TheSetupFamilies.Breakouts[^2], "2026-01-02T12:00:00Z");
        Kept(store, ProvisionalBreakout, "breakout", "T7", "2026-09-28", "2026-09-29", -0.5, 0.0);
        Kept(store, ProvisionalBreakout, "breakout", "T1", "2026-09-28", "2026-09-30", 1.0, 0.2);
        Kept(store, ProvisionalBreakout, "breakout", "T6", "2026-10-01", null, null, null);

        foreach (var (ticker, session, close) in new[] { ("T7", "2026-09-28", "100"), ("T1", "2026-09-28", "100"), ("T6", "2026-10-01", "50"), ("T6", TheSwitch, "51.5") })
        {
            PickBar(store, ticker, session, close);
        }

        // The newest per-rule test on the S&P 500: the live breakout passed against its benchmark, and the variant read
        // within luck against it.
        store.Execute(
            "INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why) VALUES " +
            $"('loop-rules-GSPC-20261003T120000Z', '2026-10', 'GSPC', 'breakout', '{LiveBreakout}', 'live', 'the live breakout', 'its benchmark', 'risks', 420, 9, 0.001, 1, 1, 3, 3, 4.5, 1, 1, NULL), " +
            $"('loop-rules-GSPC-20261003T120000Z', '2026-10', 'GSPC', 'breakout', '{ProvisionalBreakout}', 'variant', 'the variant', '{LiveBreakout}', 'risks', 380, 9, 0.3, 0, 1, 3, 2, 1.0, 1, 0, NULL);");
        store.Execute(
            "INSERT INTO loop_rule_year (run_id, index_code, family, rule, year, complete, units, total, compared_units, compared_total) VALUES " +
            $"('loop-rules-GSPC-20261003T120000Z', 'GSPC', 'breakout', '{LiveBreakout}', 2025, 1, 140, 12.5, 140, 0);");

        // Settings no store row holds, written to their own file.
        AccountFile.Write(store.Root, new AccountSettings(98_765.43m, 1.37m, 0.23m));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?variants=breakout&rule=breakout"));
        var variantKey = RunScreen.Slug(ProvisionalBreakout);

        // The account's line and the form folded beneath its press hold the settings, and the page walks the accounts
        // from them.
        Assert.Contains($"<section class=\"paper\" data-universe=\"500\" data-night=\"{TheSwitch}\"", page, StringComparison.Ordinal);
        Assert.Contains("data-walked=\"true\"", page, StringComparison.Ordinal);
        Assert.Contains("<span>Account <b>$98,765</b></span><span>Risk a trade <b>1.37%</b></span><span>Most in one stock <b>23%</b></span><span>Each rule trades its own copy of this account.</span>", page, StringComparison.Ordinal);
        Assert.Contains("<summary>Change the account</summary>", page, StringComparison.Ordinal);
        Assert.Contains("value=\"98765.43\"", page, StringComparison.Ordinal);

        // A tile a family's live or provisional rule in the one family order, from 22.11 Quality at a fair price's after them,
        // provisional where no freeze stands, and from 22.3 the short-term dip's in the page's order before the sector
        // heavyweights', and from 22.13 none of the fundamentals-first family's; every family's accounts in that order on the
        // variants' select and the scorecard's, the pullback's two standing variants counted beside its rule.
        Assert.Equal(
            [("pullback", "live"), ("breakout", "live"), ("drift", "live"), ("dip", "provisional"), ("heavyweight", "live"), ("quality", "provisional")],
            Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([^\"]+)\" data-standing=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains("<option value=\"\">Pullback · live rule and 2 variants</option>", page, StringComparison.Ordinal);
        Assert.Equal(
            ["", "breakout", "drift", "dip", "heavyweight", "quality"],
            Regex.Matches(Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, "<option value=\"([^\"]*)\"").Select(match => match.Groups[1].Value));

        // Within the breakout the live rule that passed and the variant within luck, each beside its own name; the
        // variant's record since registration too few, 2 of 300, beside its return and never in its verdict.
        var live = AccountRowOf(page, "list-breakout");
        var variant = AccountRowOf(page, variantKey);

        Assert.Contains($"<span class=\"pv-verdict\" data-verdict=\"{RuleVerdict.Passed}\">{RuleVerdict.Passed}</span>", live, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"pv-verdict\" data-verdict=\"{RuleVerdict.WithinLuck}\">{RuleVerdict.WithinLuck}</span>", variant, StringComparison.Ordinal);
        Assert.Contains("<span class=\"too-few\">too few trades (2 of 300)</span>", variant, StringComparison.Ordinal);
        Assert.DoesNotContain("too few", Regex.Match(variant, "<span class=\"pv-verdict\".*?</span>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

        // The live rule's scorecard: its verdict with its adjusted p against the bar under the pointer, its one test year as
        // a box, the live rule's 12.5 risks over 140 trades against its benchmark's nothing, and the warning that its
        // settings were tuned on these years.
        var score = Regex.Match(page, "<div class=\"score\" .*?</details>", RegexOptions.Singleline).Value;

        Assert.Contains($"<span class=\"pill verdict\" data-verdict=\"{RuleVerdict.Passed}\" title=\"adjusted p 0.0010 against the bar {LoopGate.Bar.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}\">", score, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"sy better\" data-year=\"2025\" data-word=\"Better\" data-edge=\"{(12.5 / 140).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}\" title=\"2025: 140 trades, a total of +12.50 risks against 0.00 risks\">", score, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.TunedLine, score, StringComparison.Ordinal);
        Assert.Contains($"<td><span class=\"pill\" title=\"adjusted p 0.3000 against the bar {LoopGate.Bar.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}\">{RuleVerdict.WithinLuck}</span></td>", score, StringComparison.Ordinal);

        // Each opens its own trade log on the index.
        Assert.Contains($"href=\"#/picks?universe=500&account={Uri.EscapeDataString(variantKey)}\"", variant, StringComparison.Ordinal);
        Assert.Contains("href=\"#/picks?universe=500&account=list-breakout\"", live, StringComparison.Ordinal);
        Assert.Contains("<a class=\"paper-log\" href=\"#/picks?universe=500&account=list-breakout\">trade log</a>", score, StringComparison.Ordinal);

        // The chart draws the heads and yours by default and no variant; the breakout chosen in the hash adds the
        // variant's line dashed, each line with a key whose press the shell's script toggles.
        Assert.DoesNotContain("data-standing=\"variant\" d=", page, StringComparison.Ordinal);

        var chosen = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?family=breakout"));

        Assert.Contains($"<path class=\"paper-line dashed\" data-account=\"{variantKey}\" data-family=\"breakout\" data-standing=\"variant\"", chosen, StringComparison.Ordinal);
        Assert.Contains($"<button type=\"button\" class=\"paper-key\" data-line=\"{variantKey}\"", chosen, StringComparison.Ordinal);
        Assert.Contains(".paper-key[data-line]", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);

        // The two lines beneath what the system bought and the variants' table.
        Assert.Contains(MarkRenderer.AdoptedLine, page, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.EntryPriceLine, page, StringComparison.Ordinal);

        // The variant's trade log: its verdict and its label apart, its trades newest first, the open one's result so far
        // at tonight's close, and the running total of the two ended, half a risk, not evidence yet.
        var log = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/picks?account={Uri.EscapeDataString(variantKey)}"));

        Assert.Contains($"<div class=\"paper-log-head\" data-account=\"{variantKey}\" data-family=\"breakout\" data-trades=\"3\">", log, StringComparison.Ordinal);
        Assert.Contains($"<span data-verdict=\"{RuleVerdict.WithinLuck}\">{RuleVerdict.WithinLuck}</span>", log, StringComparison.Ordinal);
        Assert.Contains("too few trades (2 of 300)", log, StringComparison.Ordinal);
        Assert.Equal(["T6", "T1", "T7"], Regex.Matches(log, "<tr data-ticker=\"([^\"]+)\" data-bought=").Select(match => match.Groups[1].Value));

        // From 19.11 each trade's stock opens its chart page.
        Assert.Equal(["T6", "T1", "T7"], Regex.Matches(log, "<td><a href=\"#/chart/([^\"]+)\">").Select(match => match.Groups[1].Value));
        Assert.Matches("<tr data-ticker=\"T6\" data-bought=\"2026-10-01\" data-open=\"true\">.*?<span class=\"gain\">\\+1\\.00</span> risks <span class=\"degraded\">so far</span>", log);
        Assert.Contains("<figure class=\"paper-running\" data-total=\"0.5\" data-ended=\"2\">", log, StringComparison.Ordinal);
        Assert.Contains(", not evidence yet", log, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.EntryPriceLine, log, StringComparison.Ordinal);

        // The lists' view: the selector with the lists first, then from the 19.4 correction of 2026-10-13 each family's
        // live or provisional account and its variants in the one family order, the pullback's before the breakout's,
        // and You last; and each family's verdict beside the lists' trades.
        var lists = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));
        var options = Regex.Matches(lists, "<select data-merge-key=\"account\".*?</select>", RegexOptions.Singleline).Single().Value;

        Assert.Equal(
            ["", "list-pullback", RunScreen.Slug("the swing filter with the market gate off, from version 1"), RunScreen.Slug("the swing filter with one-session arrival, from version 1"), "list-breakout", variantKey, "list-drift", "list-fundamentals", "list-dip", "list-heavyweight", "list-quality", "you"],
            Regex.Matches(options, "<option value=\"([^\"]*)\"").Select(match => match.Groups[1].Value));
        Assert.Contains($"<li data-family=\"breakout\" data-verdict=\"{RuleVerdict.Passed}\">", lists, StringComparison.Ordinal);

        // From the 19.4 correction of 2026-10-21 each family's line keeps its too-few words inside its sentence, between
        // its count and where its record starts: the rule setting them on a line of their own beneath a return in paper
        // trading's variants table is taken back to the sentence's flow in every sentence holding them, this one and a
        // trade log's.
        var heads = Assert.Single(Blocks(lists, "<div class=\"paper-heads\">.*?</div>"));

        Assert.Matches("Since registered: \\d+ trade\\(s\\), <span class=\"too-few\">too few (trades|months) \\(\\d+ of \\d+\\)</span>, ", heads);
        Assert.Contains(".too-few{display:block;font-size:12px;color:var(--soft)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".paper-log-since .too-few,.paper-heads .too-few{display:inline}", Stylesheet.Css, StringComparison.Ordinal);

        // From the 22.11 correction of 2026-10-25 each family is named by its label in the selector and beside its
        // verdict, the quality family's among them, and never by the word the store keeps it under.
        var chips = Regex.Matches(heads, "<span class=\"fam-swatch\" data-family=\"([^\"]+)\"></span>([^<]+)</span>").Select(match => (Family: match.Groups[1].Value, Label: match.Groups[2].Value)).ToArray();

        Assert.Equal(7, chips.Length);
        Assert.All(chips, chip => Assert.NotEqual(chip.Family, chip.Label));
        Assert.Contains(("quality", "Quality at a fair price"), chips);
        Assert.Matches("<option value=\"list-quality\" title=\"Quality at a fair price, the page's list on its starting settings\">Quality at a fair price · ", options);

        // The settings are in no byte of the store, no run log row and no exported report.
        Assert.DoesNotContain("98765", await client.GetStringAsync("/exports/name/T6"), StringComparison.Ordinal);

        using (var file = new FileStream(store.DatabaseFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var bytes = new MemoryStream())
        {
            await file.CopyToAsync(bytes);

            Assert.DoesNotContain("98765", System.Text.Encoding.Latin1.GetString(bytes.ToArray()), StringComparison.Ordinal);
        }

        Assert.Empty(Strings(store, "SELECT detail FROM run_log WHERE detail LIKE '%98765%';"));
    }

    [Fact]
    public async Task AnAccountCountsOnlyItsRulesCurrentVersionAndFoldsTheEarlierVersionsTradesIntoItsLog()
    {
        using var store = await FamilyPagesStore();

        // The breakout's variant registered on Monday 2026-09-21 and registered again on Tuesday 2026-09-29, its first
        // session that day; no replay stands on the run log, so its account starts on 2026-09-29 as the Run page's
        // record of it does.
        RegisteredRule(store, 201, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 202, TheSetupFamilies.Breakouts[^2], "2026-09-21T12:00:00Z");
        RegisteredRule(store, 203, TheSetupFamilies.Breakouts[^2], "2026-09-29T12:00:00Z", retires: ProvisionalBreakout);
        RegisteredRule(store, 204, TheSetupFamilies.Breakouts[^2], "2026-09-29T12:00:00Z");

        // Under the first version: E1 bought on 2026-09-24 and ended on 09-28 at a risk, and E2 bought on 09-25 and open.
        // Under the second: N1 bought on 09-29 and ended on 10-01 at two risks, a sale at 106, and N2 bought on 09-30 and
        // ended on the night at a loss of its risk, a sale at 97. Each bought at 100 over a stop at 97 paying nothing.
        foreach (var (ticker, bought, ended, result) in new[] { ("E1", "2026-09-24", "2026-09-28", 1.0), ("E2", "2026-09-25", (string?)null, (double?)null), ("N1", "2026-09-29", "2026-10-01", 2.0), ("N2", "2026-09-30", TheSwitch, -1.0) })
        {
            store.Execute(
                "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members, cost) VALUES " +
                $"('{ProvisionalBreakout}', '{ticker}', '{bought}', 'breakout', 1, '100', '97', NULL, 1.5, NULL, 63, {Quoted(ended)}, {Number(result)}, {Number(result is null ? null : 0.0)}, {(result is null ? "NULL" : "400")}, 0);");
        }

        foreach (var (ticker, session, close) in new[]
        {
            ("N1", "2026-09-29", "100"), ("N1", "2026-09-30", "102"), ("N1", "2026-10-01", "106"), ("N1", TheSwitch, "106"),
            ("N2", "2026-09-30", "100"), ("N2", "2026-10-01", "98"), ("N2", TheSwitch, "97"),
            ("E1", "2026-09-24", "100"), ("E2", "2026-09-25", "100"), ("E2", TheSwitch, "103"),
        })
        {
            PickBar(store, ticker, session, close);
        }

        FillAtTheirOwnBuy(store);
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1.5m, 1m));

        var trades = Strings(store, "SELECT ticker || session_date FROM family_trade ORDER BY rowid;");
        var register = Strings(store, "SELECT id || event FROM candidate_register ORDER BY id;");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?variants=breakout&rule=breakout"));
        var key = RunScreen.Slug(ProvisionalBreakout);
        var row = AccountRowOf(page, key);

        // A risk of 150, one and a half per cent of 10,000, over 3 a share buys 50 shares. 09-29: N1 for 5,000, valued at
        // 10,000. 09-30: N2 for 5,000 off a worth of 10,000, the cash at nothing, N1 at 102 over 100, 10,100. 10-01: N1
        // sold at 106, 5,300 in cash, N2 at 98 over 100, 4,900: 10,200. The night: N2 sold at 97, 4,850: 10,150. Its
        // return 0.015, over its two trades, both decided, the live rule's list taking neither. E1 and E2 are in none of it.
        Assert.StartsWith($"<tr data-account=\"{key}\" data-standing=\"variant\" data-trades=\"2\" data-shared=\"0\" data-only=\"2\">", row, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-return=\"0.015\">", row, StringComparison.Ordinal);
        Assert.Contains($"<tr data-account=\"{key}\" data-verdict=\"{RuleVerdict.NotTested}\" data-better=\"none\">", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">2 of 300 decided</td>", page, StringComparison.Ordinal);

        // It starts on 2026-09-29 and counts the second registration, named under the pointer with no trade of the first's
        // in any figure of it.
        Assert.Contains($"title=\"{char.ToUpperInvariant(ProvisionalBreakout[0])}{ProvisionalBreakout[1..]}; counts the version registered 2026-09-29, code v; from 2026-09-29\">", row, StringComparison.Ordinal);

        // Its trade log counts N1 and N2, and folds E1 and E2 beneath, each named by the version that listed it, E2's
        // result so far 1 risk at 103 over 100 on a risk of 3.
        var log = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/picks?account={Uri.EscapeDataString(key)}"));
        var folded = Regex.Match(log, "<details class=\"paper-earlier\" data-earlier=\"2\">.*?</details>", RegexOptions.Singleline).Value;
        var counted = log[..log.IndexOf("<details class=\"paper-earlier\"", StringComparison.Ordinal)];

        Assert.Contains("<p class=\"paper-log-start\" data-since=\"2026-09-29\"><b>Counts from.</b> 2026-09-29", log, StringComparison.Ordinal);
        Assert.Contains("<span class=\"paper-version\" data-earlier=\"2\">counts the version registered 2026-09-29, code v; 2 trade(s) of earlier versions left out</span>", log, StringComparison.Ordinal);
        Assert.Equal(["N2", "N1"], Regex.Matches(counted, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            [("E2", "the version registered 2026-09-21, code v"), ("E1", "the version registered 2026-09-21, code v")],
            Regex.Matches(folded, "<tr data-ticker=\"([^\"]+)\" data-bought=\"[^\"]+\" data-open=\"[^\"]+\" data-version=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Matches("<tr data-ticker=\"E2\".*?<span class=\"gain\">\\+1\\.00</span> risks <span class=\"degraded\">so far</span>", folded);
        Assert.Contains("<figure class=\"paper-running\" data-total=\"1\" data-ended=\"2\">", counted, StringComparison.Ordinal);

        // The breakout's live account, registered once on 2026-01-02, counts that version on its tile and leaves nothing
        // out, so nothing is folded beneath what the system bought; and reading the pages deleted no stored trade and no
        // registration.
        Assert.Contains("; counts the version registered 2026-01-02, code v\">", Regex.Match(page, "<div class=\"paper-tile\" data-family=\"breakout\"[^>]*>").Value, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"paper-earlier\"", page, StringComparison.Ordinal);
        Assert.Equal(trades, Strings(store, "SELECT ticker || session_date FROM family_trade ORDER BY rowid;"));
        Assert.Equal(register, Strings(store, "SELECT id || event FROM candidate_register ORDER BY id;"));
        Assert.Equal(4, trades.Count);

        // Where a replay under the version a rule stands at reproduced every trade, its account starts where the Run
        // page's record does, at the session the replay counted from.
        var standing = new CandidateRow(204, ProvisionalBreakout, TheSetupFamilies.Breakouts[^2].Evaluator, "registered", null, new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero), EvaluatorVersion: "v");

        Assert.Equal(new DateOnly(2026, 9, 29), PaperScreen.StartOf(standing, []));
        Assert.Equal(new DateOnly(2026, 9, 21), PaperScreen.StartOf(standing, [new EquityBrief.Core.Candidates.FamilyReplayRow(ProvisionalBreakout, "v", new DateOnly(2026, 9, 21), true, "reproduced", new DateTimeOffset(2026, 9, 29, 13, 0, 0, TimeSpan.Zero))]));
        Assert.Equal(new DateOnly(2026, 9, 29), PaperScreen.StartOf(standing, [new EquityBrief.Core.Candidates.FamilyReplayRow(ProvisionalBreakout, "v", new DateOnly(2026, 9, 21), false, "differs", new DateTimeOffset(2026, 9, 29, 13, 0, 0, TimeSpan.Zero))]));

        // An account whose start a replay carried back names the registration it counts and says the replay carried it.
        Assert.Equal("the version registered 2026-09-29, code v, carried back by a replay that reproduced every trade", PaperScreen.CountedWords(standing, new DateOnly(2026, 9, 21)));
        Assert.Equal("the version registered 2026-09-29, code v", PaperScreen.CountedWords(standing, new DateOnly(2026, 9, 29)));
    }

    [Fact]
    public async Task ATradeBoughtBeforeTheMemberReadingsIsValuedAsTheMemberReaderValuesItAndOneWithNoBarIsUncosted()
    {
        using var store = new TemporaryStore().Migrated();

        // OLD bought on 2026-10-01 at a close of 40, before the member readings were kept: its newest fetch on that night
        // filed 50,000,000 shares for the quarter to 2026-06-30 on 2026-08-01, stated against a close of 40 on 2026-09-30
        // that the store holds at 40 still. Its value is 50,000,000 times 40, $2 billion, the top value band, at a price
        // of 40 in the top price band: 0.072 per cent each side, 0.0288 a share round trip on a buy and sale at 40. NEW
        // bought on 2026-10-07 reads its member reading's cost; NONE bought on a session its bar is not held for reads
        // nothing.
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, shares, basis_session, basis_close) VALUES " +
            "('OLD', '2026-09-30T23:00:00Z', '2026-09-30', '2026-06-30', '2026-08-01', '50000000', '2026-09-30', '40');");
        store.Execute(
            "INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, company_value, cost, cost_double) VALUES " +
            "('GSPC', '2026-10-07', 'NEW', '20', '1', '3000000000', 0.144, 0.288);");
        PickBar(store, "OLD", "2026-09-30", "40");
        PickBar(store, "OLD", "2026-10-01", "40");
        PickBar(store, "NEW", "2026-10-07", "20");

        var read = new ReadApi(store.DatabaseFile, EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates));
        var costs = await read.MemberCostsAsync("GSPC", [("OLD", new DateOnly(2026, 10, 1)), ("NEW", new DateOnly(2026, 10, 7)), ("NONE", new DateOnly(2026, 10, 7))]);
        var closes = await read.ClosesAsync(["OLD", "NEW", "NONE"], new DateOnly(2026, 9, 1));

        Assert.Equal((null, 2_000_000_000m), costs[("OLD", new DateOnly(2026, 10, 1))]);
        Assert.Equal((0.144, 3_000_000_000m), costs[("NEW", new DateOnly(2026, 10, 7))]);
        Assert.False(costs.ContainsKey(("NONE", new DateOnly(2026, 10, 7))));

        Assert.Equal(0.0288m, PaperAccount.RoundTripOf(40m, 35m, null, null, null, closes.ContainsKey(("OLD", new DateOnly(2026, 10, 1))), costs[("OLD", new DateOnly(2026, 10, 1))].Value));
        Assert.Equal(0.0288m, PaperAccount.RoundTripOf(20m, 18m, null, null, 0.144, true, null));
        Assert.Null(PaperAccount.RoundTripOf(30m, 28m, null, null, null, closes.ContainsKey(("NONE", new DateOnly(2026, 10, 7))), null));
    }

    // An account's row of the variants' table, from its opening tag to its close.
    static string AccountRowOf(string page, string account)
    {
        var at = page.IndexOf($"<tr data-account=\"{account}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"no row for the account {account}");

        return page[at..(page.IndexOf("</tr>", at, StringComparison.Ordinal) + 5)];
    }
}
