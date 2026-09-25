namespace EquityBrief.Core.Shortlist;

// The rule each evening's list was drawn by, as the store records it and every surface names it: any of
// the six reasons firing, before the switch, and the swing filter from it. An evening the store records
// no rule for was drawn by the reasons, which is every evening before the switch.
// see: Tonight's list is the swing filter's, and an evening is listed by the rule that listed it
public static class ListRules
{
    public const string Reasons = "reasons";

    public const string Filter = "filter";

    // The words each surface names the rule in.
    public const string ByReasons = "listed by any of the six reasons firing, the rule tonight's list was drawn by before the swing filter";

    public const string ByFilter = "listed by the swing filter: every gate passed, the trigger arrived inside its window and no exclusion applied";

    public static string Said(string rule) => rule == Filter ? ByFilter : ByReasons;
}
