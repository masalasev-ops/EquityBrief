namespace EquityBrief.Core.Releases;

// One half of a measure: the releases on one side of the model's cutoff, counted.
public sealed record AgreementCount(int Compared, int Agreed, int Disagreed, int Apart)
{
    public double? Share => ReleaseProbes.Share(Agreed, Compared);
}

// What the drift's tone variant would keep of a family's settled setups on one side of the cutoff: those passing its
// condition, those failing it on results, those failing it on outlook alone, and those whose print holds no reading.
public sealed record ToneShares(int Setups, int Passing, int FailingResults, int FailingOutlook, int NotHeld)
{
    public double? PassingShare => ReleaseProbes.Share(Passing, Setups - NotHeld);
}

// The arithmetic each accuracy figure is worked by, over rows the accuracy command reads and writes nothing from: the
// results reading against the sign of the provider's surprise for the same print; any answer against a reference that
// states the same answer; the outlook a release's guided figures show against the release before's, paired by place;
// and what the tone variant's condition keeps. Each is split before the model's cutoff and after it by the caller.
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
public static class ReleaseAgreement
{
    // The results reading against the surprise's sign: better agreeing with a surprise above nothing and worse with one
    // below it, each disagreeing with the other sign; in line, not stated and a surprise of nothing counted apart.
    public static AgreementCount ResultsAgainstSurprise(IEnumerable<(string? Results, double? Surprise)> rows)
    {
        var (compared, agreed, disagreed, apart) = (0, 0, 0, 0);

        foreach (var (results, surprise) in rows)
        {
            if (surprise is not { } sign || sign == 0 || results is not ("better" or "worse"))
            {
                apart++;

                continue;
            }

            compared++;

            if (results == "better" == sign > 0)
            {
                agreed++;
            }
            else
            {
                disagreed++;
            }
        }

        return new AgreementCount(compared, agreed, disagreed, apart);
    }

    // An answer against a reference's answer to the same question, the same word agreeing; a reading not held counted
    // apart.
    public static AgreementCount Against(IEnumerable<(string? Model, string? Reference)> rows)
    {
        var (compared, agreed, disagreed, apart) = (0, 0, 0, 0);

        foreach (var (model, reference) in rows)
        {
            if (model is null || reference is null)
            {
                apart++;

                continue;
            }

            compared++;

            if (string.Equals(model, reference, StringComparison.Ordinal))
            {
                agreed++;
            }
            else
            {
                disagreed++;
            }
        }

        return new AgreementCount(compared, agreed, disagreed, apart);
    }

    // The outlook a release's guided figures show against the release before's, paired by place: raised where every pair
    // rose, lowered or withdrawn where every pair fell, kept where every pair held, and none where they disagree or no
    // figure pairs.
    public static string? OutlookFromGuidance(IReadOnlyList<decimal> newest, IReadOnlyList<decimal> before)
    {
        var pairs = newest.Zip(before).ToArray();

        if (pairs.Length == 0)
        {
            return null;
        }

        return pairs.All(pair => pair.First > pair.Second) ? "raised"
            : pairs.All(pair => pair.First < pair.Second) ? "lowered or withdrawn"
            : pairs.All(pair => pair.First == pair.Second) ? "kept"
            : null;
    }

    // What the tone variant's condition keeps: results better and outlook anything but lowered or withdrawn, a print
    // whose release holds no reading counted apart.
    public static ToneShares Tone(IEnumerable<ReleaseAnswer?> answers)
    {
        var (setups, passing, results, outlook, notHeld) = (0, 0, 0, 0, 0);

        foreach (var answer in answers)
        {
            setups++;

            if (answer is null)
            {
                notHeld++;
            }
            else if (answer.Results != "better")
            {
                results++;
            }
            else if (answer.Outlook == "lowered or withdrawn")
            {
                outlook++;
            }
            else
            {
                passing++;
            }
        }

        return new ToneShares(setups, passing, results, outlook, notHeld);
    }
}
