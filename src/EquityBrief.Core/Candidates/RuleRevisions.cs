using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Candidates;

// One revision of a family's rule on an index: its number counted from one in the order the revisions ran, the first
// session it ran, the last where a later revision followed it, none while it runs, its registrations' first and newest, and
// the settings every registration of it states.
public sealed record RuleRevision(int Number, DateOnly From, DateOnly? Until, long FirstId, long NewestId, string Candidate, IReadOnlyDictionary<string, double> Settings)
{
    public bool Running => Until is null;
}

// A rule changes through revisions, numbered, one running at a time. A revision is a run of the rule's registrations whose
// settings are the same, read from the parameters each registration states: a registration stating other settings starts
// the next revision, and one stating the same settings, a code version pinned again, a record carried on or the rule
// registered again under its own settings, continues the revision it stands in. A revision runs from its first
// registration's first session to the session before the next revision's first, and a trade belongs to the revision whose
// sessions hold the night that listed it.
// see: A rule changes only through a new revision, a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public static class RuleRevisions
{
    // The revisions of a rule's registrations, in the order they ran; none for none.
    public static IReadOnlyList<RuleRevision> Of(IEnumerable<(long Id, string Candidate, DateTimeOffset RegisteredAt, string Parameters)> lineage)
    {
        var runs = new List<(DateOnly From, long First, long Newest, string Candidate, IReadOnlyDictionary<string, double> Settings)>();

        foreach (var row in lineage.OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id))
        {
            var settings = CandidateEvaluator.Read(row.Parameters);

            if (runs.Count > 0 && Same(runs[^1].Settings, settings))
            {
                runs[^1] = runs[^1] with { Newest = row.Id, Candidate = row.Candidate };

                continue;
            }

            var from = FamilyRecords.FirstSession(row.RegisteredAt);

            // A revision registered again before its first session ran is replaced by the one after it, which runs from the
            // same session.
            if (runs.Count > 0 && runs[^1].From >= from)
            {
                runs[^1] = (runs[^1].From, row.Id, row.Id, row.Candidate, settings);

                continue;
            }

            runs.Add((from, row.Id, row.Id, row.Candidate, settings));
        }

        return
        [
            .. runs.Select((run, at) => new RuleRevision(
                at + 1,
                run.From,
                at + 1 < runs.Count ? SessionBefore(runs[at + 1].From) : null,
                run.First,
                run.Newest,
                run.Candidate,
                run.Settings)),
        ];
    }

    // The revision whose sessions hold a night, none for a night before the first revision ran.
    public static RuleRevision? At(IReadOnlyList<RuleRevision> revisions, DateOnly night) =>
        revisions.LastOrDefault(revision => revision.From <= night);

    // The settings two revisions state differently, by key, in key order.
    public static IReadOnlyList<string> Changed(IReadOnlyDictionary<string, double> before, IReadOnlyDictionary<string, double> after) =>
        [.. before.Keys.Union(after.Keys, StringComparer.Ordinal).Where(key => !before.TryGetValue(key, out var was) || !after.TryGetValue(key, out var now) || was != now).Order(StringComparer.Ordinal)];

    static bool Same(IReadOnlyDictionary<string, double> one, IReadOnlyDictionary<string, double> other) => Changed(one, other).Count == 0;

    // The trading session before a session, read off the exchange calendar.
    static DateOnly SessionBefore(DateOnly session)
    {
        var day = session.AddDays(-1);

        while (!ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(-1);
        }

        return day;
    }
}
