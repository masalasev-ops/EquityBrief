namespace EquityBrief.Core.Research;

// Two reviews of one draft set side by side: of the sentences the draft handed to both held, read by the claim
// checker's own splitter, how many both final drafts kept word for word, how many both changed, and how many one kept
// and the other changed. None where either review came to no draft, since a review that wrote nothing changed nothing.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public sealed record ReviewAgreement(int Sentences, int BothKept, int BothChanged, int Differ)
{
    public int Agreeing => BothKept + BothChanged;

    public static ReviewAgreement? Of(string handed, string? first, string? second)
    {
        if (first is null || second is null)
        {
            return null;
        }

        var sentences = ClaimRules.Sentences(handed).Select(sentence => sentence.Text).ToArray();
        var keptByFirst = Held(first);
        var keptBySecond = Held(second);
        var (bothKept, bothChanged, differ) = (0, 0, 0);

        foreach (var sentence in sentences)
        {
            switch (keptByFirst.Contains(sentence), keptBySecond.Contains(sentence))
            {
                case (true, true):
                    bothKept++;
                    break;
                case (false, false):
                    bothChanged++;
                    break;
                default:
                    differ++;
                    break;
            }
        }

        return new ReviewAgreement(sentences.Length, bothKept, bothChanged, differ);
    }

    static HashSet<string> Held(string draft) => new(ClaimRules.Sentences(draft).Select(sentence => sentence.Text), StringComparer.Ordinal);
}
