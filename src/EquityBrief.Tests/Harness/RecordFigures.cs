using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Harness;

// One figure the newest entry states of the run over the tree carrying it, beside the one the report measured.
internal sealed record StatedFigure(string Name, int Stated, int Measured)
{
    internal bool Agrees => Stated == Measured;
}

// What the newest entry of the record states of the verifying run over the tree carrying it, held to what the report
// measured. An entry states its figures before that run, so a figure the run did not measure is a prediction the run
// refuted, and the report is not green over it. An entry stating no run over its own tree, a ruling's, a record's or a
// sign-off's, is not read and holds nothing back.
// see: An entry states the figures its verifying run measures before that run, and the phase report holds them to the run
internal sealed record RecordFigures(string Entry, bool StatesItsOwnRun, IReadOnlyList<StatedFigure> Figures)
{
    internal static RecordFigures NotRead { get; } = new(string.Empty, false, []);

    // Condition 2 has the entry record the test count, so an entry stating its own run without it, or without the
    // claims the report counts, states less than the run it names.
    internal bool Agree =>
        !StatesItsOwnRun
        || (Figures.All(figure => figure.Agrees)
            && Figures.Any(figure => figure.Name == "tests ran")
            && Figures.Any(figure => figure.Name == "claims"));

    internal string Describe()
    {
        if (!StatesItsOwnRun)
        {
            return Entry.Length == 0
                ? "no entry read"
                : $"the newest entry, \"{Entry}\", states no run over the tree carrying it, so no figure is held to this run";
        }

        var differing = Figures.Where(figure => !figure.Agrees).ToArray();

        var missing = new[] { "tests ran", "claims" }
            .Where(name => Figures.All(figure => figure.Name != name))
            .ToArray();

        if (differing.Length == 0 && missing.Length == 0)
        {
            return $"the newest entry, \"{Entry}\", states " +
                string.Join(", ", Figures.Select(figure => $"{figure.Stated} {figure.Name}")) +
                ", each as this run measured";
        }

        return $"the newest entry, \"{Entry}\", states " +
            string.Join("; ", differing.Select(figure => $"{figure.Stated} {figure.Name} where this run measured {figure.Measured}")
                .Concat(missing.Select(name => $"no figure of {name}"))) +
            ", so the figures written before the run are not the ones it measured";
    }

    // The Verified block, its label's line and the indented lines under it, read as one line so a figure wrapped
    // across two reads as one.
    static readonly Regex Verified = new(@"^[ ]*Verified:(?<text>[^\r\n]*(?:\r?\n[ ]{12,}[^\r\n]*)*)", RegexOptions.Multiline);

    static readonly Regex OwnRun = new(@"`tools/verify-phase(?:\.ps1)?` green over the tree carrying this entry");

    internal static RecordFigures Of(string progress, PhaseReportModel report)
    {
        var entries = DuePoints.Entries(progress);

        if (entries.Count == 0)
        {
            return NotRead;
        }

        var newest = entries[^1];
        var heading = Regex.Replace(newest.Groups["heading"].Value, @"\s+", " ").Trim();
        var block = Verified.Match(newest.Groups["body"].Value);
        var text = block.Success ? Regex.Replace(block.Groups["text"].Value, @"\s+", " ") : string.Empty;

        if (!OwnRun.IsMatch(text))
        {
            return new RecordFigures(heading, false, []);
        }

        var figures = new List<StatedFigure>();

        void Read(string pattern, params (string Name, string Group, int Measured)[] measured)
        {
            var match = Regex.Match(text, pattern);

            if (!match.Success)
            {
                return;
            }

            foreach (var (name, group, value) in measured)
            {
                figures.Add(new StatedFigure(name, Number(match.Groups[group].Value), value));
            }
        }

        Read(@"(?<n>\d[\d,]*) of \k<n> tests ran", ("tests ran", "n", report.Suite.Executed), ("tests in the run", "n", report.Suite.Total));
        Read(@"the phase report (?<n>\d[\d,]*) tables", ("tables", "n", report.Tables.Count));
        Read(@"(?<claims>\d[\d,]*) claims, (?<passing>\d[\d,]*) passing", ("claims", "claims", report.Claims.Count), ("passing", "passing", report.Count(Verdict.Pass)));

        if (Regex.IsMatch(text, @"\bnone out of scope\b"))
        {
            figures.Add(new StatedFigure("out of scope", 0, report.Count(Verdict.OutOfScope)));
        }

        Read(@"(?<n>\d[\d,]*) out of scope", ("out of scope", "n", report.Count(Verdict.OutOfScope)));
        Read(@"(?<n>\d[\d,]*) placements and verdicts reconciled against a floor of (?<floor>\d[\d,]*)", ("placements", "n", report.Reconciled), ("floor", "floor", Reconciliation.Floor));
        Read(@"(?<n>\d[\d,]*) checks on the roster", ("checks", "n", report.Coverage.Count));

        return new RecordFigures(heading, true, figures);
    }

    static int Number(string text) =>
        int.Parse(text, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
}
