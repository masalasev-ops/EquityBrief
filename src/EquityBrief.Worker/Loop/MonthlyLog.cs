using System.Globalization;
using System.Text;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Loop;

// The monthly run's output, each line stamped with the UTC instant it ends and written both to the console and to the
// month's own log under the data root. The worker appends to its log itself, so a scheduler that ends the shell holding
// the console's redirect silences neither the run nor its record of itself: a console that refuses a line still leaves
// it in the log, and a log another program holds open loses that line and never the run.
// see: The monthly run writes its own stamped log and says every ten minutes that a step still runs
public sealed class MonthlyLog : TextWriter
{
    public const string FileName = "run.log";

    readonly IClock clock;
    readonly TextWriter console;
    readonly string file;
    readonly StringBuilder line = new();
    readonly Lock gate = new();
    bool refusedOnce;

    public MonthlyLog(IClock clock, TextWriter console, string file)
    {
        this.clock = clock;
        this.console = console;
        this.file = file;

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    }

    // The month's log: beside its report in the month's folder of the loop's folder under the data root.
    public static string PathOf(string dataRoot, string month) => Path.Combine(dataRoot, MonthlyRun.Folder, month, FileName);

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (gate)
        {
            if (value == '\n')
            {
                End();
            }
            else if (value != '\r')
            {
                line.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (gate)
        {
            foreach (var character in value)
            {
                Write(character);
            }
        }
    }

    public override void Flush()
    {
        lock (gate)
        {
            try
            {
                console.Flush();
            }
            catch (Exception refused) when (refused is IOException or ObjectDisposedException)
            {
            }
        }
    }

    void End()
    {
        var stamped = clock.UtcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + " " + line;

        line.Clear();

        try
        {
            console.WriteLine(stamped);
            console.Flush();
        }
        catch (Exception refused) when (refused is IOException or ObjectDisposedException)
        {
        }

        try
        {
            File.AppendAllText(file, stamped + "\n");
        }
        catch (IOException refused)
        {
            if (!refusedOnce)
            {
                refusedOnce = true;

                try
                {
                    console.WriteLine($"{MonthlyRun.Verb}: the log {file} refused a line, so lines go to the console alone while it does: {refused.Message}");
                }
                catch (Exception gone) when (gone is IOException or ObjectDisposedException)
                {
                }
            }
        }
    }
}
