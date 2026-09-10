using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ColorShade.Native;

internal static class DisplayInventory
{
    internal static List<DisplayTarget> Read()
    {
        // QDC_ONLY_ACTIVE_PATHS. Mode union is 64 bytes in the Windows SDK.
        const uint flags = 2;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int code = Win32.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (code != 0) throw new Win32Exception(code, "Could not enumerate the active desktop displays.");
            if (pathCount > 128 || modeCount > 512) throw new IOException("Unexpected display topology size.");
            var paths = new Win32.PathInfo[pathCount];
            IntPtr modes = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * 64));
            try
            {
                code = Win32.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (code == 122) continue; // ERROR_INSUFFICIENT_BUFFER: topology changed; re-enumerate.
                if (code != 0) throw new Win32Exception(code, "Could not read display paths.");
                var result = new List<DisplayTarget>();
                foreach (var path in paths.Take((int)pathCount))
                {
                    var source = new Win32.SourceName { Header = Win32.Header<Win32.SourceName>(1, path.Source.Adapter, path.Source.Id) };
                    var target = new Win32.TargetName { Header = Win32.Header<Win32.TargetName>(2, path.Target.Adapter, path.Target.Id) };
                    if (Win32.GetSourceName(ref source) != 0 || Win32.GetTargetName(ref target) != 0) continue;
                    var advanced = new Win32.AdvancedColor { Header = Win32.Header<Win32.AdvancedColor>(9, path.Target.Adapter, path.Target.Id) };
                    int hdrResult = Win32.GetAdvancedColor(ref advanced);
                    bool advancedEnabled = (advanced.Flags & 6) != 0; // enabled or wide-color-enforced
                    bool identity = !string.IsNullOrWhiteSpace(target.DevicePath);
                    string reason = !identity ? "Windows did not provide a stable monitor identity."
                        : hdrResult != 0 ? "The HDR / Advanced Color state could not be checked."
                        : advancedEnabled ? "HDR / Advanced Color is enabled. Use SDR for gamma adjustments." : "";
                    string label = string.IsNullOrWhiteSpace(target.FriendlyName) ? source.GdiName : target.FriendlyName;
                    result.Add(new(target.DevicePath ?? "", source.GdiName, label + " (" + source.GdiName + ")",
                        reason.Length == 0, reason));
                }
                // Clone paths share a gamma source. Never pretend to adjust them independently.
                var duplicateSources = result.GroupBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return result.Select(d => duplicateSources.Contains(d.DeviceName)
                        ? d with { CanAdjust = false, BlockReason = "Mirrored displays share a gamma source. Use Extend displays." } : d)
                    .GroupBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        throw new IOException("Display topology keeps changing. Try again once the displays settle.");
    }

    internal static DisplayTarget RequireCurrent(DisplayTarget requested)
    {
        var current = Read().SingleOrDefault(d => d.Key.Equals(requested.Key, StringComparison.OrdinalIgnoreCase));
        if (current?.CanAdjust != true || !current.DeviceName.Equals(requested.DeviceName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The display changed or is no longer a supported SDR target.");
        return current;
    }
}
