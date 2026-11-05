using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Bars;

// A member the night's day file carried nothing for, asked for with a request of its own at the end of the fetch and once
// more in the first off-peak run after the night, at most `Most` asks a night across the two.
// see: A member missing from the night's day file is asked for with its own request at the end of the fetch and once more in the off-peak run, at most 20 asks a night
public static class LateBars
{
    public const int Most = 20;

    // What the store's `source` says of a bar one of these asks wrote.
    public const string Source = "late";

    public const string AtTheFetch = "fetch";
    public const string OffPeak = "off-peak";

    public const string Arrived = "arrived";
    public const string StillMissing = "still missing";
    public const string Refused = "refused";
    public const string Failed = "failed";
    public const string PastTheLimit = "past the limit";

    // How far back an ask reaches, so its answer holds sessions the store holds to check it against.
    public const int ReachDays = 10;

    public static (ProviderBar? Bar, string Outcome, string Why) Judge(
        DateOnly session,
        IReadOnlyList<ProviderBar> answer,
        IReadOnlyDictionary<DateOnly, (decimal Close, long Volume)> stored)
    {
        var day = Day(session);
        var ordered = answer.OrderBy(bar => bar.SessionDate).ToArray();

        if (ordered.FirstOrDefault(bar => bar.SessionDate == session) is not { } bar)
        {
            return (null, StillMissing, ordered.Length == 0
                ? $"the provider's answer held nothing to {day}"
                : $"the provider's answer ended on {Day(ordered[^1].SessionDate)}, before {day}");
        }

        if (bar.Low <= 0 || bar.Low > bar.Open || bar.Low > bar.Close || bar.High < bar.Open || bar.High < bar.Close)
        {
            return (null, Refused, FormattableString.Invariant($"its bar for {day}, low {bar.Low}, open {bar.Open}, high {bar.High}, close {bar.Close}, could not have traded"));
        }

        var before = ordered.Where(one => one.SessionDate < session).Select(one => (ProviderBar?)one).LastOrDefault();

        if (before is not null && CopiedCandle.Is(bar.Volume, bar.Open, bar.High, bar.Low, bar.Close, before.Close))
        {
            return (null, StillMissing, $"the provider's bar for {day} is a copy of the session before, with no trades");
        }

        if (stored.Count > 0)
        {
            var shared = ordered.Where(one => one.SessionDate < session && stored.ContainsKey(one.SessionDate)).ToArray();

            if (shared.Length == 0)
            {
                return (null, Refused, $"the answer held no session the store holds to check it against before {day}");
            }

            var newest = shared[^1];
            var held = stored[newest.SessionDate].Close;

            if (held <= 0 || Math.Abs((newest.Close / held) - 1) > RefetchAnswer.ApartLimit)
            {
                return (null, Refused, FormattableString.Invariant($"the answer's close for {Day(newest.SessionDate)}, {newest.Close}, stands over a quarter from the stored {held}, so it is not this stock's series as stored"));
            }

            if (RefetchAnswer.Refused(stored, [.. ordered.Select(one => (one.SessionDate, one.Close))]) is { } disagreement)
            {
                return (null, Refused, $"the answer's move on {Day(disagreement.Session)} is not in the stored closes, so it is not this stock's series as stored");
            }
        }

        return (bar, Arrived, $"the provider held the bar for {day}");
    }

    // The JSON the store keeps for the rules a late bar's readings pass, and back.
    public static string QualifiedText(IReadOnlyList<string> rules) => JsonSerializer.Serialize(rules);

    public static IReadOnlyList<string>? Qualified(string? text) =>
        text is null ? null : JsonSerializer.Deserialize<string[]>(text) ?? [];

    public static string WouldHaveQualified(IReadOnlyList<string> rules) =>
        $"would have qualified under {string.Join(" and ", rules)} had its bar arrived before the night";

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
