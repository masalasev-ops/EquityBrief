using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Bars;

// Why a member's year is still not right after the provider answered, as the corporate action check stores it and as the
// pages read it back. A reason this does not read is a request that failed, and only that is said to have failed.
// see: A suspect name's line says what the provider's answer held, and says a download failed only where one did
public static partial class SuspectReason
{
    public static string Behind(DateOnly newest) =>
        FormattableString.Invariant($"its newest bar is {Day(newest)}, behind the night, and the provider holds no later session");

    public static string Copied(IReadOnlyList<DateOnly> sessions) =>
        FormattableString.Invariant($"its stored year holds {sessions.Count} copied candle(s), no volume at the close before, the newest on {Day(sessions.Max())}");

    public static string PastTheLimit(int limit, string why) =>
        FormattableString.Invariant($"past the {limit} members a night asks for: {why}");

    public static SuspectFault? Read(string? reason)
    {
        var limit = reason is null ? Match.Empty : Limit().Match(reason);
        var why = limit.Success ? reason![limit.Length..] : reason;
        int? asked = limit.Success ? int.Parse(limit.Groups["limit"].Value, CultureInfo.InvariantCulture) : null;

        if (why is not null && BehindWords().Match(why) is { Success: true } behind)
        {
            return new SuspectFault(false, DateOnly.ParseExact(behind.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), asked);
        }

        return why is not null && CopiedWords().Match(why) is { Success: true } copied
            ? new SuspectFault(true, DateOnly.ParseExact(copied.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), asked)
            : null;
    }

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^past the (?<limit>[0-9]+) members a night asks for: ")]
    private static partial Regex Limit();

    [GeneratedRegex(@"^its newest bar is (?<day>[0-9]{4}-[0-9]{2}-[0-9]{2}), behind the night")]
    private static partial Regex BehindWords();

    [GeneratedRegex(@"^its stored year holds [0-9]+ copied candle\(s\).*the newest on (?<day>[0-9]{4}-[0-9]{2}-[0-9]{2})$")]
    private static partial Regex CopiedWords();
}

// A fault in the provider's answer rather than in the asking: copied candles or an answer ending short, the newest day it
// names, and the limit it fell past where it was not asked for that night.
public sealed record SuspectFault(bool Copied, DateOnly Newest, int? PastLimit);
