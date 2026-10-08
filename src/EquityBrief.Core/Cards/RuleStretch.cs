namespace EquityBrief.Core.Cards;

// What a rule's empty stretch reads on a night: the nights it has listed nothing for, counting the night where it
// listed nothing; the mark, the stretch past which only a twentieth of its past empty nights had already gone; whether
// the night's stretch is past the mark; and the completed stretches and the sessions the mark was counted over.
public sealed record StretchReading(int Stretch, int? Mark, bool Flagged, int CompletedStretches, int Sessions);

// A rule's empty stretch and the mark it is read against. The mark is counted night by night and never stretch by
// stretch: each past empty night contributes the stretch it had reached as of that night, and the mark is the smallest
// stretch at least 95 per cent of those nights had not gone past. Counted a stretch at a time a mark flags about 18 in
// 100 empty nights of a rule whose stretches are what they are, and night by night about 5 in 100, which is what a
// 95 per cent mark is meant to say. No mark is drawn under 30 completed stretches, a stretch completing on the night a
// pick ends it, or under 504 sessions evaluated, two years of them.
// see: A card's stretch line counts its mark over past empty nights and draws none under 30 completed stretches
public static class RuleStretch
{
    public const int CompletedStretchesFloor = 30;

    public const int SessionsFloor = 504;

    // The share of past empty nights a flagged stretch is longer than.
    public const double Share = 0.95;

    // The reading over a rule's listed counts on each night it was evaluated, oldest first and the night's last; a rule
    // evaluated on no night reads a stretch of nothing with no mark.
    public static StretchReading Read(IReadOnlyList<int> listed) =>
        listed.Count == 0 ? new StretchReading(0, null, false, 0, 0) : ReadEach(listed)[^1];

    // The reading on each night in turn, each over the nights to it, so a replayed history carries the mark each night
    // read. The past empty nights are counted by the stretch each had reached, and the mark is the smallest stretch
    // the share of them had not gone past, the nearest rank counted from one.
    public static IReadOnlyList<StretchReading> ReadEach(IReadOnlyList<int> listed)
    {
        var readings = new StretchReading[listed.Count];
        var reachedBy = new int[listed.Count + 2];
        var (completed, stretch, pastEmpty) = (0, 0, 0);

        for (var at = 0; at < listed.Count; at++)
        {
            if (listed[at] > 0)
            {
                completed += stretch > 0 ? 1 : 0;
                stretch = 0;
            }
            else
            {
                stretch++;
            }

            var mark = completed >= CompletedStretchesFloor && at + 1 >= SessionsFloor && pastEmpty > 0
                ? Level(reachedBy, pastEmpty)
                : (int?)null;

            readings[at] = new StretchReading(stretch, mark, mark is { } level && stretch > level, completed, at + 1);

            // Tonight joins the past empty nights for the nights after it, never for itself.
            if (listed[at] == 0)
            {
                reachedBy[stretch]++;
                pastEmpty++;
            }
        }

        return readings;
    }

    static int Level(int[] reachedBy, int pastEmpty)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(Share * pastEmpty));
        var counted = 0;

        for (var stretch = 1; stretch < reachedBy.Length; stretch++)
        {
            counted += reachedBy[stretch];

            if (counted >= rank)
            {
                return stretch;
            }
        }

        return reachedBy.Length - 1;
    }
}
