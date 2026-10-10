using EquityBrief.Core.Cards;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.3: what could hit a pick's trade, worked by hand: the hold's last session on the exchange's
// calendar, the stock's reactions in its typical moves and in the plan's risks with how many moved past the stop's
// distance, and the next ex-dividend date inside the hold, declared where the calendar carries one and estimated from the
// company's last declared date and usual interval where it does not.
// see: A pick's next ex-dividend date is the calendar's where it declares one, and otherwise estimated from the dividend the quarters fetch kept or else from the newest fundamentals fetch and the steps its dividends leave in the bars
public partial class FixtureExpectations
{
    // The row the 16.3 correction of 2026-10-10 adds that this check reaches: section 17's dividend step.
    internal static readonly string[] DividendStepClaims = [CheckReach.Key(Scope.LimitsTable, "Dividend step")];

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

    [Fact]
    public void ThePaymentsAYearAreReadFromTheStepsADividendLeavesInTheRatioOfTheAdjustedCloseToTheRaw()
    {
        // Each session's adjusted close over a raw close of 100.00, two sessions at each ex-date, the ratio rising on it.
        static List<(DateOnly Session, decimal Close, decimal RawClose)> Bars(params (string Day, decimal Ratio)[] days) =>
            [.. days.Select(day => (DateOnly.ParseExact(day.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), 100.00m * day.Ratio, 100.00m))];

        // Steps on 2025-11-07, 2026-01-23, 2026-05-08 and 2026-07-24, 77, 105 and 77 days apart: a median of 77, nearest
        // a quarter's 91.3 by their ratio, so four a year, where a year over 77 rounded would read five. Between steps the
        // ratio holds, and a session holding it is no step.
        Assert.Equal(4, CardHitsReading.PaymentsAYear(Bars(
            ("2025-11-06", 0.990m), ("2025-11-07", 0.993m), ("2026-01-22", 0.993m), ("2026-01-23", 0.996m),
            ("2026-05-07", 0.996m), ("2026-05-08", 0.998m), ("2026-07-23", 0.998m), ("2026-07-24", 1.000m))));

        // Gaps of 28, 28, 28, 33 and 35 days between the steps: a median of 28, nearest a month's 30.4, so twelve, where a
        // year over 28 rounded would read thirteen.
        Assert.Equal(12, CardHitsReading.PaymentsAYear(Bars(
            ("2026-01-01", 0.990m), ("2026-01-02", 0.991m), ("2026-01-30", 0.992m), ("2026-02-27", 0.993m),
            ("2026-03-27", 0.994m), ("2026-04-29", 0.995m), ("2026-06-03", 0.996m))));

        // Two steps with 182 days between them read twice a year; between them a rise of exactly a fifth, a split of six for
        // five, is no step, where one a tenth of a point under it is, and three with 91 days between each read four.
        Assert.Equal(2, CardHitsReading.PaymentsAYear(Bars(("2026-01-02", 0.79m), ("2026-01-05", 0.80m), ("2026-04-02", 0.80m), ("2026-04-06", 0.96m), ("2026-07-02", 0.96m), ("2026-07-06", 0.97m))));
        Assert.Equal(4, CardHitsReading.PaymentsAYear(Bars(("2026-01-02", 0.79m), ("2026-01-05", 0.80m), ("2026-04-02", 0.80m), ("2026-04-06", 0.9592m), ("2026-07-02", 0.9592m), ("2026-07-06", 0.97m))));

        // A rise of 0.020 per cent is a step and one of 0.019 is not: 100.00 over 99.98, less one, is 0.00020004, and over
        // 99.981 it is 0.00019004. Fewer than two steps read none.
        Assert.Equal(4, CardHitsReading.PaymentsAYear(Bars(("2026-01-02", 0.9998m), ("2026-01-05", 1.0000m), ("2026-04-02", 0.9998m), ("2026-04-06", 1.0000m))));
        Assert.Null(CardHitsReading.PaymentsAYear(Bars(("2026-01-02", 0.99981m), ("2026-01-05", 1.0000m), ("2026-04-02", 0.99981m), ("2026-04-06", 1.0000m))));
        Assert.Null(CardHitsReading.PaymentsAYear(Bars(("2026-01-02", 0.99m), ("2026-01-05", 1.00m))));

        // The payments a year a dividend's steps give estimate its next date where no year's count is filed: four a year
        // from 2026-08-10 is 2026-11-09 and 1.08 / 4 = 0.27 a share; a year's count filed is read before them.
        var fetched = new DividendKept(1.08m, new DateOnly(2026, 8, 10), []) { PaymentsAYear = 4 };
        var estimated = CardHitsReading.Dividend(HitsNight, new DateOnly(2027, 1, 6), [], fetched, 100.00m, 97.00m)!;

        Assert.Equal((new DateOnly(2026, 11, 9), false, 0.27m), (estimated.Date, estimated.Declared, estimated.Amount));
        Assert.Equal(new DateOnly(2026, 11, 9), CardHitsReading.Dividend(HitsNight, new DateOnly(2027, 1, 6), [], fetched with { ByYear = [new DividendsInYear(2025, 4)], PaymentsAYear = 12 }, 100.00m, 97.00m)!.Date);
    }

    [Fact]
    public void AHoldPastTheCalendarsSessionsWithNoDividendKeptLeavesALaterDateUnreadRatherThanRuledOut()
    {
        // The calendar is asked for the 21 sessions after the night, through 2026-11-04. A hold capped at 21 ends there, so
        // the calendar answers for the whole of it and a date it does not declare is none; one capped at 22 ends a session
        // past it, 11-05, and with no dividend of the company's kept nothing can estimate a later date, so it is unread.
        var reactions = new ReactionFigures(0, null, null, 0);

        CardHits Read(int cap, IReadOnlyList<DateOnly> declared, DividendKept? kept) =>
            CardHitsReading.Read(HitsNight, cap, reactions, declared, kept, 100.00m, 97.00m, declaredSessions: 21);

        Assert.Equal((false, (DividendAhead?)null), (Read(21, [], null).DividendUnread, Read(21, [], null).Dividend));
        Assert.Equal((new DateOnly(2026, 11, 5), true), (Read(22, [], null).HoldThrough, Read(22, [], null).DividendUnread));
        Assert.True(Read(63, [], null).DividendUnread);

        // A date the calendar declares is drawn whatever is kept, and a dividend kept estimates a date or reads none, so
        // neither leaves one unread.
        Assert.Equal((new DateOnly(2026, 10, 20), false), (Read(63, [new DateOnly(2026, 10, 20)], null).Dividend!.Date, Read(63, [new DateOnly(2026, 10, 20)], null).DividendUnread));

        var kept = new DividendKept(1.08m, new DateOnly(2026, 8, 10), [new DividendsInYear(2025, 4), new DividendsInYear(2026, 3)]);

        Assert.Equal((new DateOnly(2026, 11, 9), false), (Read(63, [], kept).Dividend!.Date, Read(63, [], kept).DividendUnread));
        Assert.Equal(((DividendAhead?)null, false), (Read(63, [], new DividendKept(0m, null, [])).Dividend, Read(63, [], new DividendKept(0m, null, [])).DividendUnread));

        // Read with no count of the calendar's sessions, as a caller stating none, nothing is unread.
        Assert.False(CardHitsReading.Read(HitsNight, 63, reactions, [], null, 100.00m, 97.00m).DividendUnread);
    }
}
