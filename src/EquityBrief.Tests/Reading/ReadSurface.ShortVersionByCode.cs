using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Quarters;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the short version a name's page draws where no accepted one stands: written by code from
// computed parts alone, under a heading that says why, read from the record the page already reads the
// section's state from. Each is read off the page the route serves, against the store by queries of the
// test's own.
// see: The short version is written last from the sections that passed, and one left out is replaced by a summary code writes
public partial class ReadSurface
{
    static Match CodeSummary(string page) =>
        Regex.Match(
            page,
            "<section class=\"code-summary\" data-ticker=\"([^\"]*)\" data-section=\"The short version\" data-reason=\"([^\"]*)\" data-state=\"([^\"]*)\">(.*?)</section>",
            RegexOptions.Singleline);

    static string Part(Match summary, string part) =>
        WebUtility.HtmlDecode(Regex.Match(summary.Groups[4].Value, $"<li data-summary-part=\"{part}\"[^>]*>(.*?)</li>", RegexOptions.Singleline).Groups[1].Value);

    static string PartAttribute(Match summary, string attribute) =>
        Regex.Match(summary.Groups[4].Value, $"<li data-summary-part=\"plan\"[^>]*{attribute}=\"([^\"]*)\"").Groups[1].Value;

    // One host per store, since each host records its start on the store's run log under one stage.
    static async Task<string> NameRoute(TemporaryStore store, string ticker)
    {
        using var host = new Host(store.Root);

        return await NameRoute(host, ticker);
    }

    static async Task<string> NameRoute(Host host, string ticker)
    {
        using var client = host.CreateClient();

        return await client.GetStringAsync("/screens/name/" + ticker);
    }

    // The heading states the reason, and no model's short version is drawn beside it.
    static Match AssertDrawnByCode(string page, string reason)
    {
        var summary = CodeSummary(page);

        Assert.True(summary.Success, "no short version written by code was drawn");
        Assert.Equal("KEYS", summary.Groups[1].Value);
        Assert.Equal(reason, summary.Groups[2].Value);
        Assert.Equal(
            MarkRenderer.ShortVersionHeading(reason),
            WebUtility.HtmlDecode(Regex.Match(summary.Groups[4].Value, "<p class=\"code-heading\">(.*?)</p>").Groups[1].Value));
        Assert.False(WrittenOnThePage(page, MarkRenderer.TheShortVersion).Success);

        return summary;
    }

    [Fact]
    public async Task AShortVersionTheCheckerLeftOutIsReplacedByOneWrittenByCodeUnderTheRefusedHeadingEachPartReadAgainstTheStore()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        Insert(
            store,
            "UPDATE research_section SET status = 'fallback', reject_reason = 'rejected twice: a figure the facts file does not hold: 1.90' " +
            "WHERE ticker = 'KEYS' AND section = 'The short version' " +
            "AND version = (SELECT MAX(version) FROM research_section WHERE ticker = 'KEYS' AND section = 'The short version');");

        using var host = new Host(store.Root);

        var summary = AssertDrawnByCode(await NameRoute(host, "KEYS"), MarkRenderer.ShortVersionRefused);
        var session = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];

        // Why the name is or is not on the list, against its listing row on the night the newest list is
        // from, which is tonight's and not dated, as the page's reasons are not.
        var night = Rows(store, "SELECT MAX(session_date) FROM listing;").Single()[0];
        var byFilter = Rows(store, $"SELECT rule FROM list_rule WHERE session_date = '{night}';") is [[FilterRule]];
        var fired = byFilter
            ? []
            : Rows(store, $"SELECT json_extract(value, '$.name') FROM listing, json_each(listing.reasons) WHERE ticker = 'KEYS' AND session_date = '{night}' AND json_extract(value, '$.fired') = 1;")
                .Select(row => row[0])
                .ToArray();
        var gates = Rows(store, "SELECT passed, market + trend + setup + trigger_pass + trade FROM gate_result WHERE ticker = 'KEYS' ORDER BY session_date DESC LIMIT 1;").Single();
        var why = Part(summary, "why");

        if (byFilter && gates[0] == "1")
        {
            Assert.StartsWith($"On the list on {night}: the swing filter passed it on all five gates", why, StringComparison.Ordinal);
        }
        else if (byFilter && gates[1] == "4")
        {
            Assert.StartsWith("Close to a buy point on ", why, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(fired.Length > 0 ? $"On tonight's list for {fired.Length} reason(s): {string.Join(", ", fired)}." : "Not on tonight's list.", why);
        }

        // The business state and the heading its numbers open on, against the night's reading.
        var reading = Rows(store, $"SELECT state FROM fundamental_reading WHERE ticker = 'KEYS' AND session_date <= '{session}' ORDER BY session_date DESC LIMIT 1;");

        Assert.Equal(reading.Count > 0 ? reading[0][0] : "none", summary.Groups[3].Value);
        Assert.Equal(
            reading.Count > 0 ? NumbersSay.Heading(reading[0][0]).TrimEnd('.') + "." : "No reading of its reported quarters is stored for this night.",
            Part(summary, "business"));

        // The entry, stop and target of the plan the trade gate read, against the gate row the filter stored.
        // The row's two swing plans are made to differ and its trade gate made to read each in turn, so the
        // pair drawn is the one the gate read and no other.
        const string Newest = "(SELECT MAX(session_date) FROM gate_result WHERE ticker = 'KEYS')";

        Assert.Equal(
            EquityBrief.Core.Filter.SwingGates.Trade,
            Rows(store, $"SELECT json_extract(gates, '$.gates[4].gate') FROM gate_result WHERE ticker = 'KEYS' AND session_date = {Newest};").Single()[0]);

        Insert(store, $"UPDATE gate_result SET swing_entry = '330.5', swing_stop = '320.25', swing_target = '350.75', clear_stop = '310.5', clear_target = '370.25' WHERE ticker = 'KEYS' AND session_date = {Newest};");

        foreach (var (input, stop, target, words) in (IEnumerable<(string, string, string, string)>)[
            (EquityBrief.Core.Filter.FilterSettings.ClearWord, "310.5", "370.25", "The swing trade clear of the noise, the plan the trade gate read: "),
            (EquityBrief.Core.Filter.FilterSettings.SwingWord, "320.25", "350.75", "The swing trade at the nearest bands, the plan the trade gate read: "),
            (EquityBrief.Core.Filter.FilterSettings.LadderWord, "320.25", "350.75", "The swing trade at the nearest bands: ")])
        {
            Insert(store, $"UPDATE gate_result SET gates = json_set(gates, '$.gates[4].values.{EquityBrief.Core.Filter.SwingGates.TradeInputValue}', '{input}') WHERE ticker = 'KEYS' AND session_date = {Newest};");

            var drawn = CodeSummary(await NameRoute(host, "KEYS"));

            Assert.Equal("330.5", PartAttribute(drawn, "data-entry"));
            Assert.Equal((stop, target), (PartAttribute(drawn, "data-stop"), PartAttribute(drawn, "data-target")));
            Assert.StartsWith(words, Part(drawn, "plan"), StringComparison.Ordinal);
        }
    }

    const string FilterRule = EquityBrief.Core.Shortlist.ListRules.Filter;

    [Fact]
    public async Task AShortVersionTheNewestPassDidNotWriteIsReplacedUnderTheNotWrittenHeading()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        Insert(store, "DELETE FROM research_section WHERE ticker = 'KEYS' AND section = 'The short version';");

        AssertDrawnByCode(await NameRoute(store, "KEYS"), MarkRenderer.ShortVersionNotWritten);
    }

    [Fact]
    public async Task ANameWithNoResearchDrawsAShortVersionByCodeUnderTheHeadingThatImpliesNoModel()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);

        var summary = AssertDrawnByCode(await NameRoute(host, "KEYS"), MarkRenderer.ShortVersionNoResearch);

        Assert.DoesNotContain("model", Regex.Match(summary.Groups[4].Value, "<p class=\"code-heading\">(.*?)</p>").Groups[1].Value, StringComparison.Ordinal);

        // The industry's cycle, written by a pass on another member and drawn for every one, is not research
        // written for this name, so the heading still implies no model.
        var industry = Rows(store, "SELECT industry FROM membership WHERE ticker = 'KEYS' AND industry IS NOT NULL ORDER BY observed_at DESC LIMIT 1;").Single()[0];

        Insert(
            store,
            "INSERT INTO theme_section (theme, section, version, as_of, model, status, prose, source_ids, reject_reason, industries) " +
            $"VALUES ('{industry}', '{EquityBrief.Core.Research.ClaimRules.CycleSection}', 1, '2026-09-08', 'recorded', 'accepted', 'Orders across the industry grew [D1].', '[]', NULL, '[\"{industry}\"]');");

        var page = await NameRoute(host, "KEYS");

        Assert.True(WrittenOnThePage(page, EquityBrief.Core.Research.ClaimRules.CycleSection).Success, "the industry's cycle was not drawn");
        AssertDrawnByCode(page, MarkRenderer.ShortVersionNoResearch);
    }

    [Fact]
    public async Task ANameWithAnAcceptedShortVersionDrawsTheModelsAndNotOneWrittenByCode()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await NameRoute(store, "KEYS");

        Assert.True(WrittenOnThePage(page, MarkRenderer.TheShortVersion).Success);
        Assert.False(CodeSummary(page).Success);
    }
}
