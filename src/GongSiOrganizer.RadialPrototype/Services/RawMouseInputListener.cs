using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GongSiOrganizer.RadialPrototype.Services;

internal sealed class RawMouseInputListener : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RidevInputSink = 0x00000100;
    private const ushort RiMouseLeftButtonDown = 0x0001;
    private const ushort RiMouseMiddleButtonDown = 0x0010;

    private HwndSource? _source;
    private long _lastMiddleClickTicks;

    public event Action<NativeHitContext>? MiddleButtonDown;
    public event Action<NativeScreenPoint>? LeftButtonDown;

    public void Start()
    {
        if (_source is not null) return;
        var parameters = new HwndSourceParameters("GongSiOrganizer.RawInput")
        {
            Width = 1,
            Height = 1,
            PositionX = -32_000,
            PositionY = -32_000,
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00000080
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x02,
                Flags = RidevInputSink,
                Target = _source.Handle
            }
        };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            throw new InvalidOperationException($"注册全局鼠标输入失败：{Marshal.GetLastWin32Error()}");
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmInput) return IntPtr.Zero;

        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        _ = GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) != size)
                return IntPtr.Zero;
            var input = Marshal.PtrToStructure<RawInput>(buffer);
            if (input.Header.Type != RimTypeMouse) return IntPtr.Zero;

            if ((input.Mouse.ButtonFlags & RiMouseLeftButtonDown) != 0)
                LeftButtonDown?.Invoke(NativeDisplay.GetCursor());

            if ((input.Mouse.ButtonFlags & RiMouseMiddleButtonDown) == 0)
                return IntPtr.Zero;

            var now = Environment.TickCount64;
            var previous = Interlocked.Exchange(ref _lastMiddleClickTicks, now);
            if (now - previous < 120) return IntPtr.Zero;

            MiddleButtonDown?.Invoke(NativeDisplay.CaptureHitContext());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouse
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public uint Buttons;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawMouse Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RawInputDevice[] devices,
        uint deviceCount,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);
}
