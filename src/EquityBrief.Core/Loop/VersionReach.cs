namespace EquityBrief.Core.Loop;

// How a version's settings reach against its live rule's, read setting by setting: whether it can only take a subset of the
// live rule's trades, can take trades the live rule cannot, takes the same picks and sells them another way, takes the same
// picks in another order, or changes a setting this table does not know the direction of.
// see: A version is marked as narrowing or widening its live rule by the direction of each setting it changes
public enum VersionReachKind
{
    Narrows,
    Widens,
    SoldDifferently,
    Reordered,
    Unread,
}

public static class VersionReach
{
    public const string NarrowsWords = "narrows the live rule";

    public const string WidensWords = "widens it";

    public const string SoldDifferentlyWords = "the same picks, sold differently";

    public const string ReorderedWords = "the same picks, in another order";

    public const string UnreadWords = "changes a setting whose reach is not read here";

    // The kinds a setting is read as. A floor is a least the stock or the market must reach, a check turned on as 1 and off
    // as 0 among them: a higher value narrows. A ceiling is a most it may hold: a higher value widens. A cap counts the
    // picks or companies taken, 0 taking every one. An exit or a plan's sale moves how a trade sells and never which; an
    // order moves only the order picks are taken in. Another kind changes what is read, so it can take trades the live rule
    // cannot.
    enum Kind
    {
        Floor,
        Ceiling,
        Cap,
        Exit,
        Order,
        Other,
    }

    // Each setting's kind, by its name, and by its name within a family where the family reads it otherwise.
    static readonly IReadOnlyDictionary<string, Kind> Kinds = new Dictionary<string, Kind>(StringComparer.Ordinal)
    {
        // A least the stock or the market must reach, or a check that is on at 1 and off at 0.
        ["volumeMultiple"] = Kind.Floor,
        ["highSessions"] = Kind.Floor,
        ["strength"] = Kind.Floor,
        ["strengthFloor"] = Kind.Floor,
        ["reactionMoves"] = Kind.Floor,
        ["rewardToRisk"] = Kind.Floor,
        ["rewardToRiskFloor"] = Kind.Floor,
        ["breadthFloor"] = Kind.Floor,
        ["buy"] = Kind.Floor,
        ["floor"] = Kind.Floor,
        ["depthLow"] = Kind.Floor,
        ["stopLow"] = Kind.Floor,
        ["earningsWindowSessions"] = Kind.Floor,
        ["skipDeteriorating"] = Kind.Floor,
        ["filedState"] = Kind.Floor,
        ["raisedEstimates"] = Kind.Floor,
        ["readingVersion"] = Kind.Floor,
        ["indexAverageSessions"] = Kind.Floor,
        ["vixLookbackSessions"] = Kind.Floor,
        ["flag"] = Kind.Floor,
        ["rise"] = Kind.Floor,
        ["faster"] = Kind.Floor,
        ["quality"] = Kind.Floor,
        ["highBeta"] = Kind.Floor,
        ["market"] = Kind.Floor,
        ["marketGate"] = Kind.Floor,
        ["revenueGrew"] = Kind.Floor,
        ["peers"] = Kind.Floor,
        ["lowestClose"] = Kind.Floor,
        ["industryFall"] = Kind.Floor,
        ["fundOverSpy"] = Kind.Floor,
        ["creditAverage"] = Kind.Floor,
        ["creditChange"] = Kind.Floor,
        ["floors"] = Kind.Floor,
        ["trend"] = Kind.Floor,
        ["valuation"] = Kind.Floor,
        ["leaderShareOf"] = Kind.Floor,
        ["leaderSectors"] = Kind.Floor,

        // A most it may hold.
        ["rangeCeiling"] = Kind.Ceiling,
        ["depthHigh"] = Kind.Ceiling,
        ["dryUp"] = Kind.Ceiling,
        ["dryUpCeiling"] = Kind.Ceiling,
        ["tightnessCeiling"] = Kind.Ceiling,
        ["stopHigh"] = Kind.Ceiling,
        ["arrivalSessions"] = Kind.Ceiling,
        ["windowSessions"] = Kind.Ceiling,
        ["freshness"] = Kind.Ceiling,
        ["book"] = Kind.Ceiling,
        ["leaders"] = Kind.Ceiling,

        // A count of the picks or the companies taken, 0 taking every one.
        ["bestOf"] = Kind.Cap,
        ["largest"] = Kind.Cap,

        // How a trade sells, never which it takes.
        ["targetRiskMultiple"] = Kind.Exit,
        ["targetMultiple"] = Kind.Exit,
        ["stopMoves"] = Kind.Exit,
        ["stopFloorMoves"] = Kind.Exit,
        ["stopFloor"] = Kind.Exit,
        ["stop"] = Kind.Exit,
        ["limit"] = Kind.Exit,
        ["sell"] = Kind.Exit,
        ["exit"] = Kind.Exit,
        ["hold"] = Kind.Exit,
        ["sale"] = Kind.Exit,
        ["soldOnLeading"] = Kind.Exit,
        ["soldUnderAverage"] = Kind.Exit,

        // The order picks are taken in.
        ["order_expected"] = Kind.Order,
        ["ranking"] = Kind.Order,

        // What is read changes, so trades the live rule cannot take may be taken.
        ["largeBreadth"] = Kind.Other,
        ["exposure"] = Kind.Other,
        ["trade"] = Kind.Other,
        ["band"] = Kind.Other,
        ["entry"] = Kind.Other,
        ["fundReturn"] = Kind.Other,
        ["lookBack"] = Kind.Other,
        ["twelveOne"] = Kind.Other,
        ["design"] = Kind.Other,
        ["members"] = Kind.Other,
        ["industries"] = Kind.Other,
        ["window"] = Kind.Other,
        ["weekly"] = Kind.Other,
    };

    // A setting a family reads another way than the name alone says: the short-term dip's trend between its 50-day and
    // its 200-day average, two checks neither of which holds the other.
    static readonly IReadOnlyDictionary<(string Family, string Setting), Kind> FamilyKinds = new Dictionary<(string Family, string Setting), Kind>
    {
        [("dip", "trend")] = Kind.Other,
    };

    // A version's reach against its live rule: widening where any setting it changes can take a trade the live rule cannot,
    // narrowing where every setting it changes that moves which trades it takes only removes some and at least one does,
    // sold differently where it changes only how trades sell, reordered where only their order, and unread where a setting
    // it changes is not in the table. A setting one holds and the other does not is read as 0, a check off.
    public static VersionReachKind Of(string family, IReadOnlyDictionary<string, double> live, IReadOnlyDictionary<string, double> version)
    {
        var narrows = false;
        var exit = false;
        var order = false;

        // A market check turned on or off moves its floor and which breadth it reads together, the floor carrying the change.
        var floorMoves = !live.GetValueOrDefault("breadthFloor").Equals(version.GetValueOrDefault("breadthFloor"));

        foreach (var setting in live.Keys.Union(version.Keys, StringComparer.Ordinal))
        {
            var was = live.GetValueOrDefault(setting);
            var now = version.GetValueOrDefault(setting);

            if (was.Equals(now) || (floorMoves && setting == "largeBreadth"))
            {
                continue;
            }

            if (!FamilyKinds.TryGetValue((family, setting), out var kind) && !Kinds.TryGetValue(setting, out kind))
            {
                return VersionReachKind.Unread;
            }

            switch (kind)
            {
                case Kind.Floor when now > was:
                case Kind.Ceiling when now < was:
                case Kind.Cap when Unbounded(now) < Unbounded(was):
                    narrows = true;
                    break;
                case Kind.Floor:
                case Kind.Ceiling:
                case Kind.Cap:
                case Kind.Other:
                    return VersionReachKind.Widens;
                case Kind.Exit:
                    exit = true;
                    break;
                case Kind.Order:
                    order = true;
                    break;
            }
        }

        return narrows ? VersionReachKind.Narrows : exit ? VersionReachKind.SoldDifferently : order ? VersionReachKind.Reordered : VersionReachKind.Unread;
    }

    static double Unbounded(double cap) => cap <= 0 ? double.PositiveInfinity : cap;

    // A reach in the words a version's row draws.
    public static string Words(VersionReachKind reach) => reach switch
    {
        VersionReachKind.Narrows => NarrowsWords,
        VersionReachKind.Widens => WidensWords,
        VersionReachKind.SoldDifferently => SoldDifferentlyWords,
        VersionReachKind.Reordered => ReorderedWords,
        _ => UnreadWords,
    };
}
