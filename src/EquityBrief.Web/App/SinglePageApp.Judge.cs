using System.Globalization;
using System.Text;
using EquityBrief.Core.Judge;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One of the AI stock picker's picks as its card draws it: its rank of the night's picks, the stock and its company, the sector and
// size the judge was shown, its reason, the last close, the stop and take-profit code worked from it, the give-up distance
// where the rest follows one, and the hold and take-profit it chose.
public sealed record JudgeCardView(int Rank, int Of, string Ticker, string? Company, string? Sector, string? Size, string Reason, decimal LastClose, decimal Stop, decimal Target, decimal? GiveUp, string Hold, string Shape)
{
    // From 24.14 the facts the judge named as deciding it, with their values, none on a pick made before it named its facts;
    // and the rest of the sheet it was shown.
    public IReadOnlyList<JudgeFactView>? Named { get; init; }

    public IReadOnlyList<JudgeFactView> Rest { get; init; } = [];

    // From the 24.14 correction of 2026-11-21, on an earlier version's card, the night it was picked on and what that
    // version's account did with it.
    public DateOnly? Night { get; init; }

    public JudgeHeld? Held { get; init; }

    // On a card of the earlier version that picked across the three indices, the index its stock belongs to.
    public string? Index { get; init; }

    // From the operator's answers of 2026-11-24 the range a pick of a night from the range's first buys in, none for a pick
    // of an earlier night, which buys at the open.
    public Core.Loop.BuyRange? Range { get; init; }
}

// What an account did with one of the judge's picks: the session it bought at the open and the price it paid, its stop,
// take-profit and give-up distance from that price, the session and price it sold at with its result per $1 risked once its
// walk ended, and why the account skipped it where it did.
public sealed record JudgeHeld(DateOnly Bought, decimal Paid, decimal? Stop, decimal? Target, decimal? Trail, DateOnly? Ended, decimal? Sold, double? Result, string? Skipped);

// From the 24.14 correction of 2026-11-21 an earlier judge version as its page draws it apart from the record: its number in
// the order the versions first ran, its first and last nights, its picks' cards, its own account's scoreboard and its line.
public sealed record JudgeVersionView(int Number, DateOnly First, DateOnly Last, IReadOnlyList<JudgeCardView> Picks, JudgeScoreboard Score, IReadOnlyList<(DateOnly Session, decimal Value)> Values)
{
    // From 24.24 the variant of its version it is, "a fixed 5", none where it is a version's own account.
    public string? Variant { get; init; }
}

// One fact of a pick's sheet as its card draws it: its words, its value as the judge was shown it, and a plain comparison
// where its value's sign says one; its field's key, and the tenth it stood in among its index's stocks that night, none where
// it was not placed among them.
public sealed record JudgeFactView(string Words, string Value, string? Comparison)
{
    public string Key { get; init; } = string.Empty;

    public int? Tenth { get; init; }

    // The fields whose value is a signed figure against something, and what a value above and below it says.
    static readonly Dictionary<string, (string Up, string Down)> Signed = new(StringComparer.Ordinal)
    {
        ["return_month"] = ("Up over the month.", "Down over the month."),
        ["return_quarter"] = ("Up over three months.", "Down over three months."),
        ["return_twelve_less_one"] = ("Up over the year to a month ago.", "Down over the year to a month ago."),
        ["strength"] = ("Stronger than its index.", "Weaker than its index."),
        ["close_over_fifty"] = ("Above its 50-day average.", "Below its 50-day average."),
        ["close_over_long"] = ("Above its 200-day average.", "Below its 200-day average."),
        ["volume_ratio"] = ("More volume than it usually trades.", "Less volume than it usually trades."),
        ["estimate_revision_month"] = ("Analysts raised their estimates.", "Analysts cut their estimates."),
        ["revenue_growth"] = ("Sales grew on a year before.", "Sales fell on a year before."),
        ["growth_change"] = ("Growth sped up.", "Growth slowed."),
        ["gross_margin_change"] = ("Its gross margin widened.", "Its gross margin narrowed."),
        ["operating_margin_change"] = ("Its operating margin widened.", "Its operating margin narrowed."),
        ["return_on_assets_change"] = ("Its return on assets rose.", "Its return on assets fell."),
        ["sector_month"] = ("Ahead of its sector over the month.", "Behind its sector over the month."),
        ["sector_quarter"] = ("Ahead of its sector over three months.", "Behind its sector over three months."),
        ["surprise"] = ("Beat the estimate.", "Missed the estimate."),
        ["high_ratio"] = ("Above its 52-week high.", "Under its 52-week high."),
    };

    public static string? Compared(string key, string value) =>
        Signed.TryGetValue(key, out var words) && value.Length > 0
            ? value[0] == '+' ? words.Up : value[0] == '-' ? words.Down : null
            : null;

    // The page's own label where the stored one does not say what the reading measures, the stored text kept as the
    // picker was shown it.
    static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["share_fall"] = "Share count against a year before",
    };

    public static string LabelOf(string key, string words) => Labels.TryGetValue(key, out var own) ? own : words;

    // The fields whose value is a change, which reads "no change" where it rounds to nothing.
    static readonly HashSet<string> Changes = new(StringComparer.Ordinal)
    {
        "return_month", "return_quarter", "return_twelve_less_one", "estimate_revision_month", "revenue_growth", "growth_change",
        "gross_margin_change", "operating_margin_change", "return_on_assets_change", "sector_month", "sector_quarter", "share_fall",
    };

    public const string NoChange = "no change";

    // A fact's value as the page draws it from the text the picker was shown: a unit of one in the singular; a change that
    // rounds to nothing "no change" and any other zero with no sign; and the share count's change as fewer or more shares,
    // since the reading is higher where the shares fell.
    public static string ValueOf(string key, string shown)
    {
        var value = System.Text.RegularExpressions.Regex.Replace(shown, @"(?<![\d.])([+-]?1(?:\.0)?) (point|trading day|insider|typical daily move)s\b", "$1 $2");

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[+-]?0(?:\.0+)?(?:%| points?)$"))
        {
            return Changes.Contains(key) ? NoChange : value.TrimStart('+', '-');
        }

        return key == "share_fall" && System.Text.RegularExpressions.Regex.Match(value, @"^([+-])(\d+(?:\.\d+)?)%$") is { Success: true } share
            ? share.Groups[2].Value + (share.Groups[1].Value == "+" ? "% fewer" : "% more")
            : value;
    }
}

// One index's tile of the picker's accounts: the index, its account's return since its first session, none before it holds
// two sessions, its trades still open and those closed, and its picks tonight.
public sealed record JudgeTile(string Index, double? Return, int Open, int Closed, int Tonight)
{
    // Whether the index's own version has run a night, and where it has not the night its first picks come from.
    public bool Started { get; init; } = true;

    public DateOnly? StartsOn { get; init; }
}

// One account's line on the chart: its name and its value on each session.
public sealed record JudgeLineView(string Name, IReadOnlyList<(DateOnly Session, decimal Value)> Values);

// What the AI stock picker's page draws: the newest night it ran for and what came of it, that night's picks, the live record's
// scoreboard, whether it beats random so far and what random picks made per $1 risked, where it looked, the four accounts'
// lines and the random accounts' band, and a replayed record apart from the live one; from 24.13 the night of its first live
// picks, the open they buy at, and whether any of its picks has been bought.
public sealed record JudgeView(
    DateOnly? Night,
    string? Outcome,
    string? Why,
    IReadOnlyList<JudgeCardView> Picks,
    JudgeScoreboard Score,
    string AgainstRandom,
    double? RandomPerDollar,
    IReadOnlyList<JudgeLooked> Looked,
    IReadOnlyList<JudgeLineView> Lines,
    IReadOnlyList<JudgeBand> Band,
    JudgeScoreboard? Replayed,
    int ReplayedNights)
{
    public DateOnly? FirstNight { get; init; }

    public DateOnly? FirstOpen { get; init; }

    public bool Bought { get; init; }

    // From 24.14 the first night of the newest judge version where an earlier version picked before it, the night its
    // record counts from.
    public DateOnly? CountedFrom { get; init; }

    // From the 24.14 correction of 2026-11-21 each earlier version apart from the record, the record's own number where
    // there is one, and the earlier version whose picks the drawn night's are.
    public IReadOnlyList<JudgeVersionView> Earlier { get; init; } = [];

    public int? RecordNumber { get; init; }

    public JudgeVersionView? PicksFrom { get; init; }

    // From 24.24 the index the page is worked for, the count of that index's stocks the newest night marked worth buying,
    // none before the picker picked within each index, and the record's variant "a fixed 5" in an account of its own.
    public string Index { get; init; } = "GSPC";

    public int? Marked { get; init; }

    public JudgeVersionView? FixedFive { get; init; }

    // The three indices' tiles in the selector's order, or the chosen index's alone where the page draws no tiles.
    public IReadOnlyList<JudgeTile> Tiles { get; init; } = [];

    // The earlier version that picked across the three indices before it picked within each, in its own account, and whether
    // a trade of it is still to be bought or sold.
    public JudgeVersionView? Across { get; init; }

    public bool AcrossOpen { get; init; }

    // From the operator's correction of 2026-11-23 the night the earlier version picked across the three indices and how many it
    // picked, where the index's own version has picked on no later night; none otherwise.
    public (DateOnly Night, int Picked)? AcrossNight { get; init; }

    // The tile of the index the page reads.
    public JudgeTile? Own => Tiles.FirstOrDefault(tile => tile.Index == Index);

    // From the operator's correction of 2026-11-23 the night the page draws, the newest any version ran unless one is chosen,
    // every night a version ran, newest first, and whether the night drawn is before the newest.
    public DateOnly? Shown { get; init; }

    public IReadOnlyList<DateOnly> Nights { get; init; } = [];

    public bool Past { get; init; }

    // The earlier version's picks of the night drawn where it picked across the three indices that night, and on a later
    // night its trades still open then, from the night it picked.
    public IReadOnlyList<JudgeCardView> AcrossTonight { get; init; } = [];

    public (DateOnly Night, int Open)? AcrossStillOpen { get; init; }

    // The index's own tile as the record stands, which the scoreboard and the four accounts read whatever night is drawn.
    public JudgeTile? RecordTile { get; init; }
}

// The AI stock picker's page: the four accounts' chart, the scoreboard of its live record, each pick's card in the words of the rule
// on the open, where it looked, and one line on Home. A replayed figure stands apart from the live record, which alone counts.
// see: No line states or implies the price a stock will open at, and a buy at the open buys at whatever price it opens
// see: The AI stock picker's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the picker waits for that run
// owes: The AI stock picker's live record read at 300 closed picks
public sealed partial class SinglePageApp
{
    public const string JudgeRoute = "#/picker";

    // From the operator's correction of 2026-11-23 the page for one night, the night's date after it.
    public const string JudgeNightRoute = "#/picker/";

    // The page's address before 24.18, kept as a redirect to it.
    public const string JudgeOldRoute = "#/judge";

    public const string JudgeLede =
        "Each night, straight after the night run finishes, your local model reads a short set of facts about every stock of each index in turn, with names, tickers and dates removed and each figure placed among that index's stocks. It marks every stock it rates worth buying and picks the best quarter of them, at most 5 in each index. It chooses how long to hold and how to sell from a fixed menu; code sets every price. Its picks are never the rules' buys. They trade only in each index's own account, so we can see whether it beats that index's rules and random picks from that index.";

    // From 24.24 what the record's variant is.
    public const string FixedFiveLine = "A fixed 5 takes the first 5 its version ranked each night, where the version takes the best quarter of the stocks it marked. It asks the model nothing of its own, and its picks trade in an account of their own, kept apart from the record.";

    // From 24.24 a pick its account had no cash for.
    public const string NoCashLine = "Not bought: no cash. Its result is still recorded in the record.";

    public const string JudgeMenuLine = "The AI chose how long to hold and how to sell, from a fixed menu. Code sets every price from the stock's typical daily move and the price it buys at.";

    public const string JudgeReplayedLine = "Nights replayed from before the picker started are drawn apart and labelled replayed. Only the live record counts.";

    public static IReadOnlyList<string> JudgeLineNames { get; } = ["AI stock picker", "The rules (every live rule's trades)", "Random picks: the middle of 1,000 random accounts", "Just holding the index"];

    public string JudgeRegion(JudgeView view, string selector = "")
    {
        var region = new StringBuilder();

        var chosen = Universes.ByCode(view.Index) ?? Universes.Large;

        region.Append(CultureInfo.InvariantCulture, $"<section class=\"judge\" data-index=\"{Escaped(view.Index)}\">");
        // From the operator's correction of 2026-11-23 the night picker Home has: the tiles, the night's region and its count
        // line follow the night chosen, and the accounts, the scoreboard and the rules stay as they are.
        // see: The AI stock picker's page draws one night at a time, and its count line says what decided the number
        var query = "?" + Universes.Query + "=" + chosen.Word;
        var picker = view.Shown is { } shown ? Cards.NightPicker(shown, view.Nights, JudgeNightRoute, JudgeRoute + query, query) : string.Empty;
        var when = view.Past ? "that night" : "tonight";

        region.Append(Cards.Masthead("AI stock picker: " + chosen.Titled, $"<span class=\"m-screen\">AI stock picker: {Escaped(chosen.Titled)}</span>", (view.Shown is { } night ? (view.Past ? "Night of " : "Computed for ") + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Not run yet") + picker));
        region.Append(selector);
        region.Append(Cards.Computed("AI stock picker", $"<p data-summary>{Escaped(JudgeLede)}</p>{RunLine(view)}{CountedFromLine(view)}", title: "Your local AI picks its own stocks, in its own paper account", region: "judge-lede"));
        if (view.Tiles.Count > 1)
        {
            region.Append(Cards.Computed("Its three accounts", IndexTiles(view.Tiles, chosen, view.Past ? view.Shown : null) + AcrossOpenLine(view, chosen), title: "The AI stock picker in each index", lede: Escaped(TilesLede), region: "judge-tiles"));
        }

        if (view.Across is { } across && view.AcrossTonight.Count > 0)
        {
            region.Append(AcrossRegion(across with { Picks = view.AcrossTonight }));
        }
        else
        {
            var tonight = view.Own is { Started: false } waiting
                ? $"<p class=\"empty\" data-started=\"no\" data-summary>{Escaped(NotStartedWords(waiting.StartsOn))}</p>"
                : $"<p class=\"count-line\" data-summary>{TonightWords(view, chosen)}</p><p class=\"count-note\">{Escaped(TonightNote)}</p>" + Carousel(view.Picks, view.Score, chosen);

            region.Append(Cards.Computed((view.Past ? "That night, " : "Tonight, ") + chosen.Name, tonight, title: $"The AI stock picker's {Escaped(CapWord(chosen))} picks {when} ({view.Picks.Count})", stamp: view.Shown is { } on ? "<span class=\"stamp computed\">" + (view.Past ? "Night of " : "Computed for ") + on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "</span>" : null, region: "judge-picks"));
        }

        region.Append(Cards.Computed("Its own rules, " + chosen.Name, OwnRules(chosen), title: $"The picker's own {Escaped(CapWord(chosen))} rules", lede: Escaped(OwnRulesLede(chosen)), region: "judge-rules"));
        // From the operator's order of 2026-11-24, item 14, the night the first picks buy said once, under the four accounts.
        region.Append(Cards.Computed("Scoreboard", ((view.RecordTile ?? view.Own) is { Started: false } ? EmptyRecord(view) : Scoreboard(view, tiles: EmptyRecord(view) is null)) + AcrossRecord(view), title: "How its live record stands", region: "judge-score"));
        region.Append(Cards.Computed("Four accounts", (EmptyRecord(view) ?? $"<p data-summary>The picker's account beside the rules', random picks' and the index's, each from {(view.RecordNumber is { } number ? $"version {number}'s" : "the picker's")} first night.</p>" + JudgeChart(view.Lines, view.Band, "The four accounts' returns since the picker started")) + ApartCharts(view), title: "Four accounts, same money, same sizing, same costs", lede: Escaped(JudgeReplayedLine), region: "judge-chart"));
        region.Append(Cards.Computed("Where the AI looked", LookedSummary(view) + Looked(view), title: "How its picks compare with the rules', since it started", region: "judge-looked"));

        if (view.FixedFive is { } five)
        {
            region.Append(Cards.Computed(VersionWords(five), FixedFiveBody(view, five, chosen), title: VersionWords(five) + ": its own account, apart from the record", region: "judge-fixed-five"));
        }

        foreach (var earlier in view.Earlier)
        {
            var scope = string.Create(CultureInfo.InvariantCulture, $"version-{earlier.Number}");

            region.Append(Cards.Computed(VersionWords(earlier), VersionSummary(earlier, view.RecordNumber) + PickCards(earlier.Picks, earlier.Score, earlier, scope, chosen), title: VersionWords(earlier) + ": its own account, apart from the record", region: "judge-" + scope));
        }

        if (view.Replayed is { } replayed)
        {
            region.Append(Cards.Computed("Replayed", $"<p data-replayed=\"record\" data-summary>Replayed, {view.ReplayedNights} night{(view.ReplayedNights == 1 ? string.Empty : "s")}: {(replayed.PerDollar is { } made ? $"{replayed.Closed} trades closed, {Dollars(made)} made per $1 risked" : "no trade closed yet")}. This is not the live record and is never added to it.</p>", title: "Replayed nights, apart from the live record", region: "judge-replayed"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The line Home draws for the judge: what it picked tonight, or from 24.15 on a past night that night, and its live record
    // per $1 risked beside random picks'.
    // From the operator's correction of 2026-11-23 a night the earlier version picked across the three indices counts its picks,
    // so the line never says none where a version picked.
    public static string JudgeHomeLine(JudgeView view, bool past = false) =>
        (view.AcrossNight is { Picked: > 0 } across
            ? $"<p class=\"judge-home\" data-home=\"judge\" data-across=\"{across.Picked}\"><b>AI stock picker:</b> {Escaped(AcrossHomeWords(across.Night, across.Picked, past))} "
            : $"<p class=\"judge-home\" data-home=\"judge\"><b>AI stock picker</b> Picked {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} {(past ? "that night" : "tonight")} {(view.PicksFrom is { } from ? $"in the account of {Escaped(VersionWords(from))}, kept apart from its record" : "in its own paper account")}. ")
        + (view.Score.PerDollar is { } made
            ? $"Since it started: {Dollars(made)} per $1 risked, against {(view.RandomPerDollar is { } random ? Dollars(random) : "nothing yet")} for random picks. "
            : "No trade of it has closed yet. ")
        + $"<a href=\"{JudgeRoute}\">See the AI stock picker</a></p>";

    // "picked 5 stocks from all 1,500 on the night of 20 Nov, before it picked within each index. Bought at today's open in its
    // own paper account."
    public static string AcrossHomeWords(DateOnly night, int picked, bool past = false) =>
        string.Create(CultureInfo.InvariantCulture, $"picked {picked} stock{(picked == 1 ? string.Empty : "s")} from all 1,500 on the night of {night:d MMM}, before it picked within each index. Bought at {(past ? "the next" : "today's")} open in its own paper account.");

    static string RunLine(JudgeView view) =>
        view.Shown is null ? "<p class=\"empty\">The AI stock picker has not run yet. The night starts it after its last step.</p>"
        : view.Night is not { } night || view.Outcome is "picked" or "none" or null ? string.Empty
        : $"<p class=\"judge-run\" data-outcome=\"{Escaped(view.Outcome)}\">The picker's run for {night.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} was {Escaped(view.Outcome)}{(string.IsNullOrEmpty(view.Why) ? string.Empty : ": " + Escaped(view.Why))}. A night it misses is never filled in later.</p>";

    // From 24.14 where an earlier judge version picked before the newest, the night the record counts from; from the 24.14
    // correction of 2026-11-21 with the newest version's number, each earlier version's picks walked in an account of its own.
    static string CountedFromLine(JudgeView view) =>
        view.CountedFrom is { } from
            ? $"<p class=\"judge-version\" data-version=\"counted\">{Escaped(CountedFromWords(from, view.RecordNumber ?? 2))}</p>"
            : string.Empty;

    public static string CountedFromWords(DateOnly from, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"Its record counts version {number}'s picks alone, from the night of ")
        + from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        + ". Each earlier version's picks are bought and walked to their exits in an account of their own, drawn apart under its number and never added to the record.";

    // From 24.24 what Tonight's card says of the night: each index's count of marks and its best quarter, or before then the
    // count it chose.
    public static string TonightWords(JudgeView view, UniverseChoice chosen) =>
        view.Marked is { } marked ? JudgeNight.MarkedWords(chosen.Name, marked, view.Picks.Count, view.Past)
        : view.Picks.Count == 0 ? $"The AI stock picker chose no stock {(view.Past ? "that night" : "tonight")}."
        : string.Create(CultureInfo.InvariantCulture, $"The AI stock picker chose {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} {(view.Past ? "that night" : "tonight")}, best first.");

    // An earlier version's name: its number and the nights it picked on; from 24.24 a variant's, as its version's variant.
    public static string VersionWords(JudgeVersionView version) =>
        (version.Variant is { } variant
            ? string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(variant[0])}{variant[1..]}, version {version.Number}'s variant, picked ")
            : string.Create(CultureInfo.InvariantCulture, $"Version {version.Number}, picked "))
        + version.First.ToString("d MMM", CultureInfo.InvariantCulture)
        + (version.Last > version.First ? " to " + version.Last.ToString("d MMM", CultureInfo.InvariantCulture) : string.Empty);

    // What an earlier version's account holds: its picks, where each was bought, and its closed trades.
    public static string VersionSummaryWords(JudgeVersionView version, int? recordNumber)
    {
        var count = version.Picks.Count;
        var opens = version.Picks.Count > 0 && version.Picks.All(pick => pick.Range is not null) ? "each within its range"
            : version.Picks.Any(pick => pick.Range is not null) ? "at the open before its range began and within its range from then"
            : version.First == version.Last ? "at the open on " + Open(version.First).ToString("ddd d MMM", CultureInfo.InvariantCulture) : "each at the open after its night";
        var closed = version.Score.Closed == 0
            ? "None of its trades has closed yet."
            : string.Create(CultureInfo.InvariantCulture, $"{version.Score.Closed} of its trades {(version.Score.Closed == 1 ? "has" : "have")} closed, {version.Score.Winners} of them winners") + (version.Score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked." : ".");

        return string.Create(CultureInfo.InvariantCulture, $"{VersionWords(version)}: {count} pick{(count == 1 ? string.Empty : "s")}, bought {opens} in this version's own account and walked to their exits by their plans, the same money, sizing and costs as the record's. {BoughtSoFarWords(version.Picks)} They are never added to ")
            + (recordNumber is { } number ? string.Create(CultureInfo.InvariantCulture, $"version {number}'s record. ") : "the record. ")
            + closed;
    }

    // From the operator's order of 2026-11-24, item 13, what an account did with its picks as the store holds it: bought,
    // waiting within the order a range places or for the open before the range began, and not bought, by which side of its
    // range it opened on or for want of cash.
    // see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
    public static string BoughtSoFarWords(IReadOnlyList<JudgeCardView> picks)
    {
        var bought = picks.Count(pick => pick.Held is { Skipped: null });
        var inRange = picks.Count(pick => pick.Held is null && pick.Range is not null);
        var atOpen = picks.Count(pick => pick.Held is null && pick.Range is null);
        var above = picks.Count(pick => pick.Held is { Skipped: JudgeAccounts.OpenedAbove });
        var below = picks.Count(pick => pick.Held is { Skipped: JudgeAccounts.OpenedBelow });
        var cash = picks.Count(pick => pick.Held is { Skipped: Core.Cards.PaperAccount.UnderTheCash });
        var sides = new List<string>();

        sides.AddRange(above > 0 ? [string.Create(CultureInfo.InvariantCulture, $"{above} opened above its range")] : []);
        sides.AddRange(below > 0 ? [string.Create(CultureInfo.InvariantCulture, $"{below} below it")] : []);
        sides.AddRange(cash > 0 ? [string.Create(CultureInfo.InvariantCulture, $"{cash} for want of cash")] : []);

        return string.Create(CultureInfo.InvariantCulture, $"Bought so far: {bought} of {picks.Count}")
            + (inRange > 0 ? string.Create(CultureInfo.InvariantCulture, $"; waiting within the {Core.Loop.BuyRanges.OrderSessions}-day order: {inRange}") : string.Empty)
            + (atOpen > 0 ? string.Create(CultureInfo.InvariantCulture, $"; waiting for the open after its night: {atOpen}") : string.Empty)
            + string.Create(CultureInfo.InvariantCulture, $"; not bought: {above + below + cash}")
            + (sides.Count > 0 ? " (" + string.Join(", ", sides) + ")" : string.Empty)
            + ".";
    }

    static string VersionSummary(JudgeVersionView version, int? recordNumber) =>
        $"<p class=\"judge-version\" data-version=\"{version.Number}\" data-summary>{Escaped(VersionSummaryWords(version, recordNumber))}</p>";

    // The first session the exchange trades after a night, the open its picks buy at.
    static DateOnly Open(DateOnly night)
    {
        var day = night.AddDays(1);

        while (!Core.Bars.ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    // Each earlier version's account drawn apart beneath the four, labelled with its number and never added to them.
    static string ApartCharts(JudgeView view) =>
        string.Concat(view.Earlier.Select(version =>
            $"<div class=\"judge-apart\" data-apart=\"{version.Number.ToString(CultureInfo.InvariantCulture)}\"><p class=\"judge-apart-h\"><b>{Escaped(VersionWords(version))}, apart from the record.</b> Its own account, the same money, sizing and costs, never added to the four above.</p>"
            + JudgeChart([new JudgeLineView(VersionWords(version) + ", its own account", version.Values)], [], VersionWords(version) + "'s own account") + "</div>"))
        + (view.Across is { } across
            ? $"<div class=\"judge-apart\" data-apart=\"across\"><p class=\"judge-apart-h\"><b>{Escaped(AcrossName)}, apart from the record.</b> Its own account, the same money, sizing and costs, never added to the four above.</p>"
                + JudgeChart([new JudgeLineView(AcrossName + ", its own account", across.Values)], [], AcrossName + "'s own account") + "</div>"
            : string.Empty);

    // From the operator's order of 2026-11-24, item 11, the fixed 5 on the night shown beside its version: where it took the
    // same stocks, one line saying so with why its limit or the quarter decided, its account's figure and the stocks as chips;
    // where they differ, the shared stocks as chips and only the picks that differ as cards. Then what its account did with
    // its picks, its own chart, and the cards of its picks on the other nights.
    // see: The AI stock picker's page draws a variant's picks as cards only where they differ from its version's that night
    static string FixedFiveBody(JudgeView view, JudgeVersionView five, UniverseChoice chosen)
    {
        var html = new StringBuilder($"<p class=\"judge-version\" data-version=\"fixed-five\" data-summary>{Escaped(FixedFiveLine)}</p>");
        JudgeCardView[] its = [.. five.Picks.Where(pick => pick.Night == view.Shown)];
        var versions = view.Picks.Select(pick => pick.Ticker).ToHashSet(StringComparer.Ordinal);
        JudgeCardView[] shared = [.. its.Where(pick => versions.Contains(pick.Ticker))];
        JudgeCardView[] differ = [.. its.Where(pick => !versions.Contains(pick.Ticker))];

        if (its.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"judge-same\" data-same=\"{(differ.Length == 0 && shared.Length == view.Picks.Count ? "all" : shared.Length.ToString(CultureInfo.InvariantCulture))}\"><b>{Escaped(SameWords(shared.Length, differ.Length, view.Past))}</b> {Escaped(FixedFiveWhy(view.Marked, view.Picks.Count))} {Escaped(OwnAccountWords(five))}</p>");
            html.Append(shared.Length > 0 ? "<p class=\"judge-chips\">" + string.Concat(shared.Select(pick => $"<span class=\"judge-chip\" data-ticker=\"{Escaped(pick.Ticker)}\">{Escaped(pick.Ticker)}</span>")) + "</p>" : string.Empty);
        }

        html.Append($"<p class=\"judge-bought\" data-bought>{Escaped(VersionSummaryWords(five, view.RecordNumber))}</p>");
        html.Append(JudgeChart([new JudgeLineView(VersionWords(five) + ", its own account", five.Values)], [], VersionWords(five) + "'s own account"));
        html.Append(PickCards([.. differ, .. five.Picks.Where(pick => pick.Night != view.Shown)], five.Score, five, "fixed-five", chosen));

        return html.ToString();
    }

    // "Tonight it took the same 5 picks as the version above.", or how many it shared and how many differ.
    public static string SameWords(int shared, int differ, bool past = false) =>
        differ == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{(past ? "That night" : "Tonight")} it took the same {shared} pick{(shared == 1 ? string.Empty : "s")} as the version above.")
            : string.Create(CultureInfo.InvariantCulture, $"{(past ? "That night" : "Tonight")} it shared {shared} of its picks with the version above; {differ} differ{(differ == 1 ? "s" : string.Empty)}, drawn as {(differ == 1 ? "a card" : "cards")} below.");

    // Why a fixed 5 and its version took what they took: the version takes the best quarter of what it marked and at most its
    // limit, so where a quarter is above the limit the limit decided and the fixed 5 takes the same stocks.
    public static string FixedFiveWhy(int? marked, int picked)
    {
        if (marked is not { } count)
        {
            return string.Empty;
        }

        var quarter = (count + JudgePrompt.QuarterOf - 1) / JudgePrompt.QuarterOf;

        return quarter > JudgePrompt.MostPicks
            ? string.Create(CultureInfo.InvariantCulture, $"The version takes the best quarter of the stocks it marked, at most {JudgePrompt.MostPicks}. A quarter of {count} is {quarter}, so its limit of {JudgePrompt.MostPicks} decided.")
            : string.Create(CultureInfo.InvariantCulture, $"The version takes the best quarter of the stocks it marked, at most {JudgePrompt.MostPicks}. A quarter of {count} is {quarter}, so the quarter decided and it picked {picked}.");
    }

    // "Its own account: 0.0% since Tue 24 Nov, 0 open, 0 closed."
    public static string OwnAccountWords(JudgeVersionView version)
    {
        var first = version.Values.Count > 0 ? version.Values[0].Value : 0m;
        var last = version.Values.Count > 0 ? version.Values[^1].Value : 0m;
        var change = Math.Round(ReturnFrom(last, first) * 100, 1);

        // A change rounding to nothing reads 0.0%, never -0.0%.
        change = change == 0.0 ? 0.0 : change;
        var open = version.Picks.Count(pick => pick.Held is { Skipped: null, Ended: null });

        return string.Create(CultureInfo.InvariantCulture, $"Its own account: {change:0.0}% since {Open(version.First):ddd d MMM}, {open} open, {version.Score.Closed} closed.");
    }

    // From 24.13, until any of the judge's live picks has been bought, why the scoreboard and the four accounts hold nothing,
    // the night and the open read from its first stored picks; none once one is bought, or before it has picked.
    public static string? EmptyRecord(JudgeView view) =>
        (view.RecordTile ?? view.Own) is { Started: false } waiting
            ? "<p class=\"empty\" data-empty=\"record\" data-started=\"no\" data-summary>" + Escaped(NotStartedWords(waiting.StartsOn)) + "</p>"
        : view.Bought || view.FirstNight is not { } night || view.FirstOpen is not { } open
            ? null
            : "<p class=\"empty\" data-empty=\"record\" data-summary>" + Escaped(EmptyRecordWords(night, open, view.RecordNumber)) + "</p>";

    public static string EmptyRecordWords(DateOnly night, DateOnly open, int? number = null) =>
        (number is { } version ? string.Create(CultureInfo.InvariantCulture, $"Version {version}'s first picks") : "The picker's first picks")
        + ", made on the night of " + night.ToString("d MMM", CultureInfo.InvariantCulture)
        + ", are bought at the open on " + open.ToString("ddd d MMM", CultureInfo.InvariantCulture)
        + (number is null ? ". The accounts start from then." : ". Its record and the four accounts start from then.");

    // The scoreboard: its line, and its tiles and verdict once a pick is bought; before then the line alone, the four accounts
    // saying when the first picks buy.
    static string Scoreboard(JudgeView view, bool tiles = true)
    {
        var score = view.Score;
        var html = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<p data-summary>{(score.Closed == 0 ? "None of the picker's trades has closed yet" : $"{score.Closed} of the picker's trades have closed, {score.Winners} of them winners")}; a firm verdict waits for {score.Needed}.</p>"));

        if (!tiles)
        {
            return html.ToString();
        }

        html.Append("<div class=\"judge-tiles\">");

        html.Append(CultureInfo.InvariantCulture, $"<div class=\"tile\" data-tile=\"closed\"><b>{score.Closed}</b> trades closed<span>{score.Closed} of {score.Needed} needed for a firm verdict</span></div>");
        html.Append(score.Closed == 0
            ? "<div class=\"tile\" data-tile=\"winners\"><b>None</b> closed yet</div>"
            : string.Create(CultureInfo.InvariantCulture, $"<div class=\"tile\" data-tile=\"winners\"><b>{100.0 * score.Winners / score.Closed:0}%</b> were winners</div>"));
        html.Append(score.PerDollar is { } made ? $"<div class=\"tile\" data-tile=\"made\"><b>{Dollars(made)}</b> made per $1 risked</div>" : string.Empty);
        html.Append(score.OverSamePlan is { } over ? $"<div class=\"tile\" data-tile=\"over\"><b>{Dollars(over)}</b> {(over < 0 ? "less" : "more")} per $1 than the same plans bought on every stock those nights</div>" : string.Empty);
        html.Append("</div>");
        html.Append($"<p class=\"judge-verdict\" data-verdict=\"{Escaped(view.AgainstRandom)}\"><b>{Escaped(view.AgainstRandom)}</b> against random picks so far. Reads \"Better\", \"Worse\" or \"Not told apart yet\".</p>");

        return html.ToString();
    }

    // An earlier version's cards, or its variant's, each saying what that version's account did with it and its prices worked
    // from the price it paid once bought.
    static string PickCards(IReadOnlyList<JudgeCardView> picks, JudgeScoreboard score, JudgeVersionView version, string scope, UniverseChoice chosen) =>
        picks.Count == 0
            ? string.Empty
            : "<div class=\"judge-ideas\">" + string.Concat(picks.Select((pick, at) => PickCard(pick, score, version, string.Create(CultureInfo.InvariantCulture, $"judge-{scope}-{at + 1}"), chosen))) + "</div>";

    // Tonight's picks as a carousel: a chip for each pick in rank order, a pick its account had no cash for marked on its
    // chip, the place and the arrows of the pick shown, and one card at a time. Radio inputs choose the card, so it works
    // with no script; a night of one pick, or more than the carousel holds, draws its cards one under another.
    static string Carousel(IReadOnlyList<JudgeCardView> picks, JudgeScoreboard score, UniverseChoice chosen, string slides = "judge-slide", string cardIds = "judge-tonight", JudgeVersionView? version = null)
    {
        if (picks.Count == 0)
        {
            return string.Empty;
        }

        // A card of the earlier version names its own index, and its facts stand among that index's stocks.
        var cards = picks.Select((pick, at) => PickCard(pick, score, version, string.Create(CultureInfo.InvariantCulture, $"{cardIds}-{at + 1}"), pick.Index is { } own ? Universes.ByCode(own) ?? chosen : chosen)).ToArray();

        if (picks.Count == 1 || picks.Count > CarouselHolds)
        {
            return "<div class=\"judge-ideas\">" + string.Concat(cards) + "</div>";
        }

        string Slide(int at) => string.Create(CultureInfo.InvariantCulture, $"{slides}-{at + 1}");

        var html = new StringBuilder("<div class=\"judge-car\" data-carousel>");

        for (var at = 0; at < picks.Count; at++)
        {
            html.Append($"<input type=\"radio\" class=\"judge-slide\" name=\"{slides}\" id=\"{Slide(at)}\"{(at == 0 ? " checked" : string.Empty)}>");
        }

        html.Append("<div class=\"car-head\"><div class=\"car-chips\">");

        for (var at = 0; at < picks.Count; at++)
        {
            var unfunded = picks[at].Held is { Skipped: Core.Cards.PaperAccount.UnderTheCash };
            var outside = picks[at].Held is { Skipped: JudgeAccounts.OpenedAbove or JudgeAccounts.OpenedBelow };
            var skipped = unfunded ? ", not bought: no cash" : outside ? ", not bought: outside its range" : string.Empty;

            html.Append(CultureInfo.InvariantCulture, $"<label class=\"car-chip{(unfunded || outside ? " skip" : string.Empty)}\" for=\"{Slide(at)}\">{Escaped(picks[at].Ticker)}<span>{picks[at].Rank}{skipped}</span></label>");
        }

        html.Append("</div><div class=\"car-navs\">");

        for (var at = 0; at < picks.Count; at++)
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"car-nav\"><span>{at + 1} of {picks.Count}</span><label for=\"{Slide((at + picks.Count - 1) % picks.Count)}\" aria-label=\"Previous pick\">&#8249;</label><label for=\"{Slide((at + 1) % picks.Count)}\" aria-label=\"Next pick\">&#8250;</label></div>");
        }

        html.Append("</div></div><div class=\"judge-slides\">").Append(string.Concat(cards)).Append("</div></div>");

        return html.ToString();
    }

    // The most picks the carousel holds, the most an index's version picks a night.
    public const int CarouselHolds = JudgePrompt.MostPicks;

    // One pick's card: its place among the night's picks, the stock, what the AI saw, and two tabs, why it picked it and the
    // plan code set, chosen by radio inputs so they work with no script.
    static string PickCard(JudgeCardView pick, JudgeScoreboard score, JudgeVersionView? version, string id, UniverseChoice chosen)
    {
        var html = new StringBuilder();
        var place = pick.Night is { } on
            ? string.Create(CultureInfo.InvariantCulture, $"Pick {pick.Rank} of {pick.Of} on the night of ") + on.ToString("d MMM", CultureInfo.InvariantCulture) + (pick.Index is not null ? ", a stock of the " + chosen.Name : string.Empty)
            : string.Create(CultureInfo.InvariantCulture, $"Pick {pick.Rank} of {pick.Of} tonight, ") + chosen.Name;

        html.Append($"<article class=\"home-idea judge-card\" data-ticker=\"{Escaped(pick.Ticker)}\">");
        html.Append($"<div class=\"home-idea-h\"><div><div class=\"judge-rank\">{Escaped(place)}</div><div class=\"home-ticker\"><a href=\"{MarkRenderer.ChartRoute}{Escaped(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></div><div class=\"home-co\">{Escaped(pick.Company ?? string.Empty)}</div></div><span class=\"tag-stack\"><span class=\"ai-tag\">AI stock picker</span><span class=\"ai-note\">Not one of the rules' buys</span></span></div>");
        html.Append($"<p class=\"judge-saw\">It saw: {Escaped(pick.Sector is null or JudgeFacts.NotOnFile ? "no sector on file" : pick.Sector)}, {Escaped(SizeWords(pick.Size))}. No name, ticker or date.</p>");

        if (version is not null)
        {
            html.Append($"<p class=\"judge-since\" data-since>{Escaped(HeldWords(pick, version))}</p>");
        }
        else if (pick.Held is { Skipped: Core.Cards.PaperAccount.UnderTheCash })
        {
            html.Append($"<p class=\"judge-since\" data-cash>{Escaped(NoCashLine)}</p>");
        }
        else if (pick.Held is { Skipped: JudgeAccounts.OpenedAbove or JudgeAccounts.OpenedBelow } outside)
        {
            html.Append($"<p class=\"judge-since\" data-range-skip>{Escaped(RangeNotBoughtLine(outside))}</p>");
        }

        html.Append($"<input type=\"radio\" class=\"judge-tab\" name=\"{id}-tab\" id=\"{id}-why\" value=\"why\" checked><input type=\"radio\" class=\"judge-tab\" name=\"{id}-tab\" id=\"{id}-plan\" value=\"plan\">");
        html.Append($"<nav class=\"judge-tabs\" aria-label=\"Choose what the card shows\"><label for=\"{id}-why\" data-tab=\"why\">{Escaped(WhyTab)}</label><label for=\"{id}-plan\" data-tab=\"plan\">{Escaped(PlanTab)}</label></nav>");
        html.Append("<div class=\"judge-panel\" data-tab=\"why\">").Append(WhyPanel(pick, chosen)).Append("</div>");
        html.Append("<div class=\"judge-panel\" data-tab=\"plan\">").Append(PlanPanel(pick, score, version)).Append("</div>");
        html.Append("</article>");

        return html.ToString();
    }

    public const string WhyTab = "Why it picked it";

    public const string PlanTab = "The plan";

    // Why it picked it: its sentence in quotes, the facts it named each with where it stands among its index's stocks, and
    // the rest of what it saw folded and grouped; a pick made before it named its facts folds everything it saw, grouped,
    // under its count.
    static string WhyPanel(JudgeCardView pick, UniverseChoice chosen)
    {
        var html = new StringBuilder();

        html.Append($"<p class=\"facts-h\">{Escaped(WhyTab)}</p>");
        html.Append($"<p class=\"ai-quote\">\"{Escaped(pick.Reason)}\" <small>Its sentence, as your local model wrote it.</small></p>");

        if (pick.Named is { } named)
        {
            html.Append($"<p class=\"facts-h\">The facts it named, and where each stands among the {Escaped(chosen.Name)} stocks</p>");
            html.Append(FactTable(named, "named", "why", chosen));
            html.Append($"<p class=\"tenth-cap\">{Escaped(TenthCaption(chosen))}</p>");
            html.Append(CultureInfo.InvariantCulture, $"<details class=\"all-facts\"><summary>Everything else it saw ({pick.Rest.Count} facts), grouped</summary>").Append(Grouped(pick.Rest, "rest", chosen)).Append("</details>");
        }
        else
        {
            html.Append(CultureInfo.InvariantCulture, $"<details class=\"all-facts\" data-facts=\"before\"><summary>{Escaped(BeforeNamedWords(pick.Rest.Count))}</summary>").Append(Grouped(pick.Rest, "sheet", chosen)).Append("</details>");
        }

        return html.ToString();
    }

    // The plan code set: the buy at the next open, the take-profit, the half that trails where it does, the stop and the time
    // limit as one ladder, the risk against the reward, how the prices are worked and the record it is counted in.
    static string PlanPanel(JudgeCardView pick, JudgeScoreboard score, JudgeVersionView? version)
    {
        var held = pick.Held is { Skipped: null } taken ? taken : null;
        var from = held?.Paid ?? pick.LastClose;
        var stop = held?.Stop ?? pick.Stop;
        var target = held?.Target ?? pick.Target;
        var risk = from - stop;
        var moves = JudgeMenu.HoldOf(pick.Hold)?.StopMoves;
        var whole = string.Equals(pick.Shape, JudgeMenu.Whole, StringComparison.Ordinal);
        var html = new StringBuilder();

        html.Append("<p class=\"facts-h\">The plan code set</p><div class=\"judge-ladder\">");
        html.Append(BuyRow(pick, held));
        html.Append($"<div class=\"lad-row\" data-sale=\"target\"><i class=\"dot tp\"></i><span>{(whole ? "Take profit at" : "Take profit on half at")}</span><b class=\"aim\">${Figures.Price(target)}</b><span class=\"sale-note judge-target\">2 times the risk: ${Figures.Price(2 * risk)} up for ${Figures.Price(risk)} down.</span></div>");

        if (!whole)
        {
            html.Append($"<div class=\"lad-row\" data-sale=\"trail\"><i class=\"dot tp\"></i><span>The other half</span><b class=\"aim\">Trails</b><span class=\"sale-note\">Sells on a close under a give-up price that rises behind its highest close, ${Figures.Price(held?.Trail ?? pick.GiveUp ?? risk)} under it. Its stop moves up to the price it paid once the first half sells.</span></div>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<div class=\"lad-row\" data-sale=\"stop\"><i class=\"dot sl\"></i><span>Sell if it closes below</span><b class=\"stop\">${Figures.Price(stop)}</b><span class=\"sale-note judge-stop\">{moves:0.##} typical daily move{(moves == 1 ? string.Empty : "s")} under the price it {(held is null ? "pays" : "paid")}.</span></div>");
        html.Append($"<div class=\"lad-row\" data-sale=\"limit\"><i class=\"dot\"></i><span>Otherwise sell after</span><b class=\"aim judge-hold\">{Escaped(pick.Hold)}</b></div>");
        html.Append($"<div class=\"judge-rr\"><div class=\"home-rr\" role=\"img\" aria-label=\"Risks $1 to make about $2.00\"><div class=\"risk\" style=\"flex:1\"></div><div class=\"reward\" style=\"flex:2\"></div></div><span class=\"home-odds\">Risks $1 to make about $2.00{(whole ? string.Empty : " on the first half")}</span></div>");
        html.Append("</div>");
        html.Append($"<p class=\"home-cond\">{(held is null ? "The prices are worked from the last close until it is bought, then again from the price it actually paid." : "The prices are worked from the price it paid.")} {Escaped(JudgeMenuLine)}</p>");
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"setup-record\"><span class=\"rec-h\">{(version is null ? "The AI stock picker's record" : Escaped(VersionWords(version)) + ": its own account")}</span><span>{(score.Closed == 0 ? $"Too early to tell: <b>0</b> of {score.Needed} trades closed." : $"<b>{score.Closed}</b> of {score.Needed} trades closed{(score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked" : string.Empty)}.")} {(version is null ? "It is measured against the rules, 1,000 random picks and holding the index, with the same money, sizing and costs." : "Kept apart from the record, with the same money, sizing and costs.")}</span><span class=\"rec-sub\">{(version is null ? "The picker's record over every pick it made, not this stock's chance." : "This version's record over every pick it made, not this stock's chance.")}</span></div>");

        return html.ToString();
    }

    // The plan's buy: bought, at the price paid and the session; in a range, the range in the operator's words with what an
    // open above or below it does; or for a pick of a night before the range began, the next open. None says what price it
    // opens at.
    // see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
    static string BuyRow(JudgeCardView pick, JudgeHeld? held)
    {
        var close = $"Last close {(held is null ? string.Empty : "before it ")}${Figures.Price(pick.LastClose)}.";

        if (held is not null)
        {
            var how = pick.Range is null ? "At the open on " : "Within its range on ";

            return $"<div class=\"lad-row\" data-sale=\"buy\"><i class=\"dot lc\"></i><span>Bought at</span><b>${Figures.Price(held.Paid)}</b><span class=\"sale-note judge-buy\">{how}{held.Bought.ToString("ddd d MMM", CultureInfo.InvariantCulture)}. {close}</span></div>";
        }

        if (pick.Range is not { } range)
        {
            return $"<div class=\"lad-row\" data-sale=\"buy\"><i class=\"dot lc\"></i><span>Buys at</span><b>the next open</b><span class=\"sale-note judge-buy\">{Escaped(BeforeRangeLine)} {close}</span></div>";
        }

        var lines = Core.Loop.BuyRanges.PlanLines(range);
        var buys = lines[0];
        var verb = buys.IndexOf(' ', StringComparison.Ordinal);

        return $"<div class=\"lad-row\" data-sale=\"buy\" data-range><i class=\"dot lc\"></i><span>{Escaped(buys[..verb])}</span><b>{Escaped(buys[(verb + 1)..])}</b><span class=\"sale-note judge-buy\">{Escaped(string.Join(' ', lines.Skip(1)))} {close}</span></div>";
    }

    // The buy of a pick made on a night before the range began, which its account bought at the open.
    public const string BeforeRangeLine = "Picked before its range began, so it buys at the open.";

    // A pick its range did not buy, on the record's card: why, and what buying at the open would have made where its walk
    // there has ended.
    public static string RangeNotBoughtLine(JudgeHeld held) =>
        $"Not bought: {held.Skipped}. "
        + (held.Result is { } made
            ? $"Bought at the open it would have {(made < 0 ? "lost" : "made")} {Dollars(Math.Abs(made))} per $1 risked."
            : "What buying at the open would have made is recorded once its walk there ends.");

    // The groups everything else it saw is folded under, each holding its fields in the sheet's order; a field no group
    // names falls to the last.
    public static IReadOnlyList<(string Name, IReadOnlyList<string> Keys)> FactGroups { get; } =
    [
        ("Price and trend", ["return_month", "return_quarter", "return_twelve_less_one", "strength", "high_ratio", "close_over_fifty", "close_over_long", "rsi", "volume_ratio", "volatility_month", "depth", "since_high", "tightness", "reward_to_risk", "stretch_days", "gap_days"]),
        ("The business", ["size", "business_score", "revenue_growth", "growth_change", "gross_margin_change", "operating_margin_change", "cash_over_income", "profit", "coverage", "return_on_assets", "return_on_assets_change", "leverage_fall", "share_fall", "value_place"]),
        ("Earnings and analysts", ["surprise", "estimate_revision_month", "release_results", "release_outlook", "release_tone", "release_demand"]),
        ("Insiders, short interest and news", ["insider_net_buy", "insider_buyers", "insider_officer_buy", "insider_unplanned_sale", "short_interest_share", "news"]),
        ("Market and sector", ["sector", "sector_month", "sector_quarter", "breadth", "vix_over_median"]),
    ];

    public const string OtherFacts = "Other facts";

    static string Grouped(IReadOnlyList<JudgeFactView> facts, string kind, UniverseChoice chosen)
    {
        var html = new StringBuilder();
        var named = FactGroups.SelectMany(group => group.Keys).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, keys) in FactGroups.Append((OtherFacts, [])))
        {
            var held = name == OtherFacts
                ? facts.Where(fact => !named.Contains(fact.Key)).ToArray()
                : facts.Where(fact => keys.Contains(fact.Key, StringComparer.Ordinal)).ToArray();

            if (held.Length > 0)
            {
                html.Append($"<p class=\"grp\">{Escaped(name)}</p>").Append(FactTable(held, kind, string.Empty, chosen));
            }
        }

        return html.ToString();
    }

    // Facts as a table of aligned columns: the words, the value, the tenth's bar where it was placed and the comparison.
    static string FactTable(IEnumerable<JudgeFactView> facts, string kind, string row, UniverseChoice chosen) =>
        $"<table class=\"judge-facts\" data-facts=\"{kind}\"><colgroup><col class=\"l\"><col class=\"v\"><col class=\"b\"><col class=\"c\"></colgroup><tbody>"
        + string.Concat(facts.Select(fact => $"<tr class=\"{row}\" data-fact=\"{Escaped(fact.Key)}\"><td class=\"l\">{Escaped(fact.Words)}</td><td class=\"v\">{Escaped(fact.Value)}</td><td class=\"b\">{TenthBar(fact.Tenth, chosen)}</td><td class=\"c\">{Escaped(fact.Comparison ?? string.Empty)}</td></tr>"))
        + "</tbody></table>";

    // A figure's place among its index's stocks: ten cells, lowest tenth on the left, its own tenth filled; nothing for a
    // fact that was not placed.
    public static string TenthBar(int? tenth, UniverseChoice chosen)
    {
        if (tenth is not { } held)
        {
            return string.Empty;
        }

        var cells = string.Concat(Enumerable.Range(1, JudgeFacts.Tenths).Select(cell => cell == held ? "<i class=\"on\"></i>" : "<i></i>"));

        return string.Create(CultureInfo.InvariantCulture, $"<div class=\"tenth\" data-tenth=\"{held}\" role=\"img\" aria-label=\"Tenth {held} of {JudgeFacts.Tenths} among the {Escaped(chosen.Name)} stocks, lowest on the left\">{cells}</div>");
    }

    public static string TenthCaption(UniverseChoice chosen) =>
        $"Each bar: where the stock sits among the {chosen.Name} stocks, lowest tenth on the left. A fact with no bar is a word, or one figure for every stock of the index.";

    // Tonight's line under the count of marks: the cap, the pick past the cash, and the range each buys in.
    public static string TonightNote { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"Up to {JudgePrompt.MostPicks} a night in each index. A pick the account has no cash for, or whose price stays outside its range, is shown and marked \"not bought\", so you can see what it missed. Each buys only within its range: from {JudgeAccounts.Widths.Floor:0.##} typical daily moves under the last close to {JudgeAccounts.Widths.Ceiling:0.##} of one above it. Opening above, it waits up to {Core.Loop.BuyRanges.OrderSessions} days for the price to come back; opening below, it is not bought.");

    public const string TilesLede = "It picks small caps against small caps, mid caps against mid caps and large caps against large caps. Each index has its own account, measured against that index's rules, random picks from the same index and that index's fund.";

    // The three indices' tiles, each its account's return, its trades open and closed and its picks tonight, the index the
    // page reads marked as showing; each opens the page for its index.
    static string IndexTiles(IReadOnlyList<JudgeTile> tiles, UniverseChoice chosen, DateOnly? pastNight = null)
    {
        var html = new StringBuilder("<div class=\"idx-tiles\">");
        var past = pastNight is not null;
        var route = pastNight is { } night ? JudgeNightRoute + Cards.Day(night) : JudgeRoute;

        foreach (var tile in tiles)
        {
            var of = Universes.ByCode(tile.Index) ?? Universes.Large;
            var showing = of == chosen;

            var body = tile.Started
                ? $"<b>{(tile.Return is { } made ? Figures.Share(made) : "No trade yet")}</b><span>{tile.Open} open · {tile.Closed} closed · picked {tile.Tonight} {(past ? "that night" : "tonight")}</span>"
                : $"<b>Not started</b><span data-started=\"no\">{Escaped(NotStartedWords(tile.StartsOn)["Not started. ".Length..])}</span>";

            html.Append(CultureInfo.InvariantCulture, $"<a href=\"{route}?{Universes.Query}={of.Word}\" data-index=\"{Escaped(of.Code)}\"{(showing ? " class=\"on\" aria-current=\"page\"" : string.Empty)}><span>{Escaped(of.Name)}, {Escaped(of.Cap)}s{(showing ? " (showing)" : string.Empty)}</span>{body}</a>");
        }

        return html.Append("</div>").ToString();
    }

    // An index whose own version has not run a night: none of its picks yet, and the night its first come from.
    public static string NotStartedWords(DateOnly? startsOn) =>
        "Not started. " + (startsOn is { } night ? "Its first picks come from the night of " + night.ToString("ddd d MMM", CultureInfo.InvariantCulture) + "." : "Its first picks come from its first night.");

    // The earlier version that picked across the three indices before it picked within each, drawn under its own number.
    public const int AcrossNumber = 0;

    public const string AcrossTitle = "Picked across all 1,500 stocks by the earlier version, before it picked within each index";

    // From the operator's correction of 2026-11-23 the earlier version's picks stand in the region of the night it picked on,
    // in the carousel and tabs, each naming the index its stock belongs to and what its own account did with it. They are
    // bought and walked in its own account alone, never in an index's.
    // see: The AI stock picker's page draws one night at a time, and its count line says what decided the number
    static string AcrossRegion(JudgeVersionView across)
    {
        var count = across.Picks.Count;
        var night = across.Picks[0].Night ?? across.First;
        var closed = across.Score.Closed == 0
            ? "None of its trades has closed yet."
            : string.Create(CultureInfo.InvariantCulture, $"{across.Score.Closed} of its trades {(across.Score.Closed == 1 ? "has" : "have")} closed") + (across.Score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked." : ".");
        var summary = string.Create(CultureInfo.InvariantCulture, $"{count} pick{(count == 1 ? string.Empty : "s")} made on the night of {night:d MMM}, before the picker picked within each index, so none is an index's pick. They are bought and walked in the earlier version's own account, never in an index's account. ") + closed;

        return Cards.Computed("The night of " + night.ToString("d MMM", CultureInfo.InvariantCulture), $"<p class=\"judge-version\" data-version=\"across\" data-summary>{Escaped(summary)}</p>" + Carousel(across.Picks, across.Score, Universes.Large, "judge-across-slide", "judge-across", across), title: AcrossTitle, region: "judge-across");
    }

    // On a later night, while the earlier version holds trades still open, one line under the tiles saying how many and linking
    // to the night it picked on; none once its last trade has closed.
    static string AcrossOpenLine(JudgeView view, UniverseChoice chosen) =>
        view.AcrossStillOpen is { } open && view.AcrossTonight.Count == 0
            ? $"<p class=\"judge-across-open\" data-across-open=\"{open.Open.ToString(CultureInfo.InvariantCulture)}\">{Escaped(AcrossOpenWords(open.Night, open.Open))} <a href=\"{JudgeNightRoute}{Cards.Day(open.Night)}?{Universes.Query}={chosen.Word}\">See that night.</a></p>"
            : string.Empty;

    // "The earlier version, across all 1,500 stocks: 4 trades still open from the night of 20 Nov."
    public static string AcrossOpenWords(DateOnly night, int open) =>
        string.Create(CultureInfo.InvariantCulture, $"The earlier version, across all 1,500 stocks: {open} trade{(open == 1 ? string.Empty : "s")} still open from the night of {night:d MMM}.");

    public const string AcrossName = "The earlier version, across all 1,500 stocks";

    // The earlier version's record in the scoreboard, an earlier version's, apart from the record.
    static string AcrossRecord(JudgeView view) =>
        view.Across is not { } across ? string.Empty
        : $"<p class=\"judge-version\" data-version=\"across\" data-across=\"record\">{Escaped(AcrossName)}, an earlier version kept apart from this record: "
            + Escaped(across.Score.Closed == 0
                ? "none of its trades has closed yet."
                : string.Create(CultureInfo.InvariantCulture, $"{across.Score.Closed} of its {across.Picks.Count} trade{(across.Picks.Count == 1 ? string.Empty : "s")} closed, {across.Score.Winners} of them winners") + (across.Score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked." : "."))
            + "</p>";

    // The picker's own rules for the index the page reads: none yet on any index.
    public const string NoOwnRule = "No rule of its own yet.";

    public static string OwnRulesLede(UniverseChoice chosen) =>
        $"Rules the AI stock picker writes from the {chosen.Possessive} 2005 to 2018, each tested by code and kept only if it held up. Each index keeps its own rules.";

    static string OwnRules(UniverseChoice chosen) =>
        $"<p class=\"empty\" data-rules=\"none\" data-summary>{Escaped(NoOwnRule)} Each rule code keeps for the {Escaped(chosen.Name)} will show here as a row that unfolds into five tabs: How it chooses, Why each step, How it did, Example trades and What changed.</p>";

    // The size of company an index holds as a word before a noun.
    static string CapWord(UniverseChoice chosen) => chosen.Cap.Replace(' ', '-');

    public const string JudgeBeforeNamed = "made before the picker named its facts";

    // The fold of a pick made before the picker named its facts: everything it saw, counted.
    public static string BeforeNamedWords(int facts) =>
        string.Create(CultureInfo.InvariantCulture, $"Everything it saw ({facts} facts): {JudgeBeforeNamed}");

    // What an earlier version's account did with a pick: waiting for its open, skipped and why, bought and still open, or
    // bought and sold with its result per $1 risked.
    public static string HeldWords(JudgeCardView pick, JudgeVersionView version)
    {
        var account = version.Number == AcrossNumber ? "the earlier version's own account" : string.Create(CultureInfo.InvariantCulture, $"version {version.Number}'s own account");

        static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        // From the operator's order of 2026-11-24, item 12, a pick waiting to buy says how it buys in the approved words: within
        // its range from the range's first night, at the open before it.
        // see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
        return pick.Held switch
        {
            null when pick.Range is { } range => string.Join(' ', Core.Loop.BuyRanges.PlanLines(range)) + $" In {account}.",
            null => $"{BeforeRangeLine} In {account}.",
            { Skipped: Core.Cards.PaperAccount.UnderTheCash } => $"Not bought: no cash, in {account}. Its result is still recorded.",
            { Skipped: { } why } => $"Not bought in {account}: {why}.",
            { Ended: { } ended, Sold: { } sold } held => $"Bought on {Day(held.Bought)} at ${Figures.Price(held.Paid)} and sold on {Day(ended)} at ${Figures.Price(sold)}"
                + (held.Result is { } made ? $", {(made < 0 ? "losing" : "making")} {Dollars(Math.Abs(made))} per $1 risked" : string.Empty) + $", in {account}.",
            { } held => $"Bought on {Day(held.Bought)} at ${Figures.Price(held.Paid)} and still open in {account}.",
        };
    }

    static string SizeWords(string? size) => size switch
    {
        null or "" or JudgeFacts.NotOnFile => "a company whose size is not on file",
        _ => "a " + size + " company",
    };

    static string LookedSummary(JudgeView view)
    {
        int Picks(string group) => view.Looked.FirstOrDefault(row => row.Group == group)?.Picks ?? 0;

        return string.Create(CultureInfo.InvariantCulture, $"<p data-summary>{Picks(JudgeScore.NoSetupFound)} of its picks were stocks no rule found, it shared {Picks(JudgeScore.Shared)} with the rules, and it passed over {Picks(JudgeScore.PassedOver)} of theirs.</p>");
    }

    static string Looked(JudgeView view)
    {
        var html = new StringBuilder("<table class=\"judge-looked\"><thead><tr><th>Group</th><th>Picks</th><th>Closed</th><th>Per $1 risked</th></tr></thead><tbody>");

        foreach (var group in view.Looked)
        {
            var words = group.Group switch
            {
                JudgeScore.NoSetupFound => "Stocks the AI chose that no rule picked that night.",
                JudgeScore.Shared => "Stocks the AI and a rule both chose.",
                _ => "Stocks a rule chose that the AI did not keep.",
            };

            html.Append(CultureInfo.InvariantCulture, $"<tr data-group=\"{Escaped(group.Group)}\"><td><b>{Escaped(group.Group)}</b><br>{Escaped(words)}</td><td>{group.Picks}</td><td>{group.Closed}</td><td>{(group.PerDollar is { } made ? Dollars(made) : "none closed")}</td></tr>");
        }

        html.Append("</tbody></table>");

        return html.ToString();
    }

    // The four accounts as their return from the first session drawn, each a line, the random accounts' band shaded behind; or
    // from the 24.14 correction of 2026-11-21 an earlier version's own account alone.
    static string JudgeChart(IReadOnlyList<JudgeLineView> drawn, IReadOnlyList<JudgeBand> drawnBand, string label)
    {
        var lines = drawn.Where(line => line.Values.Count > 0).ToArray();

        if (lines.Length == 0)
        {
            return "<p class=\"empty\">No account has a session to draw yet.</p>";
        }

        var sessions = lines.SelectMany(line => line.Values.Select(value => value.Session)).Distinct().Order().ToArray();
        var at = sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var returns = lines.Select(line => (line.Name, Points: line.Values.Select(value => (at[value.Session], ReturnFrom(value.Value, line.Values[0].Value))).ToArray())).ToArray();
        var band = drawnBand.Where(row => at.ContainsKey(row.Session)).Select(row => (X: at[row.Session], Low: ReturnFrom(row.Low, drawnBand[0].Median), High: ReturnFrom(row.High, drawnBand[0].Median))).ToArray();
        var all = returns.SelectMany(line => line.Points.Select(point => point.Item2)).Concat(band.SelectMany(row => new[] { row.Low, row.High })).Append(0.0).ToArray();
        var (low, high) = (all.Min(), all.Max());

        if (high - low < 0.01)
        {
            (low, high) = (low - 0.01, high + 0.01);
        }

        const double Width = 720, Height = 240;

        string X(int index) => (sessions.Length == 1 ? Width / 2 : index * Width / (sessions.Length - 1)).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double value) => (Height - ((value - low) / (high - low) * Height)).ToString("0.#", CultureInfo.InvariantCulture);

        var svg = new StringBuilder($"<svg class=\"judge-chart\" viewBox=\"0 0 {Width} {Height}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{Escaped(label)}\">");

        if (band.Length > 0)
        {
            var edge = band.Select(row => $"{X(row.X)},{Y(row.High)}").Concat(band.Reverse().Select(row => $"{X(row.X)},{Y(row.Low)}"));

            svg.Append($"<polygon class=\"judge-band\" points=\"{string.Join(' ', edge)}\"/>");
        }

        svg.Append($"<line class=\"judge-zero\" x1=\"0\" x2=\"{Width}\" y1=\"{Y(0)}\" y2=\"{Y(0)}\"/>");

        for (var line = 0; line < returns.Length; line++)
        {
            svg.Append($"<polyline class=\"judge-line judge-line-{line}\" points=\"{string.Join(' ', returns[line].Points.Select(point => $"{X(point.Item1)},{Y(point.Item2)}"))}\"><title>{Escaped(returns[line].Name)}</title></polyline>");
        }

        svg.Append("</svg>");

        var legend = string.Concat(returns.Select((line, index) => string.Create(CultureInfo.InvariantCulture, $"<li class=\"judge-key-{index}\">{Escaped(line.Name)}: {Figures.Share(line.Points[^1].Item2)}</li>")));

        return $"{svg}<ul class=\"judge-key\">{legend}</ul>";
    }

    // An account's value over the value it started at, less one, as the chart's return: the judge's chart's one crossing.
    static double ReturnFrom(decimal value, decimal start) => start > 0m ? Core.Prices.Statistic.FromRatio((value / start) - 1m) : 0.0;

    static string Dollars(double value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
}
