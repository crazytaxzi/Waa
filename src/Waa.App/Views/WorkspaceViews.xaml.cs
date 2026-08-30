using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Waa.App.ViewModels;

namespace Waa.App.Views;

public partial class FleetQueueView : UserControl
{
    public FleetQueueView()
    {
        InitializeComponent();
    }

    private void OnFleetGridLoaded(object sender, RoutedEventArgs e)
    {
        if (FleetGrid.SelectedItem is not null)
        {
            FleetGrid.ScrollIntoView(FleetGrid.SelectedItem);
        }
    }

    private void OnFleetGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || FleetGrid.SelectedItem is not DriverRowViewModel driver)
        {
            return;
        }

        OpenDriver(driver);
        e.Handled = true;
    }

    private void OnFleetGridMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<DataGridColumnHeader>(source) is not null ||
            FindAncestor<ScrollBar>(source) is not null ||
            FindAncestor<ButtonBase>(source) is not null)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item is not DriverRowViewModel driver)
        {
            return;
        }

        FleetGrid.SelectedItem = driver;
        OpenDriver(driver);
        e.Handled = true;
    }

    private void OpenDriver(DriverRowViewModel driver)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main ||
            !main.OpenDriverCommand.CanExecute(driver))
        {
            return;
        }

        main.OpenDriverCommand.Execute(driver);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}

public partial class DriverWorkspaceView : UserControl
{
    public DriverWorkspaceView() => InitializeComponent();
}

public partial class IdleTaskView : UserControl
{
    public IdleTaskView() => InitializeComponent();
}

public partial class MissingBolTaskView : UserControl
{
    public MissingBolTaskView() => InitializeComponent();
}

public partial class WorkItemTaskView : UserControl
{
    public WorkItemTaskView() => InitializeComponent();
}

public partial class NewWorkView : UserControl
{
    public NewWorkView() => InitializeComponent();
}

public partial class ActivityDetailView : UserControl
{
    public ActivityDetailView() => InitializeComponent();
}

public partial class HandoffView : UserControl
{
    public HandoffView() => InitializeComponent();
}

public partial class UnmatchedBolView : UserControl
{
    public UnmatchedBolView() => InitializeComponent();
}

public partial class UnavailableView : UserControl
{
    public UnavailableView() => InitializeComponent();
}
