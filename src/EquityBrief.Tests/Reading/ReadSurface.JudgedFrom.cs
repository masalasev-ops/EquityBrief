using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 21.17 correction: a variant whose rule reads a release reading, the drift's tone variant and a
// Fundamentals rule asking a flag first, marked with the local model's cutoff's year its verdict is judged from, and the
// scorecard saying so beside its years better, read back off the rendered scorecard; a rule reading none carrying no such
// words, and the tone variant's sentence staying its own.
// see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
public partial class ReadSurface
{
    [Fact]
    public void AVariantReadingAReleaseReadingSaysOnTheScorecardItsVerdictReadsTheYearsFromTheCutoffAlone()
    {
        var registered = new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero);
        CandidateRow[] register =
        [
            new(1, "the live Fundamentals rule", "improving-400", "registered", null, registered, "{\"buy\":7,\"flag\":0,\"rise\":1,\"faster\":0,\"hold\":7,\"limit\":63}"),
            new(2, "the Fundamentals rule, a flag first", "improving-400", "registered", null, registered, "{\"buy\":7,\"flag\":1,\"rise\":1,\"faster\":0,\"hold\":7,\"limit\":63}"),
            new(3, "the Fundamentals rule, held at 6", "improving-400", "registered", null, registered, "{\"buy\":7,\"flag\":0,\"rise\":1,\"faster\":0,\"hold\":6,\"limit\":63}"),
        ];
        PaperAccountSource Source(string key, string standing, string words) =>
            new(key, "improving", standing, words, words, words, true, [], new DateOnly(2026, 11, 2));
        PaperAccountSource[] sources =
        [
            Source("live", PaperAccountView.Live, "the live Fundamentals rule"),
            Source("v1", PaperAccountView.Variant, "the Fundamentals rule, a flag first"),
            Source("v2", PaperAccountView.Variant, "the Fundamentals rule, held at 6"),
        ];

        // The flagged version marked with 2025, the cutoff of 2025-01-31's year, and not with the tone variant's sentence;
        // the two asking no flag unmarked; and with no cutoff stated none is marked with a year.
        var toned = PaperScreen.Toned(sources, register, new DateOnly(2025, 1, 31));

        Assert.Equal([null, 2025, null], toned.Select(source => source.JudgedFrom));
        Assert.All(toned, source => Assert.False(source.ReadsReleases));
        Assert.All(PaperScreen.Toned(sources, register), source => Assert.Null(source.JudgedFrom));

        RuleTestRow Test(string rule, string standing, int better, int counted) =>
            new("loop-rules-MID", "2026-11", "improving", rule, standing, rule, "the live Fundamentals rule", "points", 21, 7, 1, false, false, counted, better, -0.01, false, false, null);
        RuleTestRow[] tests =
        [
            Test("the live Fundamentals rule", PaperAccountView.Live, 2, 4),
            Test("the Fundamentals rule, a flag first", PaperAccountView.Variant, 0, 1),
            Test("the Fundamentals rule, held at 6", PaperAccountView.Variant, 1, 4),
        ];
        var views = PaperScreen.Views(toned, tests, [], null, [], new DateOnly(2026, 11, 2), (_, _) => null, _ => null);
        var page = new PaperPageView(views, [], [], [], new PaperNotBought([]), [], null);
        var html = WebUtility.HtmlDecode(new MarkRenderer().PaperScorecard(page, new PaperLink("MID"), "improving"));

        string Row(string key)
        {
            var from = html.IndexOf($"data-account=\"{key}\"", StringComparison.Ordinal);

            return from < 0 ? string.Empty : html[from..html.IndexOf("</tr>", from, StringComparison.Ordinal)];
        }

        Assert.Contains("0 of 1<span class=\"pv-note\" data-judged-from=\"2025\">from 2025 alone; the years before read by a model trained after them</span>", Row("v1"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-judged-from", Row("v2"), StringComparison.Ordinal);
        Assert.Contains("1 of 4", Row("v2"), StringComparison.Ordinal);
        Assert.DoesNotContain(PaperScreen.ToneNote, html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("data-judged-from").Length - 1);
    }
}
