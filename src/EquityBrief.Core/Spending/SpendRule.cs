using System.Globalization;

namespace EquityBrief.Core.Spending;

// The two caps, in money: one for a day and one for a month, each a stop.
//
// Money rather than tokens, because the same token count costs twice as much at
// peak and a token budget would mean two different things depending on the hour.
// Decimal in code and TEXT in storage, because a spend is money and money is never
// a statistic here. The figures are section 17's, marked proposed there, and the
// operating row that settles them is read on the run page's operational header.
// see: The spend cap is a stop, not an allowance
// owes: The spend cap set from the passes the ledger has priced
public sealed record SpendCaps
{
    public const string DayKey = "EquityBrief:Spend:DayCap";
    public const string MonthKey = "EquityBrief:Spend:MonthCap";

    // Section 17's proposals, in dollars. Ten a day over twenty trading days is two
    // hundred a month against a ceiling of fifty, so the month is the budget and the
    // day is a burst limit that stops a runaway inside one evening.
    public const decimal DefaultDay = 10m;
    public const decimal DefaultMonth = 50m;

    public SpendCaps(decimal day, decimal month)
    {
        if (day <= 0m || month <= 0m)
        {
            throw new InvalidOperationException(
                $"A spend cap of {day.ToString(CultureInfo.InvariantCulture)} a day and " +
                $"{month.ToString(CultureInfo.InvariantCulture)} a month is not a cap. A cap at or below zero stops " +
                "every pass, which is a setting nobody means, and research that never runs reads as research that " +
                "was never needed.");
        }

        Day = day;
        Month = month;
    }

    public decimal Day { get; }

    public decimal Month { get; }

    public static SpendCaps Default { get; } = new(DefaultDay, DefaultMonth);

    // A cap as configuration writes it, or the default where it writes none. A value
    // that is not an amount of money is refused rather than read as the default,
    // because a cap that silently became another figure is not the cap the file says.
    public static SpendCaps From(string? day, string? month) =>
        new(Amount(day, DayKey, DefaultDay), Amount(month, MonthKey, DefaultMonth));

    static decimal Amount(string? value, string key, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new InvalidOperationException(
                $"'{key}' is '{value}', which is not an amount of money written with a decimal point. It is read as " +
                "written rather than replaced by the default.");
    }
}

// One run log row's spend, with the instant its stage started.
public readonly record struct SpentRow(DateTimeOffset StartedAt, decimal Spend);

// What the run log says was spent, by UTC day and by UTC month.
//
// Summed from the rows as stored rather than from a figure a caller keeps, because a
// stage's own count of what it spent is the stage's opinion and the cap keys on the
// number. UTC, because every period here is written in UTC: the provider's own peak
// windows are, and a day read in local time would move against them twice a year.
// see: Queued work runs off-peak, and every schedule is written in UTC
public sealed class SpendLedger(IEnumerable<SpentRow> rows)
{
    readonly SpentRow[] rows = [.. rows];

    public int Rows => rows.Length;

    public decimal SpentOn(DateOnly day) =>
        rows.Where(row => DateOnly.FromDateTime(row.StartedAt.UtcDateTime) == day).Sum(row => row.Spend);

    public decimal SpentIn(int year, int month) =>
        rows.Where(row => row.StartedAt.UtcDateTime.Year == year && row.StartedAt.UtcDateTime.Month == month).Sum(row => row.Spend);

    // The first instant of the UTC month an instant falls in, which is as far back as
    // a ledger read for a verdict ever needs to go.
    public static DateTimeOffset MonthStart(DateTimeOffset instant) =>
        new(instant.UtcDateTime.Year, instant.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
}

// Whether research may spend now, and if not, which cap stopped it and when it
// resumes.
//
// `Ceiling` is the most the call being judged could cost, zero where nothing is about
// to be called, as when a page states where research stands.
public sealed record SpendVerdict(
    bool Paused,
    string? Cap,
    decimal SpentToday,
    decimal SpentThisMonth,
    SpendCaps Caps,
    DateTimeOffset? ResumesAt,
    decimal Ceiling = 0m)
{
    public const string DayCap = "day";
    public const string MonthCap = "month";

    // The one line a page and a refusal both state, so the two cannot describe one
    // pause differently.
    public string Line =>
        !Paused
            ? string.Create(CultureInfo.InvariantCulture, $"research may spend: {Money(SpentToday)} of the {Money(Caps.Day)} day cap today, and {Money(SpentThisMonth)} of the {Money(Caps.Month)} month cap this month")
            : Reached
                ? Cap == MonthCap
                    ? string.Create(CultureInfo.InvariantCulture, $"research is paused: this month's spend of {Money(SpentThisMonth)} has reached the {Money(Caps.Month)} month cap, and it resumes at {Instant(ResumesAt!.Value)}")
                    : string.Create(CultureInfo.InvariantCulture, $"research is paused: today's spend of {Money(SpentToday)} has reached the {Money(Caps.Day)} day cap, and it resumes at {Instant(ResumesAt!.Value)}")
                : Cap == MonthCap
                    ? string.Create(CultureInfo.InvariantCulture, $"research is paused: a call that could cost {Money(Ceiling)} would take this month's spend of {Money(SpentThisMonth)} past the {Money(Caps.Month)} month cap, and it resumes at {Instant(ResumesAt!.Value)}")
                    : string.Create(CultureInfo.InvariantCulture, $"research is paused: a call that could cost {Money(Ceiling)} would take today's spend of {Money(SpentToday)} past the {Money(Caps.Day)} day cap, and it resumes at {Instant(ResumesAt!.Value)}");

    // Whether the cap that stopped research has been reached, as against a call that
    // would take spend past it. Both pause; the line says which.
    public bool Reached =>
        Paused && (Cap == MonthCap ? SpentThisMonth >= Caps.Month : SpentToday >= Caps.Day);

    // Money as a page states it: to the cent where the amount has a cent, and to the
    // hundredth of a cent below one, so a pass costing a fraction of a cent is not
    // drawn as nothing.
    public static string Money(decimal amount) =>
        "$" + (amount != 0m && Math.Abs(amount) < 0.01m
            ? amount.ToString("0.0000", CultureInfo.InvariantCulture)
            : amount.ToString("0.00", CultureInfo.InvariantCulture));

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
}

// The cap as a rule: a function of the ledger, the caps, an instant, and the most the
// call about to be made could cost.
//
// Each cap refuses on its own. Research pauses where spend has reached either cap, or
// where the call's ceiling would take spend past either, until the period that cap
// governs ends: the next UTC midnight for the day, the first instant of the next UTC
// month for the month. The ceiling is why the cap is a stop rather than a line the
// last call crosses: a call's price is known only once it has been made, so a gate
// that asked only whether the cap had been reached would let the call that reaches it
// spend past it. Where both caps stop research the month is the one named, because it
// resumes later and a line saying research resumes at midnight would be wrong at
// midnight.
// see: The spend cap is a stop, not an allowance
// see: The spend cap counts a UTC day and a UTC month, and refuses a call that could take spend past either
public static class SpendRule
{
    public static SpendVerdict Judge(SpendLedger ledger, SpendCaps caps, DateTimeOffset now, decimal ceiling = 0m)
    {
        if (ceiling < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(ceiling), "A call cannot cost less than nothing.");
        }

        var utc = now.UtcDateTime;
        var today = DateOnly.FromDateTime(utc);
        var spentToday = ledger.SpentOn(today);
        var spentThisMonth = ledger.SpentIn(utc.Year, utc.Month);

        if (spentThisMonth >= caps.Month || spentThisMonth + ceiling > caps.Month)
        {
            return new SpendVerdict(true, SpendVerdict.MonthCap, spentToday, spentThisMonth, caps, SpendLedger.MonthStart(now).AddMonths(1), ceiling);
        }

        if (spentToday >= caps.Day || spentToday + ceiling > caps.Day)
        {
            return new SpendVerdict(
                true,
                SpendVerdict.DayCap,
                spentToday,
                spentThisMonth,
                caps,
                new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                ceiling);
        }

        return new SpendVerdict(false, null, spentToday, spentThisMonth, caps, null, ceiling);
    }
}
