using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.14: the drift's tone variant read off the register as the rule whose registration names a reading
// version, and the operator's sentence drawn beneath its words in what the variants did differently and in the rule
// scorecard's variants, no other rule's row carrying it.
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
public partial class ReadSurface
{
    // The row of paper trading's table this check reaches from 21.14.
    internal static readonly string[] ToneLineClaims =
    [
        CheckReach.Key("15.21 Paper trading", Scope.PaperToneLine),
    ];

    [Fact]
    public void TheToneVariantsRowsSayItsHistoryBeforeTheCutoffDoesNotCountAndNoOtherRowDoes()
    {
        const string Sentence = "Its history before 2025 does not count: the model that read those releases was trained after them. Its record from today is what counts.";

        Assert.Equal(Sentence, PaperScreen.ToneNote);

        var registered = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        CandidateRow[] register =
        [
            new(1, "the live drift rule", "drift", "registered", null, registered, "{\"windowSessions\":3}"),
            new(2, "the drift rule, release better, outlook not cut", "drift", "registered", null, registered, "{\"windowSessions\":3,\"readingVersion\":3}"),
            new(3, "the drift rule, its stop held further", "drift", "registered", null, registered, "{\"windowSessions\":3,\"stopFloorMoves\":1}"),
        ];
        PaperAccountSource Source(string key, string standing, string words) =>
            new(key, "drift", standing, key, words, null, false, [], new DateOnly(2026, 10, 20));
        PaperAccountSource[] sources =
        [
            Source("live", PaperAccountView.Live, "the live drift rule"),
            Source("v1", PaperAccountView.Variant, "the drift rule, release better, outlook not cut"),
            Source("v2", PaperAccountView.Variant, "the drift rule, its stop held further"),
        ];

        var toned = PaperScreen.Toned(sources, register);

        Assert.Equal([false, true, false], toned.Select(source => source.ReadsReleases));

        var views = PaperScreen.Views(toned, [], [], null, [], new DateOnly(2026, 10, 30), (_, _) => null, _ => null);
        var page = new PaperPageView(views, [], [], [], new PaperNotBought([]), [], null);
        var marks = new MarkRenderer();
        var link = new PaperLink("GSPC");

        foreach (var drawn in new[] { marks.PaperVariants(page, link, "drift"), marks.PaperScorecard(page, link, "drift") })
        {
            var html = WebUtility.HtmlDecode(drawn);

            string Row(string key)
            {
                var from = html.IndexOf($"data-account=\"{key}\"", StringComparison.Ordinal);

                return from < 0 ? string.Empty : html[from..html.IndexOf("</tr>", from, StringComparison.Ordinal)];
            }

            Assert.Contains(Sentence, Row("v1"), StringComparison.Ordinal);
            Assert.DoesNotContain("data-note", Row("v2"), StringComparison.Ordinal);
            Assert.Equal(1, html.Split(Sentence).Length - 1);
        }
    }
}
