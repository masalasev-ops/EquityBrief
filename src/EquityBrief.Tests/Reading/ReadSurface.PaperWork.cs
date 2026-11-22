using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Paper;

namespace EquityBrief.Tests.Reading;

// read-surface: Paper trading's accounts worked once, in the core, over the one reader in the data project, and the page
// and the worker drawing the same figures from it: every account's tile or row on the page read back beside the worker's
// line for it over constructed stores, on the S&P 400 and on the S&P 500 with a registered rule and its variant.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public partial class ReadSurface
{
    // Each constructed store's index as the page's link names it and as the store keeps it, and its night.
    static readonly (Func<Task<TemporaryStore>> Store, string Word, string Code)[] PaperWorkStores =
    [
        (PaperIndexStore, "400", "MID"),
        (PaperVariantsStore, "500", "GSPC"),
        (FamilyPagesStore, "500", "GSPC"),
    ];

    [Fact]
    public async Task ThePageAndTheWorkerDrawTheSameFiguresForEveryAccountOfEveryIndex()
    {
        var read = 0;

        foreach (var (made, word, code) in PaperWorkStores)
        {
            using var store = await made();
            var settings = PaperFiguresRun.SettingsOf(store.Root);
            var figures = await PaperFiguresRun.ReadAsync(store.DatabaseFile, code, OnTheNight, settings);

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?universe={word}"));

            Assert.Contains($"data-universe=\"{word}\" data-night=\"{OnTheNight:yyyy-MM-dd}\"", page, StringComparison.Ordinal);

            // Each live or provisional rule's tile: its walk's return, its open and closed trades and the day it counts from.
            foreach (var head in figures.Where(one => one.Standing is "live" or "provisional"))
            {
                var tile = Regex.Match(page, $"<a class=\"revision-tile\" data-family=\"{Regex.Escape(head.Family)}\" data-standing=\"{head.Standing}\" data-account=\"{Regex.Escape(head.Key)}\" data-return=\"([^\"]+)\" data-open=\"(\\d+)\" data-closed=\"(\\d+)\" data-since=\"([^\"]+)\"");

                Assert.True(tile.Success, $"{code} {head.Key}: no tile");
                Assert.Equal(
                    (Return(head.Return), head.Open.ToString(CultureInfo.InvariantCulture), head.Closed.ToString(CultureInfo.InvariantCulture), head.Since is { } since ? since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none"),
                    (tile.Groups[1].Value, tile.Groups[2].Value, tile.Groups[3].Value, tile.Groups[4].Value));
                read++;
            }

            // Each variant's row under its family's rule: its walk's return and its record's too-few words where it has them.
            foreach (var family in figures.Where(one => one.Standing == "variant").GroupBy(one => one.Family))
            {
                var chosen = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?universe={word}&rule={family.Key}"));

                foreach (var variant in family)
                {
                    var row = PaperRowOf(chosen, $"<tr class=\"vrow\" data-account=\"{variant.Key}\"");

                    Assert.Contains($"data-return=\"{Return(variant.Return)}\"", row, StringComparison.Ordinal);

                    if (variant.TooFew is { } few)
                    {
                        Assert.Contains($"<span class=\"too-few\">{few}</span>", row, StringComparison.Ordinal);
                    }

                    read++;
                }
            }

            // The worker's lines name every account it worked, in the page's family order.
            Assert.Equal(figures.Count, figures.Select(one => PaperFiguresRun.Line(code, one)).Distinct(StringComparer.Ordinal).Count());
        }

        // The pullback's tile on the S&P 400 by hand: 10,190.4 on 10,000, three open and two closed from 2026-09-28.
        using (var index = await PaperIndexStore())
        {
            var pullback = (await PaperFiguresRun.ReadAsync(index.DatabaseFile, "MID", OnTheNight, PaperFiguresRun.SettingsOf(index.Root))).Single(one => one.Family == "pullback" && one.Standing == "provisional");

            Assert.Equal((0.01904, 3, 2, new DateOnly(2026, 9, 28)), (Math.Round(pullback.Return!.Value, 5), pullback.Open, pullback.Closed, pullback.Since!.Value));
        }

        Assert.True(read >= 12, $"Read {read} account(s) back off the page, expected at least 12.");
    }

    static string Return(double? made) => made is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "none";

    // Every shipped source 24.21 writes or changes, none of them one a rule's version pins, so no rule's code version moves.
    internal static readonly string[] PaperWorkSources =
    [
        "src/EquityBrief.Core/Paper/IPaperReads.cs",
        "src/EquityBrief.Core/Paper/PaperBooks.cs",
        "src/EquityBrief.Core/Paper/PaperRows.cs",
        "src/EquityBrief.Core/Paper/PaperViews.cs",
        "src/EquityBrief.Core/Paper/PaperWork.cs",
        "src/EquityBrief.Core/Paper/PickCell.cs",
        "src/EquityBrief.Core/Paper/PickCells.cs",
        "src/EquityBrief.Core/Paper/PlanOrderWords.cs",
        "src/EquityBrief.Core/Paper/PlannedTrades.cs",
        "src/EquityBrief.Core/Paper/RuleRegister.cs",
        "src/EquityBrief.Core/Paper/RuleSentences.cs",
        "src/EquityBrief.Core/Paper/RuleWords.cs",
        "src/EquityBrief.Data/PaperReads.cs",
        "src/EquityBrief.Data/PaperReads.Picks.cs",
        "src/EquityBrief.Data/PaperReads.Plans.cs",
        "src/EquityBrief.Data/PaperReads.Register.cs",
        "src/EquityBrief.Worker/Paper/PaperFiguresRun.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Paper.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Web/Marks/PlanOrderWords.cs",
    ];

    [Fact]
    public void NoSourceThePaperWorkWritesIsOneARulesVersionPins()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(FixtureExpectations.LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(PaperWorkSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PaperWorkSources.Intersect(pinned, StringComparer.Ordinal));
    }
}
