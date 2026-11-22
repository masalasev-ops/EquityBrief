using EquityBrief.Api.Passes;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
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

    // From 24.23 the presses on the case for promotion and the night's answers to them, through Paper trading's one reader.
    public Task<(IReadOnlyList<PromotionPressRow> Presses, IReadOnlyList<PromotionSwapRow> Answers)> PromotionSwapsAsync() => Paper.PromotionSwapsAsync();

    public async Task<(int Tested, int Passed)> IdeasTestedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IdeasTested;

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    public const string NoCase = "no case stands for this variant at the newest monthly run, so nothing was promoted";

    public const string AlreadyWaiting = "a promotion of this setup on this index already waits for the next night, so nothing more was promoted";

    public const string NothingToUndo = "the press names no promotion of this setup to undo, so nothing was undone";

    const string NewestCaseOf = "SELECT figures FROM promotion_case WHERE index_code = $index AND family = $family AND variant = $variant AND month = (SELECT MAX(month) FROM promotion_case) LIMIT 1;";

    const string NewestMonth = "SELECT MAX(month) FROM promotion_case;";

    // From 24.23 a press of Promote, Keep or Undo on a case, written as the operator's word for the next night to answer and
    // never edited. Inside the rule freeze every press is refused with nothing written. Promote stands only on a variant whose
    // case stands at the newest monthly run, one waiting a setup an index; Keep is recorded and changes nothing; Undo names a
    // promotion of the setup, waiting or applied. The night's own build writes the swap at its start, so the line says the
    // night it runs from.
    // see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
    public async Task<RequestWritten> PromoteAsync(string index, string family, string variant, string press, long? undoes)
    {
        var said = press == PromotionSwaps.Keep ? "kept" : press == PromotionSwaps.Undo ? "undone" : "promoted";

        if (FrozenUntil() is not null)
        {
            return new RequestWritten(false, $"Nothing was {said}: {RuleFreezeDates.ApprovalRefused}, so no press on a case changes a rule before it.");
        }

        var night = PromotionSwaps.NextNight(await NewestNightAsync() ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
        var (presses, answers) = await Paper.PromotionSwapsAsync();
        var states = PromotionSwaps.States(presses, answers).Where(state => state.Press.Index == index && state.Press.Family == family).ToArray();

        await using var connection = Open();

        string? month;
        PromotionCaseView? standing = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestCaseOf;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$variant", variant);

            if (await command.ExecuteScalarAsync() is string figures)
            {
                standing = PromotionCases.FromFigures(figures);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestMonth;
            month = await command.ExecuteScalarAsync() as string;
        }

        string line;

        if (press == PromotionSwaps.Promote)
        {
            if (standing is null)
            {
                return new RequestWritten(false, $"Nothing was promoted: {NoCase}.");
            }

            if (states.Any(state => state.Answer is null && state.UndoneBy is null))
            {
                return new RequestWritten(false, $"Nothing was promoted: {AlreadyWaiting}.");
            }

            line = PromotionSwaps.Waiting(standing.Revision + 1, night);
        }
        else if (press == PromotionSwaps.Undo)
        {
            if (states.FirstOrDefault(state => state.Press.Id == undoes && state.UndoneBy is null && (state.Answer is null || state.Answer.Applied)) is not { } undone)
            {
                return new RequestWritten(false, $"Nothing was undone: {NothingToUndo}.");
            }

            line = undone.Answer is null ? PromotionSwaps.UndoWaiting(night) : PromotionSwaps.UndoApplied(night);
        }
        else
        {
            line = PromotionSwaps.Kept(standing?.Revision ?? 1);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertPress;
            insert.Parameters.AddWithValue("$index", index);
            insert.Parameters.AddWithValue("$family", family);
            insert.Parameters.AddWithValue("$variant", variant);
            insert.Parameters.AddWithValue("$press", press);
            insert.Parameters.AddWithValue("$undoes", press == PromotionSwaps.Undo && undoes is { } id ? id : DBNull.Value);
            insert.Parameters.AddWithValue("$month", (object?)month ?? DBNull.Value);
            insert.Parameters.AddWithValue("$pressed_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));

            await insert.ExecuteNonQueryAsync();
        }

        return new RequestWritten(true, line);
    }
}
