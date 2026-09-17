using System.Globalization;

namespace EquityBrief.Core.Candidates;

// A candidate condition: the moving average convergence histogram crossing its
// neutral rule upward by at least a registered margin.
//
// The other reading on the same panel, registered as its own candidate rather
// than folded into the first, because two conditions tested as one are a family
// of one on the register and a family of two in what is actually being tried,
// and the correction divides by what the register says. Registering each
// separately is what makes the divisor the truth about how many things were
// tested.
// see: The momentum panel is context a reader weighs, and nothing computes with it
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class MomentumHistogramTurn : CandidateEvaluator
{
    public const string EvaluatorName = "momentum-histogram-turn";

    public const string Histogram = "macd_hist";

    public const string Previous = "macd_hist_previous";

    public const string Margin = "margin";

    public override string Name => EvaluatorName;

    public override string Version => "cacff9914aac";

    public override IReadOnlyList<string> Reads => [Histogram, Previous];

    public override IReadOnlyList<string> Parameters => [Margin];

    public override CandidateVerdict Evaluate(CandidateNight night, IReadOnlyDictionary<string, double> parameters)
    {
        var histogram = Required(night, Histogram);
        var previous = Required(night, Previous);
        var margin = parameters[Margin];

        // Crossing, which is a fact about two sessions and not one: a histogram
        // that has been above the rule all month is not turning, and a condition
        // keyed on tonight's sign alone would fire every night of that month and
        // call each one a new event.
        return new CandidateVerdict(
            previous <= 0 && histogram >= margin,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Histogram] = histogram.ToString("0.####", CultureInfo.InvariantCulture),
                [Previous] = previous.ToString("0.####", CultureInfo.InvariantCulture),
                [Margin] = margin.ToString("0.####", CultureInfo.InvariantCulture),
            });
    }
}
