using System.Runtime.InteropServices;

namespace ColorShade.Native;

internal static class ForegroundDetector
{
    internal static string? RustMonitor()
    {
        IntPtr window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        Win32.GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0) return null;
        // TH32CS_SNAPPROCESS is a system-wide read-only process-name snapshot.
        // This handle is a snapshot, never a handle to the foreground process.
        IntPtr snapshot = Win32.CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1)) return null;
        bool rust = false;
        try
        {
            var entry = new Win32.ProcessEntry { Size = (uint)Marshal.SizeOf<Win32.ProcessEntry>() };
            if (!Win32.Process32First(snapshot, ref entry)) return null;
            do
            {
                if (entry.ProcessId != pid) continue;
                rust = string.Equals(entry.ExeFile, "RustClient.exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.ExeFile, "Rust.exe", StringComparison.OrdinalIgnoreCase);
                break;
            } while (Win32.Process32Next(snapshot, ref entry));
        }
        finally { Win32.CloseHandle(snapshot); }
        // Reject a foreground change during enumeration. No window titles, hooks,
        // process modules, full image paths, or memory queries are used.
        if (!rust || Win32.GetForegroundWindow() != window) return null;
        Win32.GetWindowThreadProcessId(window, out uint confirmedPid);
        return confirmedPid == pid ? MonitorForWindow(window) : null;
    }

    internal static string? MonitorForWindow(IntPtr window)
    {
        IntPtr monitor = Win32.MonitorFromWindow(window, 0);
        if (monitor == IntPtr.Zero) return null;
        var info = new Win32.MonitorInfo { Size = Marshal.SizeOf<Win32.MonitorInfo>() };
        return Win32.GetMonitorInfo(monitor, ref info) ? info.Device : null;
    }
}
