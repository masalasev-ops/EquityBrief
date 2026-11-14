using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// On the operator's ruling of 2026-11-14 the second restart is the last restart of a live rule, and no version goes live before
// the monthly run of 6 Feb 2027. A live rule's record restarts only through a register command a remedy runs, so every remedy
// written from the freeze on that runs a register command is named, by its file name, in a ruling entry of the build record
// that names the exception; a defect making a live rule trade on wrong data is the one exception the ruling allows.
public sealed class RuleFreezeCheck
{
    static readonly Regex Dated = new(@"^(\d{4}-\d{2}-\d{2})-", RegexOptions.CultureInvariant);

    static readonly Regex RulingHeading = new(@"^### [^\n]* ruling - [^\n]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // The remedies written from the freeze on that run a register command and no ruling entry names as an exception.
    public static IReadOnlyList<string> Unnamed(IEnumerable<(string Name, string Text)> remedies, string record)
    {
        var rulings = Rulings(record);

        return
        [
            .. remedies
                .Where(remedy => Dated.Match(remedy.Name) is { Success: true } date
                    && DateOnly.ParseExact(date.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture) >= RuleFreeze.From)
                .Where(remedy => remedy.Text.Split('\n').Any(line => line.TrimStart().StartsWith("register", StringComparison.Ordinal)))
                .Where(remedy => !rulings.Any(ruling => ruling.Contains(remedy.Name, StringComparison.Ordinal) && ruling.Contains("exception", StringComparison.OrdinalIgnoreCase)))
                .Select(remedy => remedy.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    // Each ruling entry of the record, its heading to the next entry's.
    static IReadOnlyList<string> Rulings(string record)
    {
        var headings = Regex.Matches(record, @"^### ", RegexOptions.Multiline).Select(match => match.Index).Append(record.Length).ToArray();
        var entries = new List<string>();

        for (var at = 0; at < headings.Length - 1; at++)
        {
            var entry = record[headings[at]..headings[at + 1]];

            if (RulingHeading.IsMatch(entry.Split('\n')[0]))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    [Fact]
    public void NoRemedyFromTheFreezeOnRestartsALiveRuleWithoutARulingNamingTheException()
    {
        var folder = Path.Combine(Repository.Root, "tools", "remedies");
        var remedies = Directory.GetFiles(folder, "*.txt").Select(path => (Path.GetFileName(path), File.ReadAllText(path))).ToArray();
        var record = File.ReadAllText(Path.Combine(Repository.Root, "docs", "PROGRESS.md"));

        Assert.True(remedies.Length >= 40, $"{remedies.Length} remedies read");
        Assert.Empty(Unnamed(remedies, record));
    }

    [Fact]
    public void TheCheckNamesARemedyRegisteringDuringTheFreezeUntilARulingNamesItsException()
    {
        (string, string)[] remedies =
        [
            ("2026-12-01-24.1-a-change.txt", "migrate\nregister --moved\n"),
            ("2026-12-02-24.1-wording.txt", "card-plans\n"),
            ("2026-11-14-23.10-the-second-restart.txt", "register --moved\n"),
        ];

        Assert.Equal(["2026-12-01-24.1-a-change.txt"], Unnamed(remedies, "### 23.11 - a checkpoint   2026-11-20\n\nBuilt: nothing.\n"));
        Assert.Equal(["2026-12-01-24.1-a-change.txt"], Unnamed(remedies, "### 24.1 - a checkpoint   2026-12-01\n\n2026-12-01-24.1-a-change.txt, the exception.\n"));
        Assert.Empty(Unnamed(remedies, "### 24.1 ruling - the defect   2026-12-01\n\nNot a checkpoint entry. The exception the freeze allows: 2026-12-01-24.1-a-change.txt.\n"));
    }

    [Fact]
    public void TheFreezeIsSaidBesideAVersionFromTheRestartOnAndUntilTheMonthlyRun()
    {
        Assert.Equal("; rules frozen until 6 Feb 2027", RuleFreeze.Beside(new DateOnly(2026, 11, 17)));
        Assert.Equal(string.Empty, RuleFreeze.Beside(new DateOnly(2026, 11, 10)));
        Assert.Equal(string.Empty, RuleFreeze.Beside(new DateOnly(2027, 2, 6)));

        var web = Path.Combine(Repository.Root, "src", "EquityBrief.Web", "Marks");
        var drawn = Directory.GetFiles(web, "*.cs").Select(File.ReadAllText).ToArray();

        Assert.Equal(4, drawn.Sum(text => Regex.Matches(text, @"This version since \{").Count));
        Assert.Equal(4, drawn.Sum(text => Regex.Matches(text, @"This version since \{[^}]*\}\{RuleFreeze\.Beside\(").Count));
    }
}
