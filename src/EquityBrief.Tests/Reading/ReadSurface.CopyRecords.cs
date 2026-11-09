using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 22.17 correction of 2026-11-09: on the operator's word the read-only copy's Home, Tonight and stock pages draw each pick's
// record, each tile's line, the picks watched, a zone plan's words and the Fundamentals family's line with its setting exactly
// as the app does, and the copy's check for the method passes them while it still refuses a route to the Loop or the Ledger, a
// version's label, a settings line and the phrases a threshold is stated in.
// see: The read-only copy draws each pick's record and the picks watched as the app does, and its check still refuses the method
public partial class ReadSurface
{
    internal static string[] CopyRecordRows => [CheckReach.Key("15.24 Home", "Records and watched picks on the read-only copy")];

    const string CopyLive = "the live breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath";

    const string CopyIndex = "the live S&P 400 pullback rule at a strength of 0.75, 1 to 3 typical moves deep, the profit gate";

    [Fact]
    public void TheCopyDrawsEachRecordAndThePicksWatchedAsTheAppDoesAndItsCheckStillRefusesTheMethod()
    {
        var lost = new SetupLongRecord("GSPC", SetupFamilies.Pullback, -0.124, 2933, 2019, 2026);
        var fundamentals = new HomeIdea(ImprovingRule.Name, "CMI", null, "Bought.", 400m, null, null, false, null, LongRecord: SetupLongRecord.Filings("GSPC", ImprovingRule.Name));
        var watched = new HomeIdea(SetupFamilies.Pullback, "BMY", null, "A dip.", 50m, 47m, 58m, false, null, LongRecord: lost);
        var zoned = new HomeIdea(SetupFamilies.Pullback, "CTSH", null, "A dip.", 49.2m, 47.5m, 58m, false, null, Zone: new ZoneBuy(49.2m, 20, 51.3m));
        var view = Day() with { Ideas = [fundamentals, zoned], IdeasCount = 2, Watching = [watched] };

        // Home: the copy's cards and its line of the picks watched read word for word as the app's, the Fundamentals family's
        // line with its setting among them.
        string Ideas(bool copying)
        {
            var page = new SinglePageApp().HomeRegion(new MarkRenderer { Copying = copying }, view, string.Empty);

            return string.Concat(Regex.Matches(page, "<article class=\"home-idea.*?</article>|<p class=\"home-watch\".*?</p>", RegexOptions.Singleline).Select(match => Read(match.Value)));
        }

        Assert.Equal(Ideas(copying: false), Ideas(copying: true));
        Assert.Contains("it sells when a new filing drops its score below 7 in every 9 of the checks that apply, or after 126 trading days.", Ideas(copying: true), StringComparison.Ordinal);
        Assert.Contains("Watching, not buying: 1 pick tonight.", Ideas(copying: true), StringComparison.Ordinal);

        // A pick's card on the copy and a setup's tile draw the record as the app's do.
        var row = new DecisionCardRow("GSPC", EntryNight, SetupFamilies.Pullback, "BMY", 1, 50m, 47m, 58m, "the rule", "{}", "[]", null, Cap: 20);
        var card = new MarkRenderer { Copying = true }.DecisionCard(CardScreen.View(row, CardContext.None with { LongRecords = new SetupRecords([lost]) }));
        var tile = new MarkRenderer { Copying = true }.SixSetups([new SetupTileView(SetupFamilies.Pullback, "Pullback", 1, "pick", "1 pick") { LongRecord = lost }], new Dictionary<string, DateOnly?>(), new Dictionary<string, string>());

        Assert.Contains("over 2,933 picks.", Read(card), StringComparison.Ordinal);
        Assert.Contains("with its plan as it stands: lost $0.12 per $1 risked", Read(tile), StringComparison.Ordinal);

        // The copy's check reads all of it and finds nothing; each kind of leak planted beside it is still found.
        var needles = SiteCopy.MethodNeedles([CopyLive, CopyIndex, "the live swing filter, version 5"], [CopyLive, CopyIndex]);
        var drawn = new SinglePageApp().HomeRegion(new MarkRenderer { Copying = true }, view, string.Empty) + card + tile;

        Assert.Empty(SiteCopy.Leaks("screens/home.html", drawn, needles));

        foreach (var (planted, leak) in new[]
        {
            ("<td>breadth 61.6% at or above its floor of 45%</td>", "a threshold's words: \"at or above\""),
            ("<option>Version: 1 to 5 moves down</option>", "a version's label: \"Version: \""),
            ("<p>The setup's settings: the pullback's base</p>", "a settings line: \"settings: \""),
            ("<a href=\"#/loop?universe=400\">the test tables</a>", "a Loop route: \"#/loop\""),
        })
        {
            Assert.Contains($"screens/home.html carries {leak}", SiteCopy.Leaks("screens/home.html", drawn + planted, needles));
        }
    }
}
