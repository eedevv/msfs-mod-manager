using System.IO;
using System.Windows;
using System.Windows.Threading;
using ModInstaller.Core.Models;

namespace ModInstaller.App;

public partial class App : Application
{
    private int _errorCount;
    private DateTime _lastErrorTime = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // Always prevent crash

        // Rate-limit error handling to prevent cascading dialog spam
        var now = DateTime.Now;
        if ((now - _lastErrorTime).TotalSeconds < 2)
        {
            _errorCount++;
            if (_errorCount > 3)
            {
                // Too many errors too fast — just log silently
                LogError(e.Exception);
                return;
            }
        }
        else
        {
            _errorCount = 1;
        }
        _lastErrorTime = now;

        // Log to file instead of showing MessageBox
        LogError(e.Exception);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogError(ex);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        LogError(e.Exception);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var logDir = AppSettings.SettingsDirectory;
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "error.log");

            // Keep log file under 100KB
            if (File.Exists(logFile) && new FileInfo(logFile).Length > 100_000)
                File.WriteAllText(logFile, ""); // Reset

            var inner = ex.InnerException != null
                ? $"\n  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                : "";
            File.AppendAllText(logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}{inner}\n" +
                $"  {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}\n\n");
        }
        catch { /* Last resort — can't even log */ }
    }
}
