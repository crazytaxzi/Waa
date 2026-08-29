using System.Windows;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.ViewModels;

namespace Waa.App;

public partial class MainWindow : Window
{
    private readonly ThemePreferenceStore _themePreferenceStore;
    private bool _started;

    public MainWindow(MainViewModel viewModel, ThemePreferenceStore themePreferenceStore)
    {
        _themePreferenceStore = themePreferenceStore;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeButtonText();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        WindowThemeHelper.SetDarkTitleBar(this, ThemeManager.IsDarkMode);

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButtonText();
        WindowThemeHelper.SetDarkTitleBar(this, ThemeManager.IsDarkMode);
    }

    private async void OnThemeButtonClick(object sender, RoutedEventArgs e)
    {
        var previous = ThemeManager.IsDarkMode;
        var next = !previous;
        ThemeButton.IsEnabled = false;
        ThemeManager.Apply(next);

        try
        {
            await Task.Run(() => _themePreferenceStore.SetDarkMode(next));
            AppLog.Write($"Appearance changed to {(next ? "dark" : "light")} mode.");
        }
        catch (Exception exception)
        {
            ThemeManager.Apply(previous);
            AppLog.Write(exception, "Theme preference save failed");
            MessageBox.Show(
                $"The appearance could not be saved. WAA returned to the prior theme.\n\n{exception.Message}",
                "WAA Appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            ThemeButton.IsEnabled = true;
        }
    }

    private void UpdateThemeButtonText() =>
        ThemeButton.Content = ThemeManager.IsDarkMode ? "Light mode" : "Dark mode";

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
    }
}
