using System.Globalization;

namespace EquityBrief.Core.Cards;

// A share drawn in per cent to one place beside the floor it is read against: as written where it is at or above the
// floor, and where it sits under the floor never at or above it, a share that would round up to the floor drawn a tenth
// under it, so a breadth of 0.4497 under a floor of 0.45 reads 44.9 beside "below its floor of 45%" and not 45.0.
public static class FloorShare
{
    public static string Percent(double share, double floor)
    {
        var written = (share * 100).ToString("0.0", CultureInfo.InvariantCulture);

        return share < floor && double.Parse(written, CultureInfo.InvariantCulture) >= floor * 100
            ? ((floor * 100) - 0.1).ToString("0.0", CultureInfo.InvariantCulture)
            : written;
    }
}
