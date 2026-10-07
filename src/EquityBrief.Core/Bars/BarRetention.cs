namespace EquityBrief.Core.Bars;

// The year of bars the store keeps: a night keeps every session from the date a year before it, which is the limits
// table's figure and the window the level builder reads. Expressed as a year rather than as a session count because
// the boundary is a date and a session count would drift with holidays, so the year holds 252, 251 or 250 closes as
// the calendar falls: 251 on a night whose date a year back was no session.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
public static class BarRetention
{
    public const int Years = 1;

    // The sessions the store's year holds to a night on the exchange's calendar, the night among them; none where the
    // year reaches outside the calendar's table.
    public static int? SessionsTo(DateOnly night)
    {
        var oldest = night.AddYears(-Years);

        if (oldest < ExchangeClosures.CoveredFrom || night > ExchangeClosures.CoveredThrough)
        {
            return null;
        }

        var held = 0;

        for (var day = oldest; day <= night; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                held++;
            }
        }

        return held;
    }
}
