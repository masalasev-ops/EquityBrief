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
// session's close on the count's split basis.
//
// The provider restates every count to the split basis of the day it is asked, so a count filed in 2019 is stated
// in today's shares, forty times NVDA's count then. The close it is multiplied by is the session's unadjusted close
// divided by every plain split between the session and that day, which puts the two on one basis; a spin-off the
// provider files as a split divides nothing, since it changed no shares. The close the bars store adjusted is not
// that close: it takes out the dividends paid since as well, and would value a company paying four per cent a year
// at about a quarter less over 2019's sessions than one paying none. A split the provider did not restate its counts
// for reads the company at the split's fraction before it, which is the residue of the provider's counts.
// see: A company's value on a session is the newest share count filed before it times the session's close on the count's split basis
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

    // The company's value on a session, from the session's unadjusted close and never its adjusted one, none where no
    // count was filed before it.
    public static decimal? On(SessionClose close, IEnumerable<FiledCount> counts, IEnumerable<FiledSplit> splits) =>
        CountOn(close.Session, counts) is { } count
            ? count.Shares * OnBasis(close.Session, close.RawClose, count.Basis, splits)
            : null;
}
