using System.Globalization;
using System.Text;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// Paper trading, section 15.21: the account's three settings, the chart of each family's live or provisional account
// with a chosen family's variants dashed beside them, and the table of every account on the index with its test years
// verdict and its record since registration side by side. An account's trades are its trade log on Past picks.
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed partial class SinglePageApp
{
    // How paper trading is read, opened from its "?".
    public const string PaperGuide =
        "Each account takes one rule's trades: a family's live or provisional rule takes the page's own list, each registered variant its own list or book, and You the trades you took from the cards. Each trade is sized off the account's value at its buy by the card's own sizing, the trades ending on a session sold before any is bought, and one the free cash cannot buy is skipped and counted; each pays its round trip, half at its buy and half at its sale. The chart draws each account's return since its record began, the live and provisional accounts always and a chosen family's variants dashed; a key's press hides or shows its line. The table puts two things side by side and never merges them: the rule's verdict in the newest monthly test over the ledger's years, and the account's own record since it was registered, which is too few to read until it holds the test's count of trades or months.";

    public string PaperRegion(
        MarkRenderer marks,
        UniverseChoice reading,
        DateOnly? night,
        IReadOnlyList<PaperAccountView> accounts,
        string? chosen,
        (decimal Size, decimal Risk, decimal Cap)? settings,
        decimal proposedCap)
    {
        var region = new StringBuilder();
        var drawn = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        var heading = "Paper trading: " + reading.Name;

        region.Append(Invariant($"<section class=\"paper\" data-universe=\"{reading.Word}\" data-night=\"{drawn}\" data-accounts=\"{accounts.Count}\" data-family=\"{Escaped(chosen ?? "none")}\" data-walked=\"{(settings is null ? "false" : "true")}\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is null ? "No night is stored yet" : Invariant($"Every rule's paper account on the {Escaped(reading.Name)}, as of the close of {drawn}")));

        region.Append(Cards.Computed(
            "Account",
            AccountForm(settings?.Size, settings?.Risk, settings?.Cap, proposedCap),
            title: "Your account",
            lede: settings is null
                ? "Set the three to walk the accounts: each trade is sized from them as a pick's card sizes its plan."
                : "Every account is walked from these, each trade sized as a pick's card sizes its plan.",
            region: "account"));

        var families = accounts
            .Where(account => account.Standing == PaperAccountView.Variant)
            .Select(account => account.Family)
            .Distinct()
            .OrderBy(EquityBrief.Core.Families.SetupFamilies.OrderOf)
            .ToArray();
        var choices = new StringBuilder();

        choices.Append("<nav class=\"paper-families\" aria-label=\"Add a family's variants to the chart\">");
        choices.Append(Invariant($"<a href=\"{PaperRoute}?universe={reading.Word}\"{(chosen is null ? " aria-current=\"true\"" : string.Empty)}>Live and provisional only</a>"));

        foreach (var family in families)
        {
            choices.Append(Invariant($"<a class=\"fam-tag\" data-family=\"{Escaped(family)}\" href=\"{PaperRoute}?universe={reading.Word}&amp;family={Uri.EscapeDataString(family)}\"{(family == chosen ? " aria-current=\"true\"" : string.Empty)}><span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>{Escaped(FamilyLabel(family))} with its variants</a>"));
        }

        choices.Append("</nav>");

        region.Append(Cards.Computed(
            "Paper trading",
            Guide(PaperGuide) + choices + (settings is null ? "<p class=\"degraded\" data-paper-chart=\"unset\">Set your account above to draw the accounts.</p>" : marks.PaperChart(accounts, chosen)),
            title: "Each account since its record began",
            lede: "In per cent of the account's size, open positions at each session's close.",
            stamp: Cards.Night(night),
            region: "paper-chart"));

        region.Append(Cards.Computed(
            "Paper trading",
            marks.PaperTable(accounts, settings is not null, reading.Word)
                + Invariant($"<p class=\"paper-adopted\">{Escaped(MarkRenderer.AdoptedLine)}</p>")
                + Invariant($"<p class=\"paper-entry\">{Escaped(MarkRenderer.EntryPriceLine)}</p>"),
            title: "Every account",
            lede: "The test years verdict and the record since registration, side by side and never merged.",
            stamp: Cards.Night(night),
            region: "paper-accounts"));
        region.Append("</section>");

        return region.ToString();
    }

    // The trade log's selector on Past picks: the page's own lists first, the live and provisional rules' trades, then
    // each variant in the one family order, then You, the choice kept in the link; the lists are the key's absence.
    public static string AccountSelector(IReadOnlyList<PaperAccountView> accounts, string? chosen)
    {
        var html = new StringBuilder();

        html.Append(Invariant($"<label class=\"account-pick\" data-account=\"{Escaped(chosen ?? "lists")}\"><span class=\"universe-lbl\">Account</span> <select data-merge-key=\"account\" aria-label=\"The account whose trades are drawn\">"));
        html.Append(Invariant($"<option value=\"\"{(chosen is null ? " selected" : string.Empty)}>Live and provisional lists</option>"));

        foreach (var account in accounts.Where(one => one.Standing == PaperAccountView.Variant).OrderBy(one => EquityBrief.Core.Families.SetupFamilies.OrderOf(one.Family)).Concat(accounts.Where(one => one.Standing == PaperAccountView.You)))
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
            Invariant($"<li data-family=\"{Escaped(account.Family)}\" data-verdict=\"{Escaped(account.Verdict)}\"><span class=\"fam-tag\" data-family=\"{Escaped(account.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(account.Family)}\"></span>{Escaped(FamilyLabel(account.Family))}</span> {Escaped(account.Name)}: {Escaped(account.Verdict)}{(account.Years.Count > 0 ? "; " + Escaped(EquityBrief.Core.Loop.RuleVerdict.Chosen) : string.Empty)}. Since registered: {account.Trades} trade(s){(account.TooFew is { } few ? Invariant($", <span class=\"too-few\">{Escaped(few)}</span>") : string.Empty)}.</li>")))
        + Invariant($"</ul><p class=\"paper-entry\">{Escaped(MarkRenderer.EntryPriceLine)}</p></div>");

    public string AccountLogRegion(MarkRenderer marks, UniverseChoice reading, DateOnly? night, IReadOnlyList<PaperAccountView> accounts, PaperAccountView account, IReadOnlyList<PaperTradeView> trades)
    {
        var region = new StringBuilder();
        var heading = "Past picks: " + reading.Name;

        region.Append(Invariant($"<section class=\"picks\" data-universe=\"{reading.Word}\" data-account=\"{Escaped(account.Key)}\" data-trades=\"{trades.Count}\">"));
        region.Append(Cards.Masthead(heading, $"<span class=\"m-screen\">{Escaped(heading)}</span>", Escaped(account.Standing == PaperAccountView.You ? "Your trades on the " + reading.Name : "The trades of " + FamilyLabel(account.Family) + " · " + account.Name)));
        region.Append(AccountSelector(accounts, account.Key));
        region.Append(Cards.Computed(
            "Trade log",
            marks.PaperLog(account, trades),
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
            : EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family;
}
