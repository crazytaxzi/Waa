using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.ViewModels;

namespace Waa.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemePreferenceStore _themePreferenceStore;

    public MainWindow(
        MainViewModel viewModel,
        ThemePreferenceStore themePreferenceStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themePreferenceStore = themePreferenceStore;
        DataContext = viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeButtonText();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Main window initialization failed");
            MessageBox.Show(
                this,
                $"WAA could not finish starting: {exception.Message}",
                "WAA startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyWindowTheme();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
        UpdateThemeButtonText();
    }

    private void OnThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var darkMode = !ThemeManager.IsDarkMode;
            ThemeManager.Apply(darkMode);
            _themePreferenceStore.SetDarkMode(darkMode);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception, "Theme preference update failed");
            MessageBox.Show(
                this,
                $"The appearance preference could not be saved: {exception.Message}",
                "WAA appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Left ||
            (Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt ||
            IsTextEditingControl(Keyboard.FocusedElement as DependencyObject) ||
            !_viewModel.BackCommand.CanExecute(null))
        {
            return;
        }

        _viewModel.BackCommand.Execute(null);
        e.Handled = true;
    }

    private void ApplyWindowTheme() =>
        WindowThemeHelper.SetDarkTitleBar(this, ThemeManager.IsDarkMode);

    private void UpdateThemeButtonText()
    {
        if (ThemeToggleButton is not null)
        {
            ThemeToggleButton.Content = ThemeManager.IsDarkMode ? "Light mode" : "Dark mode";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private static bool IsTextEditingControl(DependencyObject? element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is TextBoxBase or PasswordBox)
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}
