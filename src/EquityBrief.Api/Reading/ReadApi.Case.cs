using EquityBrief.Api.Passes;
using RuleFreezeDates = EquityBrief.Core.Candidates.RuleFreezeDates;

namespace EquityBrief.Api.Reading;

// The case for promotion's reads and its press: the ideas the monthly runs have tested to date and those that passed, which
// the case against counts; and Promote or Keep, refused inside the rule freeze with nothing written. The swap a press makes
// once the freeze has ended is its own checkpoint's, so until it lands a press after the freeze writes nothing either.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
// see: An approval inside the rule freeze is refused and replaces no live rule
public sealed partial class ReadApi
{
    const string IdeasTested = "SELECT COUNT(*), COALESCE(SUM(passed), 0) FROM loop_proposal;";

    public const string SwapNotBuilt = "the swap a promotion makes is built before the monthly run of 6 Feb 2027, and until it is a press changes nothing";

    public async Task<(int Tested, int Passed)> IdeasTestedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IdeasTested;

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    // A press of Promote or Keep on a case. Nothing is written: inside the rule freeze the press is refused with why, and after
    // it the swap waits for its own checkpoint.
    public RequestWritten Promote(bool promote) =>
        FrozenUntil() is not null
            ? new RequestWritten(false, $"Nothing was {(promote ? "promoted" : "kept")}: {RuleFreezeDates.ApprovalRefused}, so no press on a case changes a rule before it.")
            : new RequestWritten(false, $"Nothing was {(promote ? "promoted" : "kept")}: {SwapNotBuilt}.");
}
