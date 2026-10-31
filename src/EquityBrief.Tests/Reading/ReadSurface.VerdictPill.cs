using EquityBrief.Core.Cards;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 21.1 correction found by 21.15's read-back: a rule's verdict on paper trading's scorecard, the words
// naming what judges a rule the history cannot walk among them, wraps inside the scorecard's header at a phone's width
// rather than running past the screen, where every other pill stays on one line.
public partial class ReadSurface
{
    [Fact]
    public void AScorecardsLongVerdictWrapsInsideItsHeaderAndOtherPillsStayOnOneLine()
    {
        const string Verdict = "not testable: settings the history's walk does not read: entry; judged by its record from its registration on paper trading and the Run page";

        var head = new PaperAccountView("live", "drift", PaperAccountView.Live, "the live rule", "the live S&P 400 drift rule", Verdict, null, "risks", [], null, 0, 0, null, new DateOnly(2026, 10, 20));
        var page = new PaperPageView([head], [], [], [], new PaperNotBought([]), [], null);
        var drawn = new MarkRenderer().PaperScorecard(page, new PaperLink("400"), "drift");
        var header = drawn[drawn.IndexOf("<div class=\"sh-h\">", StringComparison.Ordinal)..];

        Assert.Contains("<span class=\"pill verdict\" data-verdict=\"not testable: settings the history", header, StringComparison.Ordinal);
        Assert.Contains(".sh-h .pill.verdict{white-space:normal;max-width:100%", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".pill{display:inline-block;font:600 11.5px var(--sans);border:1px solid var(--hair-2);border-radius:999px;padding:1px 9px;color:var(--ink-2);white-space:nowrap}", Stylesheet.Css, StringComparison.Ordinal);
    }
}
