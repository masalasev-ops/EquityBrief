using System.Text.RegularExpressions;
using EquityBrief.Worker;
using EquityBrief.Worker.Cloudflare;

namespace EquityBrief.Tests.Checks;

// secrets-unread. The secrets file is read by the worker alone. No script under tools, and not the workflow, reads it:
// a script names it only to hand its path to the worker, and a file that will not parse stops the worker's
// configuration and the read-only copy's check with its name, line and byte and nothing it holds, because a parser's own
// message quotes the text it stopped at and the file once reached a session's output that way.
// see: The secrets file is read by the worker alone, and no script or command or error prints any of it
public class SecretsUnread
{
    // A line naming the secrets file, the variable holding its path in the scripts, or the variable handing that path
    // to the worker.
    static readonly Regex NamesTheSecrets = new(@"(?i:secrets\.json)|\$\{?secrets\b|EquityBrief__SecretsFile", RegexOptions.Compiled);

    // A wildcard reaching the settings files, the secrets file among them.
    static readonly Regex SettingsWildcard = new(@"appsettings[^\s""']*[*?\[]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The three forms a script names it in: its path assigned, the path printed by a dry run, the path handed on.
    static readonly Regex[] Handed =
    [
        new(@"^secrets=""\$root/src/EquityBrief\.Worker/appsettings\.Secrets\.json""$", RegexOptions.Compiled),
        new(@"^echo ""[a-z-]+: secrets read from \$secrets, none copied""$", RegexOptions.Compiled),
        new(@"^export EquityBrief__SecretsFile=""\$secrets""$", RegexOptions.Compiled),
    ];

    // The lines of a script naming the secrets file or reaching it by a wildcard in any form but the three, comments
    // aside, and the lines naming it in the three forms.
    internal static (IReadOnlyList<string> Reads, IReadOnlyList<string> Handing) LinesIn(string text, string script)
    {
        var reads = new List<string>();
        var handing = new List<string>();

        foreach (var line in text.Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith('#')))
        {
            if (SettingsWildcard.IsMatch(line))
            {
                reads.Add($"{script}: {line}");
            }
            else if (NamesTheSecrets.IsMatch(line))
            {
                (Handed.Any(form => form.IsMatch(line)) ? handing : reads).Add($"{script}: {line}");
            }
        }

        return (reads, handing);
    }

    static IReadOnlyList<string> Scripts() =>
    [
        .. Directory.GetFiles(Path.Combine(Repository.Root, "tools"), "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal),
        Repository.Workflow,
    ];

    [Fact]
    public void NoScriptReadsTheSecretsFileAndEachNamesItOnlyToHandItsPathToTheWorker()
    {
        var scripts = Scripts();

        Assert.True(scripts.Count >= 25, $"Read {scripts.Count} files under tools and the workflow, expected at least 25.");

        var read = scripts.Select(script => LinesIn(File.ReadAllText(script), Path.GetRelativePath(Repository.Root, script).Replace('\\', '/'))).ToArray();
        var reads = read.SelectMany(one => one.Reads).ToArray();
        var handing = read.SelectMany(one => one.Handing).ToArray();

        Assert.Empty(reads);

        // Six scripts build a copy of main's commit and start the worker from it, each naming the file three times.
        Assert.True(handing.Length == 18, $"Read {handing.Length} lines handing the secrets file's path on, expected 18: {string.Join("; ", handing)}");
        Assert.Equal(
            ["tools/ledger-build", "tools/localrun", "tools/monthly", "tools/nightly", "tools/offpeak", "tools/publish-site"],
            handing.Select(line => line[..line.IndexOf(':', StringComparison.Ordinal)]).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheCheckFindsAScriptReadingTheSecretsFileInEitherShellAndPassesTheThreeForms()
    {
        string[] reading =
        [
            "cat \"$secrets\"",
            "jq . \"$root/src/EquityBrief.Worker/appsettings.Secrets.json\"",
            "source \"$EquityBrief__SecretsFile\"",
            "grep ApiKey ${secrets}",
            "$j = Get-Content (Join-Path $root 'src/EquityBrief.Worker/appsettings.Secrets.json') -Raw | ConvertFrom-Json",
            "Get-Content $env:EquityBrief__SecretsFile",
            "type src\\EquityBrief.Worker\\APPSETTINGS.SECRETS.JSON",
            "cat src/*/appsettings*.json",
            "Get-ChildItem -Recurse src -Filter appsettings.* | Get-Content",
        ];

        var (reads, handing) = LinesIn(string.Join('\n', reading), "constructed");

        Assert.Equal(reading.Length, reads.Count);
        Assert.Empty(handing);

        string[] forms =
        [
            "secrets=\"$root/src/EquityBrief.Worker/appsettings.Secrets.json\"",
            "    echo \"nightly: secrets read from $secrets, none copied\"",
            "export EquityBrief__SecretsFile=\"$secrets\"",
            "# a comment naming appsettings.Secrets.json and $secrets",
            "echo \"nightly: data root $data\"",
        ];

        var (none, three) = LinesIn(string.Join('\n', forms), "constructed");

        Assert.Empty(none);
        Assert.Equal(3, three.Count);
    }

    // A secrets file whose third line holds a value that is no JSON value, the parser stopping at its first letter.
    const string Malformed = "{\n  \"EquityBrief\": {\n    \"Key\": sentinel-of-the-key\n  }\n}";

    const string Refusal = "The secrets file appsettings.Secrets.json is not valid JSON at line 3, byte 12; none of it is printed.";

    [Fact]
    public void AMalformedSecretsFileStopsTheWorkersConfigurationWithItsLineAndNoneOfIt()
    {
        using var root = new TemporaryDirectory();

        var build = Path.Combine(root.Path, "build");
        var checkout = Path.Combine(root.Path, "checkout");

        Directory.CreateDirectory(build);
        Directory.CreateDirectory(checkout);
        File.WriteAllText(Path.Combine(build, "appsettings.json"), "{\"EquityBrief\":{\"DataRoot\":\"data\"}}");
        File.WriteAllText(Path.Combine(checkout, "appsettings.Secrets.json"), Malformed);

        // Named by the environment's path, as every script hands it.
        var named = Assert.Throws<InvalidOperationException>(() => WorkerConfiguration.Build(build, Path.Combine(checkout, "appsettings.Secrets.json")));

        Assert.Equal(Refusal, named.Message);
        Assert.Null(named.InnerException);
        Assert.DoesNotContain("sentinel", named.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("'s'", named.ToString(), StringComparison.Ordinal);

        // Beside the build, as a checkout's own build reads it.
        File.WriteAllText(Path.Combine(build, "appsettings.Secrets.json"), Malformed);

        var beside = Assert.Throws<InvalidOperationException>(() => WorkerConfiguration.Build(build, null));

        Assert.Equal(Refusal, beside.Message);
        Assert.Null(beside.InnerException);

        // A file that parses is read as it was.
        File.WriteAllText(Path.Combine(build, "appsettings.Secrets.json"), "{\n  // a comment the worker reads past\n  \"EquityBrief\": { \"Key\": \"the key\" }\n}");

        Assert.Equal("the key", WorkerConfiguration.Build(build, null)["EquityBrief:Key"]);
    }

    [Fact]
    public void AMalformedSecretsFileStopsTheReadOnlyCopysCheckWithItsLineAndNoneOfIt()
    {
        using var root = new TemporaryDirectory();

        var secrets = Path.Combine(root.Path, "appsettings.Secrets.json");

        File.WriteAllText(secrets, Malformed);

        var refused = Assert.Throws<InvalidOperationException>(() => SiteChecks.Needles([secrets], [], root.Path));

        Assert.Equal(Refusal, refused.Message);
        Assert.Null(refused.InnerException);
        Assert.DoesNotContain("sentinel", refused.ToString(), StringComparison.Ordinal);

        File.WriteAllText(secrets, "{ \"EquityBrief\": { \"Key\": \"a value of the key\" } }");

        Assert.Contains(SiteChecks.Needles([secrets], [], root.Path), needle => needle.Text == "a value of the key" && needle.Label == "the value of EquityBrief:Key");
    }
}
