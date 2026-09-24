using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 11.9: the facts file carries the name's group and each move's median over
// its own sessions, each print's reaction newest first and the dividend the newest filing carries,
// so prose may quote what the page draws beside them.
// see: Every number in written prose must exist in the facts file
// see: A name's facts file is assembled again for its night when an open fetches its fundamentals
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFactsFileCarriesTheGroupMediansTheReactionsNewestFirstAndTheDividendAsFiled()
    {
        using var store = await WithFacts();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // The opens' fetch after the night, and the night's facts assembled again once it is stored,
        // which is what the research verb does after a fetch.
        foreach (var name in FixtureExpectation.Names)
        {
            await new FundamentalsFetcher(RecordedFundamentalsFeed.FromFolder(Folder()), clock, store.DatabaseFile, null)
                .RunAsync(name, null, "facts-parts-fundamentals-" + name);
        }

        await new FactsAssembler(clock, store.DatabaseFile).RunAsync("facts-parts");

        var read = 0;

        foreach (var name in FixtureExpectation.Names)
        {
            using var payload = JsonDocument.Parse(Query(store, $"SELECT payload FROM facts WHERE ticker = '{name}';").Single());

            var facts = payload.RootElement.GetProperty("facts").EnumerateArray().ToDictionary(
                fact => fact.GetProperty("name").GetString()!,
                fact => (Value: fact.GetProperty("value").GetString()!, Source: fact.GetProperty("source").GetString()!),
                StringComparer.Ordinal);

            // The group, worked by hand from the membership: each Technology name's group is its
            // sector with the other two in it, and NFLX's is its sector holding nobody, since no
            // industry the fixture holds has five members.
            var technology = name != "NFLX";

            Assert.Equal((technology ? "Technology" : "Communication Services", FactsAssembler.FromMoves), facts["group"]);
            Assert.Equal(("sector", FactsAssembler.FromMoves), facts["group kind"]);
            Assert.Equal((technology ? "2" : "0", FactsAssembler.FromMoves), facts["group members"]);

            // Each of the eight moves' medians with how many members it counted, as the annotator
            // stored them, and a group holding nobody counting none and saying there is no median.
            var moves = Query(store, $"SELECT rank || '|' || group_counted || '|' || IFNULL(group_median, 'none') FROM move WHERE ticker = '{name}' ORDER BY rank;");

            Assert.Equal(8, moves.Count);

            foreach (var move in moves.Select(line => line.Split('|')))
            {
                var prefix = move[0] == "1" ? "largest move" : "move " + move[0];
                var median = move[2] == "none" ? "not available" : double.Parse(move[2], CultureInfo.InvariantCulture).ToString("0.######", CultureInfo.InvariantCulture);

                Assert.Equal((move[1], FactsAssembler.FromMoves), facts[prefix + " group members counted"]);
                Assert.Equal((median, FactsAssembler.FromMoves), facts[prefix + " group median per cent"]);

                if (!technology)
                {
                    Assert.Equal(("0", "not available"), (move[1], median));
                }
            }

            // Each print newest first, named by its place counting back from the newest with its
            // report date a value rather than a part of its name, and a figure the provider did not
            // file saying so.
            var prints = Query(
                store,
                "SELECT report_date || '|' || timing || '|' || reaction_session || '|' || IFNULL(estimate, 'none filed') || '|' || IFNULL(actual, 'none filed') || '|' || IFNULL(surprise_pct, 'none filed') || '|' || move_pct " +
                $"FROM earnings_reaction WHERE ticker = '{name}' ORDER BY report_date DESC;");

            Assert.NotEmpty(prints);

            foreach (var (print, place) in prints.Select((line, at) => (line.Split('|'), at + 1)))
            {
                var prefix = place == 1 ? "latest earnings reaction" : "earnings reaction " + place.ToString(CultureInfo.InvariantCulture);

                string Figure(string stored) =>
                    stored == "none filed" ? stored : double.Parse(stored, CultureInfo.InvariantCulture).ToString("0.######", CultureInfo.InvariantCulture);

                Assert.Equal((print[0], FactsAssembler.FromReactions), facts[prefix + " report date"]);
                Assert.Equal((print[1], FactsAssembler.FromReactions), facts[prefix + " timing"]);
                Assert.Equal((print[2], FactsAssembler.FromReactions), facts[prefix + " session"]);
                Assert.Equal((print[3], FactsAssembler.FromReactions), facts[prefix + " estimate"]);
                Assert.Equal((print[4], FactsAssembler.FromReactions), facts[prefix + " actual"]);
                Assert.Equal((Figure(print[5]), FactsAssembler.FromReactions), facts[prefix + " surprise per cent"]);
                Assert.Equal((Figure(print[6]), FactsAssembler.FromReactions), facts[prefix + " move per cent"]);

                read++;
            }

            // No print beyond the ones stored, and no date written into a fact's name, since the
            // claim checker reads a number in a name as a window the file carries.
            Assert.Equal(prints.Count * 7, facts.Count(fact => fact.Value.Source == FactsAssembler.FromReactions));
            Assert.DoesNotContain(
                facts.Where(fact => fact.Value.Source == FactsAssembler.FromReactions).Select(fact => fact.Key),
                key => System.Text.RegularExpressions.Regex.IsMatch(key, @"\d{4}-\d{2}-\d{2}"));

            // The dividend, read off each capture with this test's own JSON reading: AAPL and MSFT
            // pay one, and KEYS and NFLX file a rate of zero and no dates, so they carry no date.
            using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), $"fundamentals-{name}.json")));

            var filed = captured.RootElement.GetProperty("SplitsDividends");

            string? Number(string key) =>
                filed.GetProperty(key).ValueKind == JsonValueKind.Number
                    ? decimal.Parse(filed.GetProperty(key).GetRawText(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                    : null;

            string? Day(string key) =>
                filed.GetProperty(key).ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(filed.GetProperty(key).GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;

            foreach (var (fact, value) in new[]
            {
                ("dividend forward annual rate", Number("ForwardAnnualDividendRate")),
                ("dividend forward yield", Number("ForwardAnnualDividendYield")),
                ("dividend payout ratio", Number("PayoutRatio")),
                ("ex-dividend date", Day("ExDividendDate")),
                ("dividend pay date", Day("DividendDate")),
            })
            {
                if (value is null)
                {
                    Assert.False(facts.ContainsKey(fact), $"{name}'s file carries '{fact}', which its capture does not file.");
                }
                else
                {
                    Assert.Equal((value, FactsAssembler.FromFundamentals), facts[fact]);
                }
            }

            Assert.Equal(technology && name != "KEYS", facts.ContainsKey("ex-dividend date"));
        }

        // AAPL's, stated as well as derived.
        using (var aapl = JsonDocument.Parse(Query(store, "SELECT payload FROM facts WHERE ticker = 'AAPL';").Single()))
        {
            var values = aapl.RootElement.GetProperty("facts").EnumerateArray().ToDictionary(fact => fact.GetProperty("name").GetString()!, fact => fact.GetProperty("value").GetString()!, StringComparer.Ordinal);

            Assert.Equal(
                ("1.08", "0.0033", "0.1216", "2026-08-10", "2026-08-13"),
                (values["dividend forward annual rate"], values["dividend forward yield"], values["dividend payout ratio"], values["ex-dividend date"], values["dividend pay date"]));
        }

        // Every print the store holds reached a file.
        Assert.Equal(int.Parse(Query(store, "SELECT COUNT(*) FROM earnings_reaction;")[0], CultureInfo.InvariantCulture), read);
        Assert.InRange(read, 12, int.MaxValue);
    }
}
