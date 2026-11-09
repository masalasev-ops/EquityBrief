namespace EquityBrief.Core.History;

// What the marking page shows beside an unexplained jump or an unsettled end: the sessions around it with each close and
// volume, the stock's median volume over the 50 sessions before it, and the filings its filer made within three days of
// it, each with its form and an 8-K's items.
public sealed record MarkEvidence(
    IReadOnlyList<(DateOnly Session, double Close, long Volume)> Around,
    double? MedianVolume,
    IReadOnlyList<(DateOnly Filed, string Form, string? Items)> Filings);

// The mark the page proposes for each, by stated rules over its evidence and nothing else; the operator confirms it, changes
// it or leaves it, and nothing is marked without their confirm.
// see: The marking page proposes a mark for each unexplained jump and unsettled end from its evidence, and writes none without the operator's confirm
public static class MarkProposals
{
    // The sessions shown each side of the one marked, the sessions the median volume reads, and the days either side a
    // filing is read as standing beside it.
    public const int AroundSessions = 5;

    public const int MedianSessions = 50;

    public const int FilingDays = 3;

    public const string Kept = "checked and kept";

    public const string BadPrint = "bad print";

    public const string Ends = "history ends";

    // The kinds the page offers for a session, the marks' own.
    public static IReadOnlyList<string> Kinds { get; } = ["starts here", BadPrint, Kept, Ends];

    // An unexplained jump: kept where a filing stands within three days of it, a bad print where it came on volume under
    // its median with none, and kept otherwise, the move having come on a session that traded as usual.
    public static (string Kind, string Says) ForJump(DateOnly session, MarkEvidence evidence)
    {
        if (evidence.Filings.Where(filing => Math.Abs(filing.Filed.DayNumber - session.DayNumber) <= FilingDays).OrderBy(filing => Math.Abs(filing.Filed.DayNumber - session.DayNumber)).FirstOrDefault() is { Form: not null } beside)
        {
            return (Kept, FormattableString.Invariant($"checked and kept: its filer's {beside.Form}{(beside.Items is { } items ? " carrying items " + items : string.Empty)} of {beside.Filed:yyyy-MM-dd} stands beside the move"));
        }

        var volume = evidence.Around.Where(one => one.Session == session).Select(one => (long?)one.Volume).FirstOrDefault();

        if (volume is { } traded && evidence.MedianVolume is { } median && traded < median)
        {
            return (BadPrint, FormattableString.Invariant($"a bad print: the move came on {traded:N0} shares, under its median of {median:N0}, with no filing beside it"));
        }

        return (Kept, "checked and kept: the move came on a session that traded as usual, with no filing beside it");
    }

    // An end no filing settles: its history ends on its last session with volume.
    public static (string Kind, string Says) ForEnd(DateOnly last) =>
        (Ends, FormattableString.Invariant($"its history ends on {last:yyyy-MM-dd}, its last session with volume; no filing settles why it ended"));
}
