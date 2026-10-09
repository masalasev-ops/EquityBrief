using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 16.4: phase 16's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, every claim its counts held that landed as no row named with where it went, and the
// card's figure's key naming every part the figure draws, in the architecture and in the guide.
public partial class ArchitectureConformance
{
    // The rows of phase 16 each checkpoint landed, in the plan's order: 16.1's card rows, 16.2's taken trades' rows,
    // 16.3's follower's rows, and none at 16.4, whose figure is a picture of claims placed elsewhere.
    static (string Checkpoint, string[] Rows)[] PhaseSixteenLanded =>
    [
        ("16.1", CardRows),
        ("16.2", TakenRows),
        ("16.3", FollowerRows),
        ("16.4", []),
    ];

    // How many of each checkpoint's count landed as no row of its own, and where they went. At 16.1, of about sixteen:
    // the record's command as words in the rule recorder's catalogue row, and the fifth threshold, the concentration's,
    // landing at 16.2 with the line it sets. At 16.2, of about ten: the settings file and the three presses as section
    // 15.18's paragraphs and words in the read API's catalogue row, six, less the concentration threshold counted at 16.1
    // that landed here. At 16.3, of about twelve: the market events table as section 15.18's paragraph with its source in
    // section 23, and the dividend ask as words in the calendar fetcher's catalogue row and section 14's step. At 16.4,
    // of about two: the card's figure and its key, a drawn figure placed as a picture of claims placed elsewhere.
    static readonly (string Checkpoint, int Fewer)[] PhaseSixteenShort =
    [
        ("16.1", 2),
        ("16.2", 5),
        ("16.3", 2),
        ("16.4", 2),
    ];

    // The rows a pass that lands no checkpoint added inside phase 16, which no checkpoint's count held: the 16.1
    // ruling's two failure rows on the local model's load and its settings.
    static string[] PhaseSixteenBeside => FixtureExpectations.LocalModelClaims;

    // The rows the document gains after phase 16's report, named beside the pair and never counted in it: the 14.6
    // correction's section 18 row on a heavyweights' rebalance waiting for the closes its readings need, the 13.10
    // correction's two checklist items on the store's copies and its two section 18 rows, and phase 17's rows as each
    // checkpoint lands them until the phase's own pair is checked at its report, 17.1's six first, 17.2's twenty-four after them, 17.3's kept bars, 17.3's ledger's nine, its filings refresh's nine and its Ledger page's four, and 17.4's tester's twelve and its Loop page's three.
    internal static string[] AfterPhaseSixteen => [.. FixtureExpectations.HeavyweightWaitClaims, .. FixtureExpectations.StoreCopyRowsClaims, .. FixtureExpectations.SearchClaims, .. FixtureExpectations.RuleCardsRows, CheckReach.Key(Scope.StoresTable, "Kept bars"), .. FixtureExpectations.LedgerRows, .. NightlyCost.FiledFactsRows, .. Reading.ReadSurface.LedgerPageRows, .. FixtureExpectations.LoopRows, .. Reading.ReadSurface.LoopPageRows];

    [Fact]
    public void ThePhaseSixteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS before it\. After 16\.4 the pair is ([\d,]+) and \2, within ([\d,]+) to ([\d,]+): none at 16\.0, [^;]*; about (\d+) at 16\.1, [^;]*; about (\d+) at 16\.2, [^;]*; about (\d+) at 16\.3, [^;]*; and about (\d+) at 16\.4,";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 16.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [Figure(5), Figure(6), Figure(7), Figure(8)];

        Assert.Equal(predicted, before + counted.Sum());

        // What 16.1 to 16.4 landed: each checkpoint's count in the plan less the claims it held that landed as no row.
        var landed = PhaseSixteenLanded;

        Assert.Equal(landed.Select(checkpoint => checkpoint.Checkpoint), PhaseSixteenShort.Select(checkpoint => checkpoint.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count - PhaseSixteenShort[at].Fewer),
            landed.Select(checkpoint => checkpoint.Rows.Length));

        var rows = landed.SelectMany(checkpoint => checkpoint.Rows).Concat(PhaseSixteenBeside).ToArray();
        var actual = before + rows.Length;

        // The actual falls inside the range the plan stated, short of the pair by the claims named as landing as no row
        // less the ruling's rows no count held.
        Assert.InRange(actual, low, high);
        Assert.Equal(predicted - actual, PhaseSixteenShort.Sum(checkpoint => checkpoint.Fewer) - PhaseSixteenBeside.Length);

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it and the rows after its report, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseSixteen.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1000, 1040, 1030, 1052, 31, 1031), (before, predicted, low, high, rows.Length, actual));
    }

    [Fact]
    public void TheCardsFigureKeyNamesEveryPartTheFigureDraws()
    {
        // The architecture's figure 15.1 and the key beneath it, and the guide's same picture and the list beneath it.
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.1.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, drawn.Count).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "A pick's card, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        // Every figure on the card drawn as a placeholder and never a night's: no digit in the figure's text but the
        // part's own number, the tick, the floor of 2, the 11 sectors, the twenty and the index's name.
        foreach (var figure in new[] { Figure(architecture, "Figure 15.1."), Figure(guide, "A pick's card, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>([^<]*)</text>").Select(text => text.Groups[1].Value)
                .Where(text => !Regex.IsMatch(text, @"^\d$"))
                .Select(text => text.Replace("&#10003;", string.Empty, StringComparison.Ordinal)
                    .Replace("S&amp;P 500", string.Empty, StringComparison.Ordinal)
                    .Replace("floor of 2", string.Empty, StringComparison.Ordinal)
                    .Replace("index's 11", string.Empty, StringComparison.Ordinal)
                    .Replace("of 20 ended", string.Empty, StringComparison.Ordinal));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }

    // A figure's parts as its groups number them, and the key's lines as they number theirs, each read off the markup
    // between the figure's caption and the key's end.
    static (IReadOnlyDictionary<string, string> Drawn, IReadOnlyDictionary<string, string> Keyed) Parts(string document, string caption, string keyOpens, string keyCloses)
    {
        var figure = Figure(document, caption);
        var after = document.IndexOf(figure, StringComparison.Ordinal) + figure.Length;
        var opens = document.IndexOf(keyOpens, after, StringComparison.Ordinal);

        Assert.True(opens >= 0, $"No key follows the figure captioned '{caption}'.");

        var key = document[opens..document.IndexOf(keyCloses, opens, StringComparison.Ordinal)];

        return (
            Regex.Matches(figure, "<g data-part=\"(\\d+)\">(.*?)</g>", RegexOptions.Singleline)
                .ToDictionary(part => part.Groups[1].Value, part => part.Groups[2].Value, StringComparer.Ordinal),
            Regex.Matches(key, "<li data-part=\"(\\d+)\">(.*?)</li>", RegexOptions.Singleline)
                .ToDictionary(part => part.Groups[1].Value, part => Regex.Replace(part.Groups[2].Value, "<[^>]+>", string.Empty).Trim(), StringComparer.Ordinal));
    }

    // The figure whose caption opens with the words given, from its opening to its close.
    static string Figure(string document, string caption)
    {
        var at = document.IndexOf("<figcaption>" + caption, StringComparison.Ordinal);

        Assert.True(at >= 0, $"No figure is captioned '{caption}'.");

        var opens = document.LastIndexOf("<figure", at, StringComparison.Ordinal);

        return document[opens..(document.IndexOf("</figure>", at, StringComparison.Ordinal) + "</figure>".Length)];
    }
}
