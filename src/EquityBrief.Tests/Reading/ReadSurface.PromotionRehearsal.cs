using System.Globalization;
using System.Net;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Web.App;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Reading;

// From 24.23 the rehearsal of a promotion, in code with the clock set on both sides of the run of 2027-02-06: over the
// constructed store by default, and over a copy of the operator's store where `EquityBrief__Rehearsal__Store` names the copy's
// folder, which the rehearsal migrates and writes to and the operator's store never is. The case is worked through the case
// code from a real variant's records as of the copy's newest night, only its three conditions set to stand; inside the
// freeze the press is refused; after it Promote waits, the next night swaps, and an Undo swaps back the night after. Where
// `EquityBrief__Rehearsal__Shots` names a folder, Paper trading's and Past picks' pages after Promote and after Undo are
// written there as pages carrying the site's stylesheet, in both palettes, for screenshots.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public partial class ReadSurface
{
    public const string RehearsalStore = "EquityBrief__Rehearsal__Store";

    public const string RehearsalShots = "EquityBrief__Rehearsal__Shots";

    [Fact]
    public async Task APromotionIsRehearsedOnBothSidesOfTheRunThatEndsTheFreezeOverARealVariantsRecords()
    {
        using var constructed = Environment.GetEnvironmentVariable(RehearsalStore) is { Length: > 0 } ? null : await PaperVariantsStore();
        var root = constructed?.Root ?? Environment.GetEnvironmentVariable(RehearsalStore)!;
        var database = Path.Combine(root, "equitybrief.db");

        Assert.False(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).EndsWith(Path.DirectorySeparatorChar + "data", StringComparison.OrdinalIgnoreCase), "The rehearsal runs over a copy and never over the operator's data folder.");

        if (constructed is null)
        {
            EquityBrief.Data.Migrations.MigrationRunner.Standard().Apply(database);
        }

        var reads = new PaperReads(database);
        var newest = DateOnly.ParseExact((string)Scalar(database, "SELECT MAX(session_date) FROM listing;")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // A real variant: the first on the S&P 500 holding trades, its case worked by the case code as of the newest night.
        var worked = await PaperWork.WorkAsync(reads, "GSPC", "S&P 500", true, newest, null);
        var one = PromotionCases.Rows("GSPC", "S&P 500", worked.Sources, worked.Views, worked.Sessions, [], newest)
            .SelectMany(row => row.Variants)
            .Where(view => worked.Sources.Single(source => source.Key == view.Variant).Trades.Count > 0)
            .OrderBy(view => view.Family, StringComparer.Ordinal)
            .First();
        var standing = one with { Conditions = new PromotionConditions(true, true, true) };
        var figures = PromotionCases.Figures(standing);

        Execute(database, "INSERT INTO promotion_case (month, index_code, family, variant, night, figures, figures_key, stored_at, run_id) VALUES ('2027-02', 'GSPC', $family, $variant, $night, $figures, $key, '2027-02-06T13:00:00Z', 'rehearsal') ON CONFLICT DO NOTHING;",
            ("$family", standing.Family), ("$variant", standing.Variant), ("$night", Stamp(newest)), ("$figures", figures), ("$key", PromotionCases.FiguresKey(figures)));

        var route = SinglePageApp.CasePostRoute + "GSPC/" + standing.Family;
        var pressed = Convert.ToInt64(Scalar(database, "SELECT COUNT(*) FROM promotion_press;"), CultureInfo.InvariantCulture);

        rehearsalMinute = (int)(Convert.ToInt64(Scalar(database, "SELECT COUNT(*) FROM run_log WHERE run_id LIKE 'read-api-2027%';"), CultureInfo.InvariantCulture) % 60);

        // Before the run that ends the freeze the press is refused and nothing is written.
        using (var host = new Host(root, At(2027, 2, 5, 22)))
        using (var client = host.CreateClient())
        {
            Assert.Contains("Nothing was promoted: the rules are frozen", (await CardPress(client, route, [("variant", standing.Variant), ("press", "promote")])).Said, StringComparison.Ordinal);
            Assert.Equal(pressed, Convert.ToInt64(Scalar(database, "SELECT COUNT(*) FROM promotion_press;"), CultureInfo.InvariantCulture));
        }

        // After it: Promote waits, the next night swaps, and the pages are drawn as of that night.
        using (var host = new Host(root, At(2027, 2, 6, 14)))
        using (var client = host.CreateClient())
        {
            Assert.Contains(">Waiting: becomes revision ", (await CardPress(client, route, [("variant", standing.Variant), ("press", "promote")])).Said, StringComparison.Ordinal);
        }

        var swapNight = new DateOnly(2027, 2, 8);

        Night(database, newest, swapNight);
        Assert.True(Assert.Single((await new PromotionSwap(At(2027, 2, 8, 23), database).NightAsync($"rehearsal-swap-{rehearsalMinute}", swapNight)).Answers).Applied);
        await ShotsAsync(root, standing.Family, "after-promote", At(2027, 2, 9, 1));

        // Paper trading draws the promoted variant's trades under the revision it became, named as carried at its promotion.
        using (var host = new Host(root, At(2027, 2, 9, 2)))
        using (var client = host.CreateClient())
        {
            Assert.Contains("The trades carried to this account at a promotion", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?rule={standing.Family}")), StringComparison.Ordinal);
        }

        var promoted = (await PaperWork.SourcesAsync(reads, "GSPC", "S&P 500", true, swapNight, yours: false)).Accounts;
        var head = promoted.Single(account => account.Family == standing.Family && account.Standing == PaperAccountView.Live);

        Assert.Equal(standing.Revision + 1, head.Revisions[^1].Number);
        Assert.Equal(
            worked.Sources.Single(source => source.Key == standing.Variant).Trades.Select(trade => (trade.Trade.Ticker, trade.Trade.ListedOn)).ToHashSet(),
            head.Same.Where(trade => trade.Trade.ListedOn >= FamilyStart(worked, standing)).Select(trade => (trade.Trade.Ticker, trade.Trade.ListedOn)).ToHashSet());

        // The Undo swaps them back the night after.
        using (var host = new Host(root, At(2027, 2, 9, 14)))
        using (var client = host.CreateClient())
        {
            var press = Convert.ToInt64(Scalar(database, "SELECT MAX(id) FROM promotion_press WHERE press = 'promote';"), CultureInfo.InvariantCulture);

            Assert.Contains(">Waiting: the promotion is undone from the night of ", (await CardPress(client, route, [("variant", standing.Variant), ("press", "undo"), ("undoes", press.ToString(CultureInfo.InvariantCulture))])).Said, StringComparison.Ordinal);
        }

        var undoNight = new DateOnly(2027, 2, 9);

        Night(database, newest, undoNight);
        Assert.True(Assert.Single((await new PromotionSwap(At(2027, 2, 9, 23), database).NightAsync($"rehearsal-undo-{rehearsalMinute}", undoNight)).Answers).Applied);
        await ShotsAsync(root, standing.Family, "after-undo", At(2027, 2, 10, 1));

        var undone = (await PaperWork.SourcesAsync(reads, "GSPC", "S&P 500", true, undoNight, yours: false)).Accounts;

        Assert.Contains(undone, account => account.Key == standing.Variant);
        Assert.Equal(
            worked.Sources.Single(source => source.Key == standing.Variant).Trades.Select(trade => (trade.Trade.Ticker, trade.Trade.ListedOn)).ToHashSet(),
            undone.Single(account => account.Key == standing.Variant).Same.Select(trade => (trade.Trade.Ticker, trade.Trade.ListedOn)).ToHashSet());
    }

    static DateOnly FamilyStart(PaperWorked worked, PromotionCaseView one) =>
        worked.Sources.Single(source => source.Key == one.Variant).Trades.Select(trade => trade.Trade.ListedOn).DefaultIfEmpty(DateOnly.MinValue).Min();

    // A rehearsal run again over the same copy reads its clock a minute later for each read surface an earlier run started on
    // it, so no row it writes meets one an earlier run wrote at the same instant.
    static int rehearsalMinute;

    static FixedClock At(int year, int month, int day, int hour) =>
        FixedClock.At(new DateTimeOffset(year, month, day, hour, rehearsalMinute, 0, TimeSpan.Zero), SessionZones.UnitedStates);

    // A night the rehearsal reads its pages as of: one listing row of the newest night copied to the night given, so the
    // pages read that night as the newest.
    static void Night(string database, DateOnly newest, DateOnly night) =>
        Execute(database, "INSERT OR IGNORE INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) SELECT ticker, $night, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength FROM listing WHERE session_date = $newest LIMIT 1;",
            ("$night", Stamp(night)), ("$newest", Stamp(newest)));

    // Paper trading's and Past picks' pages as of the night the clock reads, written with the site's stylesheet in both
    // palettes where the shots folder is named.
    static async Task ShotsAsync(string root, string family, string stage, IClock clock)
    {
        if (Environment.GetEnvironmentVariable(RehearsalShots) is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);

        using var host = new Host(root, clock);
        using var client = host.CreateClient();

        foreach (var (name, path) in new[] { ("paper", $"/screens/paper?rule={family}"), ("picks", "/screens/picks") })
        {
            var fragment = await client.GetStringAsync(path);

            foreach (var palette in new[] { "light", "dark" })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(folder, $"{stage}-{name}-{palette}.html"),
                    $"<!doctype html><html lang=\"en\" data-eb-theme=\"{palette}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>{Stylesheet.Css}</style></head><body><main class=\"wrap\" id=\"screen\">{fragment}</main></body></html>");
            }
        }
    }

    static object? Scalar(string database, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    static void Execute(string database, string sql, params (string Name, string Value)[] parameters)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
