namespace EquityBrief.Core.Levels;

// How many times a band turned the price and the last session it did, read off the band's members: each swing and each
// visit it holds is a session the price reached the band and turned away, dated by the session it occurred on, while an
// average, a retracement and a shelf are prices the band holds rather than turns, recomputed every night with no date of
// their own. It is what the ladder figure states beside each band as its touches and its last touch.
// see: A member's date is the session its evidence occurred on, and a figure recomputed nightly has none of its own
public static class BandTurns
{
    // The member kinds that are turns, as the level store names them.
    public static IReadOnlyList<string> TurnKinds { get; } = ["swing high", "swing low", "touch"];

    public static (int Count, DateOnly? Last) Of(IEnumerable<(string Kind, DateOnly Date)> members)
    {
        DateOnly[] turns = [.. members.Where(member => TurnKinds.Contains(member.Kind, StringComparer.Ordinal)).Select(member => member.Date)];

        return (turns.Length, turns.Length == 0 ? null : turns.Max());
    }
}
