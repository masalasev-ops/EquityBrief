using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Improving;
using EquityBrief.Worker.Quality;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.4: a quality rule and a Fundamentals rule read conditions on the catalogue's readings and an
// order by a score beside their own parameters, each read off its registration by its family's step, and over a
// constructed night the members a book may hold are kept and ordered as the hooks say while a rule stating none keeps
// the family's own order whole.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
public partial class FixtureExpectations
{
    [Fact]
    public void AQualityRuleAndAFundamentalsRuleReadConditionsAndAScoreBesideTheirParametersAndListAsTheHooksSay()
    {
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var quality = new QualityCandidate(MarketCloses.Index);
        var improving = new ImprovingCandidate(MarketCloses.Index);

        RegisterRow Row(long id, CandidateEvaluator evaluator, string candidate, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "rule", "test", evaluator.Name, CandidateEvaluator.Write(parameters), evaluator.Version, "registered", null, at, null);

        var hooks = new Dictionary<string, double>(StringComparer.Ordinal) { ["also_close_over_long_above"] = 1, ["score_close_over_fifty"] = 1 };
        var qualityRules = QualityFamily.RulesOn(MarketCloses.Index,
        [
            Row(1, quality, FamilyRecords.LivePrefix + "the S&P 500 quality rule", QualityCandidate.ParametersOf(QualityRule.Starting)),
            Row(2, quality, "the S&P 500 quality rule over its long average", new Dictionary<string, double>(QualityCandidate.ParametersOf(QualityRule.Starting), StringComparer.Ordinal).Concat(hooks).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
        ], at.AddHours(1));
        var improvingRules = ImprovingFamily.RulesOn(MarketCloses.Index,
        [
            Row(3, improving, FamilyRecords.LivePrefix + "the S&P 500 Fundamentals rule", ImprovingCandidate.ParametersOf(ImprovingGrid.Starting)),
            Row(4, improving, "the S&P 500 Fundamentals rule over its long average", new Dictionary<string, double>(ImprovingCandidate.ParametersOf(ImprovingGrid.Starting), StringComparer.Ordinal).Concat(hooks).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
        ], at.AddHours(1));

        // Each family's two rules at the starting setting, the live rule's hooks off and the version's a condition and a
        // score, read beside parameters that stay the setting's own.
        Assert.Equal((QualityRule.Starting, QualityRule.Starting), (qualityRules[0].Settings, qualityRules[1].Settings));
        Assert.Equal((ImprovingGrid.Starting, ImprovingGrid.Starting), (improvingRules[0].Setting, improvingRules[1].Setting));
        Assert.True(qualityRules[0].Hooks.IsOff);
        Assert.True(improvingRules[0].Hooks.IsOff);
        Assert.Equal(("close_over_long", "close_over_fifty"), (qualityRules[1].Hooks.Also.Single().Column, LedgerReadings.All[qualityRules[1].Hooks.Score.Keys.Single()].Column));
        Assert.Equal(("close_over_long", "close_over_fifty"), (improvingRules[1].Hooks.Also.Single().Column, LedgerReadings.All[improvingRules[1].Hooks.Score.Keys.Single()].Column));
        Assert.True(qualityRules[1].Hooks.AreABooks);

        // Six qualifying members in the family's own order, nine in nine first: the live rule keeps all six as they stand,
        // the version keeps the four at or above their long average and orders them by their close over the 50-session
        // average, highest first, the family's order breaking the tie between E and F.
        (int Passed, int Applied, double? Place, string Ticker)[] members = [(9, 9, 0.9, "A"), (8, 9, 0.8, "B"), (8, 9, 0.7, "C"), (7, 9, 0.6, "D"), (7, 9, 0.5, "E"), (7, 9, 0.4, "F")];
        var long200 = LedgerReadings.IndexOf("close_over_long");
        var fifty = LedgerReadings.IndexOf("close_over_fifty");
        var readings = new Dictionary<string, (double Long, double Fifty)>(StringComparer.Ordinal)
        {
            ["A"] = (1.05, 1.01), ["B"] = (0.98, 1.08), ["C"] = (1.10, 1.03), ["D"] = (0.95, 1.09), ["E"] = (1.02, 1.05), ["F"] = (1.01, 1.05),
        };

        double?[] Of(string ticker)
        {
            var row = new double?[LedgerReadings.Count];

            row[long200] = readings[ticker].Long;
            row[fifty] = readings[ticker].Fifty;

            return row;
        }

        var ranked = QualityRule.Ranked(members, member => member);

        Assert.Equal(["A", "B", "C", "D", "E", "F"], ranked.Select(member => member.Ticker));
        Assert.Equal(["A", "B", "C", "D", "E", "F"], qualityRules[0].Hooks.Order(ranked, member => Of(member.Ticker)).Select(member => member.Ticker));
        Assert.Equal(["E", "F", "C", "A"], qualityRules[1].Hooks.Order(ranked, member => Of(member.Ticker)).Select(member => member.Ticker));
        Assert.Equal(["E", "F", "C", "A"], improvingRules[1].Hooks.Order(ranked, member => Of(member.Ticker)).Select(member => member.Ticker));
        Assert.Equal(["A", "B", "C", "D", "E", "F"], improvingRules[0].Hooks.Order(ranked, member => Of(member.Ticker)).Select(member => member.Ticker));
    }
}
