using System.Globalization;

namespace EquityBrief.Core.Providers;

// The stop a pull run by hand holds its provider calls to: a quarter of the day's allowance, 25,000 weighted calls.
// Each pull states what it is about to ask, in the provider's weighted calls, before its first request and from what
// it will ask, the names and the months, and a statement past the stop is refused before any request unless the
// operator says to go past it. The provider's own count of the day is not read here, so the stop is each pull's own
// and a remedy of several pulls states each in turn.
// see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
public static class ProviderStop
{
    public const int WeightedCalls = ProviderWeights.DailyAllowance / 4;

    public const string PastTheStop = "--past-the-stop";

    // The statement a pull prints before its first request.
    public static string Stated(string what, int weighted) =>
        FormattableString.Invariant($"asks about {weighted:N0} weighted call(s) of the provider's {ProviderWeights.DailyAllowance:N0} a day: {what}");

    // Refuses a statement past the stop, in words naming the count, the stop and the word that goes past it.
    public static void Hold(string what, int weighted, bool past)
    {
        if (weighted > WeightedCalls && !past)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"This pull would ask about {weighted:N0} weighted calls, {what}, past the stop of {WeightedCalls:N0}, a quarter of the day's allowance. Nothing was asked. Run it with '{PastTheStop}' on the operator's word, or narrow it with '--names'."),
                nameof(weighted));
        }
    }

    public static string Months(int months) => months.ToString("N0", CultureInfo.InvariantCulture);
}
