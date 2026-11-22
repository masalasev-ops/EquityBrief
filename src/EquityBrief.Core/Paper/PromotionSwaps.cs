using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Paper;

// The trades kept under one name and listed from one session to the session before another, which a swap hands to the account
// kept under a second name.
public sealed record PromotionCarry(string To, string From, DateOnly Since, DateOnly Until);

// One press on the case for promotion: its index, setup and variant's account, Promote, Keep or Undo, the press it undoes, the
// monthly run's month its case stood at and when it was pressed.
public sealed record PromotionPressRow(long Id, string Index, string Family, string Variant, string Press, long? Undoes, string? Month, DateTimeOffset PressedAt);

// The night's answer to a press: whether it swapped, its words, the revision the variant became, the variant's name, the
// running revision's and the name the replaced revision runs on under, the records carried, and the night it ran from.
public sealed record PromotionSwapRow(
    long PressId,
    string Index,
    string Family,
    bool Applied,
    string Words,
    int? Revision,
    string? Promoted,
    string? Running,
    string? Replaced,
    IReadOnlyList<PromotionCarry> Carries,
    DateOnly Night,
    DateTimeOffset SwappedAt);

// A press's state as the case page draws it: waiting for the night that applies it, applied, or neither.
public sealed record PromotionState(PromotionPressRow Press, PromotionSwapRow? Answer, PromotionPressRow? UndoneBy);

// The swap a promotion makes, in the core so the night that writes it and the pages that draw it read one set of words and
// one carry. A press of Promote is written by the night's own build at its start, before anything reads the register: the
// variant's settings registered as the setup's running revision and the running revision's as a variant, each carrying its
// own record; a press waiting for that night can be undone, and an applied swap is undone by the same swap back at the start
// of the next night that runs.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public static class PromotionSwaps
{
    public const string Promote = "promote";

    public const string Keep = "keep";

    public const string Undo = "undo";

    // The words the replaced revision's variant is named with: the running revision's own name with the revision it ran as.
    public static string ReplacedName(string running, int revision) =>
        "the " + (running.StartsWith(Candidates.FamilyRecords.LivePrefix, StringComparison.Ordinal) ? running[Candidates.FamilyRecords.LivePrefix.Length..] : running)
        + ", as revision " + revision.ToString(CultureInfo.InvariantCulture) + " ran";

    // The first night a press waiting now is applied on: the session after the newest night stored, the next one that runs
    // being named again each time the page is read.
    public static DateOnly NextNight(DateOnly newest)
    {
        var day = newest.AddDays(1);

        while (!ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    public static string Waiting(int revision, DateOnly night) =>
        $"Waiting: becomes revision {revision.ToString(CultureInfo.InvariantCulture)} from the night of {Day(night)}.";

    public static string Applied(int revision, int replaced, DateOnly night) =>
        $"Promoted: revision {revision.ToString(CultureInfo.InvariantCulture)} from the night of {Day(night)}, revision {replaced.ToString(CultureInfo.InvariantCulture)} running on as a variant, each carrying its own record.";

    public static string UndoWaiting(DateOnly night) =>
        $"Undone before it ran: nothing changed, and the night of {Day(night)} lists as before.";

    public static string UndoApplied(DateOnly night) =>
        $"Waiting: the promotion is undone from the night of {Day(night)}, each record carried back.";

    public static string Kept(int revision) =>
        $"Kept revision {revision.ToString(CultureInfo.InvariantCulture)}: your word is recorded and nothing changes.";

    // A press's state: its answer where the night wrote one, and the press undoing it where one stands.
    public static IReadOnlyList<PromotionState> States(IReadOnlyList<PromotionPressRow> presses, IReadOnlyList<PromotionSwapRow> answers)
    {
        var byPress = answers.ToDictionary(answer => answer.PressId);

        return
        [
            .. presses.Where(press => press.Press == Promote).Select(press => new PromotionState(
                press,
                byPress.GetValueOrDefault(press.Id),
                presses.Where(one => one.Press == Undo && one.Undoes == press.Id).OrderBy(one => one.Id).FirstOrDefault())),
        ];
    }

    // Every carry the swaps of an index applied by the night given hold, in the order they were written, less those of a
    // promotion an Undo applied by that night swapped back, whose own carries say where each record went back to.
    public static IReadOnlyList<PromotionCarry> CarriesOf(IReadOnlyList<PromotionPressRow> presses, IReadOnlyList<PromotionSwapRow> answers, string index, DateOnly night)
    {
        var applied = answers.Where(answer => answer.Applied && answer.Index == index && answer.Night <= night).ToDictionary(answer => answer.PressId);
        var undone = presses
            .Where(press => press.Press == Undo && press.Undoes is not null && applied.ContainsKey(press.Id))
            .Select(press => press.Undoes!.Value)
            .ToHashSet();

        return [.. applied.Values.Where(answer => !undone.Contains(answer.PressId)).OrderBy(answer => answer.SwappedAt).ThenBy(answer => answer.PressId).SelectMany(answer => answer.Carries)];
    }

    public static string WriteCarries(IReadOnlyList<PromotionCarry> carries) =>
        JsonSerializer.Serialize(carries.Select(carry => new { to = carry.To, from = carry.From, since = Stamp(carry.Since), until = Stamp(carry.Until) }));

    public static IReadOnlyList<PromotionCarry> ReadCarries(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        using var read = JsonDocument.Parse(text);

        return
        [
            .. read.RootElement.EnumerateArray().Select(one => new PromotionCarry(
                one.GetProperty("to").GetString()!,
                one.GetProperty("from").GetString()!,
                DateOnly.ParseExact(one.GetProperty("since").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(one.GetProperty("until").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
        ];
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
