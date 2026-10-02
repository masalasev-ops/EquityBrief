using System.Text.RegularExpressions;
using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// store-never-deleted. The operator's store is the one file this tool cannot rebuild: bars are kept a year
// and a record is read over four, so a store that is deleted takes nights with it that no provider serves
// again. Nothing here trusts a habit. Every site in the shipped source, in the scripts and in the suite that
// removes a file, moves one or writes one over whatever was there is held to a stated list, so a new one is
// a line somebody has to choose to add, and none of them names the store; and every table a migration drops
// is a stated rebuild that copies its rows across first.
// see: The operator's store is never deleted, and every site that removes a file is stated where a check holds it
public class StoreNeverDeleted
{
    // A call that removes a file or a folder, moves one, or writes one from nothing over what was there,
    // with the first thing it is handed, which is what it acts on.
    static readonly Regex Removal = new(
        @"\b(?<call>File\.(?:Delete|Move|Replace|Copy|Create|OpenWrite|WriteAll\w+)|Directory\.(?:Delete|Move))\s*\(\s*(?<on>(?:[^,()]|\((?:[^()]|\([^()]*\))*\))*)",
        RegexOptions.Compiled);

    // A stream opened so that it empties what was there.
    static readonly Regex Truncating = new(@"\bFileMode\.(?:Create|Truncate)\b", RegexOptions.Compiled);

    // What a line would have to say to act on the store itself.
    static readonly string[] NamesTheStore = [StoreLocation.DatabaseFileName, "DatabaseFile", "databaseFile", "StoreLocation"];

    // What a statement would have to say to destroy a store through its own connection.
    static readonly string[] DestroysThroughSql = ["EnsureDeleted", "VACUUM INTO", "writable_schema", "DROP DATABASE"];

    internal static IReadOnlyList<string> RemovalsIn(string text, string file)
    {
        var name = Path.GetFileName(file);

        return
        [
            .. text.Split('\n').SelectMany(line =>
                Removal.Matches(line).Select(match => $"{name}: {match.Groups["call"].Value}({match.Groups["on"].Value.Trim()})")
                    .Concat(Truncating.Matches(line).Select(match => $"{name}: {match.Value}"))),
        ];
    }

    internal static IReadOnlyList<string> NamingTheStore(string text, string file) =>
    [
        .. text.Split('\n')
            .Where(line => Removal.IsMatch(line) || Truncating.IsMatch(line))
            .Where(line => NamesTheStore.Any(word => line.Contains(word, StringComparison.Ordinal)))
            .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"),
    ];

    static bool IsTheSuite(string file) =>
        file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    [Fact]
    public void EverySiteInTheShippedSourceThatRemovesOrReplacesAFileIsStatedAndNoneNamesTheStore()
    {
        var shipped = Repository.SourceFiles().Where(file => !IsTheSuite(file)).ToArray();

        // The scope, in numbers: the shipped source is hundreds of files, and a reader that found a handful
        // would be reading the wrong folder.
        Assert.True(shipped.Length >= 200, $"Read {shipped.Length} shipped source files, expected at least 200.");

        var sites = shipped
            .SelectMany(file => RemovalsIn(File.ReadAllText(file), file))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();

        // Each stated with what it acts on, and none of it the store.
        //
        // ComparisonCommand and SourceMeasurementRun write a report file into a folder the operator names.
        // DrainLauncher copies the worker's build into a fresh partial folder, with no overwrite, moves the
        // finished copy into place and removes a partial or an old one, under the data root's own folder
        // of builds. FamilySweepRunner and SweepIdeasRunner each write a run's report and its figures into the
        // run folder they made for them under the sweep's folder. NightLock writes and removes the night's lock file.
        // SweepPointInTime removes the scratch stores it built under the machine's temporary folder, which
        // hold nothing of the operator's. SweepRunner writes and replaces its own run folder's files, and
        // removes its own saved candidates where the history moved under them.
        Assert.Equal(
            [
                "ComparisonCommand.cs: File.WriteAllTextAsync(Path.Combine(folder, name))",
                "DrainLauncher.cs: Directory.Delete(other)",
                "DrainLauncher.cs: Directory.Delete(partial)",
                "DrainLauncher.cs: Directory.Move(partial)",
                "DrainLauncher.cs: File.Copy(file)",
                "FamilySweepRunner.cs: File.WriteAllText(figures)",
                "FamilySweepRunner.cs: File.WriteAllText(report)",
                "NightLock.cs: File.Delete(holder)",
                "NightLock.cs: File.WriteAllText(holder)",
                "SourceMeasurementRun.cs: File.WriteAllTextAsync(file)",
                "SweepIdeasRunner.cs: File.WriteAllText(figures)",
                "SweepIdeasRunner.cs: File.WriteAllText(report)",
                "SweepPointInTime.cs: Directory.Delete(scratchRoot)",
                "SweepPointInTime.cs: File.Delete(file)",
            ],
            sites.Where(site => !site.StartsWith("SweepRunner.cs: ", StringComparison.Ordinal)));

        // The sweep's runner, every one of them a file inside its own run folder, reached through `Of`, or
        // one it listed from that folder.
        var runner = sites.Where(site => site.StartsWith("SweepRunner.cs: ", StringComparison.Ordinal)).ToArray();

        Assert.All(runner, site => Assert.Matches(@"^SweepRunner\.cs: File\.(?:WriteAllText\((?:Of\(|next\)|saved\))|Move\(next\)|Delete\(file\)|Create\(Of\()", site));
        Assert.Equal(13, runner.Length);

        // No removal anywhere in the shipped source is handed the store's own path.
        Assert.Empty(shipped.SelectMany(file => NamingTheStore(File.ReadAllText(file), file)));

        // And nothing destroys a store through its connection.
        Assert.Empty(shipped
            .Where(file => DestroysThroughSql.Any(word => File.ReadAllText(file).Contains(word, StringComparison.Ordinal)))
            .Select(Path.GetFileName));
    }

    [Fact]
    public void TheScratchStoresTheSweepRemovesAreUnderTheMachinesTemporaryFolder()
    {
        // The one place shipped code deletes a store file is the sweep's point-in-time check, and the store
        // it deletes is a scratch one it built itself. Where that scratch root comes from is read here, so
        // the delete cannot be pointed at the data root without this failing.
        var callers = Repository.SourceFiles()
            .Where(file => !IsTheSuite(file))
            .Select(file => (File: Path.GetFileName(file), Text: File.ReadAllText(file)))
            .Where(source => source.Text.Contains("new SweepPointInTime(", StringComparison.Ordinal))
            .ToArray();

        // One caller, the sweep's runner, which names the scratch root on the line before it hands it over.
        var caller = Assert.Single(callers);

        Assert.Equal("SweepRunner.cs", caller.File);
        Assert.Contains("var scratch = Path.Combine(Path.GetTempPath(), \"equitybrief-sweep\", ", caller.Text, StringComparison.Ordinal);
        Assert.Contains("new SweepPointInTime(scratch, ", caller.Text, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(caller.Text, @"\bscratch\s*="));
    }

    // A script line that removes or moves something.
    static readonly Regex ScriptRemoval = new(@"\bRemove-Item\b|\bMove-Item\b|\bClear-Content\b|(?<![\w-])rm\s|(?<![\w-])mv\s|(?<![\w-])rmdir\s|(?<![\w-])del\s", RegexOptions.Compiled);

    // A removal whose target is the data root itself or the store's file, in either shell's spelling.
    static readonly Regex RemovesTheDataRoot = new(
        @"(?:\$data|\$env:EquityBrief__DataRoot|\$EquityBrief__DataRoot|(?<![\w/\\.-])data)[""']?(?=\s|$|\}|\)|;)|equitybrief\.db",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static IReadOnlyList<string> ScriptRemovalsIn(string text, string script) =>
    [
        .. text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#') && ScriptRemoval.IsMatch(line))
            .Select(line => $"{Path.GetFileName(script)}: {line}"),
    ];

    [Fact]
    public void EveryRemovalInTheScriptsIsStatedAndNoneReachesTheDataRoot()
    {
        var scripts = Repository.ToolScripts();

        Assert.True(scripts.Count >= 12, $"Read {scripts.Count} scripts under tools, expected at least 12.");

        var removals = scripts.SelectMany(script => ScriptRemovalsIn(File.ReadAllText(script), script)).ToArray();

        // The store `tools/ci.*` created and drops is `data-ci`, never the data root. The night's script
        // removes its own refusal note, a partial or stale build copy under the data root's folder of
        // nights, and copies older than a week; the phase report removes its own artifacts.
        Assert.Equal(
            [
                "ci.ps1: Step \"drop the store\"  { if (Test-Path data-ci) { Remove-Item -Recurse -Force data-ci } }",
                "ci.sh: rm -rf data-ci",
                "nightly: rm -f \"$data/night.refused\"",
                "nightly: rm -f \"$data/night.refused\"",
                "nightly: rm -rf \"$copy\" \"$copy.partial\"",
                "nightly: mv \"$copy.partial\" \"$copy\"",
                "nightly: find \"$copies\" -mindepth 1 -maxdepth 1 -type d -mtime +7 ! -path \"$copy\" -exec rm -rf {} + 2>/dev/null || true",
                "verify-phase: rm -f artifacts/suite.trx artifacts/phase-report.html artifacts/phase-report.json",
            ],
            removals);

        Assert.DoesNotContain(removals, line => RemovesTheDataRoot.IsMatch(line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..]));

        // The night's copies are folders named for a commit under the data root's `nights`, so the folder
        // it clears is never the data root: the two lines that say so are read off the script.
        var nightly = File.ReadAllText(Repository.Tool("nightly"));

        Assert.Contains("copies=\"$data/nights\"", nightly, StringComparison.Ordinal);
        Assert.Contains("copy=\"$copies/$short\"", nightly, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTableAMigrationDropsIsAStatedRebuildThatCopiesItsRowsAcrossFirst()
    {
        var shipped = Repository.SourceFiles().Where(file => !IsTheSuite(file)).ToArray();
        var dropping = shipped.Where(file => File.ReadAllText(file).Contains("DROP TABLE", StringComparison.Ordinal)).Select(file => Path.GetFileName(file)!).ToArray();

        // The migrations alone drop a table.
        Assert.Equal(["SchemaMigrations.cs"], dropping);

        var migrations = File.ReadAllText(shipped.Single(file => Path.GetFileName(file) == "SchemaMigrations.cs"));
        var dropped = Regex.Matches(migrations, @"DROP TABLE\s+(?:IF EXISTS\s+)?(\w+)").Select(match => match.Groups[1].Value).ToArray();

        // Two, each a rebuild SQLite forces where a constraint a table was created with has to change.
        Assert.Equal(["research_request", "membership"], dropped);

        foreach (var table in dropped)
        {
            var copied = migrations.IndexOf($"INSERT INTO {table}_rebuilt", StringComparison.Ordinal);
            var gone = migrations.IndexOf($"DROP TABLE {table};", StringComparison.Ordinal);
            var back = migrations.IndexOf($"ALTER TABLE {table}_rebuilt RENAME TO {table};", StringComparison.Ordinal);

            Assert.True(copied >= 0 && copied < gone && gone < back, $"{table} is dropped without its rows copied to {table}_rebuilt before it and renamed back after.");
        }
    }

    [Fact]
    public void TheSuiteRemovesOnlyWhatItMadeUnderTheMachinesTemporaryFolder()
    {
        var suite = Repository.SourceFiles().Where(IsTheSuite).Where(file => Path.GetFileName(file) != "StoreNeverDeleted.cs").ToArray();
        var sites = suite
            .SelectMany(file => RemovalsIn(File.ReadAllText(file), file))
            .Where(site => site.Contains(".Delete(", StringComparison.Ordinal))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();

        // Eight, every one a folder or file inside a temporary directory the test itself made.
        Assert.Equal(
            [
                "FilingsArchiveTests.cs: Directory.Delete(folder)",
                "NightlyRun.Script.cs: Directory.Delete(Root)",
                "NightlyRun.Script.cs: Directory.Delete(folder.Path)",
                "ReadSurface.Drain.cs: File.Delete(Path.Combine(build, WorkerDrainLauncher.Assembly))",
                "ReadSurface.NightBuild.cs: File.Delete(Path.Combine(data, NightBuild.CommitFileName))",
                "ReadSurface.NightBuild.cs: File.Delete(Path.Combine(made, WorkerDrainLauncher.Assembly))",
                "TemporaryDirectory.cs: Directory.Delete(Path)",
                "TemporaryStore.cs: Directory.Delete(Root)",
            ],
            sites);

        // The two the suite builds every store and every folder with are rooted in the temporary folder.
        foreach (var maker in new[] { "TemporaryStore.cs", "TemporaryDirectory.cs" })
        {
            Assert.Contains("GetTempPath(), \"equitybrief-tests\"", File.ReadAllText(suite.Single(file => Path.GetFileName(file) == maker)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCheckFindsARemovalOfTheStoreWhereOneIsWritten()
    {
        // The permanent proof that each reader can fail, over constructed source and a constructed script.
        const string Source =
            "var location = StoreLocation.From(configuration);\n" +
            "File.Delete(location.DatabaseFile);\n" +
            "File.Move(databaseFile, databaseFile + \".old\", overwrite: true);\n" +
            "using var stream = new FileStream(path, FileMode.Create);\n" +
            "Directory.Delete(Path.Combine(root, \"nights\"), recursive: true);\n";

        Assert.Equal(
            [
                "Reset.cs: File.Delete(location.DatabaseFile)",
                "Reset.cs: File.Move(databaseFile)",
                "Reset.cs: FileMode.Create",
                "Reset.cs: Directory.Delete(Path.Combine(root, \"nights\"))",
            ],
            RemovalsIn(Source, "Reset.cs"));
        Assert.Equal(
            ["Reset.cs: File.Delete(location.DatabaseFile);", "Reset.cs: File.Move(databaseFile, databaseFile + \".old\", overwrite: true);"],
            NamingTheStore(Source, "Reset.cs"));

        const string Script =
            "# rm -rf data in a comment is not a removal\n" +
            "rm -rf \"$data\"\n" +
            "Remove-Item -Recurse -Force data\n" +
            "rm -f data/equitybrief.db\n" +
            "rm -rf data-ci\n" +
            "rm -f \"$data/night.refused\"\n";

        var removals = ScriptRemovalsIn(Script, "reset");

        Assert.Equal(5, removals.Count);
        Assert.Equal(
            ["reset: rm -rf \"$data\"", "reset: Remove-Item -Recurse -Force data", "reset: rm -f data/equitybrief.db"],
            removals.Where(line => RemovesTheDataRoot.IsMatch(line["reset: ".Length..])));
    }
}
