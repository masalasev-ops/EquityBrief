namespace EquityBrief.Core.Releases;

// A release longer than the window the model is loaded at, read from its opening that fits. The runtime counts a
// request's tokens as it refuses it, so the text is cut to the share of its characters that the context, less the
// answer's budget and a margin, holds at the density the refused request was counted at, back to the last line's end in
// its last tenth, with a marker where it was cut; a cut the runtime refuses again is cut again on its own count, up to a
// number of times, and a release still refused after them stands refused. A request the runtime answers but counts past
// the context less the answer's budget, so its answer is cut off for want of room, is cut again on that count within the
// same number, since a cut held to the density of the release as a whole can keep an opening denser than it. A release
// that fits is read whole as before.
// The version is the pin of this file, and it is part of the reading's prompt version, so a changed rule of the fit is a
// new reading version.
// see: A release longer than the model's window is read from its opening that fits, with a marker where it was cut
public static class ReleaseFit
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "a2c0158a0eff";

    // The words set where a release was cut.
    public const string Marker = "[The release continues past this point. It was cut here to fit the reading's window.]";

    // The tokens kept clear beside the answer's budget, for the chat template's own and a cut's last line.
    public const int Margin = 1024;

    // How many times a release is cut before it stands refused.
    public const int Cuts = 3;

    const string Separator = "\n\n";

    // The text cut to fit after the runtime refused a request of `sent` characters, the instruction's among them, as
    // `promptTokens` past a `context` of tokens: the characters the context less the answer's budget and the margin holds
    // at that density, less the instruction's and the marker's, back to the last line's end where one stands in the last
    // tenth of what is kept, and the marker after it; none where nothing would be kept or the cut would keep all it had.
    public static string? Cut(string text, int instructionLength, int sent, int promptTokens, int context, int answerTokens)
    {
        var room = context - answerTokens - Margin;

        if (room <= 0 || promptTokens <= 0 || sent <= 0)
        {
            return null;
        }

        var keep = (int)((long)sent * room / promptTokens) - instructionLength - Marker.Length - Separator.Length;

        if (keep <= 0)
        {
            return null;
        }

        var kept = text.Length - (text.EndsWith(Marker, StringComparison.Ordinal) ? Marker.Length + Separator.Length : 0);

        if (keep >= kept)
        {
            return null;
        }

        var line = text.LastIndexOf('\n', keep - 1);
        var end = line >= keep - (keep / 10) ? line : keep;

        return text[..end].TrimEnd() + Separator + Marker;
    }

    // The characters of the release a cut keeps, before the marker.
    public static int Kept(string cut) => cut.Length - Separator.Length - Marker.Length;

    // Whether a request the runtime answered left its answer less than the answer's budget: its tokens, as the runtime
    // counted them, past the context less that budget.
    public static bool Overran(int promptTokens, int context, int answerTokens) => promptTokens > context - answerTokens;
}
