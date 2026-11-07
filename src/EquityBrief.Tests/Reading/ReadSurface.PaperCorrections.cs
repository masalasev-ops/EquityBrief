using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
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
}
