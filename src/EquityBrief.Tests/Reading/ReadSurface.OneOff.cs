using EquityBrief.Api.Reading;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.News;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

public sealed partial class ReadSurface
{
    internal static string[] SplitNameRows => [CheckReach.Key("15.10 Run", "Names split for the first time")];

    internal static ReadApi ApiOf(TemporaryStore store) => Api(store);

    // SWK's own bullet, each figure read off the filed text: the $0.67 stands beside the phrase and the sentence ties it to earnings per share.
    const string RefundBullet = "EPS of $1.83; adjusted EPS* of $2.31; both included a benefit of approximately $0.67 from net tariff refunds 1";

    [Theory]
    [InlineData(RefundBullet, "0.67", true, 1)]
    [InlineData("Diluted earnings per share was $2.10, up 14 percent year over year, and included a favorable impact of $0.05 from tariff refunds.", "0.05", true, 1)]
    [InlineData("Included in net income per diluted share is an estimated $0.38 per share benefit from tariff refunds.", "0.38", true, 1)]
    [InlineData("The Company's third quarter results include $10 million of tariff refunds, which benefited adjusted EPS by $0.13.", "0.13", true, 1)]
    [InlineData("Results include a restructuring charge of $0.20 a share.", "0.20", false, -1)]
    [InlineData("A one-time item of $0.11 per share is in the quarter.", "0.11", false, null)]
    [InlineData("A one-time benefit of $0.11 per share is in the quarter.", "0.11", false, 1)]
    [InlineData("A one-time charge of $0.11 per share is in the quarter.", "0.11", false, -1)]
    [InlineData("A refund of $0.11 per share offset a charge in the quarter.", "0.11", false, 1)]
    [InlineData("An impairment of $0.11 per share offset a benefit in the quarter.", "0.11", false, -1)]
    [InlineData("Third-quarter reported diluted earnings per share were $0.54, including a non-cash impairment charge of ($0.62) per diluted share, or ($81) million pretax.", "0.62", true, -1)]
    [InlineData("Third quarter net loss includes approximately $82 million ($0.14 per share) in impairment charges.", "0.14", false, -1)]
    [InlineData("GAAP net income included a benefit of $22 million, net of tax, or $0.06 per diluted share, related to refunds for tariffs paid.", "0.06", true, 1)]
    public void AOneOffSentenceReadsItsAmountWhetherItIsTiedToEarningsAndWhichWayItMovedThem(string sentence, string amount, bool tied, int? direction)
    {
        var item = OneOffItems.Of(sentence);

        Assert.NotNull(item);
        Assert.Equal((decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), tied, direction), (item.Amount, item.TiedToEarnings, item.Direction));
    }

    [Theory]
    [InlineData("In the prior-year quarter, restructuring and impairment charges reduced net income by $0.03 per diluted share.")]
    [InlineData("Excluding such impairment, non-GAAP net income for the third quarter of 2026 was $424.7 million, or $9.60 per diluted share.")]
    [InlineData("Fiscal 2026 non-GAAP diluted net EPS estimates exclude $0.67 per diluted share, primarily related to restructuring and other charges.")]
    [InlineData("Net sales rose 7% and diluted earnings per share were $1.20.")]
    [InlineData("The company recorded a $56 million impairment related to its games portfolio.")]
    [InlineData("During the quarter the company paid a $0.20 per share cash dividend and took a restructuring charge.")]
    [InlineData("(in thousands, except per share amounts) Restructuring costs $ 4,276 $ 0.08 Legal settlement 1,900 0.04")]
    [InlineData("Recorded quarterly net income of $232 million, or $0.88 per common share, which includes $895 million of restructuring and related charges.")]
    [InlineData("Net loss of $52.1 million, or $2.42 per share, includes a non-cash goodwill impairment charge of $69.1 million.")]
    [InlineData("On a GAAP basis, the third quarter diluted loss per share was $0.26, including strategic expenses and restructuring costs.")]
    [InlineData("Per Diluted Share (3) $ 1.49 Adjustments: Restructuring initiatives 0.03")]
    [InlineData("Items of $1, $2, $3 and $4 with a one-time gain of $0.10 per share.")]
    [InlineData("A one-time item of $0.10 per share 0.20 0.30 0.40.")]
    [InlineData("A one-time gain on sale added $0.10 to results.")]
    [InlineData("A one-time gain on sale of $4.50 million lifted earnings per share.")]
    [InlineData("A restructuring lifted net income to $0.90 per share.")]
    [InlineData("A restructuring plan was $0.90 per share.")]
    [InlineData("Restructuring 1 0.04 Stock-based compensation 0.16 0.15 Income tax effect 2 (0.07) (0.03) Non-GAAP diluted EPS $ 0.72")]
    [InlineData("A restructuring of the sales force went on through the whole of a long and busy period of the year and into the next, and adjusted EPS rose by $0.11 in the end.")]
    [InlineData("The following are included in the $0.97 EPS for the first quarter and restructuring plans.")]
    [InlineData("For the twelve months ended December 31, EPS included approximately $0.37 per share of impairments.")]
    [InlineData("EPS $ 0.43 $ 0.32 $ 0.45 $ 0.88 Legal contingency - - 0.35 - Goodwill impairment - - 0.31 - Adjusted EPS $ 0.43 $ 0.32 $ 1.11 $ 0.88")]
    public void ASentenceThatIsNoOneOffItemOfTheQuarterIsNotRead(string sentence) =>
        Assert.Null(OneOffItems.Of(sentence));

    // A run longer than a sentence holds is a table the text flattened, and the largest item of a release is the one kept.
    [Fact]
    public void AReleaseKeepsItsLargestItemAndALongRunIsNoSentence()
    {
        var both = "Net income rose this quarter. A one-time gain on sale added $0.10 per share. It also took a restructuring charge of $0.30 per share.";

        Assert.Equal(0.30m, OneOffItems.Of(both)!.Amount);
        Assert.Equal(3, OneOffItems.Sentences(both).Count);

        var bulleted = "Sales grew • " + RefundBullet + " • Cash was strong";

        Assert.Equal(0.67m, OneOffItems.Of(bulleted)!.Amount);
        Assert.Equal(RefundBullet, OneOffItems.Of(bulleted)!.Sentence);
        Assert.Null(OneOffItems.Of("A one-time gain on sale added $0.10 per share and " + new string('x', OneOffItems.LongestSentence)));
    }

    // Each worked by hand. SWK's stored report: an actual of 2.31 against an estimate of 1.54 is a beat of 0.77, of which 0.67 is 87%,
    // leaving 0.10.
    [Fact]
    public void TheOneOffReadingSaysTheOrdersWords()
    {
        var on = new DateOnly(2026, 11, 4);
        var reading = EntryWorked() with { ReleaseOn = on, OneOffSentence = RefundBullet, OneOffAmount = 0.67m, OneOffTied = true, OneOffDirection = 1, Beat = 0.77m };

        Assert.Equal(
            "The results release of 2026-11-04 says: \"" + RefundBullet + "\" The release names an item worth about $0.67 a share. That is 87% of the $0.77 beat. Without it the beat is $0.10. This reads fixed phrases and can miss an item.",
            EntryWords.OneOff(reading));

        // A charge hurt the quarter, so the beat is larger without it; one larger than the beat leaves none; and a sentence that does
        // not tie the item to earnings, says no way it moved them, or has no beat stored states no share.
        Assert.Contains("Without it the beat is $1.07.", EntryWords.OneOff(reading with { OneOffDirection = -1, OneOffAmount = 0.30m }), StringComparison.Ordinal);
        Assert.Contains("Without it there is no beat.", EntryWords.OneOff(reading with { OneOffAmount = 0.90m }), StringComparison.Ordinal);
        Assert.DoesNotContain("beat", EntryWords.OneOff(reading with { OneOffTied = false }), StringComparison.Ordinal);
        Assert.DoesNotContain("beat", EntryWords.OneOff(reading with { OneOffDirection = null }), StringComparison.Ordinal);
        Assert.DoesNotContain("beat", EntryWords.OneOff(reading with { Beat = null }), StringComparison.Ordinal);
        Assert.DoesNotContain("beat", EntryWords.OneOff(reading with { Beat = -0.10m }), StringComparison.Ordinal);

        // Nothing matched is never said as none, and no release stored is an outline on the app and the copy.
        var nothing = reading with { OneOffSentence = null, OneOffAmount = null, OneOffTied = null, OneOffDirection = null };

        Assert.Equal("The results release of 2026-11-04: no per-share item found by these phrases.", EntryWords.OneOff(nothing));
        Assert.Null(EntryWords.OneOff(EntryWorked()));

        foreach (var marks in new[] { new MarkRenderer(), new MarkRenderer { Copying = true } })
        {
            Assert.Contains("<p class=\"entry-state\" data-entry=\"one-off\">No results release is stored for this stock, so no one-off item can be read.</p>", marks.EntryCheck(EntryWorked(), 110m, 109m, "drift"), StringComparison.Ordinal);
            Assert.Contains("<p class=\"entry-line\" data-entry=\"one-off\">", marks.EntryCheck(nothing, 110m, 109m, "drift"), StringComparison.Ordinal);
        }

        // The cards name it in each page's words.
        Assert.EndsWith(" · Release names a one-off item", EntryWords.CardLine(reading), StringComparison.Ordinal);
        Assert.EndsWith(" · The company's report includes a one-off item", EntryWords.HomeLine(reading), StringComparison.Ordinal);
        Assert.DoesNotContain("one-off", EntryWords.CardLine(nothing), StringComparison.Ordinal);
        Assert.DoesNotContain("one-off", EntryWords.HomeLine(nothing), StringComparison.Ordinal);
    }

    // A stop inside a company's name is no sentence end; a stop before a capital is, and so is any question or exclamation mark.
    [Theory]
    [InlineData("The acquisition of RXO by C.H. Robinson is viewed by many analysts as a strategic move.", null)]
    [InlineData("The article discusses the risks of the proposed acquisition of RXO by C.H. Robinson", null)]
    [InlineData("The article primarily discusses the earnings results of Rollins, Inc. and mentions Equifax only as a comparison.", null)]
    [InlineData("The article provides a commentary on Ferrovial N.V. while comparing its valuation to peers.", null)]
    [InlineData("It mentions W.R. Berkley Corporation as a peer.", null)]
    [InlineData("It mentions U.S. Bancorp as a peer.", null)]
    [InlineData("Sales rose. Margins widened too.", NewsInstruction.NotOneSentence)]
    [InlineData("Sales rose at the group. A rival said so.", NewsInstruction.NotOneSentence)]
    [InlineData("Is it good? Margins widened too.", NewsInstruction.NotOneSentence)]
    [InlineData("It did well! margins widened too.", NewsInstruction.NotOneSentence)]
    [InlineData("Sales rose to a record A. Margins widened too.", null)]
    public void ALabelsSentenceIsReadWithTheStopsInACompanysName(string reason, string? cause) =>
        Assert.Equal(cause, NewsInstruction.Read("{\"kind\":\"deal\",\"direction\":\"neutral\",\"reason\":\"" + reason + "\"}").Cause);

    [Fact]
    public void AStoryTheLabellerCouldNotLabelSaysWhyAndTheProofPassesThePageThatStatesIt()
    {
        Assert.Equal("the model's reason kept a figure, and a label's reason may hold none", NewsInstruction.WhyNotLabelled(NewsInstruction.DigitInTheReason));
        Assert.Equal("the model refused it", NewsInstruction.WhyNotLabelled(NewsInstruction.Refused));
        Assert.All(NewsInstruction.Causes, cause => Assert.NotEqual(cause, NewsInstruction.WhyNotLabelled(cause)));

        const string News = "<div class=\"news-region\" data-ticker=\"ROP\" data-articles=\"2\" data-all=\"2\" data-positive=\"1\" data-negative=\"0\" data-neutral=\"0\" data-opinion=\"0\" data-unlabelled=\"1\">";
        const string Left = "<li class=\"news-row\" data-article=\"c\" data-state=\"unreadable\" data-tabs=\"all\">";
        const string Said = "<span class=\"tag state\">This story could not be labelled: the model's reason kept a figure, and a label's reason may hold none</span>";

        var stated = ReportProof.Evaluate("ROP", News + Left + Said).Verdicts.Single(verdict => verdict.Rule == "news-labelled");

        Assert.Equal((ReportProof.Passed, "1 story could not be labelled and the page says why"), (stated.Verdict, stated.Why));

        var silent = ReportProof.Evaluate("ROP", News + Left + Left + Said).Verdicts.Single(verdict => verdict.Rule == "news-labelled");

        Assert.Equal((ReportProof.Failed, "the labeller read 1 admitted story and left it unlabelled with no word why"), (silent.Verdict, silent.Why));
    }

    [Fact]
    public void ANameSplitForTheFirstTimeIsListedWithWhereItCameFrom()
    {
        Assert.Equal(string.Empty, MarkRenderer.SplitNames([]));

        var list = System.Net.WebUtility.HtmlDecode(MarkRenderer.SplitNames([new SplitNameRow("HomeBuildingOps", "Home Building Ops", "DHI", "filing"), new SplitNameRow("ChiefFinOff", "Chief Fin Off", "CF", "insider")]));

        Assert.Contains("data-names=\"2\"", list, StringComparison.Ordinal);
        Assert.Contains("HomeBuildingOps reads \"Home Building Ops\": DHI, a segment in its filing", list, StringComparison.Ordinal);
        Assert.Contains("ChiefFinOff reads \"Chief Fin Off\": CF, an insider's title", list, StringComparison.Ordinal);
        Assert.True(JoinedNames.IsSplit("HomeBuildingOps"));
        Assert.False(JoinedNames.IsSplit("YouTube"));
        Assert.False(JoinedNames.IsSplit("Home Building"));
    }

    [Fact]
    public void AMemberWithNoNextReportDateAndNoEstimateSaysItHoldsNeither()
    {
        Assert.Contains("<dt>Next dated event</dt><dd>No next report date or estimate on file yet.</dd>", NameScreen.FactStrip("HOS", 6m, null, null, [], []), StringComparison.Ordinal);
        Assert.Equal("No next report date or estimate on file yet.", NameScreen.NoNextReport);
    }
}
