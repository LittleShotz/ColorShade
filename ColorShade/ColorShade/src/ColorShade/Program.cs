using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using ColorShade.Recovery;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ColorShade;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess)
        { MessageBox.Show("ColorShade requires Windows 11 x64.", "ColorShade"); return 1; }
        Directory.CreateDirectory(AppPaths.Data);
        if (args.Length == 2 && args[0] == "--guardian")
            return Guardian.RunAsync(args[1]).GetAwaiter().GetResult();
        if (args.Contains("--restore"))
        {
            int code;
            try { code = Guardian.RunAsync(null).GetAwaiter().GetResult(); }
            catch (Exception ex) { AppPaths.Log(ex.ToString()); code = 2; }
            if (!args.Contains("--quiet")) MessageBox.Show(code switch
            {
                0 => "Saved desktop settings have been restored.",
                3 => "ColorShade is still running. Use Restore / Retry or Exit from its tray menu, then try again.",
                _ => "Recovery is still pending. Reconnect the original monitor in SDR and try again. See colorshade.log."
            }, "ColorShade recovery");
            return code;
        }
        // No process enumeration needed for single-instance detection.
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var singleInstance = new Mutex(true, @"Local\ColorShade-UI-" + sid, out bool created);
        if (!created) { MessageBox.Show("ColorShade is already running. Open it from the system tray.", "ColorShade"); return 0; }
        AppPaths.Log("UI started; PID " + Environment.ProcessId);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/ColorShade;component/Themes/Dark.xaml", UriKind.Relative) });
        MainWindow? window = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            AppPaths.Log("UI failure: " + e.Exception);
            window?.DisconnectGuardian(); // Lease loss restores even when cleanup cannot finish.
            e.Handled = true;
            app.Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        { AppPaths.Log("Fatal UI exception: " + e.ExceptionObject); window?.DisconnectGuardian(); };
        app.SessionEnding += (_, _) => window?.DisconnectGuardian();
        app.Exit += (_, _) => window?.DisposeTray();
        try
        {
            window = new MainWindow();
            app.MainWindow = window;
            app.Startup += async (_, _) =>
            {
                await window.StartAsync();
                if (args.Contains("--open")) window.OpenWindow();
            };
            return app.Run(); // Hidden by default. Tray Open displays the settings window.
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex.ToString());
            window?.DisconnectGuardian(); window?.DisposeTray();
            MessageBox.Show("ColorShade could not start: " + ex.Message, "ColorShade");
            return 1;
        }
    }
}
