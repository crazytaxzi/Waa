using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Waa.App.Data;
using Waa.App.Infrastructure;
using Waa.App.ViewModels;

namespace Waa.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemePreferenceStore _themePreferenceStore;
    private bool _ambientMotionEnabled;
    private bool _ambientMotionRunning;

    public MainWindow(
        MainViewModel viewModel,
        ThemePreferenceStore themePreferenceStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themePreferenceStore = themePreferenceStore;
        var storedAmbientMotionPreference = themePreferenceStore.GetAmbientMotionPreference();
        _ambientMotionEnabled = storedAmbientMotionPreference ?? SystemParameters.ClientAreaAnimation;
        DataContext = viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeButtonText();
        UpdateAmbientMotionButtonText();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAmbientMotionState();

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
        UpdateAmbientMotionState();
    }

    private async void OnThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        var previous = ThemeManager.IsDarkMode;
        var next = !previous;
        ThemeToggleButton.IsEnabled = false;
        ThemeManager.Apply(next);

        try
        {
            await Task.Run(() => _themePreferenceStore.SetDarkMode(next));
            AppLog.Write($"Appearance changed to {(next ? "dark" : "light")} mode.");
        }
        catch (Exception exception)
        {
            ThemeManager.Apply(previous);
            AppLog.Write(exception, "Theme preference update failed");
            MessageBox.Show(
                this,
                $"The appearance preference could not be saved. WAA returned to the prior theme.\n\n{exception.Message}",
                "WAA appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            ThemeToggleButton.IsEnabled = true;
        }
    }

    private async void OnAmbientMotionToggleClicked(object sender, RoutedEventArgs e)
    {
        var previous = _ambientMotionEnabled;
        _ambientMotionEnabled = !previous;
        AmbientMotionToggleButton.IsEnabled = false;
        UpdateAmbientMotionState();

        try
        {
            await Task.Run(() => _themePreferenceStore.SetAmbientMotionEnabled(_ambientMotionEnabled));
            AppLog.Write($"Ambient motion changed to {(_ambientMotionEnabled ? "on" : "off")}.");
        }
        catch (Exception exception)
        {
            _ambientMotionEnabled = previous;
            UpdateAmbientMotionState();
            AppLog.Write(exception, "Ambient motion preference update failed");
            MessageBox.Show(
                this,
                $"The ambient motion preference could not be saved. WAA returned to the prior setting.\n\n{exception.Message}",
                "WAA appearance",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            UpdateAmbientMotionButtonText();
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

    private void UpdateAmbientMotionState()
    {
        if (AmbientMotionLayer is null)
        {
            return;
        }

        var shouldRun = _ambientMotionEnabled && ThemeManager.IsDarkMode;
        var storyboard = (Storyboard)FindResource("AmbientMotionStoryboard");

        if (shouldRun)
        {
            AmbientMotionLayer.Visibility = Visibility.Visible;
            if (!_ambientMotionRunning)
            {
                storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
                _ambientMotionRunning = true;
            }
        }
        else
        {
            if (_ambientMotionRunning)
            {
                storyboard.Stop(this);
                _ambientMotionRunning = false;
            }

            AmbientMotionLayer.Visibility = Visibility.Collapsed;
        }

        UpdateAmbientMotionButtonText();
    }

    private void UpdateAmbientMotionButtonText()
    {
        if (AmbientMotionToggleButton is null)
        {
            return;
        }

        AmbientMotionToggleButton.Content = _ambientMotionEnabled ? "Motion off" : "Motion on";
        AmbientMotionToggleButton.IsEnabled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_ambientMotionRunning)
        {
            ((Storyboard)FindResource("AmbientMotionStoryboard")).Stop(this);
            _ambientMotionRunning = false;
        }

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