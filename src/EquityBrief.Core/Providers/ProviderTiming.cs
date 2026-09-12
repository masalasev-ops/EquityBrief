namespace EquityBrief.Core.Providers;

// When in the session a print lands, as this provider files it.
//
// One reading rather than one per parser. The earnings calendar and the company
// fundamentals endpoint both carry the field and both send the same two words for
// it, so two readings would be two chances to disagree about one provider's
// vocabulary, and the reading is the whole of what either parser does with it.
// This is the same argument the gap stop is one reader for.
public static class ProviderTiming
{
    // Anything the provider does not send as one of its two words is unstated,
    // which is a third value and not a default: a print whose timing is not filed
    // decides which session it is priced on differently from one filed as before
    // the open, and guessing either way would put a move on the wrong bar.
    public static EventTiming Filed(string? sent) => sent switch
    {
        "BeforeMarket" => EventTiming.Before,
        "AfterMarket" => EventTiming.After,
        _ => EventTiming.Unstated,
    };
}
