using System.Windows;
using System.Windows.Threading;
using NODR.Services;

namespace NODR;

public partial class App : System.Windows.Application
{
    private static int _fatalErrorShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Keep startup diagnostics alive even when failure happens before MainWindow exists.
        CrashLogger.WriteStage("OnStartup entered");

        try
        {
            base.OnStartup(e);
            CrashLogger.WriteStage("WPF base startup complete");

            if (SensorWorker.IsWorker(e.Args))
            {
                CrashLogger.WriteStage("Sensor worker mode");
                var exitCode = SensorWorker.Run(e.Args);
                Shutdown(exitCode);
                return;
            }

            // Install global handlers before settings/theme/localization initialization so
            // startup failures in those services are no longer silent.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            CrashLogger.WriteStage("Loading application resources");
            var resourceUri = new Uri("/NODR;component/Themes/NodrResources.xaml", UriKind.Relative);
            var resources = (ResourceDictionary)System.Windows.Application.LoadComponent(resourceUri);
            Resources.MergedDictionaries.Add(resources);
            CrashLogger.WriteStage("Application resources loaded");

            CrashLogger.WriteStage("Loading settings");
            AppSettingsService.Load();
            CrashLogger.WriteStage("Initializing localization");
            LocalizationService.Instance.Initialize(AppSettingsService.Current.Language);
            CrashLogger.WriteStage("Initializing theme");
            ThemeService.Instance.Initialize(AppSettingsService.Current.Theme);
            CrashLogger.WriteStage("Creating MainWindow");

            MainWindow = new MainWindow();
            MainWindow.Show();
            CrashLogger.WriteStage("MainWindow shown");
        }
        catch (Exception ex)
        {
            CrashLogger.Write("Startup", ex);
            Console.Error.WriteLine("NODR startup exception:");
            Console.Error.WriteLine(ex);

            try
            {
                var loc = LocalizationService.Instance;
                new FatalErrorWindow(
                    loc["Fatal.StartTitle"],
                    loc.Format("Fatal.StartMessage", CrashLogger.LogPath)).ShowDialog();
            }
            catch (Exception dialogEx)
            {
                // A broken error dialog must not hide the original startup exception.
                CrashLogger.Write("Startup error dialog", dialogEx);
                Console.Error.WriteLine("NODR could not show the fatal error dialog:");
                Console.Error.WriteLine(dialogEx);
            }

            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (Interlocked.Exchange(ref _fatalErrorShown, 1) != 0)
            return;

        CrashLogger.Write("Dispatcher", e.Exception);
        var exceptionText = e.Exception.ToString();
        Console.Error.WriteLine("NODR fatal dispatcher exception:");
        Console.Error.WriteLine(exceptionText);

        const int maxDialogChars = 3500;
        var loc = LocalizationService.Instance;
        if (exceptionText.Length > maxDialogChars)
            exceptionText = exceptionText[..maxDialogChars] + loc["Fatal.Truncated"];

        try
        {
            new FatalErrorWindow(
                loc["Fatal.UnexpectedTitle"],
                loc.Format("Fatal.UnexpectedMessage", exceptionText)).ShowDialog();
        }
        finally
        {
            Current.Shutdown(1);
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            CrashLogger.Write("AppDomain", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Write("TaskScheduler", e.Exception);
        e.SetObserved();
    }
}
