using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.11's correction of the revenue step's words: a drift's revenue step on the S&P 400 and 600 missed for a
// quarter its night never read says so on each stock's line and is counted apart where a card lists none, a stock whose two
// quarters were read and whose sales were not up says that, and the step named over many stocks reads true of either.
public partial class ReadSurface
{
    // The words an S&P 400 or 600 night stores beside a missed revenue step, one for each way the step is missed, as the
    // frozen S&P 600 drift's own test reads them off the night.
    const string NotBothStored = "its print's quarter and the same quarter a year before not both stored as first filed before tonight";

    const string NoneStored = "no quarter of its revenue stored as first filed before tonight";

    const string FellTen = "revenue -10.0% on the same quarter a year before, both as first filed";

    [Fact]
    public void ADriftsRevenueStepMissedForAQuarterNeverReadSaysSoOnEachStocksLineAndIsCountedApart()
    {
        // Three stocks one step short at the revenue step: the first two never read, the third read and fallen.
        var card = new MarkRenderer().FamilyCard(new FamilyCardView(
            DriftRule.Name, "Earnings drift", "Drift after a report", "Earnings drift", "A stock jumps on its report.", 3, 4, new DateOnly(2026, 10, 20), 0, [], [], null,
            Close:
            [
                new CloseToBuyCell("N1", null, DriftRule.Name, "Earnings drift", RuleRows.Revenue, NotBothStored),
                new CloseToBuyCell("N2", null, DriftRule.Name, "Earnings drift", RuleRows.Revenue, NoneStored),
                new CloseToBuyCell("N3", null, DriftRule.Name, "Earnings drift", RuleRows.Revenue, FellTen),
            ]));

        string Missed(string ticker) =>
            Regex.Match(card, $"<tr data-ticker=\"{ticker}\".*?<td class=\"missed-gate\"[^>]*><b>([^<]*)</b>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal("Its quarter's sales not read yet", Missed("N1"));
        Assert.Equal("Its quarter's sales not read yet", Missed("N2"));
        Assert.Equal("Sales not up on a year earlier", Missed("N3"));
        Assert.Contains("The closest, N1, missed one step: its quarter's sales not read yet.", card, StringComparison.Ordinal);

        // Named over many stocks, the step's own words hold for either way it is missed.
        Assert.Equal("no quarter read with sales up on a year earlier", ReaderWords.Missed(DriftRule.Name, RuleRows.Revenue));

        // Where a card lists none, the night's reasons count a revenue never read apart from one read and not above.
        static IndexResultRow Stopped(string ticker, string words) =>
            new(ticker, DriftRule.Name, false, null, null, null, null, null, null, null, RuleRows.RevenueNotAbove,
                FamilyRule.GatesJson([new Gate(RuleRows.Setup, true, string.Empty, FamilyRule.Values()), new Gate(RuleRows.Revenue, false, words, FamilyRule.Values())]));

        Assert.Equal(ReaderWords.RevenueNotRead, TonightScreen.StoppedBy(Stopped("N1", NotBothStored)));
        Assert.Equal(ReaderWords.RevenueNotRead, TonightScreen.StoppedBy(Stopped("N2", NoneStored)));
        Assert.Equal(RuleRows.RevenueNotAbove, TonightScreen.StoppedBy(Stopped("N3", FellTen)));
    }
}
