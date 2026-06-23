using Avalonia.Controls;
using Avalonia.Interactivity;
using GitDesk.ViewModels;

namespace GitDesk;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsDialogViewModel viewModel)
        {
            Close(false);
            return;
        }

        if (!viewModel.Validate())
        {
            return;
        }

        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
