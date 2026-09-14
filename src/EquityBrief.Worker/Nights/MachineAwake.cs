using System.Runtime.InteropServices;

namespace EquityBrief.Worker.Nights;

// A hold on the machine's sleep, released when it is disposed.
//
// `Line` is what the queue's row says about it: that the machine was held awake, or
// why it was not, so a night that slept can be told from one that asked and was refused.
public interface IAwakeHold : IDisposable
{
    bool Held { get; }

    string Line { get; }
}

public interface IMachineAwake
{
    IAwakeHold Hold(string reason);
}

// The machine held awake while the overnight queue works.
//
// Asked of the operating system rather than of a scheduler, because the scheduler's
// setting decides whether the night starts and says nothing about a machine that
// sleeps once it has. Each platform's own request, behind the one interface, and a
// platform this build has no request for says so on the row rather than pretending.
// see: The overnight run holds the machine awake and reports whether it ran
// see: Nothing is written against one operating system
public sealed class MachineAwake : IMachineAwake
{
    public const string HeldLine = "held awake";

    public IAwakeHold Hold(string reason)
    {
        // A request the system cannot be asked, being a library that will not load or an
        // entry point it does not export, is a hold not taken and said so, rather than a
        // night that fails at its last step for the want of one.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsHold.Take(reason);
            }

            if (OperatingSystem.IsMacOS())
            {
                return MacHold.Take(reason);
            }
        }
        catch (Exception unasked) when (unasked is DllNotFoundException or EntryPointNotFoundException)
        {
            return new NotHeld("not held: the system's power request could not be reached: " + unasked.Message);
        }

        return new NotHeld("not held: this build asks Windows and macOS for a hold, and this machine is neither");
    }

    sealed class NotHeld(string line) : IAwakeHold
    {
        public bool Held => false;

        public string Line => line;

        public void Dispose()
        {
        }
    }

    // A power request, which the system holds against the handle rather than against a
    // thread, so an awaited pass that resumes on another thread is still covered.
    sealed class WindowsHold(IntPtr handle) : IAwakeHold
    {
        const uint ContextVersion = 0;
        const uint SimpleString = 0x1;
        const int SystemRequired = 1;

        public bool Held => handle != IntPtr.Zero;

        public string Line => HeldLine;

        internal static IAwakeHold Take(string reason)
        {
            var context = new ReasonContext { Version = ContextVersion, Flags = SimpleString, SimpleReasonString = reason };
            var request = PowerCreateRequest(ref context);

            if (request == IntPtr.Zero || request == new IntPtr(-1))
            {
                return new NotHeld($"not held: the system refused a power request, error {Marshal.GetLastPInvokeError()}");
            }

            if (!PowerSetRequest(request, SystemRequired))
            {
                var error = Marshal.GetLastPInvokeError();

                CloseHandle(request);

                return new NotHeld($"not held: the system refused to hold the power request, error {error}");
            }

            return new WindowsHold(request);
        }

        public void Dispose()
        {
            PowerClearRequest(handle, SystemRequired);
            CloseHandle(handle);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ReasonContext
        {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr PowerCreateRequest(ref ReasonContext context);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PowerSetRequest(IntPtr request, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PowerClearRequest(IntPtr request, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }

    // A power assertion, which keeps the system from idle sleep until it is released.
    sealed class MacHold(uint assertion) : IAwakeHold
    {
        const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const uint LevelOn = 255;
        const uint Utf8 = 0x08000100;

        public bool Held => true;

        public string Line => HeldLine;

        internal static IAwakeHold Take(string reason)
        {
            var type = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleSystemSleep", Utf8);
            var name = CFStringCreateWithCString(IntPtr.Zero, reason, Utf8);

            try
            {
                var result = IOPMAssertionCreateWithName(type, LevelOn, name, out var id);

                return result == 0
                    ? new MacHold(id)
                    : new NotHeld($"not held: the system refused a power assertion, result {result}");
            }
            finally
            {
                CFRelease(name);
                CFRelease(type);
            }
        }

        public void Dispose() => IOPMAssertionRelease(assertion);

        [DllImport(IOKit)]
        static extern int IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint id);

        [DllImport(IOKit)]
        static extern int IOPMAssertionRelease(uint id);

        [DllImport(CoreFoundation)]
        static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

        [DllImport(CoreFoundation)]
        static extern void CFRelease(IntPtr value);
    }
}
