using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Returns;

// The unit a record is judged over: consecutive exchange sessions, sixty-three to a block, keyed on
// the session a setup was listed on.
//
// Setups listed less than a block apart resolve over shared bars and are not independent, so a
// record counted setup by setup would read one stretch of the market as many results. A block
// counts once it holds a setup whose whole outcome window has closed, and a setup's window is the
// setup cap counted from its listing session, so it has closed once that many sessions have traded
// after it and not before, whatever it resolved on.
// see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
// see: A look counts only the setups whose whole outcome window has closed
public static class Blocks
{
    // Section 17's block length, in exchange sessions.
    public const int Sessions = 63;

    // Section 17's block floor: fewer non-empty blocks than this and nothing is compared.
    public const int Floor = 8;

    // The block a session falls in, counted in exchange sessions from the first session of the
    // record, which is block 0's first session.
    public static int Of(DateOnly first, DateOnly session) => Position(first, session) / Sessions;

    // Whether a setup listed on a session has had its whole outcome window by a night.
    public static bool Closed(DateOnly listed, DateOnly asOf) =>
        SessionsAfter(listed, asOf) >= ForwardReturnSeries.SetupSessionCap;

    // Whether every session a block holds has had its whole outcome window by a night.
    //
    // A look reads whole blocks and not the closed setups inside an unfinished one, which is
    // stricter than the cut-off the decision states and is what keeps a block's sum from moving
    // after the look that read it: a sum that grew between two looks would make the earlier look's
    // arrangement a different one, and the boundary the earlier look was read against was found
    // over the arrangement it had.
    // see: A look counts only the setups whose whole outcome window has closed
    public static bool Complete(DateOnly first, int block, DateOnly asOf) =>
        Position(first, asOf) - ((Sessions * (block + 1)) - 1) >= ForwardReturnSeries.SetupSessionCap;

    // The sessions from the first to a session, the first being 0.
    static int Position(DateOnly first, DateOnly session) =>
        session <= first ? 0 : ExchangeClosures.SessionsBetween(first, session).Count + (ExchangeClosures.IsSession(session) ? 1 : 0);

    // The sessions that have traded after a listing, up to and including a night.
    static int SessionsAfter(DateOnly listed, DateOnly asOf) =>
        asOf <= listed ? 0 : ExchangeClosures.SessionsBetween(listed, asOf).Count + (ExchangeClosures.IsSession(asOf) ? 1 : 0);
}
