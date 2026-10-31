using System.Globalization;
using System.Text;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// Paper trading, section 15.21: what the page is and the account's line with the form behind its press, a tile a family's
// live or provisional account beside the index's fund with the chart beneath, what the system bought, what the variants
// did differently, a rule's scorecard and your own trades, each part's choice kept in the link. An account's trades are its
// trade log on Past picks.
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed partial class SinglePageApp
{
    // How paper trading is read, opened from its "?".
    public const string PaperGuide =
        "Each account takes one rule's trades: a family's live or provisional rule takes the page's own list, each registered variant its own list or book, and You the trades you took from the cards. Each trade is bought at the open of the session after the night that listed it and sized off the account's value at its buy by the card's own sizing, the trades ending on a session sold before any is bought, and one the free cash cannot buy is skipped and counted; each pays its round trip, half at its buy and half at its sale. The tiles draw each family's live or provisional account's return since its record began beside the index's fund over the same days, and the chart draws each account's return session by session, a chosen family's variants dashed; a key's press hides or shows its line. An account is evidence about one unchanged rule, so a registered rule's account counts only the trades of its current version, from where the Run page's record of the rule starts, and starts again when the rule is registered again; the trades of earlier versions stay folded and left out of every figure. A provisional account and yours count every trade from the first. A rule's scorecard puts two things beside each other and never adds them together: how the rule did on the test years in the newest monthly test, and its own record since it went live, which is not evidence until it holds the test's count of trades or months.";

    // The one sentence saying what the page is.
    public const string PaperWhat = "Every stock a rule lists is bought on paper at the open of the session after the night that listed it, in a copy of your account each rule keeps, so you can see what each rule bought, how each trade stands, what its variants did differently and how the rule scores.";

    public string PaperRegion(
        MarkRenderer marks,
        UniverseChoice reading,
        DateOnly? night,
        PaperPageView page,
        PaperLink link,
        string variants,
        string rule,
        (decimal Size, decimal Risk, decimal Cap)? settings,
        decimal proposedCap,
        string selector = "",
        (decimal Size, decimal Risk, decimal Cap)? example = null)
    {
        var region = new StringBuilder();
        var accounts = page.Accounts;
        var drawn = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        var heading = "Paper trading: " + reading.Titled;
        var walked = settings ?? example;

        region.Append(Invariant($"<section class=\"paper\" data-universe=\"{reading.Word}\" data-night=\"{drawn}\" data-accounts=\"{accounts.Count}\" data-family=\"{Escaped(link.Family ?? "none")}\" data-walked=\"{(walked is null ? "false" : "true")}\" data-example=\"{(settings is null && example is not null ? "true" : "false")}\" data-bought=\"{(link.Closed ? "closed" : "open")}\" data-show=\"{Escaped(link.Show ?? "none")}\" data-variants=\"{Escaped(variants)}\" data-rule=\"{Escaped(rule)}\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is null ? "No night is stored yet" : Invariant($"Every rule's paper account on the {Escaped(reading.Name)}, as of the close of {drawn}")));
        region.Append(selector);

        // Until the operator saves an account, every account is walked on the example and the page says so at its top.
        if (settings is null && example is { } shown)
        {
            region.Append(Invariant($"<p class=\"notice paper-example\" data-example=\"true\"><b>Example account</b>: {shown.Size:#,0} dollars, {shown.Risk:0.##} per cent of it a trade and at most {shown.Cap * 100:0.##} per cent of it in one position. Change the account to save your own, and every account is walked on yours.</p>"));
        }

        // The account's line, the settings each rule's copy is walked on, the form behind its press.
        var line = walked is { } used
            ? Invariant($"<p class=\"paper-line\" data-size=\"{(settings is null ? "example" : "kept")}\"><span>Account <b>${used.Size:#,0}</b></span><span>Risk a trade <b>{used.Risk:0.##}%</b></span><span>Most in one stock <b>{used.Cap * 100:0.##}%</b></span><span>Each rule trades its own copy of this account.</span></p>")
            : "<p class=\"paper-line degraded\">No account is kept: set the three to walk the accounts.</p>";

        region.Append(Cards.Computed(
            "Paper trading",
            Invariant($"<p class=\"paper-what\">{Escaped(PaperWhat)}</p>") + line
                + Invariant($"<details class=\"paper-change\"{(walked is null ? " open" : string.Empty)}><summary>Change the account</summary>{AccountForm(settings?.Size, settings?.Risk, settings?.Cap, proposedCap)}</details>"),
            title: "Each rule's trades, in your money",
            region: "account"));

        region.Append(Cards.Computed(
            "Paper trading",
            Guide(PaperGuide) + marks.PaperTiles(accounts, page.Fund, page.Combined) + MarkRenderer.PaperFamilies(accounts, link)
                + (walked is null ? "<p class=\"degraded\" data-paper-chart=\"unset\">Set your account above to draw the accounts.</p>" : marks.PaperChart(accounts, link.Family, page.Combined)),
            title: "Each rule since its record began",
            lede: "Each family's live or provisional rule, its return on your size beside the index's fund over the same days, and beneath, each account's return in per cent of its size, session by session.",
            stamp: Cards.Night(night),
            region: "paper-chart"));

        // From 21.11 every live rule's trades in one account of your size.
        // see: The combined account holds every live rule's trades in one pot with one position a stock and three a sector, each book family a fifth of it
        region.Append(Cards.Computed(
            "Paper trading",
            walked is null ? "<p class=\"degraded\" data-combined=\"unset\">Set your account above to walk the combined account.</p>" : marks.PaperCombined(page.Combined),
            title: "All setups together",
            lede: "Every live rule's trades in one account of your size: one position a stock, at most three open in one sector, each book a fifth of the account, and a buy the free cash cannot cover skipped.",
            stamp: Cards.Night(night),
            region: "paper-combined"));

        region.Append(Cards.Computed(
            "Paper trading",
            marks.PaperBought(page, link),
            title: "What the system bought",
            lede: "The live and provisional rules' trades, newest first, each bought at the open after the night that listed it and sized from that night's close and stop.",
            stamp: Cards.Night(night),
            region: "paper-bought"));

        region.Append(Cards.Computed(
            "Paper trading",
            marks.PaperVariants(page, link, variants),
            title: "What the variants did differently",
            lede: "Each variant is its rule with a setting changed, trading in the background and never on Tonight: what it changes, the trades it shared with its rule and those it took alone.",
            stamp: Cards.Night(night),
            region: "paper-variants"));

        region.Append(Cards.Computed(
            "Paper trading",
            marks.PaperScorecard(page, link, rule),
            title: "Rule scorecard",
            lede: "On history: over the test years, did the rule beat the same plan bought on every stock in the index that night, and is the gap bigger than luck would give a rule picking at random? Since it went live: how its own trades have done, counted toward the count a verdict needs. The two are never added together.",
            stamp: Cards.Night(night),
            region: "paper-scorecard"));

        region.Append(Cards.Computed(
            "Paper trading",
            marks.PaperYours(accounts.FirstOrDefault(account => account.Standing == PaperAccountView.You), reading.Word),
            title: "Your own trades",
            lede: "The trades you took from a pick's card on this index, as an account of your size, apart from the system's.",
            stamp: Cards.Night(night),
            region: "paper-yours"));
        region.Append("</section>");

        return region.ToString();
    }

    // The trade log's selector on Past picks: the page's own lists first, the live and provisional rules' trades, then
    // each family's live or provisional account and its variants in the one family order, then You, the choice kept in
    // the link; the lists are the key's absence.
    public static string AccountSelector(IReadOnlyList<PaperAccountView> accounts, string? chosen)
    {
        var html = new StringBuilder();

        html.Append(Invariant($"<label class=\"account-pick\" data-account=\"{Escaped(chosen ?? "lists")}\"><span class=\"universe-lbl\">Account</span> <select data-merge-key=\"account\" aria-label=\"The account whose trades are drawn\">"));
        html.Append(Invariant($"<option value=\"\"{(chosen is null ? " selected" : string.Empty)}>Live and provisional lists</option>"));

        foreach (var account in accounts.Where(one => one.Standing != PaperAccountView.You).OrderBy(one => EquityBrief.Core.Families.SetupFamilies.OrderOf(one.Family)).ThenBy(one => one.Head ? 0 : 1).Concat(accounts.Where(one => one.Standing == PaperAccountView.You)))
        {
            html.Append(Invariant($"<option value=\"{Escaped(account.Key)}\" title=\"{Escaped(account.Words)}\"{(account.Key == chosen ? " selected" : string.Empty)}>{Escaped(account.Standing == PaperAccountView.You ? "You" : FamilyLabel(account.Family) + " · " + account.Name)}</option>"));
        }

        html.Append("</select></label>");

        return html.ToString();
    }

    // Beside the lists' trades: each family's live or provisional rule's test years verdict and its record's label,
    // apart, and the line about entries.
    public static string ListsVerdicts(IReadOnlyList<PaperAccountView> accounts) =>
        "<div class=\"paper-heads\"><p><b>Test years and since registered.</b></p><ul>"
        + string.Concat(accounts.Where(one => one.Head).OrderBy(one => EquityBrief.Core.Families.SetupFamilies.OrderOf(one.Family)).Select(account =>
            Invariant($"<li data-family=\"{Escaped(account.Family)}\" data-verdict=\"{Escaped(account.Verdict)}\"><span class=\"fam-tag\" data-family=\"{Escaped(account.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(account.Family)}\"></span>{Escaped(FamilyLabel(account.Family))}</span> {Escaped(account.Name)}: {Escaped(account.Verdict)}{(account.Years.Count > 0 ? "; " + Escaped(EquityBrief.Core.Loop.RuleVerdict.Chosen) : string.Empty)}. Since registered: {account.Trades} trade(s){(account.TooFew is { } few ? Invariant($", <span class=\"too-few\">{Escaped(few)}</span>") : string.Empty)}{MarkRenderer.StartWords(account)}.</li>")))
        + Invariant($"</ul><p class=\"paper-entry\">{Escaped(MarkRenderer.EntryPriceLine)}</p></div>");

    public string AccountLogRegion(MarkRenderer marks, UniverseChoice reading, DateOnly? night, IReadOnlyList<PaperAccountView> accounts, PaperAccountView account, IReadOnlyList<PaperTradeView> trades, IReadOnlyList<PaperTradeView>? earlier = null)
    {
        var region = new StringBuilder();
        var heading = "Past picks: " + reading.Titled;

        region.Append(Invariant($"<section class=\"picks\" data-universe=\"{reading.Word}\" data-account=\"{Escaped(account.Key)}\" data-trades=\"{trades.Count}\">"));
        region.Append(Cards.Masthead(heading, $"<span class=\"m-screen\">{Escaped(heading)}</span>", Escaped(account.Standing == PaperAccountView.You ? "Your trades on the " + reading.Name : "The trades of " + FamilyLabel(account.Family) + " · " + account.Name)));
        region.Append(AccountSelector(accounts, account.Key));
        region.Append(Cards.Computed(
            "Trade log",
            marks.PaperLog(account, trades, earlier ?? []),
            title: Escaped(account.Standing == PaperAccountView.You ? "Your trades" : FamilyLabel(account.Family) + " · " + account.Name),
            lede: Escaped(account.Words),
            stamp: Cards.Night(night),
            region: "paper-log"));
        region.Append("</section>");

        return region.ToString();
    }

    // A screen's "?" opening how it is read.
    static string Guide(string guide) =>
        $"<details class=\"guide\"><summary title=\"How to read this page\" aria-label=\"How to read this page\">?</summary><p class=\"guide-b\">{Escaped(guide)}</p></details>";

    static string FamilyLabel(string family) =>
        family == EquityBrief.Core.Families.HeavyweightRule.Name
            ? EquityBrief.Core.Families.SetupFamilies.SectorHeavyweights.Heading
            : family == EquityBrief.Core.Families.QualityRule.Name
                ? EquityBrief.Core.Families.QualityRule.Label
                : family == EquityBrief.Core.Families.ImprovingRule.Name
                    ? EquityBrief.Core.Families.ImprovingRule.Label
                    : EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family;
}
