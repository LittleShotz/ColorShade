namespace ColorShade.Native;

internal sealed class WindowsDisplaySystem : IDisplaySystem
{
    private readonly AmdAdl _amd = new();
    public List<DisplayTarget> Enumerate() => DisplayInventory.Read();

    public ushort[] ReadGamma(DisplayTarget target)
    {
        DisplayInventory.RequireCurrent(target);
        var ramp = new ushort[768];
        WithDc(target, dc =>
        {
            if (!Win32.GetDeviceGammaRamp(dc, ramp))
                throw new IOException("This display driver cannot read the hardware gamma ramp.");
        });
        // Reject obviously invalid captures rather than use a guessed 'default'.
        for (int channel = 0; channel < 3; channel++)
            if (ramp[channel * 256 + 255] <= ramp[channel * 256])
                throw new IOException("The driver returned an invalid gamma baseline.");
        return ramp;
    }

    public void WriteGamma(DisplayTarget target, ushort[] gamma)
    {
        if (gamma.Length != 768) throw new ArgumentException("Invalid gamma buffer.");
        DisplayInventory.RequireCurrent(target);
        WithDc(target, dc =>
        {
            if (!Win32.SetDeviceGammaRamp(dc, gamma))
                throw new IOException("The driver rejected the gamma ramp.");
        });
    }

    public SaturationState? ReadSaturation(DisplayTarget target)
    { DisplayInventory.RequireCurrent(target); return _amd.Read(target); }
    public void WriteSaturation(DisplayTarget target, int value)
    { DisplayInventory.RequireCurrent(target); _amd.Write(target, value); }
    private static void WithDc(DisplayTarget target, Action<IntPtr> work)
    {
        IntPtr dc = Win32.CreateDC("DISPLAY", target.DeviceName, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new IOException("Could not open the monitor's display device context.");
        try { work(dc); } finally { Win32.DeleteDC(dc); }
    }
    public void Dispose() => _amd.Dispose();
}
