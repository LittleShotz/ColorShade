using Microsoft.Win32;

namespace ColorShade;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Value => "\"" + AppPaths.Executable + "\" --tray";
    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue("ColorShade") as string, Value, StringComparison.OrdinalIgnoreCase);
    }
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new IOException("Could not open your Windows startup settings.");
        if (enabled) key.SetValue("ColorShade", Value, RegistryValueKind.String);
        else key.DeleteValue("ColorShade", throwOnMissingValue: false);
    }
}
