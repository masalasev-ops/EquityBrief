using EquityBrief.Core.Cards;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.3: what could hit a pick's trade, worked by hand: the hold's last session on the exchange's
// calendar, the stock's reactions in its typical moves and in the plan's risks with how many moved past the stop's
// distance, and the next ex-dividend date inside the hold, declared where the calendar carries one and estimated from the
// company's last declared date and usual interval where it does not.
// see: A pick's next ex-dividend date is the calendar's where it declares one and the last declared date plus the usual interval where it does not
public partial class FixtureExpectations
{
    static readonly DateOnly HitsNight = new(2026, 10, 6);

    [Fact]
    public async Task TheReplayKeepsEachNamesDividendAsTheCapturedAnswersFileIt()
    {
        var expected = Expected("dividend-readings");

        using var store = await FixtureReplay.ReplayedAsync();

        foreach (var name in expected.GetProperty("names").EnumerateObject())
        {
            var row = Query(store, $"SELECT IFNULL(forward_rate, '-'), IFNULL(last_ex_date, '-'), by_year FROM dividend_reading WHERE ticker = '{name.Name}';").Single().Split('|');
            using var years = System.Text.Json.JsonDocument.Parse(row[2]);
            var counted = years.RootElement.EnumerateArray().Select(year => (Year: year.GetProperty("year").GetInt32(), Count: year.GetProperty("count").GetInt32())).ToArray();

            Assert.Equal(name.Value.GetProperty("forwardRate").GetString(), row[0]);
            Assert.Equal(name.Value.GetProperty("lastExDate").GetString() ?? "-", row[1]);
            Assert.Equal(name.Value.GetProperty("years").GetInt32(), counted.Length);

            if (name.Value.TryGetProperty("lastWhole", out var whole))
            {
                Assert.Contains((whole[0].GetInt32(), whole[1].GetInt32()), counted);
                Assert.Equal(counted.OrderBy(year => year.Year), counted);
            }
        }
    }

    [Fact]
    public void AHoldEndsItsCapsCountOfSessionsAfterTheNight()
    {
        // Three sessions after Tuesday 2026-10-06 is Friday 10-09; twenty-one is 11-04; sixty-three, past Thanksgiving,
        // Christmas and New Year's Day, is Wednesday 2027-01-06; and a rule capping none reads the twenty-one.
        Assert.Equal(new DateOnly(2026, 10, 9), CardHitsReading.HoldThrough(HitsNight, 3));
        Assert.Equal(new DateOnly(2026, 11, 4), CardHitsReading.HoldThrough(HitsNight, 21));
        Assert.Equal(new DateOnly(2027, 1, 6), CardHitsReading.HoldThrough(HitsNight, 63));
        Assert.Equal(CardHitsReading.HoldThrough(HitsNight, 21), CardHitsReading.HoldThrough(HitsNight, null));
    }

    [Fact]
    public void TheReactionsAreReadInTypicalMovesAndInThePlansRisks()
    {
        // Four reactions of +3.0%, -6.0%, +1.5% and -4.5% on a close of 100.00 with a typical move of 2.00: 1.5, 3.0,
        // 0.75 and 2.25 typical moves, median 1.875. Against a buy of 100.00 and a stop of 97.00, 1.0, 2.0, 0.5 and 1.5
        // risks, median 1.25, two of them past the stop's distance and the one at exactly 1.0 not.
        var reactions = CardHitsReading.Reactions([3.0, -6.0, 1.5, -4.5], 100.00m, 2.0, 100.00m, 97.00m);

        Assert.Equal(4, reactions.Count);
        Assert.Equal(1.875, reactions.MedianTypical!.Value, 9);
        Assert.Equal(1.25, reactions.MedianRisks!.Value, 9);
        Assert.Equal(2, reactions.PastTheStop);

        // None stored reads none; a rule with no stop reads no risks.
        Assert.Equal(new ReactionFigures(0, null, null, 0), CardHitsReading.Reactions([], 100.00m, 2.0, 100.00m, 97.00m));
        Assert.Null(CardHitsReading.Reactions([3.0], 100.00m, 2.0, 100.00m, null).MedianRisks);
    }

    [Fact]
    public void TheNextExDividendDateIsDeclaredWhereTheCalendarCarriesOneAndEstimatedWhereItDoesNot()
    {
        // A forward rate of 1.08 a share paid four times in 2025, the last whole year, three times so far in 2026, the
        // last on 2026-08-10: 0.27 a payment, a usual interval of 365 / 4 = 91.25 days, 91 rounded.
        var kept = new DividendKept(1.08m, new DateOnly(2026, 8, 10), [new DividendsInYear(2025, 4), new DividendsInYear(2026, 3)]);

        // The calendar declares 2026-10-20 inside a hold through 11-04: that date, declared, 0.27 / 3.00 = 0.09 risks.
        var declared = CardHitsReading.Dividend(HitsNight, new DateOnly(2026, 11, 4), [new DateOnly(2026, 10, 20)], kept, 100.00m, 97.00m)!;

        Assert.Equal((new DateOnly(2026, 10, 20), true, 0.27m), (declared.Date, declared.Declared, declared.Amount));
        Assert.Equal(0.09, declared.InRisks!.Value, 9);

        // None declared: 2026-08-10 plus 91 days is 2026-11-09, past a hold through 11-04 and inside one through
        // 2027-01-06, drawn as estimated.
        Assert.Null(CardHitsReading.Dividend(HitsNight, new DateOnly(2026, 11, 4), [], kept, 100.00m, 97.00m));

        var estimated = CardHitsReading.Dividend(HitsNight, new DateOnly(2027, 1, 6), [], kept, 100.00m, 97.00m)!;

        Assert.Equal((new DateOnly(2026, 11, 9), false), (estimated.Date, estimated.Declared));

        // The last declared date two intervals back: 2026-04-10 plus 91 days is 07-10, before the night, and plus 183,
        // 182.5 rounded away from zero, is 10-10.
        Assert.Equal(new DateOnly(2026, 10, 10), CardHitsReading.Dividend(HitsNight, new DateOnly(2026, 11, 4), [], kept with { LastExDate = new DateOnly(2026, 4, 10) }, 100.00m, 97.00m)!.Date);

        // A rule with no stop reads the payment in per cent of the buy: 0.27 over 100.00, 0.27%.
        Assert.Equal(0.27, CardHitsReading.Dividend(HitsNight, new DateOnly(2026, 11, 4), [new DateOnly(2026, 10, 20)], kept, 100.00m, null)!.InPercent!.Value, 9);

        // A company paying none, and one whose answer files no year's count, draw no estimate; a declared date on the
        // night itself is not after it.
        Assert.Null(CardHitsReading.Dividend(HitsNight, new DateOnly(2027, 1, 6), [], new DividendKept(0m, null, []), 100.00m, 97.00m));
        Assert.Null(CardHitsReading.Dividend(HitsNight, new DateOnly(2027, 1, 6), [], kept with { ByYear = [] }, 100.00m, 97.00m));
        Assert.Null(CardHitsReading.Dividend(HitsNight, new DateOnly(2026, 11, 4), [HitsNight], kept with { LastExDate = null }, 100.00m, 97.00m));
    }
}
