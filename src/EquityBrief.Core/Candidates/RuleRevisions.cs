using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Candidates;

// One revision of a family's rule on an index: its number counted from one in the order the revisions ran, the first
// session it ran, the last where a later revision followed it, none while it runs, its registrations' first and newest, and
// the settings every registration of it states.
public sealed record RuleRevision(int Number, DateOnly From, DateOnly? Until, long FirstId, long NewestId, string Candidate, IReadOnlyDictionary<string, double> Settings)
{
    public bool Running => Until is null;

    // What a change in code changed where the revision started from one, its settings the same as the revision before.
    public string? Code { get; init; }
}

// A rule changes through revisions, numbered, one running at a time. A revision is a run of the rule's registrations whose
// settings are the same, read from the parameters each registration states: a registration stating other settings starts
// the next revision, and one stating the same settings, a code version pinned again, a record carried on or the rule
// registered again under its own settings, continues the revision it stands in. A change in code that changed what the
// rule's trades do starts the next revision too, from the session it first ran, the settings the same. A revision runs from
// its first session to the session before the next revision's first, and a trade belongs to the revision whose sessions hold
// the night that listed it.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
// see: Any change that changes what a rule's trades do starts a new revision, in its settings or in its code
public static class RuleRevisions
{
    // The revisions of a rule's registrations and the changes in its code, in the order they ran; none for none.
    public static IReadOnlyList<RuleRevision> Of(IEnumerable<(long Id, string Candidate, DateTimeOffset RegisteredAt, string Parameters)> lineage, IReadOnlyList<CodeRevision>? code = null)
    {
        var runs = new List<(DateOnly From, long First, long Newest, string Candidate, IReadOnlyDictionary<string, double> Settings, string? Code)>();

        foreach (var row in lineage.OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id))
        {
            var settings = CandidateEvaluator.Read(row.Parameters);
            var from = FamilyRecords.FirstSession(row.RegisteredAt);

            // A change in code that ran from a session before this registration's first, after the run it stands in began,
            // starts its own revision there.
            foreach (var change in (code ?? []).Where(change => change.From is { } at && runs.Count > 0 && at > runs[^1].From && at <= from))
            {
                runs.Add((change.From!.Value, runs[^1].Newest, runs[^1].Newest, runs[^1].Candidate, runs[^1].Settings, change.Changed));
            }

            if (runs.Count > 0 && Same(runs[^1].Settings, settings))
            {
                runs[^1] = runs[^1] with { Newest = row.Id, Candidate = row.Candidate };

                continue;
            }

            // A revision registered again before its first session ran is replaced by the one after it, which runs from the
            // same session, keeping the change in code that started it.
            if (runs.Count > 0 && runs[^1].From >= from)
            {
                runs[^1] = (runs[^1].From, row.Id, row.Id, row.Candidate, settings, null);

                continue;
            }

            runs.Add((from, row.Id, row.Id, row.Candidate, settings, null));
        }

        // A change in code after the newest registration's first session starts the revision that runs now.
        foreach (var change in (code ?? []).Where(change => change.From is { } at && runs.Count > 0 && at > runs[^1].From))
        {
            runs.Add((change.From!.Value, runs[^1].Newest, runs[^1].Newest, runs[^1].Candidate, runs[^1].Settings, change.Changed));
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
                run.Settings) { Code = run.Code }),
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
