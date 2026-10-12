namespace EquityBrief.Core.Loop;

// The proposals the tester names for the S&P 400's and 600's sector heavyweights' six settings a design, each read as a
// procedure, and read again by the card of a book registered before its family's freeze to say how the tester judged
// its design.
// see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
public static class SixSettings
{
    public const string DesignA = "the six settings, design (a)";

    public const string DesignB = "the six settings, design (b)";

    // A design as a registration states it, nought for design (a), and the proposal the tester names for it.
    public static string Of(double design) => design == 0 ? DesignA : DesignB;
}
