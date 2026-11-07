using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Paper trading and Home's account over the faults the operator's order of 2026-11-07 names, each held to the page as it
// should read against the page as it read on 2026-11-06.
public partial class ReadSurface
{
    static readonly DateOnly OrderNight = new(2026, 11, 6);

    // An open trade of XYZ, whose company the filer names Block, Inc., as What the system bought drew it on 2026-11-06.
    static PaperBoughtView BlockTrade() =>
        new("list-drift", "drift", "XYZ", "Block, Inc.", new(2026, 11, 4), new(2026, 11, 5), 88.29m, 83.26m, 98.56m, false, 60, null, new PaperPosition(85.48m, 85.48m, 83.26m, -0.64, -0.032, 2, PaperAccount.NearStop), null, [], null);

    static string PaperPageOf(PaperPageView page, MarkRenderer? marks = null) =>
        WebUtility.HtmlDecode(new SinglePageApp().PaperRegion(marks ?? new MarkRenderer(), Universes.Large, OrderNight, page, new PaperLink(Universes.Large.Word), "pullback", "pullback", (100_000m, 1m, 0.2m), 0.2m));

    // The page drew "XYZ Run of 63 trading days, Inc" in What the system bought: the reader's words read the filer's "Block"
    // as the tester's word for a run of 63 trading days.
    // see: Paper trading, the Loop page and the Ledger draw every stored word through one table of phrases into the reader's words
    [Fact]
    public void ACompanysNameIsDrawnAsItsFilerWroteItAndNeverInTheReadersWords()
    {
        var page = PaperPageOf(new PaperPageView([], [BlockTrade()], [], [], new PaperNotBought([]), [], null));

        Assert.Contains("<span class=\"pb-co\" data-as-filed>Block, Inc.</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Run of 63", page, StringComparison.Ordinal);

        // The page's own words are still read: a block outside a filed element is a run, and the words after a filed element
        // closes, its own elements nested inside it however deep, are read again.
        Assert.Equal("a run of 63 trading days", ReaderText.Words("a block"));
        Assert.Equal(
            "<span data-as-filed>Block <b>Edge <b>Gates</b></b> Co</span> its gain over the easy alternative",
            ReaderText.Page("<span data-as-filed>Block <b>Edge <b>Gates</b></b> Co</span> its edge"));
        Assert.Equal("<i data-as-filed/> a run of 63 trading days", ReaderText.Page("<i data-as-filed/> a block"));
    }

    // The breakout's live rule registered on 2026-11-03, its version's first session that day, and a replay under its version
    // reproduced every trade from 2 Oct. On 2026-11-06 its tile read "Down 1.8% since 2 Oct" with "counts from 2 Oct: its
    // earlier trades replay the same under this version", the scorecard counted from 2 Oct and the line beneath what the
    // system bought said "The accounts above count only the rules as registered now, since 3 Nov".
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
    [Fact]
    public void AnAccountCountsFromItsVersionsFirstSessionAndTheSameTradesBeforeItStandApartUnderIt()
    {
        const string rule = "the live breakout rule at the order's words";
        var standing = new EquityBrief.Api.Reading.CandidateRow(301, rule, "family-breakout", "registered", null, new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero), EvaluatorVersion: "v");
        var earlier = new EquityBrief.Api.Reading.CandidateRow(300, rule, "family-breakout", "registered", null, new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), EvaluatorVersion: "u");
        var replay = new EquityBrief.Core.Candidates.FamilyReplayRow(rule, "v", new DateOnly(2026, 10, 2), true, "reproduced", new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero));

        (PaperTrade, double?) Trade(string ticker, DateOnly listed) => (new PaperTrade(ticker, listed.AddDays(1), 100m, 90m, null, null, null, 1, Listed: listed), null);

        var head = new EquityBrief.Api.Reading.PaperAccountSource("list-breakout", "breakout", PaperAccountView.Live, "Live", rule, null, false, [], null, rule);
        var started = EquityBrief.Api.Reading.PaperScreen.Started(head, [Trade("OLD", new DateOnly(2026, 9, 30)), Trade("LH", new DateOnly(2026, 10, 29)), Trade("GDDY", new DateOnly(2026, 11, 4))], standing, [replay], [earlier, standing]);

        // It counts from 3 Nov, the session its version registered on; LH, listed on 29 Oct inside the replayed span,
        // stands apart as the same trades since 2 Oct; OLD, listed before the replay's start, is an earlier version's.
        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3)), (started.Since!.Value, started.VersionFrom!.Value));
        Assert.Equal(["GDDY"], started.Trades.Select(one => one.Trade.Ticker));
        Assert.Equal(["LH"], started.Same.Select(one => one.Trade.Ticker));
        Assert.Equal(new DateOnly(2026, 10, 2), started.SameSince);
        Assert.Equal(["OLD"], (started.Earlier ?? []).Select(one => one.Trade.Ticker));
        Assert.Equal("the version registered 2026-11-03, code v", started.Version);

        // Drawn: the same trades under their own press beneath what the system bought, named by the setup and the replay's
        // day; the tile counting from 3 Nov alone, no line beside it carrying an earlier day; and the line beneath naming no
        // start of its own.
        var walked = PaperAccount.Walk(new AccountSettings(100_000m, 1m, 0.1m), [.. started.Trades.Select(one => one.Item1)], [new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6)], (_, _) => 100m);
        var view = new PaperAccountView("list-breakout", "breakout", PaperAccountView.Live, "Live", rule, "within luck", null, "risks", [], walked, 1, 0, null, started.Since) { VersionFrom = started.VersionFrom };
        var lh = new PaperBoughtView("list-breakout", "breakout", "LH", "Labcorp Holdings Inc.", new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 30), 355.94m, 340m, null, true, 63, new DateOnly(2026, 11, 4), null, new PaperEnded(361.28m, 0.34, 0.015, PaperAccount.UnderTrail, 4, null), [], null);
        var groups = new TradeGroupsView(new DateOnly(2025, 9, 25), new DateOnly(2026, 11, 3), new TradeCountView(122, 71, 51, 21, 18, 27, -0.012), new TradeCountView(28, 23, 5, 2, 1, 2, -0.02), new TradeCountView(94, 48, 46, 19, 17, 25, -0.01), 13);
        var page = PaperPageOf(new PaperPageView([view], [], [], [], new PaperNotBought([]), [], null) { Same = [new PaperSameView("breakout", new DateOnly(2026, 10, 2), [lh])], Groups = groups });

        Assert.Contains("<summary>Breakout: The same trades before this version (since 2 Oct), kept in the rule's record and not in this account, 1 trade</summary>", page, StringComparison.Ordinal);
        Assert.Contains("data-since=\"2026-11-03\"", Regex.Match(page, "<div class=\"paper-tile\" data-family=\"breakout\"[^>]*>").Value, StringComparison.Ordinal);
        Assert.Contains("since 3 Nov on $100,000", page, StringComparison.Ordinal);
        Assert.DoesNotContain("since 2 Oct on", page, StringComparison.Ordinal);
        Assert.DoesNotContain("counts from 2 Oct", page, StringComparison.Ordinal);
        Assert.DoesNotContain("The accounts above count only the rules as registered now", page, StringComparison.Ordinal);
        Assert.DoesNotContain("The accounts above", groups.SystemLine, StringComparison.Ordinal);
    }

    // On 2026-11-06 each rule's own account walked the settings' fifth: $100,000 risking 1% a trade on stops half a dollar
    // under a buy of 100 held 200 shares, $20,000, a position, so a sixth pick of a session was skipped for the cash. The page
    // said "risking 1% a trade with at most 20% in one stock".
    // see: Each rule's paper account sizes a position by its risk and caps it at a tenth of the account, whatever a pick's card is capped at
    [Fact]
    public void EachRulesAccountCapsAPositionAtATenthAndHoldsTenBeforeItSkips()
    {
        var day = new DateOnly(2026, 11, 3);
        (PaperTrade, double?)[] picks = [.. Enumerable.Range(1, 11).Select(at => ((PaperTrade, double?))(new PaperTrade("P" + at.ToString("00", System.Globalization.CultureInfo.InvariantCulture), day, 100m, 99.5m, null, null, 0m, at, Listed: day.AddDays(-1)), null))];
        var head = new EquityBrief.Api.Reading.PaperAccountSource("list-drift", "drift", PaperAccountView.Live, "Live", "the live drift rule", null, false, picks, day);
        var settings = new AccountSettings(100_000m, 1m, 0.2m);

        // The risk would buy 2,000 shares; a tenth of $100,000 at 100 is 100. Ten positions spend the account and the
        // eleventh is skipped under the cash.
        var view = Assert.Single(EquityBrief.Api.Reading.PaperScreen.Views([head], [], [], settings, [day], day, (_, _) => 100m, _ => null));

        Assert.Equal((10, 1), (view.Walk!.Taken, view.Walk.Skipped));
        Assert.All(view.Walk.Trades.Where(one => one.Skipped is null), one => Assert.Equal(100, one.Shares));
        Assert.Equal(PaperAccount.UnderTheCash, view.Walk.Trades[^1].Skipped);

        // The account's card says so, and its line draws a tenth beside the cap a pick's card reads.
        var page = PaperPageOf(new PaperPageView([view], [], [], [], new PaperNotBought([]), [], null));

        Assert.Contains("<p class=\"card-summary\" data-summary>Each setup trades its own copy of your account of $100,000. <span data-as-worded>Each position risks 1% and is capped at a tenth of the account.</span></p>", page, StringComparison.Ordinal);
        Assert.Empty(PlainWords.Faults(page, "account", ["risks"]));
        Assert.Contains("<span>Most in one stock <b>10%</b></span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("with at most 20% in one stock", page, StringComparison.Ordinal);
    }

    // On 2026-11-06 the S&P 500's combined account's table drew 26 rows reading "skipped: the free cash was under its cost"
    // among its trades, and an account's trade log on Past picks drew its skipped picks as rows; What the system bought's
    // line read "Not bought: 29 listed stocks. The free cash was under its cost (16): ...".
    // see: A skipped pick never sits in a trade table, and each account's trades end on one line naming what it did not buy by reason
    [Fact]
    public void ASkippedPickNeverSitsInATradeTableAndEachAccountsTradesEndOnOneLineByReason()
    {
        var day = new DateOnly(2026, 11, 6);
        var marks = new MarkRenderer();
        var walk = new PaperWalk([(day, 100_000m)], [], 1, 2, 0, 0.0, 0.0, null, null, day, [(day, 0.0)]);
        var averaged = new PaperCombinedView(walk, day, 1, 0, 2,
        [
            new("drift", "GWW", day, 1_441.79m, 6, null, null, -0.001, true),
            new("drift", "EVRG", day, 86.32m, 0, PaperAccount.UnderTheCash, null, null, true),
            new("improving", "ODFL", day, 167.55m, 0, PaperAccount.NotSized, null, null, true),
        ]);
        var card = WebUtility.HtmlDecode(marks.PaperCombined(averaged));

        // The averaged account's table holds GWW alone, and the line beneath names EVRG and ODFL by why.
        Assert.Equal(["GWW"], Regex.Matches(card, "<tr data-family=\"[a-z]+\" data-ticker=\"([A-Z]+)\"").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("skipped:", card, StringComparison.Ordinal);
        Assert.Matches("<p class=\"paper-unbought\" data-unbought=\"2\">Not bought: <span[^>]*>1 pick, the free cash was under its cost: <a[^>]*>EVRG</a></span>; <span[^>]*>1 pick, the plan sizes no shares: <a[^>]*>ODFL</a></span>\\.</p>", card);

        // An account's trade log the same: its bought trades as rows, its skips on the line beneath.
        var account = new PaperAccountView("list-drift", "drift", PaperAccountView.Live, "Live", "the live drift rule", "within luck", null, "risks", [], walk, 2, 0, null, day);
        var log = WebUtility.HtmlDecode(marks.PaperLog(account, [new PaperTradeView("GWW", day, 1_441.79m, 1_400m, null, -0.02, true, null), new PaperTradeView("EVRG", day, 86.32m, 84m, null, null, true, PaperAccount.UnderTheCash)]));

        Assert.Equal(["GWW"], Regex.Matches(log, "<tr data-ticker=\"([A-Z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains(">1 pick, the free cash was under its cost: <a href=\"#/chart/EVRG\" title=\"listed 2026-11-06\">EVRG</a></span>.</p>", log, StringComparison.Ordinal);

        // What the system bought's line reads each reason as the count of picks and the reason.
        var page = PaperPageOf(new PaperPageView([], [BlockTrade()], [], [], new PaperNotBought([new PaperUnbought(PaperAccount.UnderTheCash, [("APP", day), ("EVRG", day)])]), [], null));

        Assert.Matches("Not bought: <span class=\"pu-reason\" data-count=\"2\">2 picks, the free cash was under its cost: ", page);
        Assert.DoesNotContain("listed stocks.", page, StringComparison.Ordinal);
    }

    // On 2026-11-06 the combined account's words said "each set of holdings a fifth of the account" while FTNT held $897, LYB
    // $863 and the heavyweights about $800 each: a fifth of the account shared across the 22 a book can hold, and no dollars
    // drawn. Each holding is now sized in its own setup's account across the most holdings that setup can hold, the words say
    // so, and each position's dollars stand beside it.
    // see: A holding with no stop is sized at an equal share of the account across the most holdings its book can hold
    [Fact]
    public void TheMonthlySetupsSizingIsStatedAsTheCodeWorksItAndEachPositionsDollarsAreDrawn()
    {
        var day = new DateOnly(2026, 11, 3);
        var walk = new PaperWalk([(day, 100_000m)], [], 2, 0, 0, 0.0, 0.0, null, null, day, [(day, 0.0)]);
        var averaged = new PaperCombinedView(walk, day, 2, 0, 2,
        [
            new("heavyweight", "ABNB", day, 170.94m, 26, null, null, 0.11, true, 22),
            new("improving", "FTNT", day, 128.20m, 35, null, null, -0.07, true, 22),
            new("drift", "IDXX", day, 662.26m, 15, null, null, 0.02, true),
        ]) { Size = 100_000m };
        var card = WebUtility.HtmlDecode(new MarkRenderer().PaperCombined(averaged));

        // $100,000 over 22 is $4,545 a holding, under the tenth; ABNB's 26 shares at 170.94 are $4,444 and FTNT's 35 at 128.20
        // $4,487.
        Assert.Contains("Each holding of a monthly setup is sized in its own setup's account at an equal share of that account across the most holdings the setup can hold, never more than a tenth of it: Fundamentals across 22, about $4,545 a holding on $100,000; Sector heavyweights across 22, about $4,545 a holding on $100,000.", card, StringComparison.Ordinal);
        Assert.Contains("<th class=\"r\">Dollars</th>", card, StringComparison.Ordinal);
        Assert.Contains("data-dollars=\"4444.44\">$4,444</td>", card, StringComparison.Ordinal);
        Assert.Contains("data-dollars=\"4487.00\">$4,487</td>", card, StringComparison.Ordinal);
        Assert.Contains("data-dollars=\"9933.90\">$9,934</td>", card, StringComparison.Ordinal);
        Assert.DoesNotContain("a fifth of the account", card, StringComparison.Ordinal);
        Assert.All(PlainWords.Terms(), term => Assert.DoesNotMatch(PlainWords.Pattern(term), PlainWords.Visible(card)));
    }

    // On 2026-11-06 the S&P 500's Home read "Our paper account counts the rules as they stand now from 2 Oct, the day their
    // first trade counts: its earlier trades replay the same under this version." with "Our paper account, as the rules stand
    // now, since 2 Oct". It reads the live setups' accounts averaged since the day every live rule had its current version,
    // 3 Nov, with the index's fund over the same days, and the system's every version apart.
    // see: Home's paper account counts since today's rules all took effect, and the system's every version stands apart and labelled
    [Fact]
    public void HomesAccountCountsSinceTodaysRulesAllTookEffectAndTheSystemStandsApart()
    {
        var past = new HomePast(new DateOnly(2025, 9, 25), 51, 21, -0.012, -0.020, true, "SPY", new DateOnly(2026, 11, 3));
        var home = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Past = past }, string.Empty));

        Assert.Contains("data-account-since=\"2026-11-03\">Since today's rules all took effect on 3 Nov, our paper account has followed every one of them with an equal share.</p>", home, StringComparison.Ordinal);
        Assert.Contains("<span>Our paper account since 3 Nov</span><b", home, StringComparison.Ordinal);
        Assert.Contains("<span>The index's fund, SPY, over the same days</span>", home, StringComparison.Ordinal);
        Assert.Contains("<p class=\"home-system\" data-system-closed=\"51\" data-made-money=\"21\">All versions since 25 Sep: 51 ideas bought and sold on paper, 21 made money.</p>", home, StringComparison.Ordinal);
        Assert.DoesNotContain("2 Oct", home, StringComparison.Ordinal);
        Assert.DoesNotContain("replay the same", home, StringComparison.Ordinal);

        // Paper trading hands Home the averaged account, whose day is the latest of the live accounts' starts.
        var settings = new AccountSettings(100_000m, 1m, 0.2m);
        PaperAccountView View(string family, DateOnly since) =>
            new(family, family, PaperAccountView.Live, "Live", family, "within luck", null, "risks", [], PaperAccount.Walk(settings, [], [since], (_, _) => null), 0, 0, null, since);

        var averaged = EquityBrief.Api.Reading.PaperScreen.Averaged([View("quality", new DateOnly(2026, 11, 2)), View("breakout", new DateOnly(2026, 11, 3)), View("heavyweight", new DateOnly(2026, 10, 21))], settings, [new DateOnly(2026, 10, 21), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)], new DateOnly(2026, 11, 3), (_, _) => null, _ => null);

        Assert.Equal(new DateOnly(2026, 11, 3), averaged!.Since);
    }

    // On 2026-11-06 the S&P 500's tiles drew one fund tile, "The index's fund, SPY -4.5% holding it over the same days, since
    // 2026-10-02", beside the breakout from 3 Nov and Quality at a fair price from 2 Nov. Each tile draws the fund over its own
    // account's days, worked by the walk over those days, and no tile of the fund since the earliest start stands beside them.
    // see: Each tile draws the index's fund over its own account's days, and no tile draws the fund over days its account did not count
    [Fact]
    public void EachTileDrawsTheIndexsFundOverItsOwnAccountsDays()
    {
        var settings = new AccountSettings(100_000m, 1m, 0.2m);
        var closes = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 2)] = 500m, [new DateOnly(2026, 11, 3)] = 490m, [new DateOnly(2026, 11, 6)] = 480.2m };
        decimal? Fund(DateOnly session) => closes.TryGetValue(session, out var close) ? close : null;

        PaperAccountView View(string family, DateOnly[] sessions) =>
            new(family, family, PaperAccountView.Live, "Live", family, "within luck", null, "risks", [], PaperAccount.Walk(settings, [], sessions, (_, _) => null, Fund), 0, 0, null, sessions[0]);

        // The breakout from 3 Nov: the fund from 490 to 480.20, down 2.0%; Quality from 2 Nov: from 500, down 3.96%, 4.0%.
        var breakout = View("breakout", [new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 6)]);
        var quality = View("quality", [new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 6)]);
        var tiles = WebUtility.HtmlDecode(new MarkRenderer().PaperTiles([breakout, quality], new PaperFundView("SPY", -0.0396, new DateOnly(2026, 11, 2), null)));

        Assert.Equal(-0.02, breakout.Walk!.FundReturn!.Value, 12);
        Assert.Matches("<div class=\"paper-tile\" data-family=\"breakout\"[^>]*>.*?<div class=\"pt-fund\" data-fund=\"SPY\" data-fund-return=\"-0.02\">SPY over the same days <span[^>]*>-2.0%</span></div></div>", tiles);
        Assert.Matches("<div class=\"paper-tile\" data-family=\"quality\"[^>]*>.*?<div class=\"pt-fund\" data-fund=\"SPY\" data-fund-return=\"-0.0396\">SPY over the same days <span[^>]*>-4.0%</span></div></div>", tiles);
        Assert.DoesNotContain("paper-tile fund", tiles, StringComparison.Ordinal);
        Assert.DoesNotContain("holding it over the same days, since", tiles, StringComparison.Ordinal);
    }

    // On 2026-11-06 the line beneath what the system bought read "Not bought: 29 listed stocks. The free cash was under its
    // cost (16): APP, EVRG, EW, GWW, ..." with no account named, every account's merged. Each account's line names the
    // account, and a stock the account holds open is never drawn as not bought, here DD, which the drift's account holds and
    // listed again.
    // see: Each table and line of what the system bought names the account it walks, and draws no stock that account holds as not bought
    [Fact]
    public void EachLineBeneathWhatTheSystemBoughtNamesItsAccountAndNoneItHolds()
    {
        var night = new DateOnly(2026, 11, 6);
        PaperTrade Trade(string ticker, DateOnly bought) => new(ticker, bought, 100m, 95m, null, null, 0m, 1, Listed: bought.AddDays(-1), Target: 110m, Cap: 60);

        var dd = Trade("DD", new DateOnly(2026, 11, 4));
        var gww = Trade("GWW", night);
        var drift = new EquityBrief.Api.Reading.PaperAccountSource("list-drift", "drift", PaperAccountView.Live, "Live", "the live drift rule", null, false, [(dd, null), (gww, null)], new DateOnly(2026, 11, 3), Unfilled: [("DD", night, PaperAccount.Waiting), ("XYZ", night, PaperAccount.Waiting)]);
        var walk = new PaperWalk([(night, 100_000m)], [new PaperTaken(dd, 10, null), new PaperTaken(gww, 0, PaperAccount.UnderTheCash)], 1, 1, 0, 0.0, 0.0, null, null, night, [(night, 0.0)]);
        var view = new PaperAccountView("list-drift", "drift", PaperAccountView.Live, "Live", "the live drift rule", "within luck", null, "risks", [], walk, 2, 0, null, new DateOnly(2026, 11, 3));
        var built = EquityBrief.Api.Reading.PaperScreen.Page([drift], [view], night, [night], (_, _) => 100m, new Dictionary<string, EquityBrief.Api.Reading.PaperStockRow>(StringComparer.Ordinal), _ => null, null, "drift", "SPY", _ => null);
        var page = PaperPageOf(built);
        var line = Regex.Match(page, "<p class=\"paper-unbought\"[^>]*data-family=\"drift\">.*?</p>").Value;

        Assert.Contains("<p class=\"paper-walks\" data-walks=\"each\">Each row is a trade of its setup's own account, which the Setup column names.</p>", page, StringComparison.Ordinal);
        Assert.StartsWith("<p class=\"paper-unbought\" data-unbought=\"1\" data-family=\"drift\"><b>Earnings drift's own account.</b> Not bought: ", line, StringComparison.Ordinal);
        Assert.Contains(">GWW</a>", line, StringComparison.Ordinal);
        Assert.DoesNotContain(">DD</a>", line, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"DD\"", page, StringComparison.Ordinal);
    }

    // On 2026-11-06 ABNB drew as held, by the sector heavyweights' account since 3 Nov, and in the line beneath what the
    // system bought as "Waits on the next trading day's open", the drift's account having listed it on 6 Nov: a listing
    // waiting on its next open was drawn among the stocks not bought, every account's merged. It is to buy in the drift's
    // own account, named so, with the heavyweights' account holding it, and a stock an account holds is never drawn as
    // waiting in that account.
    // see: A listing waiting on its next session's open is drawn as to buy in its own account and never as not bought, nor where that account holds it
    [Fact]
    public void AListingWaitingOnTheNextOpenIsToBuyInItsOwnAccountAndNeverWaitingWhereItIsHeld()
    {
        var night = new DateOnly(2026, 11, 6);
        var held = new DateOnly(2026, 11, 3);
        PaperTrade Holding(string ticker) => new(ticker, held, 170.94m, null, null, null, 0m, 1, 22, Listed: new DateOnly(2026, 11, 2));
        PaperWalk Walked(params PaperTaken[] trades) => new([(night, 100_000m)], trades, trades.Length, 0, 0, 0.0, 0.0, null, null, night, [(night, 0.0)]);

        var heavyweights = new EquityBrief.Api.Reading.PaperAccountSource("list-heavyweight", "heavyweight", PaperAccountView.Live, "Live", "the live heavyweights rule", null, true, [(Holding("ABNB"), null)], new DateOnly(2026, 10, 21), Unfilled: [("ABNB", night, PaperAccount.Waiting)]);
        var drift = new EquityBrief.Api.Reading.PaperAccountSource("list-drift", "drift", PaperAccountView.Live, "Live", "the live drift rule", null, false, [], new DateOnly(2026, 11, 3), Unfilled: [("ABNB", night, PaperAccount.Waiting), ("CPAY", night, PaperAccount.Waiting)]);
        PaperAccountView View(EquityBrief.Api.Reading.PaperAccountSource source, PaperWalk walk) =>
            new(source.Key, source.Family, PaperAccountView.Live, "Live", source.Words, "within luck", null, "risks", [], walk, source.Trades.Count, 0, null, source.Since);

        var built = EquityBrief.Api.Reading.PaperScreen.Page(
            [drift, heavyweights],
            [View(drift, Walked()), View(heavyweights, Walked(new PaperTaken(Holding("ABNB"), 26, null)))],
            night,
            [night],
            (_, _) => 190m,
            new Dictionary<string, EquityBrief.Api.Reading.PaperStockRow>(StringComparer.Ordinal),
            _ => null,
            null,
            "drift",
            "SPY",
            _ => null);
        var page = PaperPageOf(built);

        // The drift's account buys ABNB and CPAY at the next open, ABNB named as the heavyweights' account holds it; the
        // heavyweights' account, which holds ABNB, draws no waiting for it; and no line of stocks not bought draws a wait.
        Assert.Contains("<p class=\"paper-tobuy\" data-tobuy=\"2\" data-family=\"drift\"><b>Earnings drift's own account.</b> To buy at the next trading day's open: <a href=\"#/chart/ABNB\" title=\"listed 2026-11-06\">ABNB</a> (Sector heavyweights' own account already holds it), <a href=\"#/chart/CPAY\" title=\"listed 2026-11-06\">CPAY</a>.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-tobuy=\"1\" data-family=\"heavyweight\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("aits on the next", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"ABNB\" data-family=\"heavyweight\"", page, StringComparison.Ordinal);
    }

    // On 2026-11-06 FTNT, a Fundamentals holding, and MGM, Quality at a fair price's, drew "none" under Stop and Target and
    // "Not read tonight" under Where it stands. Each draws how its own rule sells it across the two cells, and whether it
    // still qualifies tonight by that rule.
    // see: A holding with no price stop draws how its rule sells it and whether it still qualifies tonight by its own rule
    [Fact]
    public void AHoldingWithNoPriceStopDrawsHowItsRuleSellsItAndWhetherItStillQualifiesTonight()
    {
        var night = new DateOnly(2026, 11, 6);
        var bought = new DateOnly(2026, 11, 3);
        PaperTrade Holding(string ticker) => new(ticker, bought, 100m, null, null, null, 0m, 1, 22, Listed: new DateOnly(2026, 11, 2));

        // The live S&P 500 Fundamentals rule holds at 7 in 9 for at most 126 sessions: FTNT at 8 of 9 still qualifies, a
        // holding at 6 of 9 does not; at 5 of 7 it is under 7 in every 9 too, 45 of 63.
        Assert.Equal("Sells when a filing drops it below 7 in every 9 of the checks that apply, or after 126 trading days", EquityBrief.Api.Reading.PaperScreen.FundamentalsSells(7, 126));
        Assert.Equal("Still qualifies: 8 of 9 apply, at least 7 in every 9", EquityBrief.Api.Reading.PaperScreen.FundamentalsTonight(8, 9, 7));
        Assert.Equal("No longer qualifies: 6 of 9 apply, under 7 in every 9", EquityBrief.Api.Reading.PaperScreen.FundamentalsTonight(6, 9, 7));
        Assert.Equal("No longer qualifies: 5 of 7 apply, under 7 in every 9", EquityBrief.Api.Reading.PaperScreen.FundamentalsTonight(5, 7, 7));
        Assert.Equal("Sold at the reshuffle if it no longer qualifies, next on 1 Dec", EquityBrief.Api.Reading.PaperScreen.QualitySells(new DateOnly(2026, 12, 1)));
        Assert.Equal("Sold at the reshuffle if it no longer leads its sector, next on 1 Dec", EquityBrief.Api.Reading.PaperScreen.HeavyweightSells(false, new DateOnly(2026, 12, 1)));

        EquityBrief.Api.Reading.PaperAccountSource Book(string family, string ticker) =>
            new("list-" + family, family, PaperAccountView.Live, "Live", "the live rule", null, true, [(Holding(ticker), null)], new DateOnly(2026, 11, 2));
        PaperAccountView View(EquityBrief.Api.Reading.PaperAccountSource source) =>
            new(source.Key, source.Family, PaperAccountView.Live, "Live", source.Words, "within luck", null, MarkRenderer.LoopPoints, [], new PaperWalk([(night, 100_000m)], [new PaperTaken(source.Trades[0].Trade, 45, null)], 1, 0, 0, 0.0, 0.0, null, null, night, [(night, 0.0)]), 1, 0, null, source.Since);

        var improving = Book("improving", "FTNT");
        var quality = Book("quality", "MGM");
        var books = new Dictionary<string, EquityBrief.Api.Reading.PaperBookLine>(StringComparer.Ordinal)
        {
            ["improving"] = new(EquityBrief.Api.Reading.PaperScreen.FundamentalsSells(7, 126), new Dictionary<string, string>(StringComparer.Ordinal) { ["FTNT"] = EquityBrief.Api.Reading.PaperScreen.FundamentalsTonight(8, 9, 7) }),
            ["quality"] = new(EquityBrief.Api.Reading.PaperScreen.QualitySells(new DateOnly(2026, 12, 1)), new Dictionary<string, string>(StringComparer.Ordinal) { ["MGM"] = "Still qualifies." }),
        };
        var built = EquityBrief.Api.Reading.PaperScreen.Page([improving, quality], [View(improving), View(quality)], night, [night], (_, _) => 110m, new Dictionary<string, EquityBrief.Api.Reading.PaperStockRow>(StringComparer.Ordinal), _ => null, null, "improving", "SPY", _ => null, null, books);
        var page = PaperPageOf(built);

        Assert.Contains("<td class=\"pb-sells\" colspan=\"2\" data-sells>Sells when a filing drops it below 7 in every 9 of the checks that apply, or after 126 trading days</td>", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"pb-sells\" colspan=\"2\" data-sells>Sold at the reshuffle if it no longer qualifies, next on 1 Dec</td>", page, StringComparison.Ordinal);
        Assert.Contains(">Still qualifies: 8 of 9 apply, at least 7 in every 9</span>", page, StringComparison.Ordinal);
        Assert.Contains(">Still qualifies.</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Not read tonight", page, StringComparison.Ordinal);
    }

    // On 2026-11-06 What the versions did differently drew each row with no start, the live rule's day only under the
    // pointer. The live rule's row says its record since its account's day, and each version's that it trades since its own.
    // see: Each row of what the versions did differently gives its start, the live rule's record since and each version's trading since
    [Fact]
    public void EachRowOfWhatTheVersionsDidDifferentlyGivesItsStart()
    {
        PaperAccountView Row(string key, string standing, DateOnly since) =>
            new(key, "pullback", standing, standing == PaperAccountView.Live ? "Live" : "V1", "words", "within luck", null, "risks", [], null, 2, 0, null, since, Short: standing == PaperAccountView.Live ? "Live rule" : "V1", Overlap: new PaperOverlap(2, 1, 1));

        var page = new PaperPageView([Row("list-pullback", PaperAccountView.Live, new DateOnly(2026, 11, 3)), Row("v1", PaperAccountView.Variant, new DateOnly(2026, 11, 2))], [], [], [], new PaperNotBought([]), [], null);
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().PaperVariants(page, new PaperLink("500"), "pullback"));

        Assert.Contains("<span class=\"pv-since\" data-since=\"2026-11-03\">record since 3 Nov</span>", AccountRowOf(drawn, "list-pullback"), StringComparison.Ordinal);
        Assert.Contains("<span class=\"pv-since\" data-since=\"2026-11-02\">trading since 2 Nov</span>", AccountRowOf(drawn, "v1"), StringComparison.Ordinal);
    }

    // On 2026-11-06 the S&P 500's pullback drew a table of nine rows of zeros, its rule and eight versions, none marked by how
    // it reaches against the rule. Each version is marked from the direction of each setting it changes, worked here over
    // the S&P 500 swing filter's eight as the register holds them, and a family with no trade at all says why in one line.
    // see: A version is marked as narrowing or widening its live rule by the direction of each setting it changes
    [Fact]
    public void EachVersionIsMarkedByItsReachAndAFamilyWithNoTradeSaysWhyInOneLine()
    {
        IReadOnlyDictionary<string, double> live = new Dictionary<string, double>(StringComparer.Ordinal) { ["bestOf"] = 0, ["depthHigh"] = 5, ["arrivalSessions"] = 3, ["strengthFloor"] = 0.5, ["marketGate"] = 1, ["trade"] = 2 };

        IReadOnlyDictionary<string, double> With(string setting, double value) => new Dictionary<string, double>(live, StringComparer.Ordinal) { [setting] = value };

        // The best 3 a night, 1 to 3 moves down, turning up within 1, stronger than 66.7% and a business not deteriorating each
        // only remove trades; the market check off and the plan at the nearest bands can take trades the rule cannot.
        Assert.Equal(VersionReachKind.Narrows, VersionReach.Of("pullback", live, With("bestOf", 3)));
        Assert.Equal(VersionReachKind.Narrows, VersionReach.Of("pullback", live, With("depthHigh", 3)));
        Assert.Equal(VersionReachKind.Narrows, VersionReach.Of("pullback", live, With("arrivalSessions", 1)));
        Assert.Equal(VersionReachKind.Narrows, VersionReach.Of("pullback", live, With("strengthFloor", 2.0 / 3)));
        Assert.Equal(VersionReachKind.Narrows, VersionReach.Of("pullback", live, With("skipDeteriorating", 1)));
        Assert.Equal(VersionReachKind.Widens, VersionReach.Of("pullback", live, With("marketGate", 0)));
        Assert.Equal(VersionReachKind.Widens, VersionReach.Of("pullback", live, With("trade", 1)));

        // A target alone sells the same picks differently, an order alone takes them in another order, the dip's trend between
        // its two averages widens, a larger book widens, a setting the table does not know is unread, and a change that
        // narrows one setting and widens another widens.
        Assert.Equal(VersionReachKind.SoldDifferently, VersionReach.Of("drift", new Dictionary<string, double> { ["targetRiskMultiple"] = 2.5 }, new Dictionary<string, double> { ["targetRiskMultiple"] = 3 }));
        Assert.Equal(VersionReachKind.Reordered, VersionReach.Of("breakout", new Dictionary<string, double>(), new Dictionary<string, double> { ["order_expected"] = 1 }));
        Assert.Equal(VersionReachKind.Widens, VersionReach.Of("dip", new Dictionary<string, double> { ["trend"] = 50 }, new Dictionary<string, double> { ["trend"] = 200 }));
        Assert.Equal(VersionReachKind.Widens, VersionReach.Of("quality", new Dictionary<string, double> { ["book"] = 10 }, new Dictionary<string, double> { ["book"] = 20 }));
        Assert.Equal(VersionReachKind.Unread, VersionReach.Of("pullback", live, With("somethingNew", 1)));
        Assert.Equal(VersionReachKind.Widens, VersionReach.Of("pullback", live, new Dictionary<string, double>(live) { ["depthHigh"] = 3, ["marketGate"] = 0 }));

        PaperAccountView Row(string key, string standing, VersionReachKind? reach, int trades) =>
            new(key, "pullback", standing, standing == PaperAccountView.Live ? "Live" : key, "words", "within luck", null, "risks", [], null, trades, 0, null, new DateOnly(2026, 11, 3), Short: key, Overlap: new PaperOverlap(trades, 0, trades)) { Reach = reach };

        string Part(params PaperAccountView[] rows) =>
            WebUtility.HtmlDecode(new MarkRenderer().PaperVariants(new PaperPageView(rows, [], [], [], new PaperNotBought([]), [], null), new PaperLink("500"), "pullback"));

        // No trade at all, every version narrowing: one line saying none could trade, and no table.
        var narrowing = Part(Row("list-pullback", PaperAccountView.Live, null, 0), Row("V1", PaperAccountView.Variant, VersionReachKind.Narrows, 0), Row("V2", PaperAccountView.Variant, VersionReachKind.Narrows, 0));

        Assert.Contains(">Pullback has made no trade since its record began on 3 Nov, and each of its versions narrows it, so none could trade either.</p>", narrowing, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class=\"paper-variants\"", narrowing, StringComparison.Ordinal);

        // One widening version: the line counts them.
        Assert.Contains(">Pullback has made no trade since its record began on 3 Nov, and none of its 2 versions has traded either: 1 narrows it and 1 widens it.</p>", Part(Row("list-pullback", PaperAccountView.Live, null, 0), Row("V1", PaperAccountView.Variant, VersionReachKind.Narrows, 0), Row("V8", PaperAccountView.Variant, VersionReachKind.Widens, 0)), StringComparison.Ordinal);

        // A family with a trade draws its table, each version marked.
        var traded = Part(Row("list-pullback", PaperAccountView.Live, null, 2), Row("V1", PaperAccountView.Variant, VersionReachKind.Narrows, 1), Row("V8", PaperAccountView.Variant, VersionReachKind.Widens, 3));

        Assert.Contains("<span class=\"pv-reach\" data-reach=\"narrows\">narrows the live rule</span>", AccountRowOf(traded, "V1"), StringComparison.Ordinal);
        Assert.Contains("<span class=\"pv-reach\" data-reach=\"widens\">widens it</span>", AccountRowOf(traded, "V8"), StringComparison.Ordinal);
        Assert.DoesNotContain("paper-no-trade", traded, StringComparison.Ordinal);
    }
}
