using System.Diagnostics;

namespace ColorShade;

internal static class AppPaths
{
    internal static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ColorShade");
    internal static string Journal => Path.Combine(Data, "recovery.json");
    internal static string Settings => Path.Combine(Data, "settings.json");
    internal static string Executable => Environment.ProcessPath ?? throw new IOException("Cannot locate ColorShade.exe.");
    private static readonly object LogGate = new();

    internal static void Log(string message)
    {
        try
        {
            lock (LogGate)
            {
                Directory.CreateDirectory(Data);
                string path = Path.Combine(Data, "colorshade.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".old", true);
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch { /* Logging must never interfere with recovery. */ }
    }

    internal static void LaunchSelf(params string[] args)
    {
        if (!Path.GetFileName(Executable).Equals("ColorShade.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Launch ColorShade.exe directly so its recovery helper can start.");
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // This only launches our own executable. No process lookup or process-handle
        // inspection is performed. Dispose the launch handle immediately.
        using var child = Process.Start(start) ?? throw new IOException("Could not start the recovery helper.");
    }
}
