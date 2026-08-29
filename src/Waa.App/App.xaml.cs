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

            var repository = new WaaRepository(paths.DatabasePath);
            repository.Initialize();
            var themePreferenceStore = new ThemePreferenceStore(paths.DatabasePath);
            ThemeManager.Apply(themePreferenceStore.GetDarkMode());

            var updateService = new ReportUpdateService(repository, new RollingSevenDayCsvParser());
            var viewModel = new MainViewModel(repository, updateService);
            var window = new MainWindow(viewModel, themePreferenceStore);
            MainWindow = window;
            window.Show();
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
