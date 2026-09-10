using System.Runtime.InteropServices;

namespace ColorShade.Native;

// Only documented ADL2 exports from AMD's installed 64-bit display driver.
// No SDK binaries, private function identifiers, or registry color edits.
internal sealed class AmdAdl : IDisposable
{
    private const int Saturation = 1 << 2;
    private IntPtr _module, _context;
    private readonly Allocate _allocate = size => size > 0 ? Marshal.AllocHGlobal(size) : IntPtr.Zero;
    private Destroy? _destroy;
    private Refresh? _refresh;
    private Count? _count;
    private Adapters? _adapters;
    private Displays? _displays;
    private Caps? _caps;
    private GetColor? _get;
    private SetColor? _set;

    internal AmdAdl()
    {
        try
        {
            if (Marshal.SizeOf<AdapterInfo>() != 1572 || Marshal.SizeOf<DisplayInfo>() != 552)
                throw new InvalidOperationException("ADL native structure layout mismatch.");
            // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32:
            // load driver and dependencies from trusted Windows locations only.
            _module = Win32.LoadLibraryEx(Path.Combine(Environment.SystemDirectory, "atiadlxx.dll"), IntPtr.Zero, 0x900);
            if (_module == IntPtr.Zero) return;
            var create = Export<Create>("ADL2_Main_Control_Create");
            _destroy = Export<Destroy>("ADL2_Main_Control_Destroy");
            _refresh = Export<Refresh>("ADL2_Main_Control_Refresh");
            _count = Export<Count>("ADL2_Adapter_NumberOfAdapters_Get");
            _adapters = Export<Adapters>("ADL2_Adapter_AdapterInfo_Get");
            _displays = Export<Displays>("ADL2_Display_DisplayInfo_Get");
            _caps = Export<Caps>("ADL2_Display_ColorCaps_Get");
            _get = Export<GetColor>("ADL2_Display_Color_Get");
            _set = Export<SetColor>("ADL2_Display_Color_Set");
            if (create(_allocate, 1, out _context) != 0) Dispose();
        }
        catch (Exception ex) { AppPaths.Log("ADL unavailable: " + ex.Message); Dispose(); }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));

    internal SaturationState? Read(DisplayTarget target)
    {
        try
        {
            var binding = Resolve(target.DeviceName);
            if (binding is null) return null;
            var (adapter, display) = binding.Value;
            if (_caps!(_context, adapter, display, out int caps, out int valid) != 0
                || (caps & valid & Saturation) == 0) return null;
            if (_get!(_context, adapter, display, Saturation, out int current, out _, out int min,
                    out int max, out int step) != 0) return null;
            var state = new SaturationState(current, min, max, Math.Max(step, 1));
            return state.IsValid ? state : null;
        }
        catch (Exception ex) { AppPaths.Log("ADL query: " + ex.Message); return null; }
    }

    internal void Write(DisplayTarget target, int value)
    {
        var binding = Resolve(target.DeviceName) ?? throw new IOException("AMD saturation target is unavailable or ambiguous.");
        int result = _set!(_context, binding.Adapter, binding.Display, Saturation, value);
        if (result != 0) throw new IOException("AMD rejected saturation (ADL code " + result + ").");
        // Do not call ADL_Flush_Driver_Data: temporary changes must not be persisted to driver settings.
    }

    private (int Adapter, int Display)? Resolve(string gdiName)
    {
        if (_context == IntPtr.Zero) return null;
        if (_refresh!(_context) != 0 || _count!(_context, out int count) != 0 || count is < 1 or > 128) return null;
        int size = Marshal.SizeOf<AdapterInfo>();
        IntPtr buffer = Marshal.AllocHGlobal(checked(size * count));
        try
        {
            // Initialize every iSize as required by ADL's array ABI.
            for (int i = 0; i < count; i++)
                Marshal.StructureToPtr(new AdapterInfo { Size = size }, buffer + i * size, false);
            if (_adapters!(_context, buffer, size * count) != 0) return null;
            var matches = new HashSet<(int Adapter, int Display)>();
            for (int i = 0; i < count; i++)
            {
                var adapter = Marshal.PtrToStructure<AdapterInfo>(buffer + i * size);
                if (adapter.Present == 0 || !gdiName.Equals(adapter.DisplayName, StringComparison.OrdinalIgnoreCase)) continue;
                IntPtr displayBuffer = IntPtr.Zero;
                try
                {
                    if (_displays!(_context, adapter.Index, out int n, out displayBuffer, 1) != 0
                        || displayBuffer == IntPtr.Zero || n is < 1 or > 128) continue;
                    int stride = Marshal.SizeOf<DisplayInfo>();
                    for (int j = 0; j < n; j++)
                    {
                        var display = Marshal.PtrToStructure<DisplayInfo>(displayBuffer + j * stride);
                        // CONNECTED=1, MAPPED=2. Associate the display with the logical
                        // Windows adapter, not an arbitrary physical GPU output.
                        if ((display.InfoMask & display.InfoValue & 3) == 3 && display.Id.LogicalAdapter == adapter.Index)
                            matches.Add((adapter.Index, display.Id.LogicalIndex));
                    }
                }
                finally { if (displayBuffer != IntPtr.Zero) Marshal.FreeHGlobal(displayBuffer); }
            }
            return matches.Count == 1 ? matches.Single() : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            try { _destroy?.Invoke(_context); } catch { }
            _context = IntPtr.Zero;
        }
        if (_module != IntPtr.Zero) { NativeLibrary.Free(_module); _module = IntPtr.Zero; }
        GC.KeepAlive(_allocate);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Allocate(int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(Allocate alloc, int connectedOnly, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Refresh(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(IntPtr context, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Adapters(IntPtr context, IntPtr info, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Displays(IntPtr context, int adapter, out int count, out IntPtr info, int forceDetect);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Caps(IntPtr context, int adapter, int display, out int caps, out int valid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetColor(IntPtr context, int adapter, int display, int color,
        out int current, out int defaultValue, out int min, out int max, out int step);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetColor(IntPtr context, int adapter, int display, int color, int value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        internal int Size, Index;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Udid;
        internal int Bus, Device, Function, Vendor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string DisplayName;
        internal int Present, Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Pnp;
        internal int OsDisplayIndex;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayId
    { internal int LogicalIndex, PhysicalIndex, LogicalAdapter, PhysicalAdapter; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] private struct DisplayInfo
    {
        internal DisplayId Id;
        internal int ControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Manufacturer;
        internal int Type, OutputType, Connector, InfoMask, InfoValue;
    }
}
