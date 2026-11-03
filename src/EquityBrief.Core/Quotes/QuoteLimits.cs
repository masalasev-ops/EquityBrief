namespace EquityBrief.Core.Quotes;

// How often a name page's delayed quote is asked, held by the page's script, the read surface and the quote job alike.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
public static class QuoteLimits
{
    // The most quotes asked in one session.
    // see: The day's live quotes are capped at 500 asks on the operator's word
    public const int DailyCap = 500;

    // The fewest minutes between two asks of one name's quote.
    public const int IntervalMinutes = 5;
}
