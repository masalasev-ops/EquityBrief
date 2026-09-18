using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance: a ruling's entry lands nothing, so the record never
// reads a ruling as building the checkpoint it is filed under.
public partial class ArchitectureConformance
{
    // The one ruling entry that opens as a building entry. Its checkpoint had
    // already landed from its own entry, so no due point moves on it.
    internal const string RulingReadAsBuilt = "8.2 ruling - the rules directory 2026-09-16";

    // A ruling entry is any heading whose checkpoint is followed by the word, in
    // the heading's own form or in the commit subject's, so a ruling headed the
    // wrong way is read rather than passed over.
    static readonly Regex RulingEntry = new(
        @"^### (?<heading>\d+\.\d+\s*(?:-\s*)?ruling\b[^\r\n]*)(?<body>(?:(?!^### )[\s\S])*)",
        RegexOptions.Multiline);

    static string Normalised(string heading) => Regex.Replace(heading, @"\s+", " ").Trim();

    internal static IReadOnlyList<string> Rulings(string progress) =>
        [.. RulingEntry.Matches(progress).Select(entry => Normalised(entry.Groups["heading"].Value))];

    internal static IReadOnlyList<string> RulingsLandingACheckpoint(string progress) =>
    [
        .. RulingEntry.Matches(progress)
            .Where(entry => DuePoints.LandsACheckpoint(entry.Groups["heading"].Value, entry.Groups["body"].Value))
            .Select(entry => Normalised(entry.Groups["heading"].Value)),
    ];

    internal static IReadOnlyList<string> RulingsNotHeadedAsRulings(string progress) =>
        [.. Rulings(progress).Where(heading => !Regex.IsMatch(heading, @"^\d+\.\d+ ruling - "))];

    [Fact]
    public void EveryRulingEntryOpensAsLandingNothingSaveTheOneNamed()
    {
        var progress = Corpus.Read("docs/PROGRESS.md");

        var rulings = Rulings(progress);

        Assert.True(rulings.Count >= 4, $"Read {rulings.Count} ruling entries, expected at least 4.");

        Assert.Empty(RulingsNotHeadedAsRulings(progress));
        Assert.Equal([RulingReadAsBuilt], RulingsLandingACheckpoint(progress));

        var named = progress.IndexOf("### " + RulingReadAsBuilt.Replace(" 2026-09-16", ""), StringComparison.Ordinal);

        Assert.Contains("8.2", DuePoints.Built(progress[..named]));

        const string Building = "### 9.1 - a thing that is built   2026-10-01\nBuilt:      a thing.\n\n";

        Assert.Equal(
            ["9.1 ruling - a ruling 2026-10-02"],
            RulingsLandingACheckpoint(Building + "### 9.1 ruling - a ruling   2026-10-02\nBuilt:      a rule.\n\n"));

        Assert.Empty(
            RulingsLandingACheckpoint(Building + "### 9.1 ruling - a ruling   2026-10-02\nNot a checkpoint entry. It rules.\n\n"));

        // Headed in the commit subject's form, a ruling is read and named as out
        // of form, and one whose heading only mentions rulings is not a ruling.
        const string SubjectForm = "### 9.1 - ruling: a ruling   2026-10-02\nBuilt:      a rule.\n\n";

        Assert.Equal(["9.1 - ruling: a ruling 2026-10-02"], RulingsNotHeadedAsRulings(Building + SubjectForm));
        Assert.Equal(["9.1 - ruling: a ruling 2026-10-02"], RulingsLandingACheckpoint(Building + SubjectForm));
        Assert.Empty(Rulings("### 9.0 planning - the rulings the phase needs   2026-10-01\nNot a checkpoint entry.\n\n"));
    }
}
