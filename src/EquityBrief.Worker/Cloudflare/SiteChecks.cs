using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Worker.Cloudflare;

// One string the copy must not carry, named by what it is and never by its value.
public sealed record Needle(string Label, string Text, bool CaseSensitive);

// What the check read in the copy's folder: the report's lines, and each reason not to upload, each naming its file.
public sealed record SiteCheck(IReadOnlyList<string> Lines, IReadOnlyList<string> Refusals, int Files, long Bytes);

// The read-only copy's folder read before anything leaves the machine. Every file is listed with its size against the
// host's limit; every form, every address of the read surface, every press header, every data attribute and every script
// source left in any file is counted and named; and every file is searched for each secret the secrets file holds, each
// absolute path of the machine and the account's settings, each named and never printed. A form, an address of a press,
// a press header, a script loaded from anywhere, a request a script makes to another site, a secret, a path, an account
// setting or a file past the host's limit refuses the upload, naming the file.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
public static partial class SiteChecks
{
    // What Cloudflare Pages takes: a file to 25 MiB and 20,000 files a deployment.
    public const long MostBytesAFile = 25L * 1024 * 1024;

    public const int MostFiles = 20_000;

    // The read surface's own addresses a press or a live fetch is sent to, which the copy carries none of. Stated here,
    // since the worker cannot reference the page that holds them, and the suite asserts every press the page sends is one.
    public static readonly IReadOnlyList<string> SurfaceAddresses =
    [
        "/passes/", "/quotes/", "/analysts/", "/analyst-ratings/", "/watch/", "/taken/", "/account", "/night/resume", "/loop/", "/screens/", "/exports/", "/marks/", "/proof/", "/sweep",
    ];

    public const string PressHeader = "X-EquityBrief-Pass";

    // The mark a card's plan carries where it was sized on the account's settings, which no page of the copy reads.
    public const string SizedOnTheAccount = "data-account=\"set\"";

    public static SiteCheck Read(string folder, IReadOnlyList<Needle> needles)
    {
        var lines = new List<string>();
        var refusals = new List<string>();
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => (Path: file, Name: Path.GetRelativePath(folder, file).Replace('\\', '/'), Size: new FileInfo(file).Length))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var total = files.Sum(file => file.Size);

        lines.Add(FormattableString.Invariant($"Files: {files.Length}, {total:N0} bytes ({total / 1048576.0:0.0} MB) in all; the host takes {MostFiles:N0} files and {MostBytesAFile / 1048576} MB a file."));

        if (files.Length > MostFiles)
        {
            refusals.Add(FormattableString.Invariant($"the copy holds {files.Length:N0} files, past the host's {MostFiles:N0}"));
        }

        var forms = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var routes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var attributes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var scripts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var hosts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var sizes = new List<string>();

        foreach (var (path, name, size) in files)
        {
            sizes.Add(FormattableString.Invariant($"  {name}  {size:N0} bytes"));

            if (size > MostBytesAFile)
            {
                refusals.Add(FormattableString.Invariant($"{name} is {size / 1048576.0:0.0} MB, past the host's {MostBytesAFile / 1048576} MB a file"));
            }

            var text = File.ReadAllText(path, Encoding.UTF8);

            foreach (Match form in FormTag().Matches(text))
            {
                Count(forms, name);
                refusals.Add($"{name} carries a form at its character {form.Index}");
            }

            foreach (Match route in RouteAttribute().Matches(text))
            {
                var value = route.Groups["value"].Value;

                Count(routes, $"{route.Groups["name"].Value}=\"{value}\"");

                if (route.Groups["name"].Value is "action" or "formaction" || SurfaceAddresses.Any(address => value.StartsWith(address, StringComparison.Ordinal)))
                {
                    refusals.Add($"{name} carries {route.Groups["name"].Value}=\"{value}\", an address of the read surface");
                }
            }

            foreach (var address in SurfaceAddresses.Where(address => QuotedAddress(address).IsMatch(text)))
            {
                refusals.Add($"{name} names the read surface's address {address} in quotes");
            }

            if (text.Contains(PressHeader, StringComparison.OrdinalIgnoreCase))
            {
                refusals.Add($"{name} carries the page's press header");
            }

            if (text.Contains(SizedOnTheAccount, StringComparison.Ordinal))
            {
                refusals.Add($"{name} carries a card's plan sized on the account");
            }

            foreach (Match attribute in DataAttribute().Matches(text))
            {
                Count(attributes, attribute.Groups["name"].Value);
            }

            foreach (Match script in ScriptTag().Matches(text))
            {
                var source = Regex.Match(script.Groups["attrs"].Value, @"\ssrc=""(?<src>[^""]*)""", RegexOptions.IgnoreCase);
                var body = script.Groups["body"].Value;

                if (source.Success)
                {
                    Count(scripts, $"{name} loads {source.Groups["src"].Value}");
                    refusals.Add($"{name} loads a script from {source.Groups["src"].Value}");
                }
                else
                {
                    Count(scripts, $"{name} inline");
                }

                if (OtherSite().IsMatch(body) || PostedRequest().IsMatch(body))
                {
                    refusals.Add($"{name} holds a script that asks another site or sends a request");
                }
            }

            foreach (Match link in LinkedSite().Matches(text))
            {
                Count(hosts, link.Groups["host"].Value.ToLowerInvariant());
            }

            foreach (var (line, label) in Found(text, needles))
            {
                refusals.Add($"{name}:{line} carries {label}");
            }
        }

        lines.Add("Each page's size:");
        lines.AddRange(sizes);
        lines.Add($"Forms left: {(forms.Count == 0 ? "none" : string.Join(", ", forms.Select(form => $"{form.Key} {form.Value}")))}.");
        lines.Add($"Routes and actions left, every attribute naming an address that opens with a slash, an action or a form's action: {(routes.Count == 0 ? "none" : string.Empty)}");
        lines.AddRange(routes.Select(route => $"  {route.Key}  {route.Value}"));
        lines.Add($"Data attributes left, by name, {attributes.Count} names:");
        lines.AddRange(attributes.Select(attribute => $"  {attribute.Key}  {attribute.Value}"));
        lines.Add($"Scripts: {(scripts.Count == 0 ? "none" : string.Join("; ", scripts.Select(script => $"{script.Key} {script.Value}")))}.");
        lines.Add($"Other sites linked, every one a source a report cites or a stock's own pages name, by host: {(hosts.Count == 0 ? "none" : string.Empty)}");
        lines.AddRange(hosts.Select(host => $"  {host.Key}  {host.Value}"));
        lines.Add($"Searched every file for {needles.Count} strings the copy must not carry: {string.Join("; ", needles.Select(needle => needle.Label).Distinct(StringComparer.Ordinal))}.");

        return new SiteCheck(lines, refusals, files.Length, total);
    }

    static void Count(SortedDictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    static Regex QuotedAddress(string address) => new($@"['""]{Regex.Escape(address)}");

    // Each line a needle is found on, by its label, once a needle a line; never the line's own words, which may be the
    // secret.
    public static IReadOnlyList<(int Line, string Label)> Found(string text, IReadOnlyList<Needle> needles)
    {
        var found = new List<(int, string)>();
        var number = 0;

        foreach (var line in text.Split('\n'))
        {
            number++;

            foreach (var label in needles
                .Where(needle => line.Contains(needle.Text, needle.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                .Select(needle => needle.Label)
                .Distinct(StringComparer.Ordinal))
            {
                found.Add((number, label));
            }
        }

        return found;
    }

    // Every string the copy must not carry: each value the secrets files hold of eight characters or more, by its key;
    // each absolute path a page could carry, in every spelling a page holds one in; and the account's settings where they
    // are kept, by their names, and their values where they are not the example every page draws without them.
    public static IReadOnlyList<Needle> Needles(IEnumerable<string> secretsFiles, IEnumerable<string> roots, string dataRoot)
    {
        var needles = new List<Needle>();

        foreach (var file in secretsFiles.Where(File.Exists))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });

            foreach (var (key, value) in Leaves(document.RootElement, string.Empty).Where(leaf => leaf.Value.Length >= 8))
            {
                needles.Add(new Needle($"the value of {key}", value, true));
            }
        }

        foreach (var root in roots.Where(root => root.Length > 3).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var forward = root.Replace('\\', '/');

            foreach (var spelling in new[] { root, forward, root.Replace("\\", "\\\\", StringComparison.Ordinal), forward.Replace(" ", "%20", StringComparison.Ordinal) }.Distinct(StringComparer.Ordinal))
            {
                needles.Add(new Needle("an absolute path of this machine", spelling, false));
            }
        }

        needles.Add(new Needle("the account settings' file", EquityBrief.Core.Cards.AccountFile.FileName, false));
        needles.Add(new Needle("an account setting's name", "riskPercent", false));
        needles.Add(new Needle("an account setting's name", "positionCap", false));

        if (EquityBrief.Core.Cards.AccountFile.Read(dataRoot) is { } account && account != EquityBrief.Core.Cards.AccountSettings.Example)
        {
            needles.Add(new Needle("the account's size", account.Size.ToString(CultureInfo.InvariantCulture), true));
            needles.Add(new Needle("the account's size", account.Size.ToString("N0", CultureInfo.InvariantCulture), true));
        }

        return needles;
    }

    static IEnumerable<(string Key, string Value)> Leaves(System.Text.Json.JsonElement element, string path)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var leaf in Leaves(property.Value, path.Length == 0 ? property.Name : path + ":" + property.Name))
                {
                    yield return leaf;
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.String && element.GetString() is { } value)
        {
            yield return (path, value);
        }
    }

    [GeneratedRegex(@"<form\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormTag();

    [GeneratedRegex(@"\s(?<name>action|formaction|href|src|data-[A-Za-z-]+)=""(?<value>/[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex RouteAttribute();

    [GeneratedRegex(@"\s(?<name>data-[A-Za-z0-9-]+)(?:=|\s|>)")]
    private static partial Regex DataAttribute();

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"https?://(?<host>[A-Za-z0-9.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OtherSite();

    // A link to another site, which a reader follows; a drawing's namespace names an address no reader is sent to.
    [GeneratedRegex(@"\shref=""https?://(?<host>[A-Za-z0-9.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedSite();

    [GeneratedRegex(@"method\s*:|['""]POST['""]|XMLHttpRequest|sendBeacon|WebSocket|EventSource", RegexOptions.IgnoreCase)]
    private static partial Regex PostedRequest();
}
