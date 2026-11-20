using System.Globalization;

namespace EquityBrief.Core.Judge;

// What a replay of the AI judge would run: the nights it replays, the minutes a night measured over the judge's live runs,
// the hours of the local model in all, and why it cannot start where it cannot.
public sealed record JudgeReplayPlan(IReadOnlyList<DateOnly> Nights, double? MinutesANight, double? Hours, string? Refused);

// The AI judge's replayed nights: only nights after the local model's knowledge cutoff whose facts the store can build as
// they stood, and none the judge has a run of already. Its count, its measured time a night and its total hours are printed
// before it starts, the total the product of the other two, and it starts only on the operator's word; a night on or before
// the cutoff is refused, since the model may remember what came after it. Every run it writes carries the replay's prefix,
// so every surface reads it apart from the live record.
// see: The AI judge's replays print their hours before they start on the operator's word, a new version's over the same 60 nights
// see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
public static class JudgeReplay
{
    public const string Prefix = "judge-replay-";

    public const string NoWord = "the replay starts only on the operator's word, given with --go";

    public const string NotMeasured = "no live night of the judge is measured yet, so its hours cannot be stated";

    public const string NothingLeft = "no night after the model's cutoff is left to replay";

    public static bool OnOrBeforeCutoff(DateOnly night, DateOnly cutoff) => night <= cutoff;

    // The nights holding facts the store can build, after the cutoff and with no run of the judge, oldest first, and the
    // minutes a night as the judge's live runs measured them.
    public static JudgeReplayPlan Plan(IReadOnlyList<DateOnly> buildable, IReadOnlyCollection<DateOnly> run, DateOnly cutoff, IReadOnlyList<double> liveMinutes)
    {
        DateOnly[] nights = [.. buildable.Where(night => !OnOrBeforeCutoff(night, cutoff) && !run.Contains(night)).Distinct().Order()];
        double? minutes = liveMinutes.Count == 0 ? null : liveMinutes.Average();
        double? hours = minutes is { } each ? each * nights.Length / 60.0 : null;

        return new JudgeReplayPlan(nights, minutes, hours, minutes is null ? NotMeasured : nights.Length == 0 ? NothingLeft : null);
    }

    // The plan in words, as the replay prints it before anything starts.
    public static string Words(JudgeReplayPlan plan) =>
        plan.Nights.Count == 0
            ? "no night to replay"
            : string.Create(CultureInfo.InvariantCulture, $"{plan.Nights.Count} night{(plan.Nights.Count == 1 ? string.Empty : "s")} from {plan.Nights[0]:yyyy-MM-dd} to {plan.Nights[^1]:yyyy-MM-dd}")
                + (plan.MinutesANight is { } minutes && plan.Hours is { } hours
                    ? string.Create(CultureInfo.InvariantCulture, $", {minutes:0.#} minutes a night as the judge's live runs measured them, {hours:0.##} hours of the local model in all")
                    : ", its hours not stated");

    // Where a night's facts built again differ from those stored for it: each member stored and not built, built and not
    // stored, or whose short row or full sheet differs, by name.
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, (string Row, string Sheet)> stored, IReadOnlyDictionary<string, (string Row, string Sheet)> built)
    {
        var differences = new List<string>();

        foreach (var ticker in stored.Keys.Union(built.Keys).Order(StringComparer.Ordinal))
        {
            if (!built.TryGetValue(ticker, out var again))
            {
                differences.Add($"{ticker} is stored and not built again");
            }
            else if (!stored.TryGetValue(ticker, out var held))
            {
                differences.Add($"{ticker} is built again and not stored");
            }
            else
            {
                if (held.Row != again.Row)
                {
                    differences.Add($"{ticker}'s short row differs");
                }

                if (held.Sheet != again.Sheet)
                {
                    differences.Add($"{ticker}'s full sheet differs");
                }
            }
        }

        return differences;
    }
}
