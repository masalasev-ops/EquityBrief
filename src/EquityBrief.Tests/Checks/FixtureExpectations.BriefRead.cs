using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.4: the brief read atop each fixture name's chart page over the fixture's whole night, each line
// worked by the suite from the figure the store holds for it in the template's words and read back off the rendered page,
// with the states the file derives by hand.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
public partial class FixtureExpectations
{
    [Fact]
    public async Task EachFixtureNamesBriefIsWorkedFromItsStoredFiguresAndReadBackOffItsChartPage()
    {
        var expected = Expected("brief-read");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());

        var night = DateOnly.ParseExact(expected.GetProperty("night").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var day = expected.GetProperty("night").GetString()!;
        string[] names = [.. expected.GetProperty("names").EnumerateArray().Select(name => name.GetString()!)];

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var read = new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero), SessionZones.UnitedStates));
        var universe = await read.UniverseAsync("GSPC", night);

        // Every member's six-month return off the ledger's readings of the night, for the suite's own places among them.
        var halfYearAt = Core.Ledger.LedgerReadings.IndexOf("return_half_year");
        var halfYears = Query(store, $"SELECT ticker, readings FROM ledger_reading WHERE index_code = 'GSPC' AND session_date = '{day}';")
            .Select(row => row.Split('|', 2))
            .ToDictionary(row => row[0], row => JsonDocument.Parse(row[1]).RootElement[halfYearAt] is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : (double?)null, StringComparer.Ordinal);

        static string Days(double days, string side, string at) =>
            Math.Round(days, 1) == 0 ? at : FormattableString.Invariant($"{days:0.0} typical {(Math.Round(days, 1) == 1 ? "day's move" : "days' moves")} {side}");

        static string Price(decimal price) => price.ToString("#,##0.00", CultureInfo.InvariantCulture);

        static string Zone(decimal low, decimal high) => low == high ? Price(low) : $"{Price(low)} to {Price(high)}";

        foreach (var ticker in names)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}"));
            var brief = Regex.Match(page, "<div class=\"brief-read\".*?<p class=\"brief-foot\">", RegexOptions.Singleline).Value;
            var lines = Regex.Matches(brief, "<li data-line=\"([^\"]+)\"[^>]*>(.*?)</li>").ToDictionary(match => match.Groups[1].Value, match => Regex.Replace(match.Groups[2].Value, "<[^>]+>", string.Empty), StringComparer.Ordinal);
            var chips = Regex.Matches(brief, "data-family=\"([^\"]+)\" data-state=\"([^\"]+)\"").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
            var member = universe.Single(row => row.Ticker == ticker);

            Assert.Equal(13, lines.Count);

            // The trend, as the file states it.
            var trend = expected.GetProperty("trend").GetProperty(ticker).GetString()!;

            Assert.Equal(trend, member.TrendState);
            Assert.Equal(trend == "range" ? "Trend: moving sideways." : "Trend: an uptrend.", lines["trend"]);

            // The bands: each near edge's distance from the close over the typical move, by the suite's own arithmetic.
            var close = member.Close!.Value;
            var move = member.TypicalMove!.Value;
            var inSupport = member.SupportFloor is { } floor && close >= floor && close <= member.NearestSupport!.Value;
            var support = inSupport
                ? $"Inside its support band at {Zone(member.SupportFloor!.Value, member.NearestSupport!.Value)}"
                : $"Support at {Price(member.NearestSupport!.Value)}, " + Days((double)Math.Abs(close - member.NearestSupport!.Value) / move, "below", "at the price");
            var resistance = $"resistance at {Price(member.NearestResistance!.Value)}, " + Days((double)Math.Abs(close - member.NearestResistance!.Value) / move, "above", "at the price");

            Assert.Equal(support + "; " + resistance + ".", lines["bands"]);

            // Momentum: the newest relative strength and trend momentum line against their middles of 50 and zero.
            double? Newest(string name) =>
                Query(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND name = '{name}' ORDER BY session_date DESC LIMIT 1;").Single() is var held && held != "null"
                    ? double.Parse(held, CultureInfo.InvariantCulture)
                    : null;

            var rsi = Newest("rsi14")!.Value;
            var line = Newest("macd_hist")!.Value;
            var words = rsi > 50 && line > 0 ? "rising" : rsi <= 50 && line <= 0 ? "falling" : "mixed";

            Assert.Equal(FormattableString.Invariant($"Momentum {words}: relative strength {rsi:0.0} {(rsi > 50 ? "above" : "at or below")} its middle of 50, the trend momentum line {(line > 0 ? "above" : "at or below")} zero."), lines["momentum"]);

            // Six months: the share of the members' returns under the stock's with half of those level with it, its own
            // among them.
            var own = halfYears[ticker]!.Value;
            var among = halfYears.Values.OfType<double>().ToArray();
            var place = (among.Count(other => other < own) + 0.5 * among.Count(other => other == own)) / among.Length;
            var rank = (int)Math.Round(place * 100, MidpointRounding.AwayFromZero);
            var suffix = (rank % 100) is 11 or 12 or 13 ? "th" : (rank % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

            Assert.Equal($"Six months: its return ranks at the {rank}{suffix} percentile of the S&P 500's stocks.", lines["strength"]);

            // The business: no reported quarter read for the night.
            Assert.Equal(["no fundamentals yet"], Query(store, $"SELECT state FROM fundamental_reading WHERE ticker = '{ticker}' AND session_date = '{day}';"));
            Assert.Equal("Sales: too few quarters are stored to read them against a year earlier.", lines["sales"]);
            Assert.Equal("Operating margin: too few quarters are stored to read it against a year earlier.", lines["margin"]);
            Assert.Equal("Earnings multiple: not read, no quarter stored.", lines["multiple"]);

            // The analysts: the newest fetch's mean target against the last stored close, and its year's revisions over 30
            // days, labelled as theirs and dated by the fetch.
            var fetch = Query(store, $"SELECT target_price, substr(fetched_at, 1, 10) FROM company WHERE ticker = '{ticker}' AND substr(fetched_at, 1, 10) <= '{day}' ORDER BY fetched_at DESC LIMIT 1;").Single().Split('|');
            var target = decimal.Parse(fetch[0], CultureInfo.InvariantCulture);
            var last = decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' ORDER BY session_date DESC LIMIT 1;").Single(), CultureInfo.InvariantCulture);
            var against = (double)((target / last) - 1m) * 100;
            var revisions = Query(store, $"SELECT up_last_thirty_days, down_last_thirty_days FROM estimate_trend WHERE ticker = '{ticker}' AND period = '0y' ORDER BY fetched_at DESC LIMIT 1;");
            var moved = revisions is [var both] && both.Split('|') is [var up, var down] && up != "null" && down != "null"
                ? $"; over 30 days {up} raised and {down} cut their estimate for the year"
                : "; which way their estimates moved over 30 days is not stored";

            Assert.Equal(
                FormattableString.Invariant($"The analysts' mean target, as fetched on {fetch[1]}: {Price(target)}, {Math.Abs(against):0.0}% {(against >= 0 ? "above" : "below")} the price{moved}."),
                lines["analysts"]);

            // The way in: the night's levels plan's first zone and its stop, its first traded sale and the trade step's reward
            // to risk on it.
            using var plan = JsonDocument.Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{ticker}' AND as_of <= '{day}' ORDER BY as_of DESC LIMIT 1;").Single());
            var tranche = plan.RootElement.GetProperty("tranches")[0];
            var low = decimal.Parse(tranche.GetProperty("lowEdge").GetString()!, CultureInfo.InvariantCulture);
            var high = decimal.Parse(tranche.GetProperty("highEdge").GetString()!, CultureInfo.InvariantCulture);
            var stop = decimal.Parse(tranche.GetProperty("stop").GetString()!, CultureInfo.InvariantCulture);
            var sale = plan.RootElement.GetProperty("exits").EnumerateArray().FirstOrDefault(exit => exit.GetProperty("traded").GetBoolean());
            var ratio = Query(store, $"SELECT ladder_reward_to_risk FROM gate_result WHERE ticker = '{ticker}' AND session_date = '{day}';").Single();

            Assert.Equal($"Not among tonight's picks. The night's levels plan buys in its first zone at {Zone(low, high)}.", lines["buy"]);
            Assert.Equal($"Its stop: a daily close below {Price(stop)}.", lines["stop"]);
            Assert.Equal(
                sale.ValueKind == JsonValueKind.Undefined
                    ? "No first sale is stored."
                    : $"Its first sale at {Price(decimal.Parse(sale.GetProperty("lowEdge").GetString()!, CultureInfo.InvariantCulture))}" + (ratio == "null" ? "." : FormattableString.Invariant($": risk $1 to make ${double.Parse(ratio, CultureInfo.InvariantCulture):0.00}.")),
                lines["exit"]);
            Assert.Equal("Your account is not set: set your account's size and risk to see a size.", lines["size"]);

            // The chips: the drift's newest print outside its window on every name, NFLX one step short of the dip at its
            // trend, and every other setup not close; none held in the heavyweights' book.
            var newest = DateOnly.ParseExact(expected.GetProperty("newestReaction").GetProperty(ticker).GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.Equal(newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Query(store, $"SELECT MAX(reaction_session) FROM earnings_reaction WHERE ticker = '{ticker}';").Single());
            Assert.True(ExchangeClosures.SessionsUntil(newest, night) > DriftRule.WindowSessions);

            var closest = expected.GetProperty("closest").TryGetProperty(ticker, out var near) ? near.GetString() : null;
            var closestWords = expected.GetProperty("closestWords").TryGetProperty(ticker, out var stepWords) ? stepWords.GetString() : null;

            // From 22.13 the Fundamentals family's chip in the fundamentals-first family's place: its night answered the name
            // with no status, no filing read for the first time and nothing flagged, and it keeps no trade.
            Assert.Equal(["|0|0"], Query(store, $"SELECT ifnull(status, '') || '|' || first_read || '|' || flagged FROM improving_result WHERE index_code = 'GSPC' AND session_date = '{day}' AND ticker = '{ticker}';"));
            Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM improving_trade WHERE ticker = '{ticker}';"));

            Assert.Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SetupFamilies.Pullback] = BriefStates.NotClose,
                    [BreakoutRule.Name] = BriefStates.NotClose,
                    [DriftRule.Name] = BriefStates.NoReport,
                    [ImprovingRule.Name] = BriefStates.NotHeld,
                    [DipRule.Name] = closest is null ? BriefStates.NotClose : BriefStates.Short,
                    [SetupFamilies.SectorHeavyweights.Name] = BriefStates.NotHeld,
                },
                chips);

            // The summary, from the same figures and the closest setup the file derives.
            var standing = closest is null
                ? "Tonight no setup is one step from it."
                : $"Tonight the closest setup is {SetupFamilies.Dips.Label}, one step short: {closestWords}.";
            var bandsPhrase = (inSupport ? "inside its support band" : Days((double)Math.Abs(close - member.NearestSupport!.Value) / move, "above its nearest support", "at its nearest support"))
                + " and " + Days((double)Math.Abs(close - member.NearestResistance!.Value) / move, "below its nearest resistance", "at its nearest resistance");

            Assert.Equal(
                $"{ticker} {(trend == "range" ? "is moving sideways" : "is in an uptrend")}, {bandsPhrase}. No reported quarter is stored for it yet. {standing}",
                Regex.Match(brief, "<p class=\"brief-summary\" data-summary>(.*?)</p>").Groups[1].Value);

            // What to watch for: the next report after the night inside the 21 sessions a hold with no cap reads, and the
            // median report move in the levels plan's risks past one, by the suite's own arithmetic; no insiders' history.
            var report = Query(store, $"SELECT MIN(event_date) FROM calendar WHERE ticker = '{ticker}' AND kind = 'earnings' AND event_date > '{day}';").Single();
            var risks = Query(store, $"SELECT move_pct FROM earnings_reaction WHERE ticker = '{ticker}';")
                .Select(moved => Math.Abs(double.Parse(moved, CultureInfo.InvariantCulture)) / 100 * (double)high / (double)(high - stop))
                .Order()
                .ToArray();
            var median = risks.Length % 2 == 1 ? risks[risks.Length / 2] : (risks[(risks.Length / 2) - 1] + risks[risks.Length / 2]) / 2;
            var said = new List<string>();

            if (report != "null" && DateOnly.ParseExact(report, "yyyy-MM-dd", CultureInfo.InvariantCulture) <= Core.Cards.CardHitsReading.HoldThrough(night, null))
            {
                said.Add($"its report on {report} falls inside a typical hold");
            }

            if (median > 1)
            {
                said.Add(FormattableString.Invariant($"its reports have moved it a median {median:0.0} times the first stop's distance"));
            }

            Assert.Equal(
                said.Count == 0 ? string.Empty : "Watch for: " + string.Join("; ", said) + ".",
                Regex.Match(brief, "<p class=\"brief-watch\">(.*?)</p>").Groups[1].Value);

            // From 22.8 the fundamentals card beneath the brief and on the full page, read back against the scorecard the
            // night stored and the states the scorecard's file derives by hand: no filed fact for three of the names, and
            // Keysight's capture reading its share check alone of the nine to its quarter of 2026-07-31.
            var scorecard = Expected("scorecard");
            var keysight = scorecard.GetProperty("keysight");
            var stored = Query(store, $"SELECT ifnull(quarter, 'none'), applied, ifnull(word, 'none'), checks FROM fundamental_score WHERE ticker = '{ticker}' AND session_date = '{day}';").Single().Split('|', 4);
            var summary = ticker == keysight.GetProperty("ticker").GetString()
                ? keysight.GetProperty("summary").GetString()!
                : scorecard.GetProperty("unfiledSummary").GetString()!;

            Assert.Equal(scorecard.GetProperty("night").GetString(), day);
            Assert.Equal(ticker == "KEYS" ? keysight.GetProperty("quarter").GetString() : "none", stored[0]);
            Assert.Equal(ticker == "KEYS" ? keysight.GetProperty("applied").GetInt32().ToString(CultureInfo.InvariantCulture) : "0", stored[1]);
            Assert.Equal("none", stored[2]);
            Assert.Equal(ticker != "KEYS", scorecard.GetProperty("unfiled").EnumerateArray().Any(name => name.GetString() == ticker));
            Assert.Equal("Fundamentals: " + char.ToLowerInvariant(summary[0]) + summary[1..], lines["fundamentals"]);

            foreach (var (path, region) in new[] { ($"/screens/chart/{ticker}", "chart-page-fundamentals"), ($"/screens/name/{ticker}", "fundamentals") })
            {
                var card = Regex.Match(path.Contains("/chart/", StringComparison.Ordinal) ? page : WebUtility.HtmlDecode(await client.GetStringAsync(path)), $"<section class=\"card\"[^>]*data-card=\"{region}\".*?</section>", RegexOptions.Singleline).Value;
                var answered = Regex.Matches(card, "<li data-check=\"(\\d)\" data-answer=\"([^\"]+)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}").ToArray();
                var context = Regex.Matches(card, "<li data-line=\"([^\"]+)\"[^>]*>(.*?)</li>").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);

                Assert.Equal(summary, Regex.Match(card, "<p class=\"fundamentals-summary\" data-summary>(.*?)</p>").Groups[1].Value);

                // Each check as stored, every one not filed but Keysight's share check, which passes; and each line not
                // scored saying it reads nothing.
                int[] passed = ticker == "KEYS" ? [.. keysight.GetProperty("passed").EnumerateArray().Select(number => number.GetInt32())] : [];

                Assert.Equal(
                    Core.Ledger.ScorecardText.ChecksOf(stored[3]).Select(check => $"{check.Number}|{Core.Ledger.ScorecardText.Word(check.Answer)}"),
                    answered);
                Assert.Equal(
                    Enumerable.Range(1, 9).Select(number => $"{number}|{(passed.Contains(number) ? "passed" : "not filed")}"),
                    answered);
                Assert.Equal("Sales against a year before: not read from its filings.", context["sales"]);
                Assert.Equal("Gross profit against assets: not read from its filings.", context["gross"]);
                Assert.Equal("Price against earnings: not read, as it earned nothing over its last four quarters or no share count is filed.", context["price"]);
            }
        }
    }
}
