namespace ColorShade.Core;

public static class GammaMath
{
    // A one-dimensional RGB LUT cannot implement saturation. The fallback is a mild
    // symmetric contrast curve. Compose with the captured calibration, never identity.
    public static ushort[] Compose(ushort[] baseline, int brightness, int contrast)
    {
        if (baseline.Length != 768) throw new ArgumentException("Expected three 256-entry ramps.");
        brightness = Math.Clamp(brightness, 0, 100);
        contrast = Math.Clamp(contrast, 0, 100);
        if (brightness == 50 && contrast == 50) return (ushort[])baseline.Clone();
        double gamma = Math.Pow(2, (brightness - 50) / 75.0);
        double slope = 1 + (contrast - 50) / 250.0;
        var result = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            double x = Math.Pow(i / 255.0, 1 / gamma);
            double a = Math.Pow(x, slope), b = Math.Pow(1 - x, slope);
            double position = a / (a + b) * 255;
            int lower = Math.Clamp((int)position, 0, 255), upper = Math.Min(lower + 1, 255);
            double fraction = position - lower;
            for (int channel = 0; channel < 3; channel++)
            {
                int offset = channel * 256;
                result[offset + i] = (ushort)Math.Clamp(Math.Round(
                    baseline[offset + lower] * (1 - fraction) + baseline[offset + upper] * fraction), 0, 65535);
            }
        }
        return result;
    }

    // Some drivers quantize to 8 bits. This is readback verification, not proof of
    // the colors actually scanned out by the monitor.
    public static bool Matches(ushort[] expected, ushort[] actual, int tolerance = 257) =>
        expected.Length == 768 && actual.Length == 768 &&
        expected.Zip(actual).All(pair => Math.Abs((int)pair.First - pair.Second) <= tolerance);
}
