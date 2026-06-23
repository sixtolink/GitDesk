using Avalonia.Controls;
using Avalonia.Interactivity;
using GitDesk.ViewModels;
using System;
using System.Threading.Tasks;

namespace GitDesk;

public partial class MergeConflictDialog : Window
{
    private Func<string, Task<bool>> _saveAsync = _ => Task.FromResult(false);
    private Func<Task<bool>> _markResolvedAsync = () => Task.FromResult(false);

    public MergeConflictDialog()
    {
        InitializeComponent();
    }

    public MergeConflictDialog(
        Func<string, Task<bool>> saveAsync,
        Func<Task<bool>> markResolvedAsync)
    {
        _saveAsync = saveAsync;
        _markResolvedAsync = markResolvedAsync;
        InitializeComponent();
    }

    private MergeConflictDialogViewModel ViewModel => (MergeConflictDialogViewModel)DataContext!;

    private void OnPreviousConflictClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectPreviousConflict();
    }

    private void OnNextConflictClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectNextConflict();
    }

    private void OnApplySelectedOursClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplySelectedOurs();
    }

    private void OnApplySelectedTheirsClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplySelectedTheirs();
    }

    private void OnApplySelectedBothClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplySelectedBoth();
    }

    private void OnApplyAllOursClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplyAllOurs();
    }

    private void OnApplyAllTheirsClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplyAllTheirs();
    }

    private void OnApplyAllBothClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ApplyAllBoth();
    }

    private async void OnUseOursClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.UseWholeOursFile();
        await _saveAsync(ViewModel.WorkingContent);
    }

    private async void OnUseTheirsClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.UseWholeTheirsFile();
        await _saveAsync(ViewModel.WorkingContent);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        await _saveAsync(ViewModel.WorkingContent);
    }

    private async void OnMarkResolvedClicked(object? sender, RoutedEventArgs e)
    {
        if (!await _saveAsync(ViewModel.WorkingContent))
        {
            return;
        }

        if (await _markResolvedAsync())
        {
            Close(true);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
