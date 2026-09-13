using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Spending;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.7: the spend cap as a rule over a ledger, each cap on its
// own, and section 17's row read off the document against the constants.
//
// Over constructed ledgers, because the rule is a function of rows and an instant and
// the edges it turns on, the last second of a UTC day and the first of a month, are
// shapes no recorded pass lands on.
public partial class FixtureExpectations
{
    static SpentRow Spent(string instant, string amount) =>
        new(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), decimal.Parse(amount, CultureInfo.InvariantCulture));

    [Fact]
    public void TheDayCapRefusesOnItsOwnAndResumesAtTheNextUtcMidnight()
    {
        // Today at the day cap and the month well below its own.
        var ledger = new SpendLedger(
        [
            Spent("2026-09-15T14:00:00Z", "6.00"),
            Spent("2026-09-15T18:30:00Z", "4.00"),
            Spent("2026-09-14T23:59:59Z", "3.00"),
        ]);

        var verdict = SpendCap.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(verdict.Paused);
        Assert.Equal(SpendVerdict.DayCap, verdict.Cap);
        Assert.Equal(10.00m, verdict.SpentToday);
        Assert.Equal(13.00m, verdict.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);
        Assert.Equal(
            "research is paused: today's spend of $10.00 has reached the $10.00 day cap, and it resumes at 2026-09-16 00:00 UTC",
            verdict.Line);

        // A cent below, and it does not.
        var below = SpendCap.Judge(
            new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.99")]),
            SpendCaps.Default,
            DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.False(below.Paused);
        Assert.Null(below.ResumesAt);

        // The day is a UTC day: the same spend a second before midnight belongs to
        // the day before, and a pass just after midnight is judged on a fresh day.
        var afterMidnight = SpendCap.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-16T00:00:01Z", CultureInfo.InvariantCulture));

        Assert.False(afterMidnight.Paused);
        Assert.Equal(0m, afterMidnight.SpentToday);
    }

    [Fact]
    public void TheMonthCapRefusesOnItsOwnAndResumesAtTheFirstOfTheNextUtcMonth()
    {
        // Nothing spent today, and the month at its cap from earlier days.
        var ledger = new SpendLedger(
        [
            Spent("2026-09-01T00:00:00Z", "20.00"),
            Spent("2026-09-10T12:00:00Z", "30.00"),
            Spent("2026-08-31T23:59:59Z", "40.00"),
        ]);

        var verdict = SpendCap.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(verdict.Paused);
        Assert.Equal(SpendVerdict.MonthCap, verdict.Cap);
        Assert.Equal(0m, verdict.SpentToday);
        Assert.Equal(50.00m, verdict.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);

        // August's forty is August's: on the last second of August the month stands at
        // forty, below its cap, and what stops research then is the day, which spent
        // all forty and resumes a second later. A December pause resumes in the next
        // year.
        var lastOfAugust = SpendCap.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-08-31T23:59:59Z", CultureInfo.InvariantCulture));

        Assert.Equal(SpendVerdict.DayCap, lastOfAugust.Cap);
        Assert.Equal(40.00m, lastOfAugust.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z", CultureInfo.InvariantCulture), lastOfAugust.ResumesAt);

        var december = SpendCap.Judge(
            new SpendLedger([Spent("2026-12-03T09:00:00Z", "50.00")]),
            SpendCaps.Default,
            DateTimeOffset.Parse("2026-12-20T09:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture), december.ResumesAt);
    }

    [Fact]
    public void WhereBothCapsAreReachedTheMonthIsNamedBecauseItResumesLater()
    {
        var ledger = new SpendLedger(
        [
            Spent("2026-09-15T10:00:00Z", "10.00"),
            Spent("2026-09-02T10:00:00Z", "45.00"),
        ]);

        var verdict = SpendCap.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T11:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(SpendVerdict.MonthCap, verdict.Cap);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);
    }

    [Fact]
    public void ACallThatCouldTakeSpendPastACapIsRefusedBeforeTheCapIsReached()
    {
        // Nine dollars ninety spent today. A call that could cost twenty cents would
        // take the day past its cap, so it is refused although the cap has not been
        // reached; one that could cost ten cents lands on the cap exactly and is let
        // through, and the next call after it meets a cap that has been reached.
        var ledger = new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.90")]);
        var now = DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture);

        var tooDear = SpendCap.Judge(ledger, SpendCaps.Default, now, ceiling: 0.20m);

        Assert.True(tooDear.Paused);
        Assert.False(tooDear.Reached);
        Assert.Equal(SpendVerdict.DayCap, tooDear.Cap);
        Assert.Equal(
            "research is paused: a call that could cost $0.20 would take today's spend of $9.90 past the $10.00 day cap, and it resumes at 2026-09-16 00:00 UTC",
            tooDear.Line);

        Assert.False(SpendCap.Judge(ledger, SpendCaps.Default, now, ceiling: 0.10m).Paused);

        var after = new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.90"), Spent("2026-09-15T20:00:00Z", "0.10")]);

        Assert.True(SpendCap.Judge(after, SpendCaps.Default, now, ceiling: 0.0001m).Reached);

        // The month refuses a call on its own terms the same way.
        var month = SpendCap.Judge(
            new SpendLedger([Spent("2026-09-02T10:00:00Z", "49.95")]),
            SpendCaps.Default,
            now,
            ceiling: 0.06m);

        Assert.Equal(SpendVerdict.MonthCap, month.Cap);
        Assert.False(month.Reached);

        Assert.Throws<ArgumentOutOfRangeException>(() => SpendCap.Judge(ledger, SpendCaps.Default, now, ceiling: -0.01m));
    }

    [Fact]
    public void ACapIsReadAsConfigurationWritesItAndRefusedWhereItIsNotMoney()
    {
        Assert.Equal(SpendCaps.Default, SpendCaps.From(null, " "));
        Assert.Equal(new SpendCaps(12.5m, 80m), SpendCaps.From("12.5", "80"));

        Assert.Contains($"'{SpendCaps.DayKey}' is '10 dollars'", Assert.Throws<InvalidOperationException>(() => SpendCaps.From("10 dollars", null)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => SpendCaps.From("0", null));
        Assert.Throws<InvalidOperationException>(() => new SpendCaps(10m, -1m));

        // A fraction of a cent is drawn as what it is rather than as nothing.
        Assert.Equal("$0.0001", SpendVerdict.Money(0.00006m));
        Assert.Equal("$0.00", SpendVerdict.Money(0m));
        Assert.Equal("$12.35", SpendVerdict.Money(12.345m));
    }

    [Fact]
    public void SectionSeventeensSpendCapRowStatesTheFiguresTheCapHolds()
    {
        // Read off the row rather than restated beside the constants.
        var row = ClaimAdmissibility.Row(Scope.LimitsTable, "Spend cap");
        var figures = Regex.Match(string.Join(" ", row), @"proposed at (\d+) and (\d+) dollars");

        Assert.True(figures.Success, "section 17's spend cap row states no proposed figures in the form this reads");
        Assert.Equal(SpendCaps.DefaultDay, decimal.Parse(figures.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultMonth, decimal.Parse(figures.Groups[2].Value, CultureInfo.InvariantCulture));

        // And the row says the cap is money per day and per month, which is what the
        // two constants are.
        Assert.Contains("per day and per month", string.Join(" ", row), StringComparison.Ordinal);
    }
}
