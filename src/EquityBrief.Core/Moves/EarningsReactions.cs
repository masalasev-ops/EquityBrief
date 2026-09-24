using System.Globalization;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Moves;

// One print as the calendar holds it: the day it was reported, when in the session, and the
// estimate, the actual and the provider's surprise, each kept as the provider sent it.
public sealed record ReactionPrint(DateOnly ReportDate, EventTiming Timing, string? Estimate, string? Actual, string? Surprise);

// One print's reaction: the print as filed, the session the earnings rule takes for it, and that
// session's move from the close before it, in per cent. The surprise is the provider's and is none
// where no estimate was filed, whatever the provider sent beside it.
public sealed record Reaction(
    DateOnly ReportDate,
    EventTiming Timing,
    DateOnly Session,
    string? Estimate,
    string? Actual,
    double? SurprisePct,
    double MovePct);

// A name's reaction record and how many of its prints the stored bars do not reach.
public sealed record ReactionRecord(IReadOnlyList<Reaction> Reactions, int Unreached);

// The earnings reaction record, a pure function of a name's prints and its session-ordered bars.
// Each print's session is the one the earnings rule takes, read by calling the rule's own function
// one print at a time, so a print whose timing the provider left unstated is handled exactly as
// that rule handles it and nothing the rule versions pin is edited to share the reading. A print
// whose session the bars do not reach, or whose session has no close before it among them, is left
// out and counted rather than read off a session the store does not hold.
// see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
public static class EarningsReactions
{
    public static ReactionRecord Of(IReadOnlyList<ReactionPrint> prints, IReadOnlyList<LadderBar> bars)
    {
        var reactions = new List<Reaction>();
        var unreached = 0;

        foreach (var print in prints.OrderBy(print => print.ReportDate))
        {
            var taken = LadderSeries.EarningsRuleFor([(print.ReportDate, print.Timing)], bars, null);

            if (taken.Count == 0)
            {
                unreached++;

                continue;
            }

            var session = taken[0].Session;
            var at = bars.First(bar => bar.SessionDate == session);
            var before = bars.Last(bar => bar.SessionDate < session);

            // The provider's surprise stands only beside an estimate: with none filed there is
            // nothing the actual was a surprise against, so a print without one is never read as
            // having met it, whatever difference the provider sent.
            double? surprise = print.Estimate is not null
                && double.TryParse(print.Surprise, NumberStyles.Float, CultureInfo.InvariantCulture, out var filed)
                    ? filed
                    : null;

            reactions.Add(new Reaction(
                print.ReportDate,
                print.Timing,
                session,
                print.Estimate,
                print.Actual,
                surprise,
                Statistic.FromRatio((at.Close - before.Close) / before.Close) * 100));
        }

        return new ReactionRecord(reactions, unreached);
    }
}
