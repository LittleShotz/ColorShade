using System.Runtime.InteropServices;

namespace ColorShade.Native;

#pragma warning disable CS0649 // Fields populated by Win32.
internal static class Win32
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal int Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal UIntPtr DefaultHeap;
        internal uint ModuleId, Threads, ParentProcessId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExeFile;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDCW")]
    internal static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetDeviceGammaRamp(IntPtr dc, [Out] ushort[] ramp);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] ramp);

    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint paths,
        [Out] PathInfo[] pathArray, ref uint modes, IntPtr modeArray, IntPtr topologyId);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int GetTargetName(ref TargetName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static extern int GetAdvancedColor(ref AdvancedColor info);

    [StructLayout(LayoutKind.Sequential)] internal struct Luid { internal uint Low; internal int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { internal uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct SourceInfo
    { internal Luid Adapter; internal uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct TargetInfo
    {
        internal Luid Adapter;
        internal uint Id, ModeIndex, Technology, Rotation, Scaling;
        internal Rational RefreshRate;
        internal uint ScanLine;
        internal int Available;
        internal uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PathInfo
    { internal SourceInfo Source; internal TargetInfo Target; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct DeviceHeader
    { internal uint Type, Size; internal Luid Adapter; internal uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
    {
        internal DeviceHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string GdiName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetName
    {
        internal DeviceHeader Header;
        internal uint Flags, Technology;
        internal ushort Manufacturer, Product;
        internal uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct AdvancedColor
    { internal DeviceHeader Header; internal uint Flags, ColorEncoding, BitsPerChannel; }
    internal static DeviceHeader Header<T>(uint type, Luid adapter, uint id) where T : struct =>
        new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
    internal static extern IntPtr LoadLibraryEx(string filename, IntPtr file, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
