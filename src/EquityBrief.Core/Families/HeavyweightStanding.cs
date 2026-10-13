namespace EquityBrief.Core.Families;

// A heavyweight holding's standing tonight, read off tonight's closes: its return over the look-back, its sector's,
// its lead over it, how far its close stands from its 200-day average as a fraction of it, and the pill's word.
public sealed record HeavyweightStanding(double? Return, double? SectorReturn, double? Lead, double? OverAverage, string Word);

// A heavyweight holding's standing tonight and a book's return since its last rebalance, worked out when a page is read
// from tonight's closes by the rule's own functions, and marked as information only: no rule reads it and it sells
// nothing, the book selling only at a rebalance or where a stock leaves the index.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
public static class HeavyweightStandings
{
    public const string StillLeading = "still leading";

    public const string LostItsLead = "lost its lead";

    public const string BelowItsAverage = "below its 200-day";

    public const string NotRead = "not read tonight";

    public const string NotForThisDesign = "not read for this design";

    // A return over a span: the close at its end over the close at its start, less one; none where either is not held or
    // the start is nothing.
    public static double? Return(double? end, double? start) =>
        end is { } last && start is { } first && first > 0 ? (last / first) - 1.0 : null;

    // The mean of the members' returns a sector's return is read as where a setting reads its members, a member holding
    // none left out, as the rule reads it; none where no member holds one.
    // see: A sector's return is the mean of its members' own returns over the look-back
    public static double? MembersMean(IEnumerable<double?> returns)
    {
        var held = returns.OfType<double>().ToArray();

        return held.Length == 0 ? null : held.Average();
    }

    // A holding's standing tonight: below its 200-day average where the rule's own exit reads its close as under it,
    // otherwise still leading where its return over the look-back beats its sector's above nothing and lost its lead where
    // it does not, and not read where tonight's close, the close a look-back before or its sector's return is not held.
    public static HeavyweightStanding Of(double? close, double? back, double? average200, double? sectorReturn)
    {
        var own = Return(close, back);
        double? lead = own is { } mine && sectorReturn is { } sector ? mine - sector : null;
        double? over = close is { } shut && average200 is { } average && average > 0 ? (shut / average) - 1.0 : null;

        var word = close is { } price && HeavyweightRule.Broken(price, average200)
            ? BelowItsAverage
            : lead switch
            {
                > 0 => StillLeading,
                { } => LostItsLead,
                null => NotRead,
            };

        return new HeavyweightStanding(own, sectorReturn, lead, over, word);
    }

    // An equal share of each return since the last rebalance, every holding or fund weighted alike, a return not held
    // left out; none where none is held.
    public static double? EqualWeighted(IEnumerable<(double? Now, double? Then)> pairs) =>
        MembersMean(pairs.Select(pair => Return(pair.Now, pair.Then)));
}
