using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.1: the palette held to the colour rules at the stylesheet's head. Every text colour the stylesheet
// states reads at 4.5 to 1 or more in both palettes against the ground it is drawn on, read off its own rule or the rule
// holding it and off the page, the surface and the plot where neither sets one; the kinds the name page draws a band's
// role, a change and a caution in read so on their own fill too; a rise and a fall are drawn by the name page's rules
// alone; and no rule drawing either names the failure's hue.
// see: A figure that rose or fell is drawn green or red with its sign on every page
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

    // Every pill style a page draws has a rule of its own, read off each place a pill is drawn in every state it draws:
    // a setup card live, provisional and on a variant, paper trading's tiles and a scorecard's head by their standing, a
    // researched report standing and stale, and the monthly run held and stopped. A stale report and a stopped run are
    // drawn in the failure's red, the accent's ink on it as on the live rule's slate.
    // see: Status is drawn in blues with amber for a wait and red for a failure alone
    [Fact]
    public void EveryPillStyleAPageDrawsHasARuleOfItsOwnAndAFailedOneIsDrawnInTheRed()
    {
        var renderer = new MarkRenderer();
        var since = new DateOnly(2026, 1, 2);
        var live = new RuleChoiceView("the live breakout rule", RuleScreenWords.LiveSlug, null, true, "Live");
        var variant = new RuleChoiceView("the breakout rule at a volume of 1.25 times", "v1", 1, false, "V1 · volume 1.25 times");
        var chosen = new RuleView(variant, [live, variant], [new RulePartView("A stock closes above its high", false)], [], [("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)], new StretchLineView(1, 48, false, 30, 600), null, true, null);

        string Card(DateOnly? liveSince, RuleView? rule) =>
            renderer.FamilyCard(new FamilyCardView("breakout", "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, liveSince, 1, [], [], "No stock passed this setup tonight.", RuleChoice: rule));

        PaperAccountView Account(string standing, string family) =>
            new(standing, family, standing, "An account", "A rule.", standing == PaperAccountView.You ? RuleVerdict.Yours : RuleVerdict.NotTested, null, "R", [], null, 0, 0, null, null);

        PaperAccountView[] accounts = [Account(PaperAccountView.Live, "pullback"), Account(PaperAccountView.Variant, "pullback"), Account(PaperAccountView.Provisional, "breakout"), Account(PaperAccountView.You, "you")];
        var page = new PaperPageView(accounts, [], [], [], new EquityBrief.Core.Cards.PaperNotBought([]), [], null);

        string Monthly(string state) =>
            renderer.MonthlyRunRegion(new MonthlyRunView(new DateTimeOffset(since.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero), state, state == WorryItem.Held ? null : "its history step did not hold", "2026-01", [], 0, 60, null), "#/loop", "#/paper");

        (string Drawing, string Markup, string[] Styles)[] drawn =
        [
            ("a live card", Card(since, null), ["live"]),
            ("a provisional card", Card(null, null), ["provisional"]),
            ("a card on a variant", Card(since, chosen), ["variant"]),
            ("paper trading's tiles", renderer.PaperTiles(accounts, null), ["live", "provisional"]),
            ("paper trading's scorecard", renderer.PaperScorecard(page, new PaperLink("500"), "breakout"), ["provisional"]),
            ("the researched list", new SinglePageApp().ResearchedRegion([new ResearchedCell("AAA", null, null, since, 1, true), new ResearchedCell("BBB", null, null, since, 1, false)]), ["failed", "live"]),
            ("a monthly run held", Monthly(WorryItem.Held), ["live"]),
            ("a monthly run stopped", Monthly(WorryItem.Failed), ["failed"]),
        ];

        foreach (var (drawing, markup, styles) in drawn)
        {
            Assert.True(
                styles.SequenceEqual(Regex.Matches(markup, "class=\"status-pill ([a-z-]+)\"").Select(pill => pill.Groups[1].Value), StringComparer.Ordinal),
                $"{drawing} draws its pills as {string.Join(", ", Regex.Matches(markup, "class=\"status-pill ([a-z-]+)\"").Select(pill => pill.Groups[1].Value))}, not {string.Join(", ", styles)}.");
        }

        // Each of the four styles has one rule of its own that colours it.
        var rules = StyleRules();
        var used = drawn.SelectMany(one => one.Styles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["failed", "live", "provisional", "variant"], used);

        foreach (var style in used)
        {
            var own = rules.Where(rule => rule.Selectors.Split(',').Any(one => one.Trim() == ".status-pill." + style)).ToArray();

            Assert.True(own.Length == 1 && Regex.IsMatch(own[0].Body, @"(?<![\w-])(?:background|border|color)\s*:"), $"The {style} pill has {own.Length} rule(s) of its own colouring it, not one.");
        }

        Assert.Equal("background:var(--fail);color:var(--accent-ink)", rules.Single(rule => rule.Selectors == ".status-pill.failed").Body);
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

        // From 19.2 a rise is the green and a fall the red, on every page: the rise's fill is the support band's, its ink the
        // support band's ink, and the fall's fill the failure's, in both palettes, and the one class pair every page marks
        // a signed figure with draws them.
        foreach (var palette in new[] { light, dark })
        {
            Assert.Equal(Colour(palette, "--sup-ink"), Colour(palette, "--up"));
            Assert.Equal(Colour(palette, "--sup-fill"), Colour(palette, "--up-fill"));
            Assert.Equal(Colour(palette, "--fail-fill"), Colour(palette, "--down-fill"));
        }

        Assert.Equal("color:var(--up)", rules.Single(rule => rule.Selectors == ".gain").Body);
        Assert.Equal("color:var(--down)", rules.Single(rule => rule.Selectors == ".loss").Body);
    }

    // The setup families' tokens, each the family's word as the store keeps it.
    static readonly string[] Families = ["pullback", "breakout", "drift", "fundamentals", "heavyweight"];

    // A colour in OKLab times a hundred, from its six digits over the sRGB curve.
    static (double L, double A, double B) OkLab(string hex)
    {
        static double Linear(int channel)
        {
            var c = channel / 255.0;

            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var (r, g, b) = (Linear(int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture)), Linear(int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture)), Linear(int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));

        return (
            100 * ((0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s)),
            100 * ((1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s)),
            100 * ((0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s)));
    }

    static double Apart(string one, string other)
    {
        var (a, b) = (OkLab(one), OkLab(other));

        return Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2));
    }

    // 19.2: each setup family's hue reads as a mark at 3 to 1 on every ground and stands 15 or more apart in OKLab from the
    // green, the orange, the red and each other family's, its tint holds ink at 4.5 to 1, in both palettes, and no rule
    // draws text in a family's hue.
    // see: Each setup family has a hue of its own drawn as a mark and a tint beside its word
    [Fact]
    public void EachFamilysHueReadsAsAMarkStandsApartAndItsTintHoldsInkInBothPalettes()
    {
        var rules = StyleRules();
        var (light, dark) = Palettes();
        var read = 0;

        foreach (var (name, palette) in new[] { ("light", light), ("dark", dark) })
        {
            string Solid(string token) => Over(Colour(palette, token), Colour(palette, "--surface"));

            var fixedHues = new[] { "--sup", "--res", "--fail" }.Select(Solid).ToArray();
            var hues = Families.Select(family => Solid($"--fam-{family}")).ToArray();

            foreach (var (family, at) in Families.Select((family, at) => (family, at)))
            {
                foreach (var ground in Grounds)
                {
                    var beneath = Over(Colour(palette, ground), Colour(palette, ground));
                    var mark = Over(Colour(palette, $"--fam-{family}"), Colour(palette, ground));
                    var tint = Over(Colour(palette, $"--fam-{family}-tint"), Colour(palette, ground));
                    var ink = Over(Colour(palette, "--ink"), Colour(palette, ground));

                    Assert.True(ResearchMarked.Contrast(mark, beneath) >= 3, FormattableString.Invariant($"{family}'s mark on {ground} in the {name} palette reads at {ResearchMarked.Contrast(mark, beneath):0.00}."));
                    Assert.True(ResearchMarked.Contrast(ink, tint) >= 4.5, FormattableString.Invariant($"ink on {family}'s tint over {ground} in the {name} palette reads at {ResearchMarked.Contrast(ink, tint):0.00}."));
                    read++;
                }

                Assert.All(fixedHues, other => Assert.True(Apart(hues[at], other) >= 15, FormattableString.Invariant($"{family} stands {Apart(hues[at], other):0.0} from {other} in the {name} palette.")));
                Assert.All(hues.Where((_, other) => other != at), other => Assert.True(Apart(hues[at], other) >= 15, FormattableString.Invariant($"{family} stands {Apart(hues[at], other):0.0} from {other} in the {name} palette.")));
            }
        }

        // Five families on three grounds in two palettes.
        Assert.Equal(30, read);

        // No rule draws text in a family's hue or its tint.
        Assert.DoesNotContain(rules, rule => Regex.IsMatch(rule.Body, @"(?<![\w-])color\s*:\s*var\(--fam-"));
    }

    // The 18.1 correction of 2026-10-11: every written section's prose is set in the page's own type. The short version's
    // stood at 19 pixels in the serif beside every other section's 15 in the sans.
    [Fact]
    public void EveryWrittenSectionsProseIsSetInThePagesOwnType()
    {
        const string SetsType = @"(?:^|;)\s*font(?:-size|-family)?\s*:";
        var rules = StyleRules();

        // No rule picks a written section out by its name to give it a face or a size of its own.
        Assert.Empty(rules.Where(rule => rule.Selectors.Contains("[data-section=", StringComparison.Ordinal) && Regex.IsMatch(rule.Body, SetsType)).Select(rule => rule.Selectors));

        // And the rule for every section's prose sets none, so it reads at the body's 15 pixels in the sans.
        Assert.DoesNotMatch(SetsType, rules.Single(rule => rule.Selectors == ".written-section .prose").Body);
        Assert.Contains("font:15px/1.6 var(--sans)", rules.Single(rule => rule.Selectors == "body").Body, StringComparison.Ordinal);
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
