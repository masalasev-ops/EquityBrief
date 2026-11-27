using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// From 24.31 one row the trained partner learns on: a member's readings on a session of 2005 to 2018 and its return over
// the month after, the session that month ends on beside it. It holds no name, ticker or date the tables read.
public sealed record PartnerRow(DateOnly Session, DateOnly Ended, IReadOnlyList<double?> Readings, double Label);

// One cell of a table the partner hands the picker: the readings and the fifths it stands in, how many cases it holds and
// what they made over the month after on average.
public sealed record PartnerCell(int Reading, int Fifth, int? Second, int? SecondFifth, int Cases, double? Mean);

// From 24.31 the trained partner: a tree model fitted for each index on that index's own members over 2005 to 2014 and
// checked on 2015 to 2018, both inside the years the picker may read, and the tables code reads off it, which readings
// mattered most and how the best two readings' fifths together did, each cell with its count of cases and never a name, a
// ticker, a date or anything from 2019 on. No rule reads the partner and its tables move no rule's version.
// see: The trained partner learns each index's members over 2005 to 2014 and is checked on 2015 to 2018, and its tables name no stock and read nothing from 2019
public static class Partner
{
    public const int TrainFrom = 2005;
    public const int TrainThrough = 2014;
    public const int CheckFrom = 2015;
    public const int CheckThrough = 2018;

    // The sessions a row's return is read over: the month after its session.
    public const int LabelSessions = 21;

    // The parts a reading's range is cut into for the grid, its cut points read off the years the partner learned on.
    public const int Parts = 5;

    // The readings that mattered most the tables hold, highest first.
    public const int MostImportant = 10;

    // The rows the partner learns on, their sessions of 2005 to 2014 and their months ended before 2015 began; and the rows
    // it is checked on, their sessions of 2015 to 2018 and their months ended inside 2018; a row of 2019 or after is in
    // neither.
    public static (IReadOnlyList<int> Train, IReadOnlyList<int> Check) Split(IReadOnlyList<PartnerRow> rows)
    {
        var checkStarts = new DateOnly(CheckFrom, 1, 1);
        var checkEnds = new DateOnly(CheckThrough, 12, 31);
        var train = new List<int>();
        var check = new List<int>();

        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];

            if (row.Session.Year is >= TrainFrom and <= TrainThrough && row.Ended < checkStarts)
            {
                train.Add(at);
            }
            else if (row.Session.Year is >= CheckFrom and <= CheckThrough && row.Ended <= checkEnds)
            {
                check.Add(at);
            }
        }

        return (train, check);
    }

    // How well the partner told the check years apart: the mean return over the month after of the fifth of the check rows
    // it scored highest less that of the fifth it scored lowest, none where fewer than a fifth's worth hold a score.
    public static double? Spread(IScoreModel model, IReadOnlyList<PartnerRow> rows, IReadOnlyList<int> check)
    {
        var scored = check
            .Select(at => (Score: model.Score(rows[at].Readings), rows[at].Label))
            .Where(one => one.Score is not null)
            .OrderBy(one => one.Score!.Value)
            .ToArray();
        var fifth = scored.Length / Parts;

        return fifth == 0 ? null : scored.TakeLast(fifth).Average(one => one.Label) - scored.Take(fifth).Average(one => one.Label);
    }

    // Which readings mattered most: for each reading the partner weighs, the mean over the check rows of how far its score
    // moves when that reading alone is set to its median over the rows it learned on; highest first, at most so many.
    public static IReadOnlyList<(int Reading, double Importance)> Importances(IScoreModel model, IReadOnlyList<PartnerRow> rows, IReadOnlyList<int> train, IReadOnlyList<int> check)
    {
        var found = new List<(int Reading, double Importance)>();

        foreach (var reading in model.Readings)
        {
            double[] held = [.. train.Select(at => rows[at].Readings[reading]).OfType<double>().Order()];

            if (held.Length == 0)
            {
                continue;
            }

            var median = held.Length % 2 == 1 ? held[held.Length / 2] : (held[(held.Length / 2) - 1] + held[held.Length / 2]) / 2;
            var moved = new List<double>();

            foreach (var at in check)
            {
                if (model.Score(rows[at].Readings) is not { } score)
                {
                    continue;
                }

                var flattened = rows[at].Readings.ToArray();

                flattened[reading] = median;

                if (model.Score(flattened) is { } other)
                {
                    moved.Add(Math.Abs(score - other));
                }
            }

            if (moved.Count > 0)
            {
                found.Add((reading, moved.Average()));
            }
        }

        return [.. found.OrderByDescending(one => one.Importance).ThenBy(one => one.Reading).Take(MostImportant)];
    }

    // A reading's cut points into its fifths, read off the rows the partner learned on alone.
    public static IReadOnlyList<double> CutPoints(IReadOnlyList<PartnerRow> rows, IReadOnlyList<int> train, int reading)
    {
        double[] held = [.. train.Select(at => rows[at].Readings[reading]).OfType<double>().Order()];

        return held.Length == 0 ? [] : [.. Enumerable.Range(1, Parts - 1).Select(part => held[Math.Min(held.Length - 1, part * held.Length / Parts)])];
    }

    // The fifth a figure stands in by the cut points: the first at or under the first cut, the last over the last.
    public static int FifthOf(IReadOnlyList<double> cuts, double value)
    {
        var fifth = 1;

        foreach (var cut in cuts)
        {
            if (value > cut)
            {
                fifth++;
            }
        }

        return fifth;
    }

    // A reading's table: each of its fifths with the cases of 2005 to 2018 in it and their mean return over the month after.
    public static IReadOnlyList<PartnerCell> Table(IReadOnlyList<PartnerRow> rows, IReadOnlyList<int> train, IReadOnlyList<int> check, int reading)
    {
        var cuts = CutPoints(rows, train, reading);

        return
        [
            .. Enumerable.Range(1, Parts).Select(fifth =>
            {
                double[] labels = [.. train.Concat(check).Where(at => rows[at].Readings[reading] is { } value && FifthOf(cuts, value) == fifth).Select(at => rows[at].Label)];

                return new PartnerCell(reading, fifth, null, null, labels.Length, labels.Length == 0 ? null : labels.Average());
            }),
        ];
    }

    // The grid of two readings: each pair of their fifths with the cases of 2005 to 2018 holding both and their mean return
    // over the month after, the first reading's fifths down and the second's across.
    public static IReadOnlyList<PartnerCell> Grid(IReadOnlyList<PartnerRow> rows, IReadOnlyList<int> train, IReadOnlyList<int> check, int first, int second)
    {
        var firstCuts = CutPoints(rows, train, first);
        var secondCuts = CutPoints(rows, train, second);
        var cells = new List<PartnerCell>();

        for (var down = 1; down <= Parts; down++)
        {
            for (var across = 1; across <= Parts; across++)
            {
                var (row, column) = (down, across);
                double[] labels =
                [
                    .. train.Concat(check)
                        .Where(at => rows[at].Readings[first] is { } a && rows[at].Readings[second] is { } b && FifthOf(firstCuts, a) == row && FifthOf(secondCuts, b) == column)
                        .Select(at => rows[at].Label),
                ];

                cells.Add(new PartnerCell(first, row, second, column, labels.Length, labels.Length == 0 ? null : labels.Average()));
            }
        }

        return cells;
    }

    // A reading's name as the tables carry it: its column in the catalogue, never a stock's.
    public static string NameOf(int reading) => LedgerReadings.All[reading].Column;
}
