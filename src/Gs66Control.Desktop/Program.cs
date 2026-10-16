using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Gs66Control.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var smoke = args.Length == 2 && args[0] == "--smoke-test";
        if (args.Length != 0 && !smoke) return 2;
        using var mutex = new Mutex(true, "Local\\Gs66Control.Desktop", out var first);
        if (!first && !smoke)
        {
            var window = FindWindow(null, "GS66 Control");
            if (window != IntPtr.Zero) { ShowWindowAsync(window, 9); SetForegroundWindow(window); }
            return 0;
        }
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var output = smoke ? Path.GetFullPath(args[1]) : null;
            app.Run(new MainWindow(output));
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            if (smoke) File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "error.txt"), ex.ToString());
            else MessageBox.Show(ex.Message, "GS66 Control 启动失败");
            return 1;
        }
        finally { if (first) mutex.ReleaseMutex(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
