using System.Net;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.10: a family's versions chosen by their recent record on the Loop page, a row a procedure with what it
// does in the reader's words, the version it would run now, its trades, its adjusted p and whether it beat staying put,
// and that an approval waits on the operator's word; nothing for a family the per-rule test walked none for.
// see: A family's version is chosen each month by its recent record as a procedure judged against staying with the live rule, and an approval of it waits on the operator's word
public partial class ReadSurface
{
    // The row of the Loop page's table this check reaches from 21.10.
    internal static readonly string[] VersionChoicePageClaims =
    [
        CheckReach.Key(Scope.LoopPage, Scope.LoopChoices),
    ];

    [Fact]
    public void TheLoopPageDrawsAFamilysVersionsChosenByTheirRecordAndNoPressForThem()
    {
        LoopChoiceView[] choices =
        [
            new("drift", "choosing a version by its record over the 126 sessions before", "each month the drift rule standing here whose mean edge after costs over the 126 sessions before stands highest over at least 50 ended trades, the live rule where none does; it would run 'the S&P 400 drift rule, graded' now, and switched 3 time(s) over the 58 month(s) of the test years", 1204, 0.412, false, null),
            new("drift", "choosing a version by its record over the 504 sessions before", "the same over 504 sessions", 1188, 0.031, true, null),
        ];
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().LoopChoices("drift", choices));

        Assert.Contains("<h5>Choosing a version by its recent record</h5>", drawn, StringComparison.Ordinal);
        Assert.Contains("data-choices=\"2\"", drawn, StringComparison.Ordinal);
        Assert.Contains("it would run 'the S&P 400 drift rule, graded' now, and switched 3 time(s) over the 58 month(s) of the test years", drawn, StringComparison.Ordinal);
        Assert.Contains("over the 126 trading days before", drawn, StringComparison.Ordinal);
        Assert.Contains(">1204</td><td class=\"r num\">0.412</td><td>no</td>", drawn, StringComparison.Ordinal);
        Assert.Contains(">1188</td><td class=\"r num\">0.031</td><td>yes</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("waits on your word; there is no press for it here", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", drawn, StringComparison.Ordinal);
        Assert.Equal(string.Empty, new MarkRenderer().LoopChoices("breakout", choices));
    }
}
