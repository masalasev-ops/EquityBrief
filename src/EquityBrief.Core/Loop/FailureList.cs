namespace EquityBrief.Core.Loop;

// One closed pick of a rule as its failure list reads it: its edge, the result less the same plan bought on every member
// that session, and its readings as they stood.
public sealed record FailurePick(double Edge, IReadOnlyDictionary<string, double> Readings);

// One row of a failure list: a reading and one end of its tenths, the picks holding the reading and its losers, and how many
// of each stand in that end.
public sealed record FailureRow(string Reading, bool Highest, int Picks, int Losers, int InBand, int LosersInBand)
{
    // The share of the rule's losers standing in the band, and the share of all its picks.
    public double LosersShare => Losers == 0 ? 0 : LosersInBand * 1.0 / Losers;

    public double PicksShare => Picks == 0 ? 0 : InBand * 1.0 / Picks;

    // How much more often a loser stands in the band than a pick does.
    public double Gap => LosersShare - PicksShare;

    public string Band => Highest ? "its highest three tenths" : "its lowest three tenths";
}

// A rule's failure list: which readings go with its losers. For each reading of the catalogue, over the rule's closed picks
// holding it, the share of its losers standing in the reading's lowest three tenths and in its highest three, against the
// share of all its picks there, the tenths those of the event's setups over the early years alone; a loser is a pick whose
// edge is nothing or below. The rows where losers gather most come first, a band holding fewer picks than the least
// left out, so a handful of trades never heads the list.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
public static class FailureList
{
    // The tenths at each end a band holds.
    public const int BandTenths = 3;

    // The least picks a band holds to be listed.
    public const int LeastInBand = 30;

    // The most rows a list shows.
    public const int MostRows = 8;

    public static IReadOnlyList<FailureRow> Of(IReadOnlyList<FailurePick> picks, IReadOnlyDictionary<string, double[]> cuts)
    {
        var rows = new List<FailureRow>();

        foreach (var (reading, levels) in cuts.OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            var holding = picks
                .Where(pick => pick.Readings.TryGetValue(reading, out var value) && double.IsFinite(value))
                .Select(pick => (Loser: pick.Edge <= 0, Tenth: IdeaGrammar.TenthOf(pick.Readings[reading], levels)))
                .ToArray();
            var losers = holding.Count(pick => pick.Loser);

            if (losers == 0)
            {
                continue;
            }

            foreach (var highest in new[] { false, true })
            {
                var band = holding.Where(pick => highest ? pick.Tenth > IdeaGrammar.Tenths - BandTenths : pick.Tenth <= BandTenths).ToArray();
                var row = new FailureRow(reading, highest, holding.Length, losers, band.Length, band.Count(pick => pick.Loser));

                if (row.InBand >= LeastInBand && row.Gap > 0)
                {
                    rows.Add(row);
                }
            }
        }

        return [.. rows.OrderByDescending(row => row.Gap).ThenBy(row => row.Reading, StringComparer.Ordinal).ThenBy(row => row.Highest).Take(MostRows)];
    }
}
