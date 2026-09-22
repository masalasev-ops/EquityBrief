namespace EquityBrief.Core.Candidates;

// The value keys a night writes for a candidate beyond the indicator readings,
// which arrive under the names the indicator series already gives them.
//
// One place because more than one evaluator reads the same figure. A key spelled
// one way where the night writes it and another way where an evaluator reads it
// is a candidate skipped on every name-night, and the skip names a key that
// looks right in both files.
public static class NightValues
{
    public const string Close = "close";

    public const string PreviousClose = "close_previous";

    // Tonight's volume over the fifty-day average, written only where the name
    // traded tonight and the average is above nought, which is the population
    // the night's median is taken over.
    public const string VolumeRatio = "volume_ratio";

    // Where tonight's volume ranks inside the name's own fifty-day window, 1
    // being the largest. Recorded and never read by a condition.
    public const string VolumeRank = "volume_rank";

    public const string NightMedianRatio = "night_median_ratio";

    // How many buying zones the night's plan held for the name, which is what
    // makes the absence of the edges below a measurement rather than a hole.
    public const string Zones = "zones";

    public const string ZoneLow = "zone_low";

    public const string ZoneHigh = "zone_high";

    // The strength of the band the nearest zone sits on and how many sessions
    // have passed since that band's newest member. Recorded and never read by a
    // condition.
    public const string ZoneStrength = "zone_strength";

    public const string ZoneNewestMemberSessions = "zone_newest_member_sessions";

    // How many of the name's bands the close crossed since the previous session,
    // and how far past the crossed edge the furthest of them finished. The count
    // is written whenever the night could ask the question, so nought is a night
    // that crossed nothing rather than a night that could not tell.
    public const string Crossings = "crossings";

    public const string CrossingDistance = "crossing_distance";
}
