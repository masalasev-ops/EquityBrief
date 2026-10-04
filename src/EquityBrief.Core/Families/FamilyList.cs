namespace EquityBrief.Core.Families;

// The names one family passed on a night, in that family's own order.
public sealed record FamilyQualifiers(string Family, IReadOnlyList<string> Tickers);

// A trade a stock still holds from an earlier night: the family that listed it and the night it was listed on.
public readonly record struct HeldTrade(string Family, DateOnly Listed);

// One stock under one family on one night, as the page's list holds it: listed, with its place down the
// page and the other families it qualified under, or held back, with why.
public sealed record FamilyPick(
    string Ticker,
    string Family,
    string State,
    int? Place,
    IReadOnlyList<string> Also,
    HeldTrade? HeldBy);

// The page's list on one night, drawn from what each family passed.
//
// The families are read in the page's order and each family's names in its own. A stock with a trade still
// open from any family is listed by none. A stock already listed tonight under an earlier family is not
// listed again, and the row that lists it carries the other family's label. A family lists at most five,
// and a name past its five may still be listed by a later family it qualified under. A pure function of
// what it is handed, so the night and a test read one answer.
// see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public static class FamilyList
{
    public const string Listed = "listed";

    public const string UnderAnother = "under another";

    public const string OpenTrade = "open trade";

    public const string PastFive = "past five";

    public static IReadOnlyList<FamilyPick> Draw(
        IReadOnlyList<FamilyQualifiers> qualifying,
        IReadOnlyDictionary<string, HeldTrade> open,
        int perFamily = SetupFamilies.ListedANight)
    {
        // Every family a stock qualified under tonight, in the page's order, which a listed row's labels are read from.
        var familiesOf = qualifying
            .SelectMany(family => family.Tickers.Select(ticker => (ticker, family.Family)))
            .GroupBy(pair => pair.ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Family).ToArray(), StringComparer.Ordinal);

        var listedUnder = new Dictionary<string, string>(StringComparer.Ordinal);
        var picks = new List<FamilyPick>();
        var place = 0;

        foreach (var family in qualifying)
        {
            var taken = 0;

            foreach (var ticker in family.Tickers.Distinct(StringComparer.Ordinal))
            {
                if (open.TryGetValue(ticker, out var held))
                {
                    picks.Add(new FamilyPick(ticker, family.Family, OpenTrade, null, [], held));
                }
                else if (listedUnder.ContainsKey(ticker))
                {
                    picks.Add(new FamilyPick(ticker, family.Family, UnderAnother, null, [], null));
                }
                else if (taken >= perFamily)
                {
                    picks.Add(new FamilyPick(ticker, family.Family, PastFive, null, [], null));
                }
                else
                {
                    taken++;
                    listedUnder[ticker] = family.Family;
                    picks.Add(new FamilyPick(
                        ticker,
                        family.Family,
                        Listed,
                        ++place,
                        [.. familiesOf[ticker].Where(other => !string.Equals(other, family.Family, StringComparison.Ordinal))],
                        null));
                }
            }
        }

        return picks;
    }

    // Whether a session's list was drawn from the families, read where the listings are: the lister records
    // each session it draws, whether or not any family passed a stock on it, so a session it holds no record
    // of was listed as it was before the families, by the swing filter's passing names or by the reasons.
    public static string HasPicks(string session) => $"EXISTS (SELECT 1 FROM family_night fn WHERE fn.session_date = {session})";

    public static string IsListed(string ticker, string session) =>
        $"EXISTS (SELECT 1 FROM family_pick fp WHERE fp.session_date = {session} AND fp.ticker = {ticker} AND fp.state = '{Listed}')";

    // Whether a stock was on a session's list, for a query over the listings: listed by a family where the
    // families drew that session's list, and by the rule the query hands in where they drew none. One
    // statement of it, so every reader of the list answers alike for a night on either side of the families.
    public static string OnTheList(string ticker, string session, string otherwise) =>
        $"CASE WHEN {HasPicks(session)} THEN {IsListed(ticker, session)} ELSE ({otherwise}) END";

    // A listed stock's place down the page on a session the families drew, and nothing on any other, for
    // a query that orders by it ahead of the order the list had before them.
    public static string PlaceOn(string ticker, string session) =>
        $"(SELECT fp.place FROM family_pick fp WHERE fp.session_date = {session} AND fp.ticker = {ticker} AND fp.state = '{Listed}')";
}
