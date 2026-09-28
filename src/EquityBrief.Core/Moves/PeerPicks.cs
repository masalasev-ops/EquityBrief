using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Moves;

// One member of a name's group its peers table draws: whether it shares the name's industry, how closely
// its daily moves followed the name's, and over how many daily returns the two both hold. The likeness is
// absent where they share fewer than the floor, and the count says so.
public sealed record PeerPick(string Ticker, bool SameIndustry, double? Likeness, int Sessions);

// Which members of a name's group its peers table draws, and in what order: the members sharing the name's
// industry first, then the rest of the group, each part by how closely the member's daily moves followed
// the name's, ten at most. A pure function of the membership and the stored closes, as the move arithmetic
// is, so the annotator reads and writes and this decides.
// see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
public static class PeerPicks
{
    // How many other members the table draws. Ten is the operator's: a table of every member drew a
    // median of fifteen rows and up to eighty-six beneath the moves, which lengthened the page without
    // comparing the name with anything more.
    public const int Shown = 10;

    // The fewest daily returns the two must both hold before a likeness is read. Sixty is a quarter of a
    // year of trading, the span the return beside it is taken over; over fewer, a correlation of daily
    // moves is a figure a handful of sessions can swing, so a member sharing fewer is drawn after every
    // member holding one, with the count rather than a figure over fewer.
    public const int FewestSessions = 60;

    // The members the table draws, in the order it draws them. Within each part a member holding a
    // likeness comes before one that does not, the higher likeness first, and the ticker settles a tie.
    public static IReadOnlyList<PeerPick> Of(
        string ticker,
        Group group,
        IReadOnlyList<GroupMember> members,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>> closes)
    {
        var industry = members.FirstOrDefault(member => string.Equals(member.Ticker, ticker, StringComparison.Ordinal))?.Industry;
        var own = closes.TryGetValue(ticker, out var held) ? held : null;

        bool Shares(string peer) =>
            industry is not null
            && members.Any(member => string.Equals(member.Ticker, peer, StringComparison.Ordinal) && string.Equals(member.Industry, industry, StringComparison.Ordinal));

        return
        [
            .. group.Members
                .Select(peer =>
                {
                    var (likeness, sessions) = own is not null && closes.TryGetValue(peer, out var theirs) ? Likeness(own, theirs) : (null, 0);

                    return new PeerPick(peer, Shares(peer), likeness, sessions);
                })
                .OrderByDescending(pick => pick.SameIndustry)
                .ThenByDescending(pick => pick.Likeness.HasValue)
                .ThenByDescending(pick => pick.Likeness ?? 0)
                .ThenBy(pick => pick.Ticker, StringComparer.Ordinal)
                .Take(Shown),
        ];
    }

    // The correlation of two names' daily returns, each session's close against the close of the session
    // before it, read along the first name's own stored sessions wherever the second holds a close on both.
    // A return is a ratio of prices, so it crosses into a statistic here, where the crossing is named. A
    // pair sharing fewer returns than the floor, or one whose returns never moved, has no likeness.
    public static (double? Likeness, int Sessions) Likeness(IReadOnlyDictionary<DateOnly, decimal> own, IReadOnlyDictionary<DateOnly, decimal> other)
    {
        var days = own.Keys.Order().ToArray();
        var ours = new List<double>();
        var theirs = new List<double>();

        for (var at = 1; at < days.Length; at++)
        {
            if (own[days[at - 1]] is > 0m and var ownFrom
                && other.TryGetValue(days[at - 1], out var otherFrom) && otherFrom > 0m
                && other.TryGetValue(days[at], out var otherTo))
            {
                ours.Add(Statistic.FromRatio((own[days[at]] - ownFrom) / ownFrom));
                theirs.Add(Statistic.FromRatio((otherTo - otherFrom) / otherFrom));
            }
        }

        if (ours.Count < FewestSessions)
        {
            return (null, ours.Count);
        }

        var ourMean = ours.Average();
        var theirMean = theirs.Average();
        var together = 0.0;
        var ourSpread = 0.0;
        var theirSpread = 0.0;

        for (var at = 0; at < ours.Count; at++)
        {
            together += (ours[at] - ourMean) * (theirs[at] - theirMean);
            ourSpread += (ours[at] - ourMean) * (ours[at] - ourMean);
            theirSpread += (theirs[at] - theirMean) * (theirs[at] - theirMean);
        }

        return ourSpread > 0 && theirSpread > 0
            ? (together / Math.Sqrt(ourSpread * theirSpread), ours.Count)
            : (null, ours.Count);
    }
}
