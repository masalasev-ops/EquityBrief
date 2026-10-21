namespace EquityBrief.Core.Families;

// A share count as one quarterly balance sheet files it: the quarter it closes, the day it was filed, the shares
// outstanding, and the session the count was asked on, whose split basis the provider restates every count to.
public sealed record FiledCount(DateOnly PeriodEnd, DateOnly Filed, decimal Shares, DateOnly Basis);

// A split as the provider files it: the first session trading on the new basis, and the shares one block of old
// shares became, as new over old.
//
// The provider files a spin-off's and a merger's price adjustment as a split too, 1,281 for 1,000 at GE HealthCare's
// spin-off, and neither changes a company's shares. A plain split is told from them by its ratio, within a
// ten-thousandth of whole shares for one share, as two for one has, of one share for whole shares, as one for fifty
// has, or of two whole numbers each at most ten, as three for two has. Within, because the provider files CSX's
// three for one as 959,692 for 319,897.
public sealed record FiledSplit(DateOnly ExDate, decimal NewShares, decimal OldShares)
{
    // The largest whole number either side of a plain split's ratio where neither side is one.
    public const int PlainMost = 10;

    // How near a plain ratio a filed one has to be, as a share of the plain ratio.
    public const decimal PlainWithin = 0.0001m;

    public bool Plain
    {
        get
        {
            var ratio = NewShares / OldShares;
            var inverse = OldShares / NewShares;

            if (Near(ratio, decimal.Round(ratio)) || Near(inverse, decimal.Round(inverse)))
            {
                return true;
            }

            for (var made = 1m; made <= PlainMost; made++)
            {
                for (var from = 1m; from <= PlainMost; from++)
                {
                    if (Near(ratio, made / from))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    static bool Near(decimal filed, decimal plain) => plain > 0m && Math.Abs(filed - plain) <= plain * PlainWithin;
}

// A session's close as a listing's bar stores it: adjusted for every split and dividend since, and unadjusted.
public sealed record SessionClose(DateOnly Session, decimal Close, decimal RawClose);

// A company's value on a session: the share count of the newest balance sheet filed before the session, times the
// session's close, the two brought to one split basis.
//
// The provider restates every count to the split basis of the day it is asked, so a count filed in 2019 is stated
// in today's shares, forty times NVDA's count then. The close it is multiplied by is the session's unadjusted close
// divided by every plain split between the session and that day, which puts the two on one basis; a spin-off the
// provider files as a split divides nothing, since it changed no shares. A session after that day reads the count
// multiplied by every plain split after the day and on or before the session instead, so a split after the newest
// count was asked moves no value. The close the bars store adjusted is not that close: it takes out the dividends
// paid since as well, and would value a company paying four per cent a year at about a quarter less over 2019's
// sessions than one paying none. A split the provider did not restate its counts for reads the company at the split's
// fraction before it, which is the residue of the provider's counts.
// see: A company's value on a session is the newest share count filed before it times the session's close, the two on one basis by every plain split between the count's day and the session
public static class CompanyValue
{
    // The count a session reads: of the balance sheets filed before the session, the one closing the latest quarter.
    // A sheet filed on the session itself is not read, since nothing says it was filed before the close.
    public static FiledCount? CountOn(DateOnly session, IEnumerable<FiledCount> counts) =>
        counts
            .Where(count => count.Filed < session)
            .OrderByDescending(count => count.PeriodEnd)
            .ThenByDescending(count => count.Filed)
            .FirstOrDefault();

    // A session's unadjusted close on the split basis of a later session: divided by what one share became at
    // every plain split after the session and on or before that one.
    public static decimal OnBasis(DateOnly session, decimal rawClose, DateOnly basis, IEnumerable<FiledSplit> splits) =>
        splits
            .Where(split => split.Plain && split.ExDate > session && split.ExDate <= basis)
            .Aggregate(rawClose, (close, split) => close * split.OldShares / split.NewShares);

    // A member's value on the night, from the counts its newest fetch holds: the count of the latest quarter filed
    // before the night, times tonight's close brought to the fetch's split basis by the fetch's own close of its newest
    // session against that session's close as the store holds it tonight. The night keeps no history of splits, and
    // the store rescales a session's close at every split since, so the two closes of one session carry the splits
    // between the fetch and the night. None where no count was filed before the night or the session is not held.
    // see: The night values a member from its newest fetch, tonight's close brought to the count's basis by the fetch's own close
    public static decimal? Tonight(DateOnly night, decimal close, IEnumerable<FiledCount> counts, decimal basisClose, decimal? basisCloseNow) =>
        CountOn(night, counts) is { } count && basisCloseNow is { } now && now > 0m
            ? count.Shares * close * basisClose / now
            : null;

    // A count's shares on the split basis of a later session: multiplied by what one share became at every plain split
    // after the day the count was asked and on or before that session.
    public static decimal SharesOn(FiledCount count, DateOnly session, IEnumerable<FiledSplit> splits) =>
        splits
            .Where(split => split.Plain && split.ExDate > count.Basis && split.ExDate <= session)
            .Aggregate(count.Shares, (shares, split) => shares * split.NewShares / split.OldShares);

    // The company's value on a session, from the session's unadjusted close and never its adjusted one, none where no
    // count was filed before it.
    public static decimal? On(SessionClose close, IEnumerable<FiledCount> counts, IReadOnlyList<FiledSplit> splits) =>
        CountOn(close.Session, counts) is { } count
            ? SharesOn(count, close.Session, splits) * OnBasis(close.Session, close.RawClose, count.Basis, splits)
            : null;

    // The plain splits a listing's bars carry, each on the first session on its new basis: a session whose close over
    // its unadjusted close stands at a plain ratio to the session before's, as the provider restates a year at a split,
    // a dividend's step and a spin-off's reading as none. A split on a session a dividend goes ex reads as none as
    // well, its step off the plain ratio by the dividend.
    public static IReadOnlyList<FiledSplit> SplitsIn(IReadOnlyList<SessionClose> closes)
    {
        var splits = new List<FiledSplit>();

        for (var at = 1; at < closes.Count; at++)
        {
            var (before, after) = (closes[at - 1], closes[at]);

            if (before.Close <= 0m || before.RawClose <= 0m || after.Close <= 0m || after.RawClose <= 0m)
            {
                continue;
            }

            var step = after.Close / after.RawClose / (before.Close / before.RawClose);
            var split = new FiledSplit(after.Session, step, 1m);

            if (Math.Abs(step - 1m) > FiledSplit.PlainWithin && split.Plain)
            {
                splits.Add(split);
            }
        }

        return splits;
    }
}
