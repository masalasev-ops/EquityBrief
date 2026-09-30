using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Filter;

// One listing a rule's list made, as the open trade rule reads it: the stock, the night, and what became of
// the trade its night's plan states on its capped horizon, with whether an outcome row is stored at all.
public sealed record OpenTradeListing(
    string Ticker,
    DateOnly Night,
    bool OutcomeStored,
    string? Outcome,
    DateOnly? ResolvedOn,
    int CapSessions);

// One open trade per stock on each rule's list. A trade is the one listed, bought at that night's close with
// that night's stop and target, and it is open until a close reaches its target, falls through its stop or
// its sessions run out. A stock whose trade is open is not listed again, whatever the setup, and is free again
// from the night after the trade ends, a stop's night included. Each rule keeps its own open trades, so the
// live list's never block a candidate's and a candidate's never block the live list's.
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
public static class OpenTrades
{
    // The exclusion the filter writes for a stock whose trade is open, once the rule reaches it, naming the
    // night the open trade was listed on, and whether an exclusion is one of them.
    public const string ExclusionPrefix = "an open trade from ";

    public static string Exclusion(DateOnly listed) => ExclusionPrefix + listed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsExclusion(string exclusion) => exclusion.StartsWith(ExclusionPrefix, StringComparison.Ordinal);

    // Whether a trade listed on one night is still open on a later one. A trade is open while its outcome
    // is undecided, and on the session it was decided on, since a stop or a target reached at that close
    // frees the stock only from the night after. A trade whose outcome row is missing is read as open until
    // its cap's sessions have passed, because a row that is not there says nothing either way and a stock
    // listed twice inside the cap on a missing row is the case the rule exists for.
    public static bool IsOpenOn(DateOnly listed, DateOnly night, bool outcomeStored, string? outcome, DateOnly? resolvedOn, int capSessions)
    {
        if (night <= listed)
        {
            return false;
        }

        if (!outcomeStored)
        {
            return ExchangeClosures.SessionsUntil(listed, night) is not { } held || held <= capSessions;
        }

        if (outcome is null)
        {
            return true;
        }

        return resolvedOn is { } ended && ended >= night;
    }

    // The walk over one rule's listings: for each stock in night order the first listing is the kept trade,
    // a later listing is a repeat of the kept trade while that trade is open on the later night, and otherwise
    // becomes the kept trade. A repeat never becomes the trade that blocks the next listing, because a repeat
    // is not a trade the rule made. The answer is, for each listing, the night of the kept trade it repeats,
    // and null where it is itself the kept trade.
    public static IReadOnlyDictionary<(string Ticker, DateOnly Night), DateOnly?> Walk(IEnumerable<OpenTradeListing> listings)
    {
        var verdicts = new Dictionary<(string Ticker, DateOnly Night), DateOnly?>();

        foreach (var stock in listings.GroupBy(listing => listing.Ticker, StringComparer.Ordinal))
        {
            OpenTradeListing? kept = null;

            foreach (var listing in stock.OrderBy(listing => listing.Night))
            {
                if (kept is { } held && IsOpenOn(held.Night, listing.Night, held.OutcomeStored, held.Outcome, held.ResolvedOn, held.CapSessions))
                {
                    verdicts[(listing.Ticker, listing.Night)] = held.Night;

                    continue;
                }

                kept = listing;
                verdicts[(listing.Ticker, listing.Night)] = null;
            }
        }

        return verdicts;
    }

    // The kept trade of a stock that is open on a night, among the listings before that night, or none: what
    // a stock the filter passes tonight is held against.
    public static OpenTradeListing? OpenOn(string ticker, DateOnly night, IEnumerable<OpenTradeListing> listings)
    {
        var before = listings.Where(listing => string.Equals(listing.Ticker, ticker, StringComparison.Ordinal) && listing.Night < night).ToArray();
        var walked = Walk(before);

        return before
            .Where(listing => walked[(listing.Ticker, listing.Night)] is null)
            .OrderByDescending(listing => listing.Night)
            .FirstOrDefault(listing => IsOpenOn(listing.Night, night, listing.OutcomeStored, listing.Outcome, listing.ResolvedOn, listing.CapSessions));
    }
}
