using EquityBrief.Core.Cards;

namespace EquityBrief.Core.Paper;

// From the operator's answers of 2026-11-28, item 4: each open trailing trade's stop as each night raised it, stored by the
// night's order changes, so a sale is read against the stop its close fell under and one above its first stop says it was sold
// on its trailing stop.
// see: A trailing trade's stop is stored as each night raises it, and a sale above its first stop is sold on its trailing stop
public sealed record TrailStopRow(string Family, string Ticker, DateOnly ListedOn, DateOnly Session, decimal FirstStop, decimal Stop);

public static class TrailStops
{
    // The stop a trailing trade's close was held to on a session: the one the night before it stored, none where no night did.
    public static decimal? Before(IEnumerable<TrailStopRow>? rows, string? family, string ticker, DateOnly listedOn, DateOnly session)
    {
        TrailStopRow? found = null;

        foreach (var row in rows ?? [])
        {
            if (row.Family == family && row.Ticker == ticker && row.ListedOn == listedOn && row.Session < session && (found is null || row.Session > found.Session))
            {
                found = row;
            }
        }

        return found?.Stop;
    }

    // Each of an account's trailing trades open on the night, with its stop as the night raised it.
    public static IReadOnlyList<TrailStopRow> Of(string family, IEnumerable<PaperTrade> trades, DateOnly night, IReadOnlyList<DateOnly> sessions, Func<string, DateOnly, decimal?> close)
    {
        var rows = new List<TrailStopRow>();

        foreach (var trade in trades)
        {
            if (trade.Trail is null || trade.Stop is not { } first || trade.Bought > night || trade.Ended is { } end && end <= night)
            {
                continue;
            }

            if (PaperAccount.RaisedStop(trade, night, sessions, close) is { } raised)
            {
                rows.Add(new TrailStopRow(family, trade.Ticker, trade.ListedOn, night, first, raised));
            }
        }

        return rows;
    }
}
