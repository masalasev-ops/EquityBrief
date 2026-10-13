using System.Globalization;
using System.Text;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// What the universe draws from 19.5 beside its rows: the state its link carries, the rows its filters, its search and
// its chips kept in their order, a tile a GICS sector, the members filing no sector, the look-back the tiles' funds are
// read over, whether the index's night is read for a member one gate short of a setup, the page sizes offered, and the
// swing filter's dry-up ceiling the dry-up is drawn against.
public sealed record UniverseView(
    UniverseState State,
    IReadOnlyList<UniverseCell> Kept,
    IReadOnlyList<SectorLine> Tiles,
    int NotFiled,
    int LookBack,
    bool ShortRead,
    IReadOnlyList<int> PageSizes,
    double? DryUpCeiling);

// Universe's guide and the corrections a page folds onto the Run page.
public sealed partial class SinglePageApp
{
    // How the universe is read, opened from its "?".
    public const string UniverseGuide =
        "Each tile is a GICS sector: its members, how many are in an uptrend where the trend is read on the index, how many tonight's lists hold, and its sector fund's return over the live sector heavyweights rule's look-back; a tile filters the table to its sector. The table is every member, nearest a level first unless a heading sorts it, each heading's press sorting on its column and the next press turning it the other way. Search finds a ticker by its opening letters or a company by any part of its name. Near support and near resistance keep a close within one typical day's move of its nearest band's edge, a close inside the band counting; one gate short keeps a member that missed one gate of a setup, its tag naming the setup and the gate. The distance picture fixes each close at its centre line, the nearest support in green to its left and the nearest resistance in orange to its right, one tick per typical day. The strength is the stock's place among the members' returns, the pullback is in typical moves, and the dry-up and the tightness carry a bar with a tick at the live rule's threshold. In the strip each column is one session, marked on an evening the stock was on the list. On a phone each row is a card of the name, the close, the trend and the distance picture.";

    // How long a correction notice stands on the pages that draw it, counted from the newest session it affects.
    public const int CorrectionAgeDays = 30;

    // Whether a correction's notice is past its age on a night: the night more than the age after the newest session the
    // correction affects.
    // see: A correction notice older than thirty days by the newest session it affects is folded onto the Run page
    public static bool CorrectionIsOld(IReadOnlyList<DateOnly>? sessions, DateOnly? night) =>
        sessions is { Count: > 0 } && night is { } on && on.DayNumber - sessions.Max().DayNumber > CorrectionAgeDays;

    // The Run page's folded region of the corrections older than their age, each with the sessions it affects and what
    // they could not do; nothing where none is.
    public static string CorrectionsRegion(IReadOnlyList<DateOnly>? sessions, DateOnly? night)
    {
        if (!CorrectionIsOld(sessions, night))
        {
            return string.Empty;
        }

        var dates = string.Join(", ", sessions!.Order().Select(session => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var body = new StringBuilder();

        body.Append(Invariant($"<details class=\"corrections\" data-sessions=\"{Escaped(dates)}\"><summary>Corrections older than {CorrectionAgeDays} days</summary>"));
        body.Append(Invariant($"<p class=\"written-before-correction\" data-sessions=\"{Escaped(dates)}\">The listings for {Escaped(dates)} were written before a correction: {WrittenBeforeTheCorrectionText}.</p></details>"));

        return Cards.Computed("Corrections", body.ToString(), title: "Corrections", lede: "Notices of corrections to stored rows, kept here once they are more than thirty days old.", region: "corrections");
    }
}
