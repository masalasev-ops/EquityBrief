using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
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
}
