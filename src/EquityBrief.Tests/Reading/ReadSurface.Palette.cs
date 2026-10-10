using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.1: the palette held to the colour rules at the stylesheet's head. Every text colour the stylesheet
// states reads at 4.5 to 1 or more in both palettes against the ground it is drawn on, read off its own rule or the rule
// holding it and off the page, the surface and the plot where neither sets one; the kinds the name page draws a band's
// role, a change and a caution in read so on their own fill too; a rise and a fall are drawn by the name page's rules
// alone; and no rule drawing either names the failure's hue.
// see: A figure that rose or fell is drawn in a hue of its own on the name page and its export alone
public partial class ReadSurface
{
    static readonly string[] Grounds = ["--page", "--surface", "--plot"];

    // The one text colour drawn under 4.5 to 1 on purpose: a day the night picker cannot open, drawn faint as a disabled
    // control is, which WCAG's contrast rule leaves out.
    const string DisabledDay = ".np-grid .np-off";

    // The stylesheet's rules with its comments taken out, each as its selector list and its body. A rule inside a media
    // block is read with its own selectors, since the block's braces hold it.
    static IReadOnlyList<(string Selectors, string Body)> StyleRules() =>
    [
        .. Regex
            .Matches(Regex.Replace(Stylesheet.Css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}")
            .Select(rule => (rule.Groups["selectors"].Value.Trim(), rule.Groups["body"].Value)),
    ];

    static Dictionary<string, string> TokensOf(string body) =>
        Regex.Matches(body, @"(--[\w-]+)\s*:\s*([^;]+);")
            .ToDictionary(token => token.Groups[1].Value, token => token.Groups[2].Value.Trim(), StringComparer.Ordinal);

    // The two palettes: the light one off every root block in order, the names the marks draw with among them, and the
    // dark one the toggle's block lays over it, which the machine's own dark block states token for token.
    static (IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string> Dark) Palettes()
    {
        var rules = StyleRules();
        var light = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var root in rules.Where(rule => rule.Selectors == ":root"))
        {
            foreach (var (token, value) in TokensOf(root.Body))
            {
                light[token] = value;
            }
        }

        var toggled = TokensOf(rules.Single(rule => rule.Selectors.StartsWith(":root[data-theme='dark']", StringComparison.Ordinal)).Body);
        var machine = TokensOf(rules.Single(rule => rule.Selectors.StartsWith(":root:not([data-theme='light'])", StringComparison.Ordinal)).Body);

        Assert.Equal(toggled.OrderBy(token => token.Key, StringComparer.Ordinal), machine.OrderBy(token => token.Key, StringComparer.Ordinal));

        var dark = new Dictionary<string, string>(light, StringComparer.Ordinal);

        foreach (var (token, value) in toggled)
        {
            dark[token] = value;
        }

        return (light, dark);
    }

    // A token's colour in a palette, its alpha beside it, following the names the marks draw with to what they name.
    static (double R, double G, double B, double A) Colour(IReadOnlyDictionary<string, string> palette, string token)
    {
        var value = palette[token];

        while (Regex.Match(value, @"^var\((--[\w-]+)\)$") is { Success: true } named)
        {
            value = palette[named.Groups[1].Value];
        }

        if (Regex.Match(value, @"^#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$") is { Success: true } hex)
        {
            return (Channel(hex.Groups[1].Value), Channel(hex.Groups[2].Value), Channel(hex.Groups[3].Value), 1);
        }

        var rgba = Regex.Match(value, @"^rgba\((\d+),(\d+),(\d+),([\d.]+)\)$");

        Assert.True(rgba.Success, $"{token} is '{value}', which is neither a colour in six digits nor one with its alpha.");

        return (Number(rgba.Groups[1].Value), Number(rgba.Groups[2].Value), Number(rgba.Groups[3].Value), Number(rgba.Groups[4].Value));

        static double Channel(string digits) => int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static double Number(string digits) => double.Parse(digits, CultureInfo.InvariantCulture);
    }

    // A colour laid over a ground at its alpha, times an opacity, as six digits.
    static string Over((double R, double G, double B, double A) colour, (double R, double G, double B, double A) ground, double opacity = 1)
    {
        var alpha = colour.A * opacity;

        static string Digits(double value) => ((int)Math.Round(value)).ToString("x2", CultureInfo.InvariantCulture);

        return "#" + Digits((colour.R * alpha) + (ground.R * (1 - alpha)))
            + Digits((colour.G * alpha) + (ground.G * (1 - alpha)))
            + Digits((colour.B * alpha) + (ground.B * (1 - alpha)));
    }

    // A selector's steps, split at its combinators.
    static string[] Steps(string selector) => Regex.Split(selector.Trim(), @"\s*[ >~+]\s*");

    // The ground a selector's text is drawn on: the background the selector's own rule sets, or the nearest rule holding
    // it, read as the selector's steps cut back one at a time, sets; none where no such rule sets one.
    static string? GroundOf(IReadOnlyList<(string Selectors, string Body)> rules, string selector)
    {
        var steps = Steps(selector);

        for (var held = steps.Length; held > 0; held--)
        {
            var holder = steps[..held];
            var ground = rules
                .Where(rule => rule.Selectors.Split(',').Any(one => Steps(one).SequenceEqual(holder, StringComparer.Ordinal)))
                .Select(rule => Regex.Match(rule.Body, @"(?<![\w-])background(?:-color)?\s*:\s*var\((--[\w-]+)\)"))
                .LastOrDefault(set => set.Success);

            if (ground is not null)
            {
                return ground.Groups[1].Value;
            }
        }

        return null;
    }

    [Fact]
    public void EveryTextColourReadsAtFourAndAHalfToOneOnItsGroundInBothPalettes()
    {
        var rules = StyleRules();
        var (light, dark) = Palettes();
        var read = 0;
        var under = new List<string>();

        foreach (var (selectors, body) in rules)
        {
            if (Regex.Match(body, @"(?<![\w-])color\s*:\s*var\((--[\w-]+)\)") is not { Success: true } text)
            {
                continue;
            }

            var opacity = Regex.Match(body, @"(?<![\w-])opacity\s*:\s*([\d.]+)") is { Success: true } faded
                ? double.Parse(faded.Groups[1].Value, CultureInfo.InvariantCulture)
                : 1;

            foreach (var selector in selectors.Split(',').Select(one => one.Trim()))
            {
                read++;

                var ground = GroundOf(rules, selector);

                foreach (var (name, palette) in new[] { ("light", light), ("dark", dark) })
                {
                    foreach (var beneath in Grounds)
                    {
                        // A ground with an alpha of its own is laid over each of the three it may stand on, and the text
                        // over that at its own alpha and its rule's opacity.
                        var drawnOn = Over(Colour(palette, ground ?? beneath), Colour(palette, beneath));
                        var shown = Over(Colour(palette, text.Groups[1].Value), Opaque(drawnOn), opacity);
                        var contrast = ResearchMarked.Contrast(shown, drawnOn);

                        if (contrast < 4.5)
                        {
                            under.Add(FormattableString.Invariant($"{selector} in {text.Groups[1].Value} on {ground ?? beneath} in the {name} palette at {contrast:0.00}"));
                        }
                    }
                }
            }
        }

        // The text colours are stated by more than two hundred and forty selectors, and the one drawn faint on purpose
        // is the only one under the ratio, in both palettes.
        Assert.True(read >= 240, $"Read {read} selector(s) stating a text colour, expected at least 240.");
        Assert.Equal([DisabledDay], under.Select(line => line[..line.IndexOf(" in --", StringComparison.Ordinal)]).Distinct());
    }

    // Six digits read back as an opaque colour.
    static (double R, double G, double B, double A) Opaque(string hex) =>
        (int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         1);

    [Fact]
    public void TheNamePagesKindsReadOnTheirOwnFillAndARiseOrAFallIsDrawnThereAloneAndNeverAsAFailure()
    {
        var rules = StyleRules();
        var (light, dark) = Palettes();

        // A band's role, a change and a caution each read at 4.5 to 1 or more on every ground and on their own fill laid
        // over it, in both palettes, since a pill or a cell may draw one on the other.
        foreach (var (text, fill) in new[] { ("--sup-ink", "--sup-fill"), ("--res-ink", "--res-fill"), ("--up", "--up-fill"), ("--down", "--down-fill"), ("--warn", "--warn-fill") })
        {
            foreach (var (name, palette) in new[] { ("light", light), ("dark", dark) })
            {
                foreach (var ground in Grounds)
                {
                    var beneath = Over(Colour(palette, ground), Colour(palette, ground));
                    var tinted = Over(Colour(palette, fill), Colour(palette, ground));
                    var ink = Over(Colour(palette, text), Colour(palette, ground));

                    Assert.True(ResearchMarked.Contrast(ink, beneath) >= 4.5, FormattableString.Invariant($"{text} on {ground} in the {name} palette reads at {ResearchMarked.Contrast(ink, beneath):0.00}."));
                    Assert.True(ResearchMarked.Contrast(ink, tinted) >= 4.5, FormattableString.Invariant($"{text} on {fill} over {ground} in the {name} palette reads at {ResearchMarked.Contrast(ink, tinted):0.00}."));
                }
            }
        }

        // A rise and a fall are drawn by the name page's rules alone: every selector of a rule naming either hue opens on
        // the name page's region or the masthead's price, which the exported file carries too.
        string[] riseAndFall = ["--up", "--up-fill", "--down", "--down-fill"];

        var drawing = rules.Where(rule => riseAndFall.Any(token => rule.Body.Contains($"var({token})", StringComparison.Ordinal))).ToArray();

        Assert.True(drawing.Length >= 6, $"Read {drawing.Length} rule(s) drawing a rise or a fall, expected at least 6.");
        Assert.All(
            drawing.SelectMany(rule => rule.Selectors.Split(',').Select(one => one.Trim())),
            selector => Assert.Matches(@"^(\.name|\.m-price)\s", selector));

        // No rule drawing a rise or a fall, by its hue or by the classes the page marks one with, names the failure's.
        var marked = rules.Where(rule =>
            riseAndFall.Any(token => rule.Body.Contains($"var({token})", StringComparison.Ordinal))
            || rule.Selectors.Split(',').Any(one => Regex.IsMatch(Steps(one)[^1], @"\.(?:up|down|col-up|col-down|nb-positive|nb-negative)(?![\w-])"))).ToArray();

        Assert.True(marked.Length >= drawing.Length, $"Read {marked.Length} rule(s) marking a rise or a fall.");
        Assert.All(marked, rule => Assert.DoesNotMatch(@"var\(--fail(?:-fill)?\)", rule.Body));
    }

    [Fact]
    public async Task TheNamePageIsReadInAColumnAndEachRegionIsRuledDownItsLeftByItsRole()
    {
        // The page and its file in a column of a thousand pixels, and each role's rule in its hue: a buy in the support
        // green, a caution in amber, the plan's entries ruled in the support green and its exits in the resistance orange.
        Assert.Contains("main .name,.exported .name{max-width:1000px;margin-inline:auto}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".name .card[data-role='buy']{border-left-color:var(--sup)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".name .card[data-role='caution']{border-left-color:var(--warn)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".name .tranche-table td:first-child{box-shadow:inset 3px 0 0 var(--sup);", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".name .exit-table td:first-child{box-shadow:inset 3px 0 0 var(--res);", Stylesheet.Css, StringComparison.Ordinal);

        // On a rendered page the plan carries the buy role and the risks the caution role with the word written above them.
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        store.Execute($"INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES ('{name}', '{MarkRenderer.TheRisks}', 1, '{night}', 'a writer', 'accepted', 'A sentence.', '[]', NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{name}");

        Assert.Contains("<section class=\"card\" id=\"plan\" data-role=\"buy\" data-card=\"plan\">", page, StringComparison.Ordinal);
        Assert.Contains($"data-section=\"{System.Net.WebUtility.HtmlEncode(MarkRenderer.TheRisks)}\" data-role=\"caution\"><div class=\"spine\"><span class=\"role-word\">Caution</span>", page, StringComparison.Ordinal);
    }
}
