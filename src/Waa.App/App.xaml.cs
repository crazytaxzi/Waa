using System.Windows;
using System.Windows.Threading;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.Services;
using Waa.App.ViewModels;
using Waa.Core;

namespace Waa.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var paths = AppPaths.Create();
            paths.EnsureDirectories();
            AppLog.Initialize(paths.LogDirectory);
            AppLog.Write("WAA starting.");

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var retirement = new LegacyDatabaseRetirementService()
                .RetireIfNeeded(paths.DatabasePath, paths.DataDirectory);
            if (retirement.Retired)
            {
                AppLog.Write(retirement.Message);
            }

            var repository = new WaaRepository(paths.DatabasePath);
            repository.Initialize();
            var missingBolRepository = new MissingBolRepository(paths.DatabasePath);
            missingBolRepository.Initialize();
            var themePreferenceStore = new ThemePreferenceStore(paths.DatabasePath);
            if (retirement.DarkMode is bool darkMode)
            {
                themePreferenceStore.SetDarkMode(darkMode);
            }

            if (retirement.AmbientMotionEnabled is bool ambientMotionEnabled)
            {
                themePreferenceStore.SetAmbientMotionEnabled(ambientMotionEnabled);
            }

            ThemeManager.Apply(themePreferenceStore.GetDarkMode());

            var updateService = new ReportUpdateService(
                repository,
                missingBolRepository,
                new RollingSevenDayCsvParser(),
                new MissingBolWorkbookParser());
            var viewModel = new MainViewModel(
                repository,
                updateService,
                new WindowsClipboardService(),
                missingBolRepository: missingBolRepository);
            var window = new MainWindow(viewModel, themePreferenceStore);
            MainWindow = window;
            window.Show();

            if (retirement.Retired)
            {
                MessageBox.Show(
                    "A previous-generation WAA data store was detected and archived intact before current WAA started.\n\n" +
                    $"Archive:\n{retirement.ArchiveDirectory}\n\n" +
                    "Current WAA is now using a fresh database. Old PTA, call-session, note, reminder, timer, " +
                    "transition, and old Missing BOL state were preserved in the archive rather than guessed into the current workflow.",
                    "Previous WAA Data Archived",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Fatal startup failure");
            MessageBox.Show(
                $"WAA could not start.\n\n{exception.Message}",
                "WAA Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Unhandled UI error");
        MessageBox.Show(
            $"WAA encountered an unexpected error. Your saved data was not intentionally changed.\n\n{e.Exception.Message}",
            "WAA Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
