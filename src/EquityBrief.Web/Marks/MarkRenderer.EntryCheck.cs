using EquityBrief.Core.EntryCheck;

namespace EquityBrief.Web.Marks;

public sealed partial class MarkRenderer
{
    // The Entry check region of a stock a family lists: every reading as a plain sentence, and the state a dashed outline saying
    // it is not set. The read-only copy draws the sentences and leaves out the outline, which speaks of the test.
    // see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
    public string EntryCheck(EntryReading? reading, decimal? buy, decimal? stop, string? family)
    {
        if (reading is null)
        {
            return string.Empty;
        }

        var rule = family is null ? null : (EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family).ToLowerInvariant();

        string Line(string key, string? said) =>
            said is null ? string.Empty : $"<p class=\"entry-line\" data-entry=\"{key}\">{Escaped(said)}</p>";

        return $"<section class=\"entry-check\" id=\"entry-check\" data-ticker=\"{Escaped(reading.Ticker)}\"><h3>Entry check</h3>" +
            Line("stretch", EntryWords.Stretch(reading)) +
            Line("hot-run", EntryWords.HotRun(reading)) +
            Line("gap", EntryWords.Gap(reading, buy, stop, rule)) +
            Line("highest", EntryWords.Highest(reading)) +
            Line("older", EntryWords.Older(reading)) +
            (Copying ? string.Empty : $"<p class=\"entry-state\">{Escaped(EntryWords.StateNotSet)}</p>") +
            "</section>";
    }

    // A figure dated before the newest report says so.
    // see: A figure fetched before the newest report says so with its fetch date, and a company's value names the day its shares were filed
    public static string SetBeforeWords(DateOnly report) => FormattableString.Invariant($"Set before the {report:yyyy-MM-dd} report");

    public static string SetBefore(DateOnly? set, DateOnly? report) =>
        set is { } day && report is { } newest && day < newest ? $"<p class=\"set-before\" data-report=\"{DayOf(newest)}\">{Escaped(SetBeforeWords(newest))}</p>" : string.Empty;

    public static string FetchedBefore(DateOnly? fetched, DateOnly? report) =>
        fetched is { } day && report is { } newest && day < newest
            ? $"<p class=\"fetched-before\" data-fetched-on=\"{DayOf(day)}\">The provider's figures here were fetched on {DayOf(day)}, before the {DayOf(newest)} report.</p>"
            : string.Empty;

    // The card's one line under the buy: the flags that apply, or that none does, and the link to the stock's own page.
    public static string EntryCheckLine(EntryReading? reading, string ticker) =>
        reading is null
            ? string.Empty
            : EntryLine(EntryWords.Flags(reading).Count, EntryWords.CardLine(reading), ticker);

    // An idea's line on Home, in Home's plain words.
    // see: Home's line of an idea's entry check says each flag in plain words, and dates a day as d Mon yyyy
    public static string HomeEntryCheckLine(EntryReading? reading, string ticker) =>
        reading is null
            ? string.Empty
            : EntryLine(EntryWords.HomeFlags(reading).Count, EntryWords.HomeLine(reading), ticker);

    static string EntryLine(int flags, string line, string ticker) =>
        $"<p class=\"entry-card-line\" data-entry-flags=\"{flags.ToString(Invariant)}\">{Escaped(line)} <a href=\"#/name/{Uri.EscapeDataString(ticker)}\">{Escaped(EntryWords.See)}</a></p>";
}
