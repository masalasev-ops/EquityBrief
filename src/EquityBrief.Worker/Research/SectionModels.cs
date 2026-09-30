namespace EquityBrief.Worker.Research;

// The paid lane's model for each section: the profile the research job's map names for a section, and the job's
// own `Use` for every section the map names none for, each profile held by a spend cap of its own, so every paid
// call is still made through a cap and a section's rows carry its own profile's identity. The caps read one
// ledger, so the day's and the month's caps hold across them.
// see: Research names a profile per section as well as per job, and a Claude profile states its thinking
// see: Every paid call is made through the spend cap, which holds each paid job's model
public sealed class SectionModels(SpendCap job, IReadOnlyDictionary<string, SpendCap>? sections = null)
{
    public SpendCap Job => job;

    public SpendCap For(string section) =>
        sections is not null && sections.TryGetValue(section, out var own) ? own : job;

    // Every cap the lane holds, each once, the job's first.
    public IReadOnlyList<SpendCap> Distinct =>
        [job, .. (sections?.Values ?? []).Where(cap => !ReferenceEquals(cap, job)).Distinct()];

    // The caps a set of sections is written through, each once.
    public IReadOnlyList<SpendCap> For(IEnumerable<string> sections) =>
        [.. sections.Select(For).Distinct()];

    public int Probes => Distinct.Sum(cap => cap.Probes);

    public IReadOnlyList<string> Models => [.. Distinct.Select(cap => cap.Model).Distinct(StringComparer.Ordinal)];
}
