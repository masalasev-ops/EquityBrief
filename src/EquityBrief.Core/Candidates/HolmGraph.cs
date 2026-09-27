namespace EquityBrief.Core.Candidates;

// One candidate as the graph reads it: whether it has already been promoted,
// whether it has been retired, and whether its looks cross at a level.
public sealed record GraphMember(string Candidate, bool Promoted, bool Retired, Func<double, bool> Crosses);

// What the graph gives a candidate: the level it is tested at now, the step it
// stands at, and whether its own looks have crossed.
public sealed record GraphLevel(string Candidate, double Level, int Step, bool Crossed);

// How the level moves between the candidates as they are decided.
//
// The level at the first step is the significance over the distinct trials counted,
// the rules a look has read and the rules still running, and never over the
// candidates one window opened with: the level is shared across every trial for the
// life of the system rather than fresh for each window. Each candidate spends its
// own share across its own looks. A candidate whose looks cross has been shown, so
// its share passes in equal parts to those still standing and each is tested again
// at the level it now has. A candidate that is retired takes its share out of the
// family: it passes to no one, because a level released by evidence and a level
// released by giving up are different things and only the first was earned. The
// members step in the order they are given: at each step, the first not yet stepped
// that is promoted or crosses at the level it holds.
// see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running
public static class HolmGraph
{
    public static IReadOnlyList<GraphLevel> Levels(IReadOnlyList<GraphMember> family, double significance, int trials)
    {
        if (family.Count == 0)
        {
            return [];
        }

        if (trials < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trials),
                trials,
                "A family with a member counts at least one trial, and a level over none would be a level nobody divided.");
        }

        var level = family.ToDictionary(member => member.Candidate, _ => significance / trials, StringComparer.Ordinal);
        var step = family.ToDictionary(member => member.Candidate, _ => 0, StringComparer.Ordinal);
        var crossed = new HashSet<string>(StringComparer.Ordinal);

        // A candidate retired without having crossed is out of the graph before
        // anything is passed, so nothing reaches it and its own share reaches
        // nobody.
        var standing = family.Where(member => member.Promoted || !member.Retired).ToArray();

        while (true)
        {
            var next = standing.FirstOrDefault(member =>
                !crossed.Contains(member.Candidate)
                && (member.Promoted || member.Crosses(level[member.Candidate])));

            if (next is null)
            {
                break;
            }

            crossed.Add(next.Candidate);
            step[next.Candidate] = crossed.Count;

            var receiving = standing.Where(member => !crossed.Contains(member.Candidate)).ToArray();

            if (receiving.Length > 0)
            {
                var share = level[next.Candidate] / receiving.Length;

                foreach (var member in receiving)
                {
                    level[member.Candidate] += share;
                }
            }
        }

        return
        [
            .. family.Select(member => new GraphLevel(
                member.Candidate,
                level[member.Candidate],
                crossed.Contains(member.Candidate) ? step[member.Candidate] : crossed.Count + 1,
                crossed.Contains(member.Candidate))),
        ];
    }
}
