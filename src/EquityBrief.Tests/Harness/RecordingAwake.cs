using EquityBrief.Worker.Nights;

namespace EquityBrief.Tests.Harness;

// A hold on the machine's sleep that asks nothing of the operating system and writes down
// when it was taken and released, into a list a test's other doubles can write to as well,
// so the order of the hold and what happened under it is read off one list.
internal sealed class RecordingAwake(List<string>? events = null) : IMachineAwake
{
    public List<string> Events { get; } = events ?? [];

    public IAwakeHold Hold(string reason)
    {
        Events.Add("held: " + reason);

        return new Handle(this);
    }

    sealed class Handle(RecordingAwake owner) : IAwakeHold
    {
        public bool Held => true;

        public string Line => MachineAwake.HeldLine;

        public void Dispose() => owner.Events.Add("released");
    }
}
