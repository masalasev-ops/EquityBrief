namespace EquityBrief.Core.Filter;

// The swing family's shadow as the swing filter's stage runs it, handed in by the night: how far back
// the arrival windows it evaluates reach, its verdicts on one member's inputs as the member's row stores
// them, and what it says on the stage's row. An interface in a file of its own, so the filter's answers
// reach none of a candidate's code, and a candidate's pin can read the filter without the filter's pin
// reading the candidate's.
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
public interface IMemberShadow
{
    // The longest arrival window a standing candidate reads.
    int ArrivalReach { get; }

    // The verdicts on one member's inputs, the member skipped where the night holds no bar for it or
    // holds it across a gap.
    string Evaluate(GateInputs inputs, bool stale, DateOnly? gap);

    // How many verdicts were written.
    int Evaluated { get; }

    // The candidates skipped for a fault in the code, which fails the stage.
    IReadOnlyList<string> Faults { get; }

    // The shadow's sentence on the stage's row.
    string Said { get; }
}
