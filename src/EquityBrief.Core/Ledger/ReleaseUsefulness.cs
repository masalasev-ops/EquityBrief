using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Ledger;

// One settled setup as the usefulness table reads it: its family, the release its release readings read, and what its
// path came to, its result in risks and its edge over its benchmark.
public sealed record SettledRelease(string Family, LedgerRelease Release, double Result, double Edge);

// What one answer came to: a family's settled setups whose release answered a question with a word, on one side of the
// model's cutoff by the day the SEC accepted the release; how many, how many won, a winner one whose edge was above
// nothing, and their mean result and mean edge.
public sealed record AnswerEdge(string Family, string Question, string Answer, string Side, int Setups, int Winners, double MeanResult, double MeanEdge);

// The rows the newest readings' run stored for a family on an index, with the run, its month and the cutoff they were
// split at, which the Ledger page draws.
public sealed record AnswerTable(string RunId, string Month, DateOnly Cutoff, IReadOnlyList<AnswerEdge> Rows);

// From 20.8 whether the release readings are useful: each answer of each question against the result and the edge of
// every family's settled setups whose release the ledger read, with the counts, over the releases the SEC accepted after
// the model's cutoff and beside them over those before it. The years after the cutoff are the ones relied on, since a
// model trained on text up to its cutoff may have read what followed a release before it; the years before are shown.
// A release whose reading was refused or is not held stands under its own answer, so no setup is dropped unseen.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public static class ReleaseUsefulness
{
    public const string Before = "before";

    public const string After = "after";

    // The answer a release stands under where no word of its is held.
    public const string NotRead = "not read";

    // A release the SEC accepted on or before the cutoff's day, in UTC, is before it.
    public static string SideOf(DateTimeOffset accepted, DateOnly cutoff) =>
        DateOnly.FromDateTime(accepted.UtcDateTime) <= cutoff ? Before : After;

    // Every question's every answer a setup holds, in the questions' order and each question's words' order, the
    // releases not read last, and each answer's side before the cutoff and then after it, where it holds any.
    public static IReadOnlyList<AnswerEdge> Of(IReadOnlyList<SettledRelease> setups, DateOnly cutoff)
    {
        var rows = new List<AnswerEdge>();

        foreach (var family in setups.Select(setup => setup.Family).Distinct(StringComparer.Ordinal))
        {
            var own = setups.Where(setup => string.Equals(setup.Family, family, StringComparison.Ordinal)).ToArray();

            foreach (var question in ReleaseQuestions.All)
            {
                foreach (var answer in question.Words.Select(word => word.Word).Append(NotRead))
                {
                    foreach (var side in new[] { Before, After })
                    {
                        var held = own
                            .Where(setup => string.Equals(setup.Release.Answer?.Word(question.Key) ?? NotRead, answer, StringComparison.Ordinal))
                            .Where(setup => SideOf(setup.Release.Accepted, cutoff) == side)
                            .ToArray();

                        if (held.Length > 0)
                        {
                            rows.Add(new AnswerEdge(
                                family,
                                question.Key,
                                answer,
                                side,
                                held.Length,
                                held.Count(setup => setup.Edge > 0),
                                held.Average(setup => setup.Result),
                                held.Average(setup => setup.Edge)));
                        }
                    }
                }
            }
        }

        return rows;
    }
}
