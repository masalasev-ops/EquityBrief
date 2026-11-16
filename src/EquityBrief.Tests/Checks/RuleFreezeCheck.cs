using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// On the operator's ruling of 2026-11-14 the second restart is the last restart of a live rule, and no version goes live before
// the monthly run of 6 Feb 2027. A live rule changes or its record restarts only through a register command a remedy runs, so
// every remedy written from the freeze on that runs one changing a live rule or registering one again is named, by its file
// name, in a ruling entry of the build record that names the exception; a defect making a live rule trade on wrong data is the
// one exception the ruling allows. On their review of PR 699 a remedy registering background versions alone passes: a
// candidate, a family's variants, a ranking, an exposure, an order, a standing target or a plan registered as a variant, or the
// retirement of a version that is not live. A line refused names a live rule, or is a form that registers or retires one itself:
// a family or a freeze registered whole, or a live rule given a plan, withdrawn or registered again without its flag. From the
// replay fix every version a code change moved registered again passes, since `register --moved` itself refuses inside the
// freeze where the replay at the time it runs does not reproduce every trade of a live rule, which no reading of a remedy's
// words can say.
public sealed class RuleFreezeCheck
{
    static readonly Regex Dated = new(@"^(\d{4}-\d{2}-\d{2})-", RegexOptions.CultureInvariant);

    static readonly Regex RulingHeading = new(@"^### [^\n]* ruling - [^\n]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // The register forms that register or retire a live rule themselves, whatever names they are given.
    internal static readonly string[] LiveForms =
    [
        EquityBrief.Worker.Candidates.RegisterVerb.TheThree,
        EquityBrief.Worker.Candidates.RegisterVerb.TheFamily,
        EquityBrief.Worker.Candidates.RegisterVerb.Family,
        EquityBrief.Worker.Candidates.RegisterVerb.TheFamilyAgain,
        EquityBrief.Worker.Candidates.RegisterVerb.FamilyAgain,
        EquityBrief.Worker.Candidates.RegisterVerb.IndexFamily,
        EquityBrief.Worker.Candidates.RegisterVerb.WithoutFlag,
        EquityBrief.Worker.Candidates.RegisterVerb.Withdraw,
    ];

    // A remedy line's arguments as the remedy runner splits them, a double-quoted argument kept whole.
    static IReadOnlyList<string> Arguments(string line) =>
        [.. Regex.Matches(line, "\"([^\"]*)\"|(\\S+)").Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)];

    // Whether a remedy line changes a live rule or registers one again: a register command giving a live rule a plan other than as
    // a variant beside it, naming a live rule in any other form, or of a form that registers or retires one itself.
    public static bool TouchesALiveRule(string line)
    {
        var arguments = Arguments(line.Trim());

        if (arguments is not ["register", ..])
        {
            return false;
        }

        if (arguments.Contains(EquityBrief.Worker.Candidates.RegisterVerb.Planned))
        {
            return !arguments.Contains(EquityBrief.Worker.Candidates.RegisterVerb.VariantOnly);
        }

        return arguments.Skip(1).Any(EquityBrief.Core.Candidates.FamilyRecords.IsLive)
            || arguments.Skip(1).Any(argument => LiveForms.Contains(argument, StringComparer.Ordinal));
    }

    // The remedies written from the freeze on that change a live rule or register one again and no ruling entry names as an
    // exception.
    public static IReadOnlyList<string> Unnamed(IEnumerable<(string Name, string Text)> remedies, string record)
    {
        var rulings = Rulings(record);

        return
        [
            .. remedies
                .Where(remedy => Dated.Match(remedy.Name) is { Success: true } date
                    && DateOnly.ParseExact(date.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture) >= RuleFreeze.From)
                .Where(remedy => remedy.Text.Split('\n').Any(TouchesALiveRule))
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
            ("2026-12-01-24.1-a-change.txt", "migrate\nregister --family-again drift --evidence \"x\"\n"),
            ("2026-12-02-24.1-wording.txt", "card-plans\nregister --moved --evidence \"a pinned source moved\"\n"),
            ("2026-11-14-23.10-the-second-restart.txt", "register --family-again drift --evidence \"x\"\n"),
        ];

        Assert.Equal(["2026-12-01-24.1-a-change.txt"], Unnamed(remedies, "### 23.11 - a checkpoint   2026-11-20\n\nBuilt: nothing.\n"));
        Assert.Equal(["2026-12-01-24.1-a-change.txt"], Unnamed(remedies, "### 24.1 - a checkpoint   2026-12-01\n\n2026-12-01-24.1-a-change.txt, the exception.\n"));
        Assert.Empty(Unnamed(remedies, "### 24.1 ruling - the defect   2026-12-01\n\nNot a checkpoint entry. The exception the freeze allows: 2026-12-01-24.1-a-change.txt.\n"));
    }

    // On the operator's review of PR 699 a remedy registering background versions alone passes during the freeze, and one
    // changing a live rule or registering one again is named until a ruling names its exception, each form read.
    [Fact]
    public void ARemedyRegisteringBackgroundVersionsAlonePassesAndOneTouchingALiveRuleIsNamed()
    {
        const string Background =
            "register --index-variants --index MID --variants \"strength=0.5\" --evidence \"the pullback search's versions\" --again\n"
            + "register --candidate \"the S&P 400 pullback at a strength of 0.5\" --rule \"the S&P 400 pullback\" --test forward --evaluator pullback-400 --parameters strength=0.5\n"
            + "register --plan \"the live S&P 400 pullback rule at a strength of 0.75\" --combination \"2 typical days under the close within 20 sessions\" --variant-only --evidence \"its plan without its take-profit\"\n"
            + "register --ranked --index SML --by score\nregister --exposed --index MID\nregister --ordered --index SML\nregister --standing --index MID\n"
            + "register --retire \"the S&P 400 pullback at a strength of 0.5\" --evidence \"a version that is not live\"\n"
            + "register --moved --evidence \"a pinned source moved\"\n"
            + "card-plans\n";

        Assert.All(Background.Split('\n'), line => Assert.False(TouchesALiveRule(line), line));
        Assert.Empty(Unnamed([("2026-12-03-24.2-the-pullback-search-versions.txt", Background)], string.Empty));

        string[] live =
        [
            "register --plan \"the live S&P 400 pullback rule at a strength of 0.75\" --combination \"2 typical days under the close within 20 sessions\" --evidence \"a new plan\"",
            "register --withdraw \"the live S&P 500 quality rule at a score of 7 in 9\" --evidence \"withdrawn\"",
            "register --retire \"the live S&P 400 pullback rule at a strength of 0.75\" --evidence \"retired\"",
            "register --candidate \"the live S&P 600 drift rule within 3 sessions\" --rule r --test t --evaluator drift-600 --parameters a=1",
            "register --index-family pullback --index MID --parameters strength=0.75",
            "register --family breakout",
            "register --family-again drift --evidence \"x\"",
            "register --the-family-again --evidence \"x\"",
            "register --without-flag MID --evidence \"x\"",
            "  register --the-three",
        ];

        Assert.All(live, line => Assert.True(TouchesALiveRule(line), line));
        Assert.Equal(
            [.. live.Select((_, at) => FormattableString.Invariant($"2026-12-{at + 1:00}-24.2-change-{at}.txt")).Order(StringComparer.Ordinal)],
            Unnamed(live.Select((line, at) => (FormattableString.Invariant($"2026-12-{at + 1:00}-24.2-change-{at}.txt"), "migrate\n" + line + "\n")), string.Empty));
        Assert.False(TouchesALiveRule("plan-points --index GSPC"));
        Assert.False(TouchesALiveRule("ledger-build --index MID --family breakout"));
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
