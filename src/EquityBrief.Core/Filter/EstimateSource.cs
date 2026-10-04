using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Filter;

// The analysts' estimates the swing filter's stage hands its shadow, handed in by the night: for the members a rule
// reading them passes on everything else, each member's reading for the night, those the night already stored read
// back and the rest asked for once each, and what it says on the stage's row. An interface in a file of its own, as
// the shadow is, so the filter's answers reach none of the code that asks.
// see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
public interface IEstimateSource
{
    Task<IReadOnlyDictionary<string, EstimateReading>> ReadAsync(DateOnly night, IReadOnlyList<string> tickers, string runId, CancellationToken cancellation = default);

    // The source's sentence on the stage's row.
    string Said { get; }
}
