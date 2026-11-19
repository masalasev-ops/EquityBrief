using System.Text;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// Paper trading by revisions: the account's card; every rule's running revision as a tile with every running revision
// together against the index's fund and the chart beneath; the Rule and Show controls; the revision Show draws; how it has
// done; its variants, each unfolding to its own trades; its earlier revisions, folded; and your own trades.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public sealed partial class SinglePageApp
{
    public const string RevisionsWhat = "A rule changes only through a new revision. Each revision keeps its own trades and its own record from the day it started running, so nothing an earlier revision did is mixed into a later one. A variant is a copy of the running revision with one thing changed, traded on paper beside it.";

    public const string EveryRunningRevision = "Every rule, the revisions running now";

    // Past picks' Rule and Show controls, in its Account list's place: the rules first, every rule's running revision as the
    // key's absence; and for a rule chosen, its running revision, its variants by their one change and its earlier revisions
    // with the dates they ran, each choice a trade log kept in the link.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public static string PicksControls(IReadOnlyList<PaperAccountView> accounts, string? chosen)
    {
        var heads = accounts.Where(account => account.Head).OrderBy(account => EquityBrief.Core.Families.SetupFamilies.OrderOf(account.Family)).ToArray();
        var key = chosen?.Split('#')[0];
        var picked = accounts.FirstOrDefault(account => account.Key == key);
        var head = picked is null ? null : heads.FirstOrDefault(one => one.Family == picked.Family);
        var html = new StringBuilder("<div class=\"pick-bar\" data-controls=\"rule-show\" data-as-worded>");

        html.Append(Invariant($"<label><span class=\"universe-lbl\">Rule</span> <select data-merge-key=\"{PaperScreenAccountKey}\" aria-label=\"The rule whose trades are drawn\"><option value=\"\"{(head is null ? " selected" : string.Empty)}>{Escaped(EveryRunningRevision)}</option>"));

        foreach (var one in heads)
        {
            html.Append(Invariant($"<option value=\"{Escaped(one.Key)}\"{(head?.Key == one.Key ? " selected" : string.Empty)}>{Escaped(MarkRenderer.FamilyName(one.Family))}</option>"));
        }

        html.Append("</select></label>");

        if (head is not null)
        {
            html.Append(Invariant($"<label><span class=\"universe-lbl\">Show</span> <select data-merge-key=\"{PaperScreenAccountKey}\" aria-label=\"What of the rule is drawn\"><option value=\"{Escaped(head.Key)}\"{(chosen == head.Key ? " selected" : string.Empty)}>{Escaped(head.Revision?.Tag ?? "Its starting settings")}</option>"));

            var variants = accounts.Where(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant).ToArray();

            if (variants.Length > 0)
            {
                html.Append(Invariant($"<optgroup label=\"Variants of {Escaped(head.Revision?.Name.ToLowerInvariant() ?? "the rule")}\">"));

                foreach (var variant in variants)
                {
                    html.Append(Invariant($"<option value=\"{Escaped(variant.Key)}\"{(chosen == variant.Key ? " selected" : string.Empty)}>{Escaped(MarkRenderer.VariantName(variant))}</option>"));
                }

                html.Append("</optgroup>");
            }

            var earlier = head.Revisions.Where(revision => !revision.Running).Reverse().ToArray();

            if (earlier.Length > 0)
            {
                html.Append("<optgroup label=\"Earlier revisions, no longer running\">");

                foreach (var revision in earlier)
                {
                    var value = MarkRenderer.EarlierKey(head.Key, revision.Number);

                    html.Append(Invariant($"<option value=\"{Escaped(value)}\"{(chosen == value ? " selected" : string.Empty)}>{Escaped(revision.Tag)}</option>"));
                }

                html.Append("</optgroup>");
            }

            html.Append("</select></label>");
        }

        return html.Append("</div>").ToString();
    }

    static string VariantsSaid(IReadOnlyList<PaperAccountView> accounts, PaperAccountView head, string name)
    {
        var count = accounts.Count(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant);

        return count == 0
            ? Invariant($"{MarkRenderer.FamilyName(head.Family)} has no variant of {name.ToLowerInvariant()} trading on paper.")
            : Invariant($"{count} {(count == 1 ? "variant" : "variants")} of {name.ToLowerInvariant()} {(count == 1 ? "trades" : "trade")} on paper, each with one thing changed.");
    }

    // The link key Past picks keeps the chosen trade log under.
    public const string PaperScreenAccountKey = "account";

    string RevisionsLayout(
        MarkRenderer marks,
        DateOnly? night,
        PaperPageView page,
        PaperLink link,
        (decimal Size, decimal Risk, decimal Cap)? settings,
        decimal proposedCap,
        (decimal Size, decimal Risk, decimal Cap)? example,
        string line)
    {
        var region = new StringBuilder();
        var accounts = page.Accounts;
        var walked = settings ?? example;
        var heads = accounts.Where(account => account.Head).OrderBy(account => EquityBrief.Core.Families.SetupFamilies.OrderOf(account.Family)).ToArray();

        region.Append(Cards.Computed(
            "Paper trading",
            MarkRenderer.CardSummary("Each rule buys its picks on paper in its own copy of an account of your size, so you can see what it bought, how each trade stands and how it has done.") + line
                + Invariant($"<details class=\"paper-change\"{(walked is null ? " open" : string.Empty)}><summary>Change the account</summary>{AccountForm(settings?.Size, settings?.Risk, settings?.Cap, proposedCap)}</details>")
                + Invariant($"<p class=\"paper-what\">{Escaped(RevisionsWhat)}</p>"),
            title: "Each rule's trades, in your money",
            region: "account"));

        region.Append(Cards.Computed(
            "Paper trading",
            MarkRenderer.CardSummary("The revision of each rule running now, whose picks you see on Home and Tonight. Press a rule to open it below.")
                + MarkRenderer.RunningTiles(accounts, page.Combined, page.Fund, link, page.Traded)
                + (walked is null ? "<p class=\"degraded\" data-paper-chart=\"unset\">Set your account above to draw the accounts.</p>" : marks.PaperChart(accounts, link.Family, page.Combined, link, page.FundCloses, page.Fund?.Fund))
                + (walked is null ? string.Empty : Invariant($"<details class=\"paper-combined-fold\"><summary>Every running revision's trades together</summary>{marks.PaperCombined(page.Combined)}</details>")),
            title: "Every rule, the revision running now",
            stamp: Cards.Night(night),
            region: "paper-chart"));

        if ((heads.FirstOrDefault(head => head.Family == link.Rule) ?? heads.FirstOrDefault()) is not { } chosen)
        {
            region.Append("<p class=\"degraded\" data-rule=\"none\">No rule stands on this index yet.</p>");

            return region.ToString();
        }

        var variants = accounts.Where(account => account.Family == chosen.Family && account.Standing == PaperAccountView.Variant).ToArray();
        var pickedVariant = variants.FirstOrDefault(variant => variant.Key == link.Pick);
        var pickedRevision = link.Pick is { } pick && pick.StartsWith(PaperLink.RevisionPrefix, StringComparison.Ordinal) && int.TryParse(pick[PaperLink.RevisionPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? chosen.Revisions.FirstOrDefault(revision => revision.Number == number && !revision.Running)
            : null;
        var pickKept = pickedVariant is not null || pickedRevision is not null ? link.Pick : null;
        var drawnLink = link with { Rule = chosen.Family, Pick = pickKept, Unfold = pickedVariant?.Key ?? link.Unfold };
        var revision = pickedRevision ?? chosen.Revision;
        var name = revision?.Name ?? "its starting settings";

        region.Append(MarkRenderer.RuleShowControls(accounts, chosen, pickKept));
        region.Append(Cards.Computed(
            MarkRenderer.FamilyName(chosen.Family),
            Invariant($"<p class=\"card-summary\" data-summary>The plan {Escaped(name.ToLowerInvariant())} follows, its trades and how it has done. Every price on this card comes from the plan below.</p>") + marks.RevisionCard(page, chosen, revision, drawnLink),
            title: MarkRenderer.FamilyName(chosen.Family) + ", " + name.ToLowerInvariant(),
            stamp: Cards.Night(night),
            region: "paper-revision"));

        if (pickedRevision is null)
        {
            region.Append(Cards.Computed(
                MarkRenderer.FamilyName(chosen.Family),
                MarkRenderer.CardSummary("Two records, never added together: its trades since it started running, and the test over past years that chose it.") + marks.HowItHasDone(page, chosen),
                title: "How " + name.ToLowerInvariant() + " has done",
                stamp: Cards.Night(night),
                region: "paper-scorecard"));

            region.Append(Cards.Computed(
                MarkRenderer.FamilyName(chosen.Family),
                MarkRenderer.CardSummary(VariantsSaid(accounts, chosen, name)) + marks.VariantsOf(page, chosen, drawnLink) + MarkRenderer.EarlierRevisionsOf(page, chosen, drawnLink),
                title: "Variants of " + name.ToLowerInvariant(),
                stamp: Cards.Night(night),
                region: "paper-variants"));
        }
        else
        {
            region.Append(Cards.Computed(
                MarkRenderer.FamilyName(chosen.Family),
                MarkRenderer.CardSummary(Invariant($"{MarkRenderer.FamilyName(chosen.Family)} ran {chosen.Revisions.Count(one => !one.Running)} earlier {(chosen.Revisions.Count(one => !one.Running) == 1 ? "revision" : "revisions")} before the one running now.")) + MarkRenderer.EarlierRevisionsOf(page, chosen, drawnLink),
                title: "Earlier revisions of " + MarkRenderer.FamilyName(chosen.Family),
                stamp: Cards.Night(night),
                region: "paper-variants"));
        }

        var you = accounts.FirstOrDefault(account => account.Standing == PaperAccountView.You);

        region.Append(Cards.Computed(
            "Paper trading",
            MarkRenderer.CardSummary(MarkRenderer.PaperYoursSaid(you)) + marks.PaperYours(you, link.Universe),
            title: "Your own trades",
            lede: "Unchanged: the trades you marked as taken from a pick's card.",
            stamp: Cards.Night(night),
            region: "paper-yours"));

        return region.ToString();
    }
}
